using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Shared.Migration;
using Quarry.Generators.Sql;
using Quarry.Generators.Translation;
using Quarry.Generators.Utilities;

namespace Quarry.Generators.Parsing;

/// <summary>
/// Analyzes query chains from TranslatedCallSite data to produce QueryPlan instances.
/// No SemanticModel or syntax tree walking — all metadata comes from RawCallSite fields
/// populated during Stage 2 discovery.
/// </summary>
internal static class ChainAnalyzer
{
    /// <summary>
    /// Test capture hook: when non-null, Analyze() appends results here.
    /// Set from test code before running the generator, read after.
    /// </summary>
    [ThreadStatic]
    internal static List<AnalyzedChain>? TestCapturedChains;

    /// <summary>
    /// UniqueIds of sites that belong to lambda inner chains and should be excluded
    /// from interceptor generation. Populated during Analyze() and read by PipelineOrchestrator.
    /// </summary>
    [ThreadStatic]
    internal static HashSet<string>? ConsumedLambdaInnerSiteIds;

    /// <summary>
    /// Maximum number of conditional bits for PrebuiltDispatch.
    /// 8 bits = up to 256 dispatch variants. Beyond this, classify as RuntimeBuild (compile error).
    /// </summary>
    private const int MaxConditionalBits = 8;

    /// <summary>
    /// Maximum nesting depth of if-blocks before abandoning analysis.
    /// </summary>
    private const int MaxIfNestingDepth = 2;

    /// <summary>
    /// Analyzes all chains from the collected translated call sites.
    /// Groups by ChainId, identifies execution terminals, classifies tiers,
    /// and builds QueryPlan instances.
    /// </summary>
    public static IReadOnlyList<AnalyzedChain> Analyze(
        ImmutableArray<TranslatedCallSite> sites,
        EntityRegistry registry,
        CancellationToken ct,
        List<DiagnosticInfo>? diagnostics = null)
    {
        // Group sites by ChainId
        var chains = new Dictionary<string, List<TranslatedCallSite>>(StringComparer.Ordinal);
        var unchained = new List<TranslatedCallSite>();

        foreach (var site in sites)
        {
            ct.ThrowIfCancellationRequested();
            var chainId = site.Bound.Raw.ChainId;
            if (chainId != null)
            {
                if (!chains.TryGetValue(chainId, out var list))
                {
                    list = new List<TranslatedCallSite>();
                    chains[chainId] = list;
                }
                list.Add(site);
            }
            else
            {
                unchained.Add(site);
            }
        }

        // Collect all operand chain IDs referenced by set operation sites.
        // These chains are consumed as right-hand operands and should not generate standalone carriers.
        var operandChainIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kvp in chains)
        {
            foreach (var site in kvp.Value)
            {
                var opId = site.Bound.Raw.OperandChainId;
                if (opId != null)
                    operandChainIds.Add(opId);
            }
        }

        // Analyze operand chains first (those without terminals, consumed by set operations).
        // Build a lookup of ChainId → QueryPlan for use by main chains.
        var operandPlans = new Dictionary<string, QueryPlan>(StringComparer.Ordinal);
        foreach (var opId in operandChainIds)
        {
            ct.ThrowIfCancellationRequested();
            if (chains.TryGetValue(opId, out var opSites))
            {
                try
                {
                    var opChain = AnalyzeOperandChain(opSites, registry, ct);
                    if (opChain != null)
                        operandPlans[opId] = opChain;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    var first = opSites.Count > 0 ? opSites[0] : null;
                    PipelineErrorBag.Report(
                        first?.Bound.Raw.FilePath ?? "",
                        first?.Bound.Raw.Line ?? 0,
                        first?.Bound.Raw.Column ?? 0,
                        $"Operand chain analysis failed: {ex.Message}");
                }
            }
        }

        var results = new List<AnalyzedChain>();

        // Multi-pass analysis: inner CTE chains first, then outer chains.
        // Inner chains (argument to With()) are analyzed and assembled to SQL
        // before outer chains so their SQL is available for CteDefinitions.
        var innerChainGroups = new Dictionary<string, List<TranslatedCallSite>>(StringComparer.Ordinal);
        var lambdaInnerChainGroups = new Dictionary<string, List<TranslatedCallSite>>(StringComparer.Ordinal);
        var outerChainGroups = new Dictionary<string, List<TranslatedCallSite>>(StringComparer.Ordinal);
        // Build lambda inner chain lookup: lambdaSpanStart → ChainId
        var lambdaInnerChainIds = new Dictionary<int, string>();
        const string LambdaInnerMarker = ":lambda-inner:";
        foreach (var kvp in chains)
        {
            if (kvp.Key.Contains(":cte-inner:"))
                innerChainGroups[kvp.Key] = kvp.Value;
            else if (kvp.Key.Contains(LambdaInnerMarker))
            {
                lambdaInnerChainGroups[kvp.Key] = kvp.Value;
                // Extract lambdaSpanStart from suffix
                var markerIdx = kvp.Key.LastIndexOf(LambdaInnerMarker, StringComparison.Ordinal);
                if (markerIdx >= 0)
                {
                    var spanStartText = kvp.Key.Substring(markerIdx + LambdaInnerMarker.Length);
                    if (int.TryParse(spanStartText, out var lambdaSpanStart))
                        lambdaInnerChainIds[lambdaSpanStart] = kvp.Key;
                }
            }
            else
                outerChainGroups[kvp.Key] = kvp.Value;
        }

        // Record all lambda inner chain site IDs so PipelineOrchestrator can exclude
        // them from interceptor generation (their SQL is embedded in the outer chain).
        if (lambdaInnerChainGroups.Count > 0)
        {
            ConsumedLambdaInnerSiteIds ??= new HashSet<string>(StringComparer.Ordinal);
            foreach (var kvp in lambdaInnerChainGroups)
            {
                foreach (var site in kvp.Value)
                    ConsumedLambdaInnerSiteIds.Add(site.UniqueId);
            }
        }

        // Pass 1: Analyze and assemble inner CTE chains.
        // Key: argSpanStart of the With() argument syntax — uniquely identifies the call site
        // within a single source file because two distinct With(...) invocations in the same
        // file always have distinct argument span starts. (Different files are processed in
        // separate generator runs, so cross-file collisions cannot occur here.)
        var cteInnerResults = new Dictionary<int, (AnalyzedChain Chain, AssembledPlan Assembled)>();
        const string CteInnerMarker = ":cte-inner:";
        foreach (var kvp in innerChainGroups)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var analyzed = AnalyzeChainGroup(kvp.Value, registry, ct, diagnostics);
                if (analyzed != null)
                {
                    var assembled = SqlAssembler.Assemble(analyzed, registry);
                    // Extract argSpanStart from ChainId suffix ":cte-inner:NNNN".
                    // Locate the marker explicitly so we don't depend on substring positions
                    // beyond the documented format.
                    var suffix = kvp.Key;
                    var markerIdx = suffix.LastIndexOf(CteInnerMarker, StringComparison.Ordinal);
                    if (markerIdx >= 0)
                    {
                        var spanStartText = suffix.Substring(markerIdx + CteInnerMarker.Length);
                        if (int.TryParse(spanStartText, out var argSpanStart))
                        {
                            cteInnerResults[argSpanStart] = (analyzed, assembled);
                        }
                    }
                    // Also add to results so the inner chain gets a carrier and interceptors.
                    // The inner chain may also be invoked standalone (e.g., the user binds it
                    // to a variable and executes it directly), so we cannot suppress its
                    // standalone interception even though With(...) embeds its SQL.
                    // The plan originally called for suppression, but standalone-callability
                    // makes that infeasible without breaking user code.
                    results.Add(analyzed);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var first = kvp.Value.Count > 0 ? kvp.Value[0] : null;
                PipelineErrorBag.Report(
                    first?.Bound.Raw.FilePath ?? "",
                    first?.Bound.Raw.Line ?? 0,
                    first?.Bound.Raw.Column ?? 0,
                    $"CTE inner chain analysis failed: {ex.Message}");
            }
        }

        // Pass 2: Analyze outer chains with CTE inner results available
        foreach (var kvp in outerChainGroups)
        {
            ct.ThrowIfCancellationRequested();
            var chainSites = kvp.Value;
            // A chain is an operand only if it's referenced as one AND has no execution terminal.
            // When both main and operand share the same ChainId, the main chain contains
            // both the set operation and the operand — inline splitting handles the separation.
            bool isOperandChain = operandChainIds.Contains(kvp.Key)
                && !chainSites.Any(s => IsExecutionKind(s.Bound.Raw.Kind) || s.Bound.Raw.Kind == InterceptorKind.Prepare);

            try
            {
                var inlineOperandChains = new List<AnalyzedChain>();
                var analyzed = AnalyzeChainGroup(chainSites, registry, ct, diagnostics, operandPlans, isOperandChain, inlineOperandChains,
                    cteInnerResults: cteInnerResults.Count > 0 ? cteInnerResults : null,
                    lambdaInnerChainIds: lambdaInnerChainIds.Count > 0 ? lambdaInnerChainIds : null,
                    lambdaInnerChainGroups: lambdaInnerChainGroups.Count > 0 ? lambdaInnerChainGroups : null);
                if (analyzed != null)
                    results.Add(analyzed);
                results.AddRange(inlineOperandChains);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var first = chainSites.Count > 0 ? chainSites[0] : null;
                PipelineErrorBag.Report(
                    first?.Bound.Raw.FilePath ?? "",
                    first?.Bound.Raw.Line ?? 0,
                    first?.Bound.Raw.Column ?? 0,
                    $"Chain analysis failed: {ex.Message}");
            }
        }

        TestCapturedChains?.AddRange(results);

        return results;
    }

    /// <summary>
    /// Analyzes a single chain group (all sites sharing the same ChainId).
    /// </summary>
    private static AnalyzedChain? AnalyzeChainGroup(
        List<TranslatedCallSite> chainSites,
        EntityRegistry registry,
        CancellationToken ct,
        List<DiagnosticInfo>? diagnostics = null,
        Dictionary<string, QueryPlan>? operandPlans = null,
        bool isOperandChain = false,
        List<AnalyzedChain>? inlineOperandChains = null,
        Dictionary<int, (AnalyzedChain Chain, AssembledPlan Assembled)>? cteInnerResults = null,
        Dictionary<int, string>? lambdaInnerChainIds = null,
        Dictionary<string, List<TranslatedCallSite>>? lambdaInnerChainGroups = null,
        bool isLambdaInnerChain = false)
    {
        // Find the execution terminal, detect .Trace()/.Prepare(), and collect clause sites
        TranslatedCallSite? executionSite = null;
        TranslatedCallSite? prepareSite = null;
        var clauseSites = new List<TranslatedCallSite>();
        var preparedTerminals = new List<TranslatedCallSite>();
        bool isTraced = false;
        int executionCount = 0;

        foreach (var site in chainSites)
        {
            if (site.Bound.Raw.Kind == InterceptorKind.Prepare)
            {
                prepareSite = site;
            }
            else if (site.Bound.Raw.IsPreparedTerminal)
            {
                // Terminal called on a PreparedQuery variable
                preparedTerminals.Add(site);
            }
            else if (IsExecutionKind(site.Bound.Raw.Kind))
            {
                executionSite = site;
                executionCount++;
            }
            else if (site.Bound.Raw.Kind == InterceptorKind.Trace)
            {
                isTraced = true;
                // Trace sites are excluded from clause processing
            }
            else
            {
                clauseSites.Add(site);
            }
        }

        // Handle .Prepare() chains
        if (prepareSite != null)
        {
            // QRY035: PreparedQuery escapes scope
            var escapeReason = prepareSite.Bound.Raw.PreparedQueryEscapeReason;
            if (escapeReason != null)
            {
                // Extract variable name from ChainId (format: "filepath:offset:varName")
                var prepChainId = prepareSite.Bound.Raw.ChainId;
                string varName = "prepared";
                if (prepChainId != null)
                {
                    var lastColon = prepChainId.LastIndexOf(':');
                    if (lastColon >= 0)
                        varName = prepChainId.Substring(lastColon + 1);
                }
                diagnostics?.Add(new DiagnosticInfo(
                    Quarry.Generators.DiagnosticDescriptors.PreparedQueryEscapesScope.Id,
                    prepareSite.Bound.Raw.Location,
                    varName, escapeReason));
                return null;
            }

            if (preparedTerminals.Count == 0)
            {
                // QRY036: no terminals on PreparedQuery — dead code
                var loc = prepareSite.Bound.Raw.Location;
                diagnostics?.Add(new DiagnosticInfo(
                    Quarry.Generators.DiagnosticDescriptors.PreparedQueryNoTerminals.Id,
                    loc,
                    $"{loc.FilePath}({loc.Line},{loc.Column})"));
                return null;
            }

            if (preparedTerminals.Count == 1)
            {
                // Single-terminal collapse: treat as if .Prepare() didn't exist
                // Keep prepareSite so the emitter can generate a pass-through interceptor
                executionSite = preparedTerminals[0];
                executionCount = 1;
                preparedTerminals.Clear();
                // Fall through to normal single-terminal processing
            }
            else
            {
                // Multi-terminal: use the first terminal as the execution site for plan building,
                // but record all terminals for the emitter
                executionSite = preparedTerminals[0];
                executionCount = 1;
                // Fall through to normal processing — PreparedTerminals will be set on AnalyzedChain
            }
        }

        if (executionSite == null)
        {
            // Operand chains (consumed by set operations) have no terminal.
            // Use the chain root or first clause site as a synthetic execution site
            // so the carrier still gets generated with ChainRoot + clause interceptors.
            if (isOperandChain)
            {
                // Find chain root as synthetic execution site
                foreach (var site in chainSites)
                {
                    if (site.Bound.Raw.Kind == InterceptorKind.ChainRoot)
                    {
                        executionSite = site;
                        break;
                    }
                }
                if (executionSite == null && clauseSites.Count > 0)
                    executionSite = clauseSites[clauseSites.Count - 1];
                if (executionSite == null)
                    return null;
            }
            else
            {
                // CTE inner chains (direct form) don't have execution terminals — use chain root
                for (int i = clauseSites.Count - 1; i >= 0; i--)
                {
                    if (clauseSites[i].Bound.Raw.Kind == InterceptorKind.ChainRoot
                        && clauseSites[i].Bound.Raw.IsCteInnerChain)
                    {
                        executionSite = clauseSites[i];
                        clauseSites.RemoveAt(i);
                        break;
                    }
                }

                // Lambda inner chains have no ChainRoot or execution terminal.
                // Use the first clause site for entity/table resolution — it stays
                // in clauseSites so it's still processed as a clause.
                if (executionSite == null && isLambdaInnerChain && clauseSites.Count > 0)
                    executionSite = clauseSites[0];

                if (executionSite == null)
                    return null;
            }
        }

        // Detect forked chains (multiple execution terminals sharing one ChainId)
        // Note: prepared multi-terminal chains are NOT forks — they're intentional
        if (executionCount > 1)
        {
            // Extract variable name from the ChainId (format: "filepath:offset:varName")
            var chainId = executionSite.Bound.Raw.ChainId;
            string? varName = null;
            if (chainId != null)
            {
                var lastColon = chainId.LastIndexOf(':');
                if (lastColon >= 0)
                    varName = chainId.Substring(lastColon + 1);
            }
            return MakeRuntimeBuildChain(executionSite, clauseSites,
                "Forked query chain", registry, isTraced, forkedVariableName: varName);
        }

        // Sort clause sites by source location for deterministic ordering
        clauseSites.Sort((a, b) =>
        {
            var cmp = a.Bound.Raw.Line.CompareTo(b.Bound.Raw.Line);
            if (cmp != 0) return cmp;
            return a.Bound.Raw.Column.CompareTo(b.Bound.Raw.Column);
        });

        // ── Split operand chain sites from main chain ──
        // When a chain group contains set operation sites (Union, Intersect, etc.),
        // the operand chain's sites (ChainRoot + clauses) are interleaved with the
        // main chain's sites because they share the same ChainId.
        // Split them out: sites between a set operation and the next set operation/terminal
        // that start with a ChainRoot belong to the operand.
        var inlineOperandPlans = new List<(SetOperatorKind Kind, QueryPlan Plan)>();
        if (clauseSites.Any(s => IsSetOperationKind(s.Bound.Raw.Kind)))
        {
            var mainSites = new List<TranslatedCallSite>();
            var i = 0;
            while (i < clauseSites.Count)
            {
                var site = clauseSites[i];
                if (IsSetOperationKind(site.Bound.Raw.Kind))
                {
                    var setOpKind = MapToSetOperatorKind(site.Bound.Raw.Kind);
                    mainSites.Add(site); // Keep the set op site in the main chain
                    i++;

                    // Collect operand sites: starts with ChainRoot, followed by clauses.
                    // Use the operand argument's end position to bound collection.
                    var operandSites = new List<TranslatedCallSite>();
                    var opArgEndLine = site.Bound.Raw.OperandArgEndLine;
                    var opArgEndCol = site.Bound.Raw.OperandArgEndColumn;
                    while (i < clauseSites.Count)
                    {
                        var next = clauseSites[i];
                        if (IsSetOperationKind(next.Bound.Raw.Kind)
                            || IsExecutionKind(next.Bound.Raw.Kind)
                            || next.Bound.Raw.Kind == InterceptorKind.Prepare)
                            break;
                        // Boundary check: if the next site is past the operand argument's end,
                        // it's a post-union clause on the main chain (e.g., OrderBy after Union).
                        if (opArgEndLine != null && operandSites.Count > 0)
                        {
                            var nextLine = next.Bound.Raw.Line;
                            var nextCol = next.Bound.Raw.Column;
                            if (nextLine > opArgEndLine.Value
                                || (nextLine == opArgEndLine.Value && nextCol >= opArgEndCol))
                                break;
                        }
                        // A new ChainRoot inside the argument signals the operand chain start
                        if (next.Bound.Raw.Kind == InterceptorKind.ChainRoot && operandSites.Count == 0)
                        {
                            operandSites.Add(next);
                            i++;
                            continue;
                        }
                        // Sites after the operand's ChainRoot belong to the operand
                        if (operandSites.Count > 0)
                        {
                            // Check if this is another ChainRoot (which would mean we've left the operand)
                            if (next.Bound.Raw.Kind == InterceptorKind.ChainRoot)
                                break;
                            operandSites.Add(next);
                            i++;
                            continue;
                        }
                        // Post-union clause on the main chain (e.g., OrderBy after Union)
                        mainSites.Add(next);
                        i++;
                    }

                    // Analyze the operand sites into a QueryPlan
                    if (operandSites.Count > 0)
                    {
                        var opPlan = AnalyzeOperandChain(operandSites, registry, ct);
                        if (opPlan != null)
                        {
                            inlineOperandPlans.Add((setOpKind, opPlan));

                            // Create an AnalyzedChain for the operand so it gets its own carrier
                            if (inlineOperandChains != null)
                            {
                                TranslatedCallSite? opExecSite = null;
                                var opClauseSites = new List<TranslatedCallSite>();
                                foreach (var os in operandSites)
                                {
                                    if (os.Bound.Raw.Kind == InterceptorKind.ChainRoot && opExecSite == null)
                                        opExecSite = os;
                                    opClauseSites.Add(os);
                                }
                                if (opExecSite != null)
                                    inlineOperandChains.Add(new AnalyzedChain(opPlan, opExecSite, opClauseSites, isOperandChain: true));
                            }
                        }
                    }
                }
                else
                {
                    mainSites.Add(site);
                    i++;
                }
            }
            clauseSites = mainSites;
        }

        // Check for disqualifiers from RawCallSite flags
        var disqualifyReason = CheckDisqualifiers(chainSites);
        if (disqualifyReason != null)
        {
            return MakeRuntimeBuildChain(executionSite, clauseSites, disqualifyReason, registry, isTraced);
        }

        ct.ThrowIfCancellationRequested();

        // For navigation join chains, synthetically discovered post-join sites may not have
        // JoinedEntityTypeNames (Roslyn couldn't resolve the post-join call's receiver type).
        // Build the names from the Join clause site's entity + joined entity and propagate.
        IReadOnlyList<string>? resolvedJoinNames = null;
        IReadOnlyList<EntityRef>? resolvedJoinEntities = null;
        foreach (var site in clauseSites)
        {
            if (site.Bound.Raw.Kind is InterceptorKind.Join or InterceptorKind.LeftJoin or InterceptorKind.RightJoin
                or InterceptorKind.CrossJoin or InterceptorKind.FullOuterJoin
                && site.Bound.JoinedEntity != null)
            {
                // First check if the Join already has JoinedEntityTypeNames from discovery
                if (site.Bound.JoinedEntityTypeNames != null && site.Bound.JoinedEntityTypeNames.Count >= 2)
                {
                    resolvedJoinNames = site.Bound.JoinedEntityTypeNames;
                    resolvedJoinEntities = site.Bound.JoinedEntities;
                }
                else
                {
                    // Build from entity + joinedEntity (navigation join case)
                    resolvedJoinNames = new List<string> { site.Bound.Raw.EntityTypeName, site.Bound.JoinedEntity.EntityName };
                    resolvedJoinEntities = new List<EntityRef> { site.Bound.Entity, site.Bound.JoinedEntity };
                }
                break;
            }
        }
        if (resolvedJoinNames != null)
        {
            if (executionSite.Bound.JoinedEntityTypeNames == null)
                executionSite = executionSite.WithJoinedEntityTypeNames(resolvedJoinNames, resolvedJoinEntities);
            // Only propagate JoinedEntityTypeNames to sites AFTER the join —
            // pre-join sites use single-entity builder types.
            bool seenJoin = false;
            for (int i = 0; i < clauseSites.Count; i++)
            {
                if (clauseSites[i].Bound.Raw.Kind is InterceptorKind.Join or InterceptorKind.LeftJoin or InterceptorKind.RightJoin
                    or InterceptorKind.CrossJoin or InterceptorKind.FullOuterJoin)
                {
                    seenJoin = true;
                    continue;
                }
                if (seenJoin && clauseSites[i].Bound.JoinedEntityTypeNames == null)
                {
                    clauseSites[i] = clauseSites[i].WithJoinedEntityTypeNames(resolvedJoinNames, resolvedJoinEntities);
                }
            }

            // Retranslate pre-join clause sites with join context so their SQL gets
            // table alias qualification (e.g., "t0"."IsActive" instead of "IsActive").
            // Pre-join sites keep single-entity builder types (JoinedEntityTypeNames stays null),
            // but their SQL must use "t0" prefixes because the assembled query includes JOINs.
            if (resolvedJoinEntities != null)
            {
                seenJoin = false;
                for (int i = 0; i < clauseSites.Count; i++)
                {
                    if (clauseSites[i].Bound.Raw.Kind is InterceptorKind.Join or InterceptorKind.LeftJoin or InterceptorKind.RightJoin
                    or InterceptorKind.CrossJoin or InterceptorKind.FullOuterJoin)
                    {
                        seenJoin = true;
                        continue;
                    }
                    if (!seenJoin && clauseSites[i].Clause != null && clauseSites[i].Bound.Raw.Expression != null)
                    {
                        var enrichedBound = clauseSites[i].Bound.WithJoinedEntities(
                            joinedEntities: resolvedJoinEntities);

                        var retranslated = CallSiteTranslator.Translate(enrichedBound, registry, ct);
                        if (retranslated.Clause != null && retranslated.Clause.IsSuccess)
                        {
                            clauseSites[i] = new TranslatedCallSite(
                                clauseSites[i].Bound, retranslated.Clause,
                                retranslated.KeyTypeName, retranslated.ValueTypeName);
                        }
                    }
                }
            }
        }

        // Identify conditional clauses from NestingContext
        var conditionalTerms = new List<ConditionalTerm>();
        var bitIndex = 0;
        var branchGroups = new Dictionary<string, List<(TranslatedCallSite Site, int BitIndex)>>(StringComparer.Ordinal);

        // Baseline nesting depth: clauses at or below the execution terminal's depth
        // are not conditionally included — the entire chain is simply inside nested control flow.
        var baselineDepth = executionSite.Bound.Raw.NestingContext?.NestingDepth ?? 0;

        foreach (var site in clauseSites)
        {
            var condInfo = site.Bound.Raw.NestingContext;
            if (condInfo == null)
                continue;

            // Only clauses deeper than the execution terminal are genuinely conditional.
            var relativeDepth = condInfo.NestingDepth - baselineDepth;
            if (relativeDepth <= 0)
                continue;

            // Check relative nesting depth
            if (relativeDepth > MaxIfNestingDepth)
            {
                return MakeRuntimeBuildChain(executionSite, clauseSites, "Conditional nesting depth exceeds maximum", registry, isTraced);
            }

            var role = MapInterceptorKindToClauseRole(site.Bound.Raw.Kind);
            if (role == null)
                continue;

            conditionalTerms.Add(new ConditionalTerm(bitIndex, role.Value));

            // Group by condition text for mutual exclusivity detection
            if (!branchGroups.TryGetValue(condInfo.ConditionText, out var group))
            {
                group = new List<(TranslatedCallSite, int)>();
                branchGroups[condInfo.ConditionText] = group;
            }
            group.Add((site, bitIndex));
            bitIndex++;
        }

        // Determine tier
        var totalBits = bitIndex;
        OptimizationTier tier;
        if (totalBits <= MaxConditionalBits)
            tier = OptimizationTier.PrebuiltDispatch;
        else
            tier = OptimizationTier.RuntimeBuild;

        ct.ThrowIfCancellationRequested();

        // Compute possible masks
        var possibleMasks = tier == OptimizationTier.PrebuiltDispatch
            ? EnumerateMaskCombinations(conditionalTerms, branchGroups, clauseSites)
            : Array.Empty<int>();

        // Collect unmatched method names (sites not in the chain that are tracked but not intercepted)
        // In the new pipeline, all sites in the chain are matched by ChainId — unmatched is N/A.
        // But we track Limit/Offset/Distinct/WithTimeout which have no clause translation.
        // New batch insert chains (BatchInsertColumnSelector → Values → terminal) are fully tracked.
        IReadOnlyList<string>? unmatchedMethodNames = null;

        // Build QueryPlan terms from TranslatedClause data
        var whereTerms = new List<WhereTerm>();
        var postUnionWhereTerms = new List<WhereTerm>();
        var postUnionGroupByExprs = new List<SqlExpr>();
        var postUnionHavingExprs = new List<SqlExpr>();
        bool seenSetOperation = false;
        var orderTerms = new List<OrderTerm>();
        var groupByExprs = new List<SqlExpr>();
        var havingExprs = new List<SqlExpr>();
        var setTerms = new List<SetTerm>();
        var joinPlans = new List<JoinPlan>();
        var implicitJoinInfos = new List<ImplicitJoinInfo>();
        var insertColumns = new List<InsertColumn>();
        var parameters = new List<QueryParameter>();
        var paramGlobalIndex = 0;
        PaginationPlan? pagination = null;
        var hasLimit = false;
        var hasOffset = false;
        int? limitLiteral = null;
        int? offsetLiteral = null;
        bool isDistinct = false;
        SelectProjection? projection = null;
        var setOperationPlans = new List<SetOperationPlan>();
        var primaryTable = new TableRef(
            executionSite.Bound.TableName,
            executionSite.Bound.SchemaName);

        // Build CTE definitions from CteDefinition/FromCte sites in the chain.
        // Forward iteration: each CteDefinition is processed before any FromCte that
        // references it, so the FromCte lookup against cteDefinitions can match by name.
        var cteDefinitions = new List<CteDef>();
        // Track CTE short names already added so we can emit QRY082 on duplicates.
        // Two With<X>(...) calls in one chain produce two CTE entries with the same
        // name, which would render an invalid WITH clause with duplicate aliases.
        var seenCteNames = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < clauseSites.Count; i++)
        {
            var site = clauseSites[i];
            var raw = site.Bound.Raw;

            if (raw.Kind == InterceptorKind.CteDefinition)
            {
                // Lambda form: recursively analyze the inner chain group
                if (raw.LambdaInnerSpanStart.HasValue
                    && lambdaInnerChainIds != null
                    && lambdaInnerChainGroups != null
                    && lambdaInnerChainIds.TryGetValue(raw.LambdaInnerSpanStart.Value, out var innerChainId)
                    && lambdaInnerChainGroups.TryGetValue(innerChainId, out var innerChainSites))
                {
                    var lambdaInnerAnalyzed = AnalyzeChainGroup(innerChainSites, registry, ct, diagnostics, isLambdaInnerChain: true);
                    if (lambdaInnerAnalyzed != null)
                    {
                        var lambdaInnerAssembled = SqlAssembler.Assemble(lambdaInnerAnalyzed, registry);
                        var lambdaInnerSql = lambdaInnerAssembled.SqlVariants.TryGetValue(0, out var lambdaVariant)
                            ? lambdaVariant.Sql : "";
                        var lambdaInnerParams = lambdaInnerAnalyzed.Plan.Parameters;
                        var lambdaColumns = raw.CteColumns ?? Array.Empty<CteColumn>();
                        var lambdaCteName = CteNameHelpers.ExtractShortName(raw.CteEntityTypeName) ?? "CTE";

                        if (!seenCteNames.Add(lambdaCteName))
                        {
                            diagnostics?.Add(new DiagnosticInfo(
                                Quarry.Generators.DiagnosticDescriptors.DuplicateCteName.Id,
                                raw.Location,
                                lambdaCteName));
                        }

                        var lambdaCteParamOffset = paramGlobalIndex;

                        // Reduce the inner plan's projection to only DTO columns when the
                        // plan has an identity projection (all entity columns). Lambda inner
                        // chains produce identity projections because the Select inside the
                        // lambda body is non-analyzable (receiver is a lambda parameter).
                        // The CTE DTO columns carry the correct subset.
                        var lambdaInnerPlan = lambdaInnerAnalyzed.Plan;
                        if (lambdaColumns.Count > 0 && lambdaInnerPlan.Projection.IsIdentity)
                        {
                            var dtoPropertyNames = new HashSet<string>(StringComparer.Ordinal);
                            foreach (var cc in lambdaColumns)
                                dtoPropertyNames.Add(cc.PropertyName);

                            var filteredCols = new List<ProjectedColumn>();
                            var ord = 0;
                            foreach (var col in lambdaInnerPlan.Projection.Columns)
                            {
                                if (dtoPropertyNames.Contains(col.PropertyName))
                                    filteredCols.Add(col with { Ordinal = ord++ });
                            }

                            var reducedProjection = new SelectProjection(
                                lambdaInnerPlan.Projection.Kind,
                                lambdaInnerPlan.Projection.ResultTypeName,
                                filteredCols,
                                lambdaInnerPlan.Projection.CustomEntityReaderClass,
                                isIdentity: false);

                            lambdaInnerPlan = new QueryPlan(
                                lambdaInnerPlan.Kind,
                                lambdaInnerPlan.PrimaryTable,
                                lambdaInnerPlan.Joins,
                                lambdaInnerPlan.WhereTerms,
                                lambdaInnerPlan.OrderTerms,
                                lambdaInnerPlan.GroupByExprs,
                                lambdaInnerPlan.HavingExprs,
                                reducedProjection,
                                lambdaInnerPlan.Pagination,
                                lambdaInnerPlan.IsDistinct,
                                lambdaInnerPlan.SetTerms,
                                lambdaInnerPlan.InsertColumns,
                                lambdaInnerPlan.ConditionalTerms,
                                lambdaInnerPlan.PossibleMasks,
                                lambdaInnerPlan.Parameters,
                                lambdaInnerPlan.Tier,
                                lambdaInnerPlan.NotAnalyzableReason,
                                lambdaInnerPlan.UnmatchedMethodNames,
                                lambdaInnerPlan.ForkedVariableName,
                                implicitJoins: lambdaInnerPlan.ImplicitJoins,
                                setOperations: lambdaInnerPlan.SetOperations,
                                postUnionWhereTerms: lambdaInnerPlan.PostUnionWhereTerms,
                                postUnionGroupByExprs: lambdaInnerPlan.PostUnionGroupByExprs,
                                postUnionHavingExprs: lambdaInnerPlan.PostUnionHavingExprs,
                                cteDefinitions: lambdaInnerPlan.CteDefinitions);
                        }

                        cteDefinitions.Add(new CteDef(
                            lambdaCteName, lambdaInnerSql, lambdaInnerParams, lambdaColumns,
                            innerPlan: lambdaInnerPlan,
                            parameterOffset: lambdaCteParamOffset));

                        foreach (var p in lambdaInnerParams)
                        {
                            parameters.Add(new QueryParameter(
                                globalIndex: paramGlobalIndex++,
                                clrType: p.ClrType,
                                valueExpression: p.ValueExpression,
                                isCaptured: p.IsCaptured,
                                expressionPath: p.ExpressionPath,
                                isCollection: p.IsCollection,
                                elementTypeName: p.ElementTypeName,
                                typeMappingClass: p.TypeMappingClass,
                                isEnum: p.IsEnum,
                                enumUnderlyingType: p.EnumUnderlyingType,
                                isSensitive: p.IsSensitive,
                                entityPropertyExpression: p.EntityPropertyExpression,
                                needsUnsafeAccessor: p.NeedsUnsafeAccessor,
                                isDirectAccessible: p.IsDirectAccessible,
                                collectionAccessExpression: p.CollectionAccessExpression,
                                capturedFieldName: p.CapturedFieldName,
                                capturedFieldType: p.CapturedFieldType,
                                isStaticCapture: p.IsStaticCapture,
                                isEnumerableCollection: p.IsEnumerableCollection));
                        }
                    }
                    else
                    {
                        var dtoShort = CteNameHelpers.ExtractShortName(raw.CteEntityTypeName) ?? "TDto";
                        diagnostics?.Add(new DiagnosticInfo(
                            Quarry.Generators.DiagnosticDescriptors.CteInnerChainNotAnalyzable.Id,
                            raw.Location,
                            dtoShort));
                    }
                }
                // Direct form: match this CTE definition to its inner chain's assembled SQL
                else if (cteInnerResults != null && raw.CteInnerArgSpanStart.HasValue
                    && cteInnerResults.TryGetValue(raw.CteInnerArgSpanStart.Value, out var inner))
                {
                    // Get the inner SQL from mask 0 (inner chains don't have conditional clauses)
                    var innerSql = inner.Assembled.SqlVariants.TryGetValue(0, out var variant)
                        ? variant.Sql : "";
                    var innerParams = inner.Chain.Plan.Parameters;
                    var columns = raw.CteColumns ?? Array.Empty<CteColumn>();
                    var cteName = CteNameHelpers.ExtractShortName(raw.CteEntityTypeName) ?? "CTE";

                    // Reject duplicate CTE names in the same chain via QRY082. We still add
                    // the CteDef so downstream code (placeholder rebasing, carrier emission)
                    // sees a coherent plan; the diagnostic surfaces as a compile-time error
                    // and prevents the broken SQL from ever running.
                    if (!seenCteNames.Add(cteName))
                    {
                        diagnostics?.Add(new DiagnosticInfo(
                            Quarry.Generators.DiagnosticDescriptors.DuplicateCteName.Id,
                            raw.Location,
                            cteName));
                    }

                    // Capture the starting index in the outer carrier's parameter slots
                    // BEFORE prepending — this is where the inner params will live in the outer carrier.
                    // EmitCteDefinition uses this offset to copy from inner carrier into outer carrier.
                    var cteParamOffset = paramGlobalIndex;

                    cteDefinitions.Add(new CteDef(
                        cteName, innerSql, innerParams, columns,
                        innerPlan: inner.Chain.Plan,
                        parameterOffset: cteParamOffset));

                    // Prepend CTE inner parameters to the outer parameter list
                    // Re-index from the current paramGlobalIndex
                    foreach (var p in innerParams)
                    {
                        parameters.Add(new QueryParameter(
                            globalIndex: paramGlobalIndex++,
                            clrType: p.ClrType,
                            valueExpression: p.ValueExpression,
                            isCaptured: p.IsCaptured,
                            expressionPath: p.ExpressionPath,
                            isCollection: p.IsCollection,
                            elementTypeName: p.ElementTypeName,
                            typeMappingClass: p.TypeMappingClass,
                            isEnum: p.IsEnum,
                            enumUnderlyingType: p.EnumUnderlyingType,
                            isSensitive: p.IsSensitive,
                            entityPropertyExpression: p.EntityPropertyExpression,
                            needsUnsafeAccessor: p.NeedsUnsafeAccessor,
                            isDirectAccessible: p.IsDirectAccessible,
                            collectionAccessExpression: p.CollectionAccessExpression,
                            capturedFieldName: p.CapturedFieldName,
                            capturedFieldType: p.CapturedFieldType,
                            isStaticCapture: p.IsStaticCapture,
                            isEnumerableCollection: p.IsEnumerableCollection));
                    }
                }
                else
                {
                    // Inner chain analysis missed (cteInnerResults null, no CteInnerArgSpanStart,
                    // or no entry for this site's argSpanStart). The CTE cannot be assembled — emit
                    // a user-visible diagnostic via the deferred-diagnostics channel so the user sees
                    // QRY080 instead of failing at runtime with "no such table: <CteName>" when a
                    // sibling FromCte<T>() rewrites primaryTable.
                    var dtoShort = CteNameHelpers.ExtractShortName(raw.CteEntityTypeName) ?? "TDto";
                    diagnostics?.Add(new DiagnosticInfo(
                        Quarry.Generators.DiagnosticDescriptors.CteInnerChainNotAnalyzable.Id,
                        raw.Location,
                        dtoShort));
                }
            }
            else if (raw.Kind == InterceptorKind.FromCte)
            {
                // FromCte: override primary table with the CTE name — but only if a matching
                // CteDefinition was successfully resolved above. Otherwise we would emit
                // SELECT ... FROM "Name" referencing an undeclared CTE.
                var cteName = CteNameHelpers.ExtractShortName(raw.CteEntityTypeName) ?? "CTE";
                bool hasMatchingCte = false;
                for (int j = 0; j < cteDefinitions.Count; j++)
                {
                    if (cteDefinitions[j].Name == cteName) { hasMatchingCte = true; break; }
                }
                if (hasMatchingCte)
                {
                    primaryTable = new TableRef(cteName, schemaName: null);
                }
                else
                {
                    // Emit QRY081 via the deferred diagnostics channel so the user sees a clear
                    // source-side error rather than runtime "no such table" or QRY900 InternalError.
                    diagnostics?.Add(new DiagnosticInfo(
                        Quarry.Generators.DiagnosticDescriptors.FromCteWithoutWith.Id,
                        raw.Location,
                        cteName));
                }
            }
        }
        // CteDefinition/FromCte sites remain in clauseSites for interceptor emission

        // Determine query kind — for prepared terminals, use the Prepare site's builder kind
        // since the prepared terminal's BuilderKind is always Query (from PreparedQuery type)
        var effectiveBuilderKind = (prepareSite != null)
            ? prepareSite.Bound.Raw.BuilderKind
            : executionSite.Bound.Raw.BuilderKind;
        var queryKind = DetermineQueryKind(executionSite.Bound.Raw.Kind, effectiveBuilderKind);

        // Process clause sites to build terms
        var consumedConditionalTerms = new HashSet<int>();
        for (int i = 0; i < clauseSites.Count; i++)
        {
            var site = clauseSites[i];
            var raw = site.Bound.Raw;
            var kind = raw.Kind;
            var role = MapInterceptorKindToClauseRole(kind);
            int? clauseBitIndex = null;

            // Check if this clause is conditional
            if (raw.NestingContext != null)
            {
                // Find its bit index — match by role and consume each term only once
                for (int ci = 0; ci < conditionalTerms.Count; ci++)
                {
                    if (conditionalTerms[ci].Role == role && !consumedConditionalTerms.Contains(ci))
                    {
                        clauseBitIndex = conditionalTerms[ci].BitIndex;
                        consumedConditionalTerms.Add(ci);
                        break;
                    }
                }
            }

            if (site.Clause != null && site.Clause.IsSuccess)
            {
                var clause = site.Clause;
                var expr = clause.ResolvedExpression;

                // Remap parameters and enrich with column metadata (IsEnum, IsSensitive)
                var clauseParams = RemapParameters(clause.Parameters, ref paramGlobalIndex);
                EnrichParametersFromColumns(clauseParams, expr, executionSite.Bound.Entity, resolvedJoinEntities);
                parameters.AddRange(clauseParams);

                switch (clause.Kind)
                {
                    case ClauseKind.Where:
                        if (seenSetOperation)
                            postUnionWhereTerms.Add(new WhereTerm(expr, clauseBitIndex));
                        else
                            whereTerms.Add(new WhereTerm(expr, clauseBitIndex));
                        break;

                    case ClauseKind.OrderBy:
                        orderTerms.Add(new OrderTerm(expr, clause.IsDescending, clauseBitIndex));
                        break;

                    case ClauseKind.GroupBy:
                        if (seenSetOperation)
                            postUnionGroupByExprs.Add(expr);
                        else
                            groupByExprs.Add(expr);
                        break;

                    case ClauseKind.Having:
                        if (seenSetOperation)
                            postUnionHavingExprs.Add(expr);
                        else
                            havingExprs.Add(expr);
                        break;

                    case ClauseKind.Set:
                        if (clause.SetAssignments != null)
                        {
                            // Enrich Set parameters with column metadata (IsEnum, IsSensitive)
                            // that EnrichParametersFromColumns missed because Set assignments
                            // use a different expression structure than Where comparisons.
                            EnrichSetParametersFromColumns(clauseParams, clause.SetAssignments,
                                executionSite.Bound.Entity, parameters);

                            // SetAction: multiple assignments. Parameters were remapped above,
                            // so walk backwards from paramGlobalIndex to assign each non-inlined
                            // assignment its correct parameter slot.
                            var setParamCount = clauseParams.Count;
                            var nextSetParamIdx = paramGlobalIndex - setParamCount;
                            foreach (var assignment in clause.SetAssignments)
                            {
                                // Quote the column name — ColumnSql stores the unquoted property name
                                var quotedCol = Quarry.Generators.Sql.SqlFormatting.QuoteIdentifier(site.Bound.Dialect, assignment.ColumnSql);
                                var col = new ResolvedColumnExpr(quotedCol);
                                SqlExpr valueExpr;
                                if (assignment.HasColumnExpression && assignment.BoundValueExpression != null)
                                {
                                    // Column expression (e.g., e.EndTime - e.StartTime + @p0):
                                    // use the bound+extracted SqlExpr tree directly.
                                    valueExpr = assignment.BoundValueExpression;
                                    nextSetParamIdx += CountParamSlots(valueExpr);
                                }
                                else if (assignment.IsInlined && assignment.InlinedSqlValue != null)
                                {
                                    // Detect boolean literals for dialect-specific formatting
                                    var inlinedVal = assignment.InlinedSqlValue;
                                    var lowerVal = inlinedVal.ToLowerInvariant();
                                    var clrType = (lowerVal == "true" || lowerVal == "false") ? "bool" : "object";
                                    valueExpr = new LiteralExpr(inlinedVal, clrType);
                                }
                                else
                                {
                                    // Parameter reference — LocalIndex=0 within each SetTerm.
                                    // The SQL assembler computes the global index via paramBase.
                                    valueExpr = new ParamSlotExpr(0, "object", "@p" + nextSetParamIdx);
                                    nextSetParamIdx++;
                                }
                                setTerms.Add(new SetTerm(col, valueExpr, assignment.CustomTypeMappingClass, clauseBitIndex));
                            }
                        }
                        else
                        {
                            // Single Set: column = value from the expression
                            // The lambda u => u.Column produces the column reference only.
                            // The value parameter is the second arg to Set(), handled at runtime
                            // by the emitter via SetClauseInfo.ValueParameterIndex.
                            var col = new ResolvedColumnExpr(SqlExprRenderer.Render(expr, site.Bound.Dialect));
                            // LocalIndex=0 within this SetTerm — assembler computes global index
                            var valueIdx = clauseParams.Count > 0 ? paramGlobalIndex - 1 : paramGlobalIndex;
                            var valExpr = new ParamSlotExpr(0, "object", "@p" + valueIdx);
                            setTerms.Add(new SetTerm(col, valExpr, clause.CustomTypeMappingClass, clauseBitIndex));
                        }
                        break;

                    case ClauseKind.Join:
                        var joinTable = new TableRef(
                            clause.JoinedTableName ?? "",
                            clause.JoinedSchemaName,
                            clause.TableAlias);
                        var joinKind = clause.JoinKind ?? JoinClauseKind.Inner;
                        var onCondition = joinKind == JoinClauseKind.Cross ? null : (SqlExpr?)expr;
                        joinPlans.Add(new JoinPlan(joinKind, joinTable, onCondition, raw.IsNavigationJoin));
                        break;
                }

                // Collect implicit joins from One<T> navigation access
                if (clause.ImplicitJoins != null)
                {
                    foreach (var ij in clause.ImplicitJoins)
                    {
                        // Dedup across clauses by target alias
                        var isDuplicate = implicitJoinInfos.Any(existing => existing.TargetAlias == ij.TargetAlias);
                        if (!isDuplicate)
                        {
                            implicitJoinInfos.Add(ij);
                        }
                    }
                }
            }
            else if (kind == InterceptorKind.UpdateSetAction && raw.SetActionAssignments != null)
            {
                // SetAction (Action<T> lambda): parameters and assignments stored on RawCallSite
                // because Action<T> can't be parsed to SqlExpr.
                // When column expressions are present, bound assignments and parameters come from
                // the TranslatedClause (bound+extracted in CallSiteTranslator); otherwise from RawCallSite.
                var clauseAssignments = site.Clause?.SetAssignments ?? raw.SetActionAssignments;
                var hasColumnExprs = clauseAssignments.Any(a => a.HasColumnExpression);
                var clauseParamSource = hasColumnExprs && site.Clause?.Parameters != null
                    ? site.Clause.Parameters
                    : raw.SetActionParameters;

                if (clauseParamSource != null && clauseParamSource.Count > 0)
                {
                    var clauseParams = RemapParameters(clauseParamSource, ref paramGlobalIndex);
                    parameters.AddRange(clauseParams);
                }

                // Build set terms from assignments. Non-inlined/non-column-expr assignments
                // consume parameters in order.
                var setParamCount = clauseParamSource?.Count ?? 0;
                var nextParamIdx = paramGlobalIndex - setParamCount;

                foreach (var assignment in clauseAssignments)
                {
                    // Quote the column name using the dialect — SetActionAssignment.ColumnSql
                    // stores the unquoted property name from discovery
                    var quotedCol = Quarry.Generators.Sql.SqlFormatting.QuoteIdentifier(site.Bound.Dialect, assignment.ColumnSql);
                    var col = new ResolvedColumnExpr(quotedCol);
                    SqlExpr valueExpr;
                    if (assignment.HasColumnExpression && assignment.BoundValueExpression != null)
                    {
                        // Column expression (e.g., e.EndTime - e.StartTime + @p0):
                        // use the bound+extracted SqlExpr tree directly.
                        // Count params in the expression to advance the index.
                        valueExpr = assignment.BoundValueExpression;
                        nextParamIdx += CountParamSlots(valueExpr);
                    }
                    else if (assignment.IsInlined && assignment.InlinedSqlValue != null)
                    {
                        // Detect boolean literals for dialect-specific formatting
                        var inlinedVal = assignment.InlinedSqlValue;
                        var lowerVal = inlinedVal.ToLowerInvariant();
                        var clrType = (lowerVal == "true" || lowerVal == "false") ? "bool" : "object";
                        valueExpr = new LiteralExpr(inlinedVal, clrType);
                    }
                    else
                    {
                        // LocalIndex=0 within each SetTerm — assembler computes global index
                        valueExpr = new ParamSlotExpr(0, "object", "@p" + nextParamIdx);
                        nextParamIdx++;
                    }
                    setTerms.Add(new SetTerm(col, valueExpr, assignment.CustomTypeMappingClass, clauseBitIndex));
                }
            }
            else if (kind == InterceptorKind.UpdateSetPoco && site.Bound.UpdateInfo != null)
            {
                // UpdateSetPoco: build SET terms from UpdateInfo columns.
                // Each column gets a parameter slot with a local index (0-based
                // within this clause) so the assembler can renumber them in SQL order.
                var updateInfo = site.Bound.UpdateInfo;
                foreach (var col in updateInfo.Columns)
                {
                    var colExpr = new ResolvedColumnExpr(col.QuotedColumnName);
                    // Each SET value is a standalone expression with one param at LocalIndex=0.
                    // The assembler's paramBase handles the actual position in the SQL output.
                    var valExpr = new ParamSlotExpr(0, col.ClrType, "@p0");
                    setTerms.Add(new SetTerm(colExpr, valExpr, col.CustomTypeMappingClass, clauseBitIndex));
                    parameters.Add(new QueryParameter(
                        paramGlobalIndex,
                        col.ClrType,
                        $"entity.{col.PropertyName}",
                        typeMappingClass: col.CustomTypeMappingClass,
                        isEnum: col.IsEnum,
                        isSensitive: col.IsSensitive,
                        entityPropertyExpression: $"__c.Entity.{col.PropertyName}"));
                    paramGlobalIndex++;
                }
            }
            else if (kind == InterceptorKind.Limit)
            {
                hasLimit = true;
                limitLiteral = raw.ConstantIntValue;
            }
            else if (kind == InterceptorKind.Offset)
            {
                hasOffset = true;
                offsetLiteral = raw.ConstantIntValue;
            }
            else if (kind == InterceptorKind.Distinct)
            {
                isDistinct = true;
            }
            else if (IsSetOperationKind(kind))
            {
                seenSetOperation = true;
                QueryPlan? opPlan = null;
                var opKind = MapToSetOperatorKind(kind);

                // Lambda form: recursively analyze the inner chain group
                if (raw.LambdaInnerSpanStart.HasValue
                    && lambdaInnerChainIds != null
                    && lambdaInnerChainGroups != null
                    && lambdaInnerChainIds.TryGetValue(raw.LambdaInnerSpanStart.Value, out var setOpInnerChainId)
                    && lambdaInnerChainGroups.TryGetValue(setOpInnerChainId, out var setOpInnerSites))
                {
                    var lambdaInnerAnalyzed = AnalyzeChainGroup(setOpInnerSites, registry, ct, diagnostics, isLambdaInnerChain: true);
                    if (lambdaInnerAnalyzed != null)
                        opPlan = lambdaInnerAnalyzed.Plan;
                }

                // Inline operand plans (same-ChainId operands split during preprocessing)
                if (opPlan == null && inlineOperandPlans.Count > 0)
                {
                    // Find the first unmatched inline plan with matching kind
                    for (int ip = 0; ip < inlineOperandPlans.Count; ip++)
                    {
                        if (inlineOperandPlans[ip].Kind == opKind)
                        {
                            opPlan = inlineOperandPlans[ip].Plan;
                            inlineOperandPlans.RemoveAt(ip);
                            break;
                        }
                    }
                }

                // Fall back to external operand plans (different-ChainId operands)
                if (opPlan == null)
                {
                    var opChainId = raw.OperandChainId;
                    if (opChainId != null && operandPlans != null && operandPlans.TryGetValue(opChainId, out var extPlan))
                        opPlan = extPlan;
                }

                if (opPlan != null)
                {
                    setOperationPlans.Add(new SetOperationPlan(opKind, opPlan, paramGlobalIndex, raw.OperandEntityTypeName));
                    // Absorb operand parameters into the main chain's global parameter space
                    foreach (var opParam in opPlan.Parameters)
                    {
                        parameters.Add(new QueryParameter(
                            globalIndex: paramGlobalIndex++,
                            clrType: opParam.ClrType,
                            valueExpression: opParam.ValueExpression,
                            isCaptured: opParam.IsCaptured,
                            expressionPath: opParam.ExpressionPath,
                            isCollection: opParam.IsCollection,
                            elementTypeName: opParam.ElementTypeName,
                            typeMappingClass: opParam.TypeMappingClass,
                            isEnum: opParam.IsEnum,
                            enumUnderlyingType: opParam.EnumUnderlyingType,
                            isSensitive: opParam.IsSensitive,
                            capturedFieldName: opParam.CapturedFieldName,
                            capturedFieldType: opParam.CapturedFieldType,
                            isStaticCapture: opParam.IsStaticCapture));
                    }
                }
            }
            else if (kind == InterceptorKind.Select && raw.ProjectionInfo != null)
            {
                // Disqualify chains with failed projections (e.g., anonymous types)
                if (raw.ProjectionInfo.FailureReason != ProjectionFailureReason.None)
                {
                    return MakeRuntimeBuildChain(executionSite, clauseSites,
                        raw.ProjectionInfo.NonOptimalReason ?? "Projection analysis failed",
                        registry, isTraced);
                }
                projection = BuildProjection(raw.ProjectionInfo, executionSite, registry,
                    site.Bound.Dialect, implicitJoinInfos, joinPlans, diagnostics);

                if (raw.ProjectionInfo.ProjectionParameters is { Count: > 0 } projParams
                    && projection != null)
                {
                    projection = RemapProjectionParameters(projection, projParams, parameters, ref paramGlobalIndex);
                }
            }
        }

        // Build pagination
        if (hasLimit || hasOffset)
        {
            pagination = new PaginationPlan(
                literalLimit: limitLiteral,
                literalOffset: offsetLiteral,
                limitParamIndex: hasLimit && limitLiteral == null ? paramGlobalIndex++ : (int?)null,
                offsetParamIndex: hasOffset && offsetLiteral == null ? paramGlobalIndex++ : (int?)null);
        }

        // Default projection if none specified
        if (projection == null)
        {
            projection = new SelectProjection(
                ProjectionKind.Entity,
                executionSite.Bound.Raw.ResultTypeName ?? executionSite.Bound.Raw.EntityTypeName,
                Array.Empty<ProjectedColumn>(),
                isIdentity: true);
        }

        // Enrich identity projections with entity columns so SqlAssembler renders
        // explicit column names instead of SELECT *.
        if (projection.IsIdentity)
        {
            projection = EnrichIdentityProjectionWithEntityColumns(projection, executionSite.Bound.Entity);
        }

        // Handle insert columns — prefer prepare site (has initializer-derived columns),
        // then clause sites, then execution site (which may have all-columns fallback for prepared terminals).
        InsertInfo? resolvedInsertInfo = null;
        if (queryKind == QueryKind.Insert || queryKind == QueryKind.BatchInsert)
        {
            if (prepareSite?.Bound.InsertInfo != null)
                resolvedInsertInfo = prepareSite.Bound.InsertInfo;

            if (resolvedInsertInfo == null)
            {
                foreach (var cs in clauseSites)
                {
                    if (cs.Bound.InsertInfo != null) { resolvedInsertInfo = cs.Bound.InsertInfo; break; }
                }
            }
        }
        resolvedInsertInfo ??= executionSite.Bound.InsertInfo;
        if ((queryKind == QueryKind.Insert || queryKind == QueryKind.BatchInsert) && resolvedInsertInfo != null)
        {
            var insertInfo = resolvedInsertInfo;
            for (int c = 0; c < insertInfo.Columns.Count; c++)
            {
                var col = insertInfo.Columns[c];
                insertColumns.Add(new InsertColumn(col.QuotedColumnName, paramGlobalIndex++));
            }
        }

        // Default to identity projection (whole entity) when no Select clause was found
        if (projection == null)
        {
            projection = new SelectProjection(
                ProjectionKind.Entity,
                executionSite.Bound.Raw.ResultTypeName ?? executionSite.Bound.Raw.EntityTypeName,
                Array.Empty<ProjectedColumn>(),
                isIdentity: true);
        }

        // Table-qualify primary entity columns when implicit joins are present.
        // Without qualification, column names like "Id" become ambiguous when the
        // primary entity and a joined entity share the same column name.
        if (projection != null && implicitJoinInfos.Count > 0 && projection.Columns.Count > 0)
        {
            var qualified = new List<ProjectedColumn>();
            foreach (var col in projection.Columns)
            {
                if (col.TableAlias == null && !col.IsAggregateFunction)
                {
                    qualified.Add(col with { TableAlias = "t0" });
                }
                else
                {
                    qualified.Add(col);
                }
            }
            projection = new SelectProjection(
                projection.Kind, projection.ResultTypeName, qualified,
                projection.CustomEntityReaderClass, projection.IsIdentity);
        }

        var plan = new QueryPlan(
            kind: queryKind,
            primaryTable: primaryTable,
            joins: joinPlans,
            whereTerms: whereTerms,
            orderTerms: orderTerms,
            groupByExprs: groupByExprs,
            havingExprs: havingExprs,
            projection: projection!,
            pagination: pagination,
            isDistinct: isDistinct,
            setTerms: setTerms,
            insertColumns: insertColumns,
            conditionalTerms: conditionalTerms,
            possibleMasks: possibleMasks,
            parameters: parameters,
            tier: tier,
            unmatchedMethodNames: unmatchedMethodNames,
            implicitJoins: implicitJoinInfos.Count > 0 ? implicitJoinInfos : null,
            setOperations: setOperationPlans.Count > 0 ? setOperationPlans : null,
            postUnionWhereTerms: postUnionWhereTerms.Count > 0 ? postUnionWhereTerms : null,
            postUnionGroupByExprs: postUnionGroupByExprs.Count > 0 ? postUnionGroupByExprs : null,
            postUnionHavingExprs: postUnionHavingExprs.Count > 0 ? postUnionHavingExprs : null,
            cteDefinitions: cteDefinitions.Count > 0 ? cteDefinitions : null);

        // Trace logging: only for traced chains. Reconstruct per-site discovery/binding/
        // translation traces from the TranslatedCallSite data, then log chain-level analysis.
        if (isTraced)
        {
            var chainUid = executionSite.Bound.Raw.UniqueId;

            // Per-site retroactive trace (discovery + binding + translation)
            foreach (var site in clauseSites)
            {
                LogSiteTrace(chainUid, site);
            }
            LogSiteTrace(chainUid, executionSite);

            // Chain-level analysis trace
            LogChainTrace(chainUid, plan, executionSite);
        }

        return new AnalyzedChain(plan, executionSite, clauseSites, isTraced,
            preparedTerminals: preparedTerminals.Count > 1 ? preparedTerminals : null,
            prepareSite: prepareSite,
            isOperandChain: isOperandChain);
    }

    /// <summary>
    /// Counts the number of ParamSlotExpr nodes in an SqlExpr tree.
    /// </summary>
    private static int CountParamSlots(SqlExpr expr)
        => SqlExprRenderer.CollectParameters(expr).Count;

    /// <summary>
    /// Remaps clause-local parameters to global parameter indices.
    /// </summary>
    private static List<QueryParameter> RemapParameters(
        IReadOnlyList<ParameterInfo> clauseParams,
        ref int globalIndex)
    {
        var result = new List<QueryParameter>(clauseParams.Count);
        foreach (var p in clauseParams)
        {
            result.Add(new QueryParameter(
                globalIndex: globalIndex++,
                clrType: p.ClrType,
                valueExpression: p.ValueExpression,
                isCaptured: p.IsCaptured,
                expressionPath: p.ExpressionPath,
                isCollection: p.IsCollection,
                elementTypeName: p.CollectionElementType,
                typeMappingClass: p.CustomTypeMappingClass,
                isEnum: p.IsEnum,
                enumUnderlyingType: p.EnumUnderlyingType,
                needsUnsafeAccessor: p.IsCaptured && p.CanGenerateDirectPath,
                isDirectAccessible: false, // Computed during carrier analysis
                collectionAccessExpression: null, // Computed during carrier analysis
                capturedFieldName: p.CapturedFieldName,
                capturedFieldType: p.CapturedFieldType,
                isStaticCapture: p.IsStaticCapture,
                isEnumerableCollection: IsEnumerableOnlyCollection(p)));
        }
        return result;
    }

    /// <summary>
    /// Remaps projection parameters (e.g., window function variable args) into the global
    /// parameter list and replaces local @__proj{N} placeholders with {@globalIndex}.
    /// Returns the updated projection or the original if no parameters needed remapping.
    /// </summary>
    private static SelectProjection RemapProjectionParameters(
        SelectProjection projection,
        IReadOnlyList<ParameterInfo> projParams,
        List<QueryParameter> parameters,
        ref int paramGlobalIndex)
    {
        var remapped = RemapParameters(projParams, ref paramGlobalIndex);
        var localToGlobal = new Dictionary<string, string>(remapped.Count);
        for (int pi = 0; pi < remapped.Count; pi++)
            localToGlobal[$"@__proj{pi}"] = $"{{@{remapped[pi].GlobalIndex}}}";

        parameters.AddRange(remapped);

        var updatedCols = new List<ProjectedColumn>(projection.Columns.Count);
        foreach (var col in projection.Columns)
        {
            if (col.SqlExpression != null && col.SqlExpression.Contains("@__proj"))
            {
                var sql = col.SqlExpression;
                foreach (var kvp in localToGlobal)
                    sql = sql.Replace(kvp.Key, kvp.Value);
                updatedCols.Add(col with { SqlExpression = sql });
            }
            else
            {
                updatedCols.Add(col);
            }
        }
        return new SelectProjection(
            projection.Kind, projection.ResultTypeName, updatedCols,
            projection.CustomEntityReaderClass, projection.IsIdentity);
    }

    /// <summary>
    /// Determines whether a collection parameter's type only implements IEnumerable&lt;T&gt;
    /// (not IReadOnlyList&lt;T&gt;). Arrays and types implementing IReadOnlyList return false.
    /// </summary>
    private static bool IsEnumerableOnlyCollection(ParameterInfo p)
    {
        if (!p.IsCollection || p.CollectionReceiverSymbol is not Microsoft.CodeAnalysis.ITypeSymbol typeSymbol)
            return false;

        // Arrays implement IReadOnlyList<T>
        if (typeSymbol is Microsoft.CodeAnalysis.IArrayTypeSymbol)
            return false;

        // Check the type itself (if it's an interface like IReadOnlyList<T>)
        if (IsReadOnlyListInterface(typeSymbol))
            return false;

        // Check all implemented interfaces
        foreach (var iface in typeSymbol.AllInterfaces)
        {
            if (IsReadOnlyListInterface(iface))
                return false;
        }

        return true;
    }

    private static bool IsReadOnlyListInterface(Microsoft.CodeAnalysis.ITypeSymbol type)
    {
        return type is Microsoft.CodeAnalysis.INamedTypeSymbol named
            && named.Name == "IReadOnlyList"
            && named.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic";
    }

    /// <summary>
    /// Enriches clause parameters with column metadata (IsEnum, IsSensitive, EnumUnderlyingType)
    /// by walking the resolved expression tree to find column-parameter pairs, then looking up
    /// column metadata from the entity definition.
    /// </summary>
    private static void EnrichParametersFromColumns(
        List<QueryParameter> clauseParams,
        SqlExpr? expression,
        EntityRef? entity,
        IReadOnlyList<EntityRef>? joinedEntities)
    {
        if (expression == null || clauseParams.Count == 0)
            return;

        // Build column lookup by unquoted column name from all available entities
        var columnLookup = new Dictionary<string, ColumnInfo>(StringComparer.OrdinalIgnoreCase);
        if (entity != null)
        {
            foreach (var col in entity.Columns)
                columnLookup[col.ColumnName] = col;
        }
        if (joinedEntities != null)
        {
            foreach (var je in joinedEntities)
            {
                foreach (var col in je.Columns)
                {
                    // Don't overwrite — primary entity columns take precedence
                    if (!columnLookup.ContainsKey(col.ColumnName))
                        columnLookup[col.ColumnName] = col;
                }
            }
        }

        if (columnLookup.Count == 0)
            return;

        // Walk expression tree to find column-param pairs
        var paramColumnMap = new Dictionary<int, ColumnInfo>();
        WalkExprForColumnParamPairs(expression, columnLookup, paramColumnMap);

        if (paramColumnMap.Count == 0)
            return;

        // Enrich parameters with column metadata
        for (int i = 0; i < clauseParams.Count; i++)
        {
            var p = clauseParams[i];
            // ParamSlotExpr local indices correspond to clause-local ordering (0, 1, 2...).
            // clauseParams[i] was created from clauseParams[i] in RemapParameters, so
            // the local index is just i.
            if (!paramColumnMap.TryGetValue(i, out var col))
                continue;

            clauseParams[i] = EnrichParameterFromColumn(p, col);
        }
    }

    /// <summary>
    /// Enriches a single parameter with column metadata (IsEnum, EnumUnderlyingType, IsSensitive).
    /// </summary>
    private static QueryParameter EnrichParameterFromColumn(QueryParameter param, ColumnInfo col)
    {
        var isEnum = col.IsEnum || param.IsEnum;
        var isSensitive = col.Modifiers.IsSensitive || param.IsSensitive;
        var enumUnderlying = param.EnumUnderlyingType ?? (isEnum ? (col.DbClrType ?? "int") : null);
        return param.WithEnrichment(isEnum, enumUnderlying, isSensitive);
    }

    /// <summary>
    /// Enriches Set clause parameters with column metadata (IsEnum, IsSensitive) by matching
    /// each non-inlined SetActionAssignment's ColumnSql (property name) to a column in the entity.
    /// This handles the case that EnrichParametersFromColumns misses because Set assignments
    /// use a different expression structure than Where/Having comparisons.
    /// </summary>
    private static void EnrichSetParametersFromColumns(
        List<QueryParameter> clauseParams,
        IReadOnlyList<SetActionAssignment> assignments,
        EntityRef? entity,
        List<QueryParameter> allParameters)
    {
        if (entity == null || clauseParams.Count == 0)
            return;

        // Build lookup by property name
        var columnByProperty = new Dictionary<string, ColumnInfo>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var col in entity.Columns)
            columnByProperty[col.PropertyName] = col;

        // Match each non-inlined assignment to its clause parameter (in order)
        int paramIdx = 0;
        foreach (var assignment in assignments)
        {
            if (assignment.IsInlined)
                continue;

            if (paramIdx >= clauseParams.Count)
                break;

            if (columnByProperty.TryGetValue(assignment.ColumnSql, out var colInfo))
            {
                var p = clauseParams[paramIdx];
                var enriched = EnrichParameterFromColumn(p, colInfo);
                if (!ReferenceEquals(enriched, p))
                {
                    clauseParams[paramIdx] = enriched;

                    // Also update in allParameters if already added
                    for (int i = 0; i < allParameters.Count; i++)
                    {
                        if (allParameters[i].GlobalIndex == p.GlobalIndex)
                        {
                            allParameters[i] = enriched;
                            break;
                        }
                    }
                }
            }

            paramIdx++;
        }
    }

    /// <summary>
    /// Recursively walks an expression tree to find BinaryOpExpr/InExpr/LikeExpr nodes
    /// where one side is a ResolvedColumnExpr and the other contains a ParamSlotExpr.
    /// Records the mapping from param local index to the matched ColumnInfo.
    /// </summary>
    private static void WalkExprForColumnParamPairs(
        SqlExpr expr,
        Dictionary<string, ColumnInfo> columnLookup,
        Dictionary<int, ColumnInfo> paramColumnMap)
    {
        switch (expr)
        {
            case BinaryOpExpr bin:
                // Check if this is a column = param or param = column comparison
                TryMatchColumnParam(bin.Left, bin.Right, columnLookup, paramColumnMap);
                TryMatchColumnParam(bin.Right, bin.Left, columnLookup, paramColumnMap);
                // Recurse into both sides for nested expressions (e.g., AND/OR)
                WalkExprForColumnParamPairs(bin.Left, columnLookup, paramColumnMap);
                WalkExprForColumnParamPairs(bin.Right, columnLookup, paramColumnMap);
                break;

            case InExpr inExpr:
                // IN clause: operand is the column, values contain params
                if (inExpr.Operand is ResolvedColumnExpr inCol)
                {
                    var colInfo = LookupColumn(inCol, columnLookup);
                    if (colInfo != null)
                    {
                        foreach (var val in inExpr.Values)
                        {
                            if (val is ParamSlotExpr paramSlot)
                                paramColumnMap[paramSlot.LocalIndex] = colInfo;
                        }
                    }
                }
                break;

            case LikeExpr like:
                // LIKE: operand is the column, pattern contains param
                if (like.Operand is ResolvedColumnExpr likeCol)
                {
                    var colInfo = LookupColumn(likeCol, columnLookup);
                    if (colInfo != null && like.Pattern is ParamSlotExpr likeParam)
                        paramColumnMap[likeParam.LocalIndex] = colInfo;
                }
                break;

            case UnaryOpExpr unary:
                WalkExprForColumnParamPairs(unary.Operand, columnLookup, paramColumnMap);
                break;

            case FunctionCallExpr func:
                foreach (var arg in func.Arguments)
                    WalkExprForColumnParamPairs(arg, columnLookup, paramColumnMap);
                break;

            case IsNullCheckExpr isNull:
                WalkExprForColumnParamPairs(isNull.Operand, columnLookup, paramColumnMap);
                break;
        }
    }

    /// <summary>
    /// If columnSide is a ResolvedColumnExpr and paramSide is (or contains) a ParamSlotExpr,
    /// records the column-param mapping.
    /// </summary>
    private static void TryMatchColumnParam(
        SqlExpr columnSide,
        SqlExpr paramSide,
        Dictionary<string, ColumnInfo> columnLookup,
        Dictionary<int, ColumnInfo> paramColumnMap)
    {
        if (columnSide is not ResolvedColumnExpr colExpr)
            return;

        var colInfo = LookupColumn(colExpr, columnLookup);
        if (colInfo == null)
            return;

        // Direct param
        if (paramSide is ParamSlotExpr paramSlot)
        {
            paramColumnMap[paramSlot.LocalIndex] = colInfo;
            return;
        }

        // Param wrapped in function call (e.g., LOWER(@p0))
        if (paramSide is FunctionCallExpr funcExpr)
        {
            foreach (var arg in funcExpr.Arguments)
            {
                if (arg is ParamSlotExpr funcParam)
                    paramColumnMap[funcParam.LocalIndex] = colInfo;
            }
        }
    }

    /// <summary>
    /// Strips dialect-specific quotes from a column name and looks it up in the column dictionary.
    /// </summary>
    private static ColumnInfo? LookupColumn(ResolvedColumnExpr colExpr, Dictionary<string, ColumnInfo> columnLookup)
    {
        var quoted = colExpr.QuotedColumnName;
        if (quoted.Length < 2)
            return null;

        // Strip quotes: "col" → col, `col` → col, [col] → col
        var first = quoted[0];
        string unquoted;
        if (first == '[')
            unquoted = quoted.Substring(1, quoted.Length - 2); // [col]
        else if (first == '"' || first == '`')
            unquoted = quoted.Substring(1, quoted.Length - 2); // "col" or `col`
        else
            unquoted = quoted;

        columnLookup.TryGetValue(unquoted, out var col);
        return col;
    }

    /// <summary>
    /// Resolves a navigation-aggregate column carrying an unbound <see cref="SubqueryExpr"/>
    /// (set by ProjectionAnalyzer when it recognizes <c>u.Orders.Sum(o =&gt; o.Total)</c> etc.).
    /// Binds the subquery against the owning entity, renders it to dialect SQL, and resolves
    /// the aggregate's CLR result type from the selector. Returns the fully-populated column,
    /// or null if binding fails (in which case <paramref name="diagnostics"/> receives QRY073).
    /// </summary>
    private static ProjectedColumn? ResolveProjectionSubqueryColumn(
        ProjectedColumn col,
        EntityRegistry registry,
        SqlDialect dialect,
        EntityInfo? primaryEntity,
        IReadOnlyDictionary<string, EntityInfo>? joinedEntities,
        IReadOnlyDictionary<string, string>? tableAliases,
        string? primaryParameterName,
        List<DiagnosticInfo>? diagnostics,
        DiagnosticLocation location)
    {
        if (col.SubqueryExpression is not SubqueryExpr subquery || col.OuterParameterName == null)
            return null;

        // Determine the outer entity that owns the navigation. For single-entity Select,
        // primaryEntity is the only candidate; for joined Select, look up via the parameter map.
        EntityInfo? outerEntity = null;
        string? lambdaParamForBind = primaryParameterName;
        if (primaryParameterName != null && col.OuterParameterName == primaryParameterName)
        {
            outerEntity = primaryEntity;
        }
        else if (joinedEntities != null && joinedEntities.TryGetValue(col.OuterParameterName, out var je))
        {
            outerEntity = je;
            lambdaParamForBind = col.OuterParameterName;
        }

        if (outerEntity == null || lambdaParamForBind == null)
        {
            EmitProjectionSubqueryDiagnostic(diagnostics, location, subquery);
            return null;
        }

        SqlExpr boundExpr;
        try
        {
            boundExpr = SqlExprBinder.Bind(
                subquery,
                outerEntity,
                dialect,
                lambdaParamForBind,
                joinedEntities: joinedEntities,
                tableAliases: tableAliases,
                inBooleanContext: false,
                entityLookup: registry.ByEntityName);
        }
        catch
        {
            EmitProjectionSubqueryDiagnostic(diagnostics, location, subquery);
            return null;
        }

        // SqlExprBinder.BindSubquery returns the original (unresolved) node when the
        // navigation can't be resolved on the outer entity. Detect that and bail out.
        if (boundExpr is SubqueryExpr boundSub && !boundSub.IsResolved)
        {
            EmitProjectionSubqueryDiagnostic(diagnostics, location, subquery);
            return null;
        }

        var renderedSql = SqlExprRenderer.Render(boundExpr, dialect);
        if (string.IsNullOrEmpty(renderedSql))
        {
            EmitProjectionSubqueryDiagnostic(diagnostics, location, subquery);
            return null;
        }

        var resolvedType = ResolveSubqueryResultType(subquery, outerEntity, registry);
        return col with
        {
            ColumnName = "",
            SqlExpression = renderedSql,
            ClrType = resolvedType,
            FullClrType = resolvedType,
            IsAggregateFunction = true,
            IsValueType = true,
            ReaderMethodName = TypeClassification.GetReaderMethod(resolvedType),
            Alias = col.PropertyName,
            SubqueryExpression = null,
            OuterParameterName = null,
        };
    }

    private static void EmitProjectionSubqueryDiagnostic(
        List<DiagnosticInfo>? diagnostics,
        DiagnosticLocation location,
        SubqueryExpr subquery)
    {
        if (diagnostics == null) return;
        diagnostics.Add(new DiagnosticInfo(
            DiagnosticDescriptors.ProjectionSubqueryUnresolved.Id,
            location,
            subquery.NavigationPropertyName,
            subquery.OuterParameterName,
            subquery.SubqueryKind.ToString()));
    }

    /// <summary>
    /// Resolves the CLR result type of an aggregate subquery against its target entity's
    /// column metadata. Falls back to per-aggregate defaults that mirror <c>Sql.cs</c>.
    /// </summary>
    private static string ResolveSubqueryResultType(
        SubqueryExpr subquery, EntityInfo outerEntity, EntityRegistry registry)
    {
        if (subquery.SubqueryKind == SubqueryKind.Count)
            return "int";

        var targetEntity = ResolveSubqueryTargetEntity(subquery, outerEntity, registry);
        var selectorType = TryResolveSelectorClrType(subquery.Selector, targetEntity);

        return subquery.SubqueryKind switch
        {
            SubqueryKind.Sum => selectorType ?? "decimal",
            SubqueryKind.Min => selectorType ?? "object",
            SubqueryKind.Max => selectorType ?? "object",
            // Sql.Avg(int)/Sql.Avg(long) → double; Sql.Avg(decimal) → decimal; Sql.Avg(double) → double.
            SubqueryKind.Avg => selectorType switch
            {
                "decimal" => "decimal",
                "double" => "double",
                _ => "double",
            },
            _ => selectorType ?? "object",
        };
    }

    /// <summary>
    /// Finds the target entity for a navigation-aggregate subquery. For HasManyThrough,
    /// the selector resolves against the through-target entity (e.g., Address), not the
    /// junction. For HasMany, the selector resolves against the directly related entity.
    /// </summary>
    private static EntityInfo? ResolveSubqueryTargetEntity(
        SubqueryExpr subquery, EntityInfo outerEntity, EntityRegistry registry)
    {
        foreach (var tn in outerEntity.ThroughNavigations)
        {
            if (tn.PropertyName == subquery.NavigationPropertyName)
                return registry.ByEntityName.TryGetValue(tn.TargetEntityName, out var t) ? t : null;
        }
        foreach (var nav in outerEntity.Navigations)
        {
            if (nav.PropertyName == subquery.NavigationPropertyName)
                return registry.ByEntityName.TryGetValue(nav.RelatedEntityName, out var t) ? t : null;
        }
        return null;
    }

    /// <summary>
    /// Walks an unbound aggregate selector down to the leaf <see cref="ColumnRefExpr"/>
    /// and looks up its CLR type in the target entity's columns. Returns null when the
    /// selector is missing, isn't a column reference, or the property is not on the target.
    /// </summary>
    private static string? TryResolveSelectorClrType(SqlExpr? selector, EntityInfo? targetEntity)
    {
        if (selector == null || targetEntity == null) return null;
        if (selector is not ColumnRefExpr colRef) return null;
        foreach (var col in targetEntity.Columns)
        {
            if (col.PropertyName == colRef.PropertyName)
                return col.ClrType;
        }
        return null;
    }

    /// <summary>
    /// Builds a SelectProjection from ProjectionInfo, enriching columns with entity metadata.
    /// During discovery, the source generator can't see its own generated entity types, so
    /// ProjectionInfo columns may have empty ClrType/ColumnName. We fix these by cross-referencing
    /// with EntityRef.Columns which has the authoritative column metadata from schema analysis.
    /// For multi-entity (joined) projections, resolves all joined entities from the registry.
    /// </summary>
    private static SelectProjection BuildProjection(
        ProjectionInfo projInfo,
        TranslatedCallSite executionSite,
        EntityRegistry registry,
        SqlDialect dialect,
        List<ImplicitJoinInfo> implicitJoins,
        IReadOnlyList<JoinPlan>? joinPlans = null,
        List<DiagnosticInfo>? diagnostics = null)
    {
        // Build column lookups for enrichment
        // For joined queries, build per-tableAlias lookups from all joined entities
        var joinedEntityTypeNames = executionSite.Bound.JoinedEntityTypeNames;
        var isJoined = joinedEntityTypeNames != null && joinedEntityTypeNames.Count >= 2;

        Dictionary<string, Dictionary<string, ColumnInfo>>? perAliasLookup = null;
        Dictionary<string, ColumnInfo>? entityColumnLookup = null;
        var entityRef = executionSite.Bound.Entity;

        if (isJoined)
        {
            // Multi-entity: build per-tableAlias column lookups
            perAliasLookup = new Dictionary<string, Dictionary<string, ColumnInfo>>(StringComparer.Ordinal);
            for (int i = 0; i < joinedEntityTypeNames!.Count; i++)
            {
                var alias = $"t{i}";
                var entry = registry.Resolve(joinedEntityTypeNames[i]);
                if (entry != null)
                {
                    var lookup = new Dictionary<string, ColumnInfo>(StringComparer.Ordinal);
                    foreach (var ec in EntityRef.FromEntityInfo(entry.Entity).Columns)
                        lookup[ec.PropertyName] = ec;
                    perAliasLookup[alias] = lookup;
                }
            }
        }
        else if (entityRef != null && entityRef.Columns.Count > 0)
        {
            // Single-entity: flat lookup
            entityColumnLookup = new Dictionary<string, ColumnInfo>(StringComparer.Ordinal);
            foreach (var ec in entityRef.Columns)
                entityColumnLookup[ec.PropertyName] = ec;
        }

        // Determine whether a table alias is on the nullable side of an outer join.
        // Cascading: a RIGHT/FULL OUTER join at position i makes all tables 0..i nullable.
        bool IsJoinNullable(string? tableAlias)
        {
            if (tableAlias == null || joinPlans == null || joinPlans.Count == 0)
                return false;
            int k = int.Parse(tableAlias.Substring(1)); // "t0" -> 0, "t1" -> 1, etc.
            // Right side of its own join: join[k-1] is Left or FullOuter
            if (k >= 1 && k - 1 < joinPlans.Count)
            {
                var kind = joinPlans[k - 1].Kind;
                if (kind == JoinClauseKind.Left || kind == JoinClauseKind.FullOuter)
                    return true;
            }
            // Left side of a later Right or FullOuter join
            for (int i = Math.Max(k, 0); i < joinPlans.Count; i++)
            {
                var kind = joinPlans[i].Kind;
                if (kind == JoinClauseKind.Right || kind == JoinClauseKind.FullOuter)
                    return true;
            }
            return false;
        }

        // Build entity-info maps for any navigation-aggregate subquery columns. Lazily
        // initialized so non-subquery projections pay no lookup cost.
        EntityInfo? primaryEntityForBind = null;
        Dictionary<string, EntityInfo>? joinedEntitiesForBind = null;
        Dictionary<string, string>? tableAliasesForBind = null;
        string? primaryParamForBind = null;
        var hasSubqueryColumn = false;
        if (projInfo.Columns != null)
        {
            foreach (var c in projInfo.Columns)
            {
                if (c.SubqueryExpression != null) { hasSubqueryColumn = true; break; }
            }
        }
        if (hasSubqueryColumn)
        {
            // Single-entity primary: look up EntityInfo via the chain's entity ref.
            if (entityRef != null && registry.ByEntityName.TryGetValue(entityRef.EntityName, out var primaryEi))
                primaryEntityForBind = primaryEi;
            var lambdaParams = projInfo.LambdaParameterNames;
            if (lambdaParams != null && lambdaParams.Count > 0)
            {
                primaryParamForBind = lambdaParams[0];
                if (isJoined && joinedEntityTypeNames != null)
                {
                    joinedEntitiesForBind = new Dictionary<string, EntityInfo>(StringComparer.Ordinal);
                    tableAliasesForBind = new Dictionary<string, string>(StringComparer.Ordinal);
                    var count = System.Math.Min(lambdaParams.Count, joinedEntityTypeNames.Count);
                    for (int i = 0; i < count; i++)
                    {
                        if (registry.ByEntityName.TryGetValue(joinedEntityTypeNames[i], out var je))
                        {
                            joinedEntitiesForBind[lambdaParams[i]] = je;
                            tableAliasesForBind[lambdaParams[i]] = je.TableName;
                        }
                    }
                    // Primary entity for binding is t0; ensure it points at the joined entry.
                    if (joinedEntitiesForBind.TryGetValue(primaryParamForBind, out var firstJe))
                        primaryEntityForBind = firstJe;
                }
            }
        }

        var columns = new List<ProjectedColumn>();
        if (projInfo.Columns != null)
        {
            foreach (var rawCol in projInfo.Columns)
            {
                // SqlExpression now uses canonical {identifier} placeholders — dialect
                // quoting is deferred to render time (SqlFormatting.QuoteSqlExpression).
                var col = rawCol;

                // Navigation-aggregate subquery in projection (e.g., u.Orders.Sum(o => o.Total)).
                // Bind correlation/table/alias against the owning entity, render to SQL, and
                // resolve the aggregate's CLR result type. Issue #257.
                if (col.SubqueryExpression != null)
                {
                    var resolvedSubCol = ResolveProjectionSubqueryColumn(
                        col, registry, dialect,
                        primaryEntityForBind, joinedEntitiesForBind, tableAliasesForBind,
                        primaryParamForBind, diagnostics, executionSite.Location);
                    if (resolvedSubCol != null)
                    {
                        columns.Add(resolvedSubCol);
                        continue;
                    }
                    // Binding failed; QRY073 already emitted. Fall through to the existing
                    // empty-column emit so downstream stages don't crash on a half-formed column.
                    columns.Add(col with
                    {
                        ClrType = "object",
                        FullClrType = "object",
                        IsAggregateFunction = true,
                        SubqueryExpression = null,
                        OuterParameterName = null,
                    });
                    continue;
                }

                // Aggregate columns with unresolved type ("object"): resolve from the
                // referenced entity column. During discovery, Min/Max default to "object"
                // because the semantic model can't resolve generated entity property types.
                if (col.IsAggregateFunction && TypeClassification.IsUnresolvedTypeName(col.ClrType) && col.SqlExpression != null)
                {
                    var resolvedType = TryResolveAggregateTypeFromSql(col.SqlExpression, entityColumnLookup, perAliasLookup, col.TableAlias);
                    if (resolvedType != null)
                    {
                        columns.Add(col with
                        {
                            ClrType = resolvedType,
                            FullClrType = resolvedType,
                            IsAggregateFunction = true,
                            IsValueType = true,
                            ReaderMethodName = TypeClassification.GetReaderMethod(resolvedType),
                        });
                        continue;
                    }
                }

                // Navigation column enrichment: resolve through One<T> chain
                if (col.NavigationHops != null && col.NavigationHops.Count > 0 && entityRef != null)
                {
                    var resolved = ResolveNavigationColumn(
                        col, entityRef, registry, dialect, implicitJoins,
                        diagnostics, executionSite.Location);
                    if (resolved != null)
                    {
                        columns.Add(resolved);
                        continue;
                    }
                }

                if (NeedsEnrichment(col))
                {
                    ColumnInfo? entityCol = null;

                    if (isJoined && perAliasLookup != null && col.TableAlias != null)
                    {
                        // Multi-entity: match by TableAlias + PropertyName, then ColumnName
                        if (perAliasLookup.TryGetValue(col.TableAlias, out var aliasLookup))
                        {
                            if (!aliasLookup.TryGetValue(col.PropertyName, out entityCol)
                                && !string.IsNullOrEmpty(col.ColumnName))
                            {
                                // Named tuple elements have PropertyName != entity property name
                                // (e.g. "Id" vs "UserId"), so fall back to ColumnName which
                                // stores the original entity member name from the expression.
                                aliasLookup.TryGetValue(col.ColumnName, out entityCol);
                            }
                        }
                    }
                    else if (entityColumnLookup != null)
                    {
                        // Single-entity: match by PropertyName, then ColumnName
                        if (!entityColumnLookup.TryGetValue(col.PropertyName, out entityCol)
                            && !string.IsNullOrEmpty(col.ColumnName))
                        {
                            // Named tuple elements have PropertyName != entity property name
                            // (e.g. "Id" vs "UserId"), so fall back to ColumnName which
                            // stores the original entity member name from the expression.
                            entityColumnLookup.TryGetValue(col.ColumnName, out entityCol);
                        }
                    }

                    if (entityCol != null)
                    {
                        columns.Add(col with
                        {
                            ColumnName = entityCol.ColumnName,
                            ClrType = TypeClassification.IsUnresolvedTypeName(col.ClrType) ? entityCol.ClrType : col.ClrType,
                            FullClrType = TypeClassification.IsUnresolvedTypeName(col.FullClrType) ? entityCol.FullClrType : col.FullClrType,
                            IsNullable = entityCol.IsNullable,
                            CustomTypeMapping = entityCol.CustomTypeMappingClass ?? col.CustomTypeMapping,
                            IsValueType = entityCol.IsValueType,
                            ReaderMethodName = entityCol.DbReaderMethodName ?? entityCol.ReaderMethodName,
                            IsForeignKey = entityCol.Kind == ColumnKind.ForeignKey,
                            ForeignKeyEntityName = entityCol.ReferencedEntityName,
                            IsEnum = entityCol.IsEnum,
                            IsJoinNullable = IsJoinNullable(col.TableAlias),
                        });
                        continue;
                    }
                }
                // Apply join-nullable override for unenriched columns
                if (!col.IsJoinNullable && IsJoinNullable(col.TableAlias))
                {
                    columns.Add(col with { IsJoinNullable = true });
                }
                else
                {
                    columns.Add(col);
                }
            }
        }

        // Joined entity projection: populate all columns from the entity at the given alias.
        // At discovery time, the column lookup is empty (no EntityInfo available), so
        // JoinedEntityAlias signals that we need to create the full column list here.
        if (projInfo.JoinedEntityAlias != null && columns.Count == 0 && isJoined && perAliasLookup != null)
        {
            var aliasIndex = int.Parse(projInfo.JoinedEntityAlias.Substring(1));
            if (aliasIndex >= 0 && aliasIndex < joinedEntityTypeNames!.Count)
            {
                var entry = registry.Resolve(joinedEntityTypeNames[aliasIndex]);
                if (entry != null)
                {
                    var ordinal = 0;
                    var entityJoinNullable = IsJoinNullable(projInfo.JoinedEntityAlias);
                    foreach (var col in EntityRef.FromEntityInfo(entry.Entity).Columns)
                    {
                        columns.Add(new ProjectedColumn(
                            propertyName: col.PropertyName,
                            columnName: col.ColumnName,
                            clrType: col.ClrType,
                            fullClrType: col.FullClrType,
                            isNullable: col.IsNullable,
                            ordinal: ordinal++,
                            tableAlias: projInfo.JoinedEntityAlias,
                            readerMethodName: col.DbReaderMethodName ?? col.ReaderMethodName,
                            isValueType: col.IsValueType,
                            customTypeMapping: col.CustomTypeMappingClass,
                            isForeignKey: col.Kind == ColumnKind.ForeignKey,
                            foreignKeyEntityName: col.ReferencedEntityName,
                            isEnum: col.IsEnum,
                            isJoinNullable: entityJoinNullable));
                    }
                }
            }
        }

        // Rebuild result type name from enriched columns
        var resultTypeName = projInfo.ResultTypeName ?? executionSite.Bound.Raw.ResultTypeName ?? executionSite.Bound.Raw.EntityTypeName;
        if (projInfo.Kind == ProjectionKind.Tuple && columns.Count > 0)
        {
            var rebuilt = TypeClassification.BuildTupleTypeName(columns, fallbackToObject: false);
            if (!string.IsNullOrEmpty(rebuilt))
                resultTypeName = rebuilt;
        }
        else if (projInfo.Kind == ProjectionKind.SingleColumn && columns.Count == 1)
        {
            var col = columns[0];
            var colType = !string.IsNullOrWhiteSpace(col.ClrType) ? col.ClrType : col.FullClrType;
            if (!string.IsNullOrWhiteSpace(colType) && colType != "?" && colType != "object")
            {
                if (col.IsNullable && !colType.EndsWith("?"))
                    colType += "?";
                resultTypeName = colType;
            }
        }
        // Resolve result type for joined entity projection from alias
        if (TypeClassification.IsUnresolvedTypeName(resultTypeName) && projInfo.JoinedEntityAlias != null && isJoined)
        {
            var aliasIndex = int.Parse(projInfo.JoinedEntityAlias.Substring(1));
            if (aliasIndex >= 0 && aliasIndex < joinedEntityTypeNames!.Count)
                resultTypeName = joinedEntityTypeNames[aliasIndex];
        }
        // Fix unresolved "?" result type by checking enriched columns
        if (TypeClassification.IsUnresolvedTypeName(resultTypeName) && columns.Count > 0)
        {
            if (columns.Count == 1)
            {
                var col = columns[0];
                var colType = !string.IsNullOrWhiteSpace(col.ClrType) ? col.ClrType : col.FullClrType;
                if (!string.IsNullOrWhiteSpace(colType) && colType != "?")
                    resultTypeName = col.IsNullable && !colType.EndsWith("?") ? colType + "?" : colType;
            }
            else
            {
                var rebuilt = TypeClassification.BuildTupleTypeName(columns, fallbackToObject: false);
                if (!string.IsNullOrEmpty(rebuilt))
                    resultTypeName = rebuilt;
            }
        }

        return new SelectProjection(
            kind: projInfo.Kind,
            resultTypeName: resultTypeName,
            columns: columns,
            customEntityReaderClass: projInfo.CustomEntityReaderClass ?? entityRef?.CustomEntityReaderClass,
            isIdentity: projInfo.Kind == ProjectionKind.Entity);
    }

    /// <summary>
    /// Checks if a projected column needs enrichment (has missing type or column name info).
    /// </summary>
    private static bool NeedsEnrichment(ProjectedColumn col)
    {
        // Aggregates have empty ColumnName by design — don't trigger enrichment for them
        if (col.IsAggregateFunction)
            return TypeClassification.IsUnresolvedTypeName(col.ClrType);
        return TypeClassification.IsUnresolvedTypeName(col.ClrType)
            || TypeClassification.IsUnresolvedTypeName(col.FullClrType)
            || string.IsNullOrWhiteSpace(col.ColumnName);
    }

    /// <summary>
    /// Resolves a navigation column by walking the One&lt;T&gt; chain, creating or reusing
    /// implicit joins for each hop, and looking up the final column on the terminal entity.
    /// </summary>
    private static ProjectedColumn? ResolveNavigationColumn(
        ProjectedColumn col,
        EntityRef sourceEntity,
        EntityRegistry registry,
        SqlDialect dialect,
        List<ImplicitJoinInfo> implicitJoins,
        List<DiagnosticInfo>? diagnostics = null,
        DiagnosticLocation location = default)
    {
        if (col.NavigationHops == null) return null;

        var currentEntity = sourceEntity;
        string currentAlias = "t0"; // primary entity alias when implicit joins exist
        int aliasCounter = implicitJoins.Count; // continue from existing aliases

        foreach (var hop in col.NavigationHops)
        {
            // Find the SingleNavigationInfo for this hop
            SingleNavigationInfo? nav = null;
            foreach (var sn in currentEntity.SingleNavigations)
            {
                if (sn.PropertyName == hop) { nav = sn; break; }
            }
            if (nav == null) return null;

            // Resolve target entity from registry
            var targetEntry = registry.Resolve(nav.TargetEntityName);
            if (targetEntry == null)
            {
                diagnostics?.Add(new DiagnosticInfo(
                    "QRY063",
                    location,
                    hop, currentEntity.EntityName, nav.TargetEntityName));
                return null;
            }
            var targetRef = EntityRef.FromEntityInfo(targetEntry.Entity);

            // Create or reuse implicit join
            var joinInfo = ImplicitJoinHelper.CreateOrReuse(
                nav, currentEntity, currentAlias, targetRef,
                dialect, implicitJoins, ref aliasCounter);
            if (joinInfo == null) return null;

            currentEntity = targetRef;
            currentAlias = joinInfo.TargetAlias;
        }

        // Resolve the final property on the terminal entity
        ColumnInfo? targetCol = null;
        foreach (var c in currentEntity.Columns)
        {
            if (c.PropertyName == col.ColumnName) { targetCol = c; break; }
        }
        if (targetCol == null) return null;

        return new ProjectedColumn(
            propertyName: col.PropertyName,
            columnName: targetCol.ColumnName,
            clrType: targetCol.ClrType,
            fullClrType: targetCol.FullClrType,
            isNullable: targetCol.IsNullable,
            ordinal: col.Ordinal,
            customTypeMapping: targetCol.CustomTypeMappingClass,
            isValueType: targetCol.IsValueType,
            readerMethodName: targetCol.DbReaderMethodName ?? targetCol.ReaderMethodName,
            tableAlias: currentAlias,
            isForeignKey: targetCol.Kind == ColumnKind.ForeignKey,
            foreignKeyEntityName: targetCol.ReferencedEntityName,
            isEnum: targetCol.IsEnum);
    }

    /// <summary>
    /// Public entry point for resolving aggregate type from SQL (used by bridge enrichment).
    /// </summary>
    internal static string? TryResolveAggregateTypeFromSqlPublic(
        string sqlExpression,
        Dictionary<string, ColumnInfo> entityColumnLookup)
    {
        return TryResolveAggregateTypeFromSql(sqlExpression, entityColumnLookup, null, null);
    }

    /// <summary>
    /// Public entry point for getting reader method (used by bridge enrichment).
    /// </summary>
    internal static string GetReaderMethodForTypePublic(string clrType)
    {
        return TypeClassification.GetReaderMethod(clrType);
    }

    /// <summary>
    /// Tries to resolve the CLR type for an aggregate column by extracting the referenced
    /// column name from the SQL expression and looking it up in entity column metadata.
    /// E.g., SUM({Total}) → extract "Total" → look up Total column → type is "decimal".
    /// </summary>
    private static string? TryResolveAggregateTypeFromSql(
        string sqlExpression,
        Dictionary<string, ColumnInfo>? entityColumnLookup,
        Dictionary<string, Dictionary<string, ColumnInfo>>? perAliasLookup,
        string? tableAlias)
    {
        // Extract the column name from canonical {identifier} expressions like:
        // SUM({Total}), MIN({t0}.{Total})
        var propName = ExtractColumnNameFromAggregateSql(sqlExpression);
        if (propName == null)
            return null;

        ColumnInfo? col = null;
        if (tableAlias != null && perAliasLookup != null)
        {
            if (perAliasLookup.TryGetValue(tableAlias, out var aliasLookup))
                aliasLookup.TryGetValue(propName, out col);
        }
        else if (entityColumnLookup != null)
        {
            entityColumnLookup.TryGetValue(propName, out col);
        }

        if (col != null && !string.IsNullOrWhiteSpace(col.ClrType) && col.ClrType != "object")
            return col.ClrType;

        return null;
    }

    /// <summary>
    /// Extracts a column/property name from an aggregate SQL expression that uses
    /// canonical <c>{identifier}</c> placeholders.
    /// E.g. <c>SUM({Total})</c> → <c>Total</c>, <c>MIN({t0}.{Total})</c> → <c>Total</c>.
    /// For COUNT(*), returns null.
    /// </summary>
    private static string? ExtractColumnNameFromAggregateSql(string sql)
    {
        // Skip COUNT(*)
        if (sql.Contains("*"))
            return null;

        // For window functions like "LAG({Total}) OVER (ORDER BY {Date})",
        // extract from the function arguments only (before " OVER ("), not from
        // the OVER clause which contains ORDER BY / PARTITION BY column names.
        var searchRegion = sql;
        var overIndex = sql.IndexOf(" OVER (", StringComparison.Ordinal);
        if (overIndex >= 0)
            searchRegion = sql.Substring(0, overIndex);

        // Find the last {identifier} placeholder (rightmost = the column, not the alias)
        int lastOpen = searchRegion.LastIndexOf('{');
        if (lastOpen < 0)
            return null;

        int close = searchRegion.IndexOf('}', lastOpen + 1);
        if (close <= lastOpen + 1)
            return null;

        return searchRegion.Substring(lastOpen + 1, close - lastOpen - 1);
    }

    /// <summary>
    /// Checks for disqualifying conditions from RawCallSite flags.
    /// </summary>
    private static string? CheckDisqualifiers(List<TranslatedCallSite> chainSites)
    {
        // Chains entirely inside a loop body are allowed: SQL shape is constant across
        // iterations, only parameter values change, and those are read from captures at
        // execution time. However, chains that CROSS a loop boundary (some sites inside,
        // some outside) are not analyzable.
        bool anyInLoop = false, anyOutsideLoop = false;
        foreach (var site in chainSites)
        {
            var raw = site.Bound.Raw;
            if (raw.IsInsideLoop) anyInLoop = true;
            else anyOutsideLoop = true;
            if (raw.IsCapturedInLambda)
                return "Chain variable captured in a lambda expression";
            if (raw.IsPassedAsArgument)
                return "Chain variable passed as argument to non-Quarry method or captured in lambda";
            if (raw.IsAssignedFromNonQuarryMethod)
                return "Chain variable assigned from non-Quarry method";
        }
        if (anyInLoop && anyOutsideLoop)
            return "Chain crosses a loop boundary (some clauses inside loop, some outside)";
        return null;
    }


    /// <summary>
    /// Determines the QueryKind from the execution terminal's InterceptorKind.
    /// </summary>
    private static QueryKind DetermineQueryKind(InterceptorKind kind, BuilderKind builderKind)
    {
        if (kind == InterceptorKind.InsertExecuteNonQuery ||
            kind == InterceptorKind.InsertExecuteScalar ||
            kind == InterceptorKind.InsertToDiagnostics)
            return QueryKind.Insert;

        if (kind == InterceptorKind.BatchInsertExecuteNonQuery ||
            kind == InterceptorKind.BatchInsertExecuteScalar ||
            kind == InterceptorKind.BatchInsertToDiagnostics)
            return QueryKind.BatchInsert;

        return builderKind switch
        {
            BuilderKind.Delete or BuilderKind.ExecutableDelete => QueryKind.Delete,
            BuilderKind.Update or BuilderKind.ExecutableUpdate => QueryKind.Update,
            BuilderKind.Insert => QueryKind.Insert,
            BuilderKind.BatchInsert or BuilderKind.ExecutableBatchInsert => QueryKind.BatchInsert,
            _ => QueryKind.Select
        };
    }

    /// <summary>
    /// Enumerates all possible ClauseMask values from conditional terms and branch groups.
    /// </summary>
    private static IReadOnlyList<int> EnumerateMaskCombinations(
        List<ConditionalTerm> conditionalTerms,
        Dictionary<string, List<(TranslatedCallSite Site, int BitIndex)>> branchGroups,
        List<TranslatedCallSite> clauseSites)
    {
        if (conditionalTerms.Count == 0)
            return new[] { 0 };

        var independentBits = new List<int>();
        var exclusiveGroups = new List<List<int>>();

        foreach (var kvp in branchGroups)
        {
            var group = kvp.Value;
            // Determine if this branch group is mutually exclusive
            var hasMutuallyExclusive = group.Any(g =>
                g.Site.Bound.Raw.NestingContext?.BranchKind == BranchKind.MutuallyExclusive);

            if (hasMutuallyExclusive && group.Count >= 2)
            {
                exclusiveGroups.Add(group.Select(g => g.BitIndex).ToList());
            }
            else
            {
                independentBits.AddRange(group.Select(g => g.BitIndex));
            }
        }

        // Build combinations
        var masks = new List<int> { 0 };

        // Independent bits: each can be on or off
        foreach (var bit in independentBits)
        {
            var newMasks = new List<int>(masks.Count * 2);
            foreach (var mask in masks)
            {
                newMasks.Add(mask);                      // bit off
                newMasks.Add(mask | (1 << bit));         // bit on
            }
            masks = newMasks;
        }

        // Mutually exclusive groups: exactly one bit from the group is set
        foreach (var group in exclusiveGroups)
        {
            var newMasks = new List<int>(masks.Count * group.Count);
            foreach (var mask in masks)
            {
                foreach (var bit in group)
                {
                    newMasks.Add(mask | (1 << bit));
                }
            }
            masks = newMasks;
        }

        return masks;
    }

    /// <summary>
    /// Creates a RuntimeBuild (tier 3) result.
    /// </summary>
    private static AnalyzedChain MakeRuntimeBuildChain(
        TranslatedCallSite executionSite,
        List<TranslatedCallSite> clauseSites,
        string reason,
        EntityRegistry? registry = null,
        bool isTraced = false,
        string? forkedVariableName = null)
    {
        var primaryTable = new TableRef(
            executionSite.Bound.TableName,
            executionSite.Bound.SchemaName);
        var queryKind = DetermineQueryKind(executionSite.Bound.Raw.Kind, executionSite.Bound.Raw.BuilderKind);

        // Even for runtime chains, enrich the Select projection so emitters can produce
        // concrete-typed interceptors (required for C# interceptor signature matching)
        SelectProjection? projection = null;
        if (registry != null)
        {
            foreach (var site in clauseSites)
            {
                if (site.Bound.Raw.Kind == InterceptorKind.Select && site.Bound.Raw.ProjectionInfo != null)
                {
                    var runtimeImplicitJoins = new List<ImplicitJoinInfo>();
                    projection = BuildProjection(site.Bound.Raw.ProjectionInfo, executionSite, registry,
                        site.Bound.Dialect, runtimeImplicitJoins);
                    break;
                }
            }
        }
        if (projection == null)
        {
            projection = new SelectProjection(
                ProjectionKind.Entity,
                executionSite.Bound.Raw.ResultTypeName ?? executionSite.Bound.Raw.EntityTypeName,
                Array.Empty<ProjectedColumn>(),
                isIdentity: true);
        }

        var plan = new QueryPlan(
            kind: queryKind,
            primaryTable: primaryTable,
            joins: Array.Empty<JoinPlan>(),
            whereTerms: Array.Empty<WhereTerm>(),
            orderTerms: Array.Empty<OrderTerm>(),
            groupByExprs: Array.Empty<SqlExpr>(),
            havingExprs: Array.Empty<SqlExpr>(),
            projection: projection,
            pagination: null,
            isDistinct: false,
            setTerms: Array.Empty<SetTerm>(),
            insertColumns: Array.Empty<InsertColumn>(),
            conditionalTerms: Array.Empty<ConditionalTerm>(),
            possibleMasks: Array.Empty<int>(),
            parameters: Array.Empty<QueryParameter>(),
            tier: OptimizationTier.RuntimeBuild,
            notAnalyzableReason: reason,
            forkedVariableName: forkedVariableName);

        return new AnalyzedChain(plan, executionSite, clauseSites, isTraced);
    }

    /// <summary>
    /// Maps an InterceptorKind to a ClauseRole.
    /// Returns null for kinds that are not clause roles (e.g., execution methods).
    /// </summary>
    internal static ClauseRole? MapInterceptorKindToClauseRole(InterceptorKind kind)
    {
        return kind switch
        {
            InterceptorKind.Select => ClauseRole.Select,
            InterceptorKind.Where => ClauseRole.Where,
            InterceptorKind.OrderBy => ClauseRole.OrderBy,
            InterceptorKind.ThenBy => ClauseRole.ThenBy,
            InterceptorKind.GroupBy => ClauseRole.GroupBy,
            InterceptorKind.Having => ClauseRole.Having,
            InterceptorKind.Join => ClauseRole.Join,
            InterceptorKind.LeftJoin => ClauseRole.Join,
            InterceptorKind.RightJoin => ClauseRole.Join,
            InterceptorKind.CrossJoin => ClauseRole.Join,
            InterceptorKind.FullOuterJoin => ClauseRole.Join,
            InterceptorKind.Set => ClauseRole.Set,
            InterceptorKind.DeleteWhere => ClauseRole.DeleteWhere,
            InterceptorKind.UpdateSet => ClauseRole.UpdateSet,
            InterceptorKind.UpdateSetAction => ClauseRole.UpdateSet,
            InterceptorKind.UpdateSetPoco => ClauseRole.UpdateSet,
            InterceptorKind.UpdateWhere => ClauseRole.UpdateWhere,
            InterceptorKind.Limit => ClauseRole.Limit,
            InterceptorKind.Offset => ClauseRole.Offset,
            InterceptorKind.Distinct => ClauseRole.Distinct,
            InterceptorKind.WithTimeout => ClauseRole.WithTimeout,
            InterceptorKind.ChainRoot => ClauseRole.ChainRoot,
            InterceptorKind.DeleteTransition => ClauseRole.DeleteTransition,
            InterceptorKind.UpdateTransition => ClauseRole.UpdateTransition,
            InterceptorKind.AllTransition => ClauseRole.AllTransition,
            InterceptorKind.InsertTransition => ClauseRole.InsertTransition,
            InterceptorKind.BatchInsertColumnSelector => ClauseRole.InsertTransition,
            InterceptorKind.BatchInsertValues => ClauseRole.BatchInsertValues,
            InterceptorKind.Union => ClauseRole.SetOperation,
            InterceptorKind.UnionAll => ClauseRole.SetOperation,
            InterceptorKind.Intersect => ClauseRole.SetOperation,
            InterceptorKind.IntersectAll => ClauseRole.SetOperation,
            InterceptorKind.Except => ClauseRole.SetOperation,
            InterceptorKind.ExceptAll => ClauseRole.SetOperation,
            InterceptorKind.CteDefinition => ClauseRole.CteDefinition,
            InterceptorKind.FromCte => ClauseRole.FromCte,
            _ => null
        };
    }

    /// <summary>
    /// Checks if an InterceptorKind represents an execution method.
    /// </summary>
    internal static bool IsExecutionKind(InterceptorKind kind)
    {
        return kind is InterceptorKind.ExecuteFetchAll
            or InterceptorKind.ExecuteFetchFirst
            or InterceptorKind.ExecuteFetchFirstOrDefault
            or InterceptorKind.ExecuteFetchSingle
            or InterceptorKind.ExecuteFetchSingleOrDefault
            or InterceptorKind.ExecuteScalar
            or InterceptorKind.ExecuteNonQuery
            or InterceptorKind.ToAsyncEnumerable
            or InterceptorKind.ToDiagnostics
            or InterceptorKind.InsertExecuteNonQuery
            or InterceptorKind.InsertExecuteScalar
            or InterceptorKind.InsertToDiagnostics
            or InterceptorKind.BatchInsertExecuteNonQuery
            or InterceptorKind.BatchInsertExecuteScalar
            or InterceptorKind.BatchInsertToDiagnostics;
    }

    /// <summary>
    /// Analyzes an operand chain (right-hand side of a set operation) that has no terminal.
    /// Builds a SELECT QueryPlan from the chain's clause sites.
    /// </summary>
    private static QueryPlan? AnalyzeOperandChain(
        List<TranslatedCallSite> chainSites,
        EntityRegistry registry,
        CancellationToken ct)
    {
        // Find the chain root and collect clause sites
        TranslatedCallSite? rootSite = null;
        var clauseSites = new List<TranslatedCallSite>();

        foreach (var site in chainSites)
        {
            var kind = site.Bound.Raw.Kind;
            if (kind == InterceptorKind.ChainRoot)
            {
                rootSite = site;
            }
            else if (kind == InterceptorKind.Trace)
            {
                // Skip trace markers
            }
            else if (!IsExecutionKind(kind))
            {
                clauseSites.Add(site);
            }
        }

        // Need at least a chain root to determine the entity/table
        if (rootSite == null)
        {
            // No chain root — try to use the first clause site's entity info
            if (chainSites.Count == 0) return null;
            rootSite = chainSites[0];
        }

        // Sort clause sites by source location
        clauseSites.Sort((a, b) =>
        {
            var cmp = a.Bound.Raw.Line.CompareTo(b.Bound.Raw.Line);
            if (cmp != 0) return cmp;
            return a.Bound.Raw.Column.CompareTo(b.Bound.Raw.Column);
        });

        var primaryTable = new TableRef(
            rootSite.Bound.TableName,
            rootSite.Bound.SchemaName);

        var whereTerms = new List<WhereTerm>();
        var orderTerms = new List<OrderTerm>();
        var groupByExprs = new List<SqlExpr>();
        var havingExprs = new List<SqlExpr>();
        var joinPlans = new List<JoinPlan>();
        var implicitJoinInfos = new List<ImplicitJoinInfo>();
        var parameters = new List<QueryParameter>();
        var paramGlobalIndex = 0;
        bool isDistinct = false;
        SelectProjection? projection = null;

        foreach (var site in clauseSites)
        {
            ct.ThrowIfCancellationRequested();
            var clause = site.Clause;
            var raw = site.Bound.Raw;
            var kind = raw.Kind;

            if (clause != null && clause.IsSuccess)
            {
                var clauseParams = RemapParameters(clause.Parameters, ref paramGlobalIndex);
                EnrichParametersFromColumns(clauseParams, clause.ResolvedExpression, rootSite.Bound.Entity, null);
                parameters.AddRange(clauseParams);

                switch (clause.Kind)
                {
                    case ClauseKind.Where:
                        whereTerms.Add(new WhereTerm(clause.ResolvedExpression));
                        break;
                    case ClauseKind.OrderBy:
                        orderTerms.Add(new OrderTerm(clause.ResolvedExpression, clause.IsDescending));
                        break;
                    case ClauseKind.GroupBy:
                        groupByExprs.Add(clause.ResolvedExpression);
                        break;
                    case ClauseKind.Having:
                        havingExprs.Add(clause.ResolvedExpression);
                        break;
                    case ClauseKind.Join:
                        var joinTable = new TableRef(clause.JoinedTableName ?? "", clause.JoinedSchemaName, clause.TableAlias);
                        var joinKind = clause.JoinKind ?? JoinClauseKind.Inner;
                        var onCondition = joinKind == JoinClauseKind.Cross ? null : (SqlExpr?)clause.ResolvedExpression;
                        joinPlans.Add(new JoinPlan(joinKind, joinTable, onCondition, raw.IsNavigationJoin));
                        break;
                }

                if (clause.ImplicitJoins != null)
                {
                    foreach (var ij in clause.ImplicitJoins)
                    {
                        if (!implicitJoinInfos.Any(existing => existing.TargetAlias == ij.TargetAlias))
                            implicitJoinInfos.Add(ij);
                    }
                }
            }
            else if (kind == InterceptorKind.Distinct)
            {
                isDistinct = true;
            }
            else if (kind == InterceptorKind.Select && raw.ProjectionInfo != null)
            {
                if (raw.ProjectionInfo.FailureReason == ProjectionFailureReason.None)
                {
                    projection = BuildProjection(raw.ProjectionInfo, rootSite, registry,
                        rootSite.Bound.Dialect, implicitJoinInfos, null);

                    if (raw.ProjectionInfo.ProjectionParameters is { Count: > 0 } projParams
                        && projection != null)
                    {
                        projection = RemapProjectionParameters(projection, projParams, parameters, ref paramGlobalIndex);
                    }
                }
            }
        }

        // Default identity projection
        if (projection == null)
        {
            projection = new SelectProjection(
                ProjectionKind.Entity,
                rootSite.Bound.Raw.ResultTypeName ?? rootSite.Bound.Raw.EntityTypeName,
                Array.Empty<ProjectedColumn>(),
                isIdentity: true);
            projection = EnrichIdentityProjectionWithEntityColumns(projection, rootSite.Bound.Entity);
        }

        return new QueryPlan(
            kind: QueryKind.Select,
            primaryTable: primaryTable,
            joins: joinPlans,
            whereTerms: whereTerms,
            orderTerms: orderTerms,
            groupByExprs: groupByExprs,
            havingExprs: havingExprs,
            projection: projection,
            pagination: null,
            isDistinct: isDistinct,
            setTerms: Array.Empty<SetTerm>(),
            insertColumns: Array.Empty<InsertColumn>(),
            conditionalTerms: Array.Empty<ConditionalTerm>(),
            possibleMasks: new[] { 0 },
            parameters: parameters,
            tier: OptimizationTier.PrebuiltDispatch,
            implicitJoins: implicitJoinInfos.Count > 0 ? implicitJoinInfos : null);
    }

    /// <summary>
    /// Enriches an identity projection with entity columns from the EntityRef metadata.
    /// Replaces the empty column list with full column definitions so SqlAssembler renders
    /// explicit column names instead of SELECT *.
    /// </summary>
    private static SelectProjection EnrichIdentityProjectionWithEntityColumns(
        SelectProjection projection,
        EntityRef? entityRef)
    {
        if (entityRef == null || entityRef.Columns.Count == 0)
            return projection;

        var entityCols = new List<ProjectedColumn>();
        var ord = 0;
        foreach (var ec in entityRef.Columns)
        {
            entityCols.Add(new ProjectedColumn(
                propertyName: ec.PropertyName,
                columnName: ec.ColumnName,
                clrType: ec.ClrType,
                fullClrType: ec.FullClrType,
                isNullable: ec.IsNullable,
                ordinal: ord++,
                customTypeMapping: ec.CustomTypeMappingClass,
                isValueType: ec.IsValueType,
                readerMethodName: ec.DbReaderMethodName ?? ec.ReaderMethodName,
                isForeignKey: ec.Kind == ColumnKind.ForeignKey,
                foreignKeyEntityName: ec.ReferencedEntityName,
                isEnum: ec.IsEnum));
        }
        return new SelectProjection(
            projection.Kind,
            projection.ResultTypeName,
            entityCols,
            customEntityReaderClass: entityRef.CustomEntityReaderClass,
            isIdentity: true);
    }

    /// <summary>
    /// Checks if an InterceptorKind represents a set operation (Union, Intersect, Except, etc.).
    /// </summary>
    internal static bool IsSetOperationKind(InterceptorKind kind)
    {
        return kind is InterceptorKind.Union
            or InterceptorKind.UnionAll
            or InterceptorKind.Intersect
            or InterceptorKind.IntersectAll
            or InterceptorKind.Except
            or InterceptorKind.ExceptAll;
    }

    /// <summary>
    /// Maps an InterceptorKind to the corresponding SetOperatorKind.
    /// </summary>
    private static SetOperatorKind MapToSetOperatorKind(InterceptorKind kind)
    {
        return kind switch
        {
            InterceptorKind.Union => SetOperatorKind.Union,
            InterceptorKind.UnionAll => SetOperatorKind.UnionAll,
            InterceptorKind.Intersect => SetOperatorKind.Intersect,
            InterceptorKind.IntersectAll => SetOperatorKind.IntersectAll,
            InterceptorKind.Except => SetOperatorKind.Except,
            InterceptorKind.ExceptAll => SetOperatorKind.ExceptAll,
            _ => throw new InvalidOperationException($"Not a set operation kind: {kind}")
        };
    }

    /// <summary>
    /// Logs retroactive discovery/binding/translation trace for a single site.
    /// Called from ChainAnalyzer when a traced chain is detected, reconstructing
    /// trace data from the TranslatedCallSite objects already on hand.
    /// </summary>
    private static void LogSiteTrace(string chainUid, TranslatedCallSite site)
    {
        var raw = site.Bound.Raw;
        var log = IR.TraceCapture.Log;

        // ── Discovery ──
        log(chainUid, $"[Trace] Discovery ({raw.MethodName} at line {raw.Line}):");
        log(chainUid, $"  kind={raw.Kind}, builderKind={raw.BuilderKind}, isAnalyzable={raw.IsAnalyzable}");
        log(chainUid, $"  chainId={raw.ChainId ?? "(null)"}, uniqueId={raw.UniqueId}");
        log(chainUid, $"  builderType={raw.BuilderTypeName}, entityType={raw.EntityTypeName}");
        if (raw.ResultTypeName != null)
            log(chainUid, $"  resultType={raw.ResultTypeName}");
        if (raw.Expression != null)
            log(chainUid, $"  parsedExpr={FormatExpr(raw.Expression)}");
        if (raw.ClauseKind.HasValue)
            log(chainUid, $"  clauseKind={raw.ClauseKind.Value}, isDescending={raw.IsDescending}");
        if (raw.JoinedEntityTypeName != null)
            log(chainUid, $"  joinedEntityType={raw.JoinedEntityTypeName}, isNavigationJoin={raw.IsNavigationJoin}");
        if (raw.JoinedEntityTypeNames != null)
            log(chainUid, $"  joinedEntityTypes=[{string.Join(", ", raw.JoinedEntityTypeNames)}]");
        if (raw.NestingContext != null)
            log(chainUid, $"  conditional: depth={raw.NestingContext.NestingDepth}, condition=\"{raw.NestingContext.ConditionText}\", branch={raw.NestingContext.BranchKind}");
        if (raw.ConstantIntValue.HasValue)
            log(chainUid, $"  constantIntValue={raw.ConstantIntValue.Value}");
        if (raw.ProjectionInfo != null)
            log(chainUid, $"  projection: kind={raw.ProjectionInfo.Kind}, columns={raw.ProjectionInfo.Columns.Count}, resultType={raw.ProjectionInfo.ResultTypeName}");
        if (!raw.IsAnalyzable && raw.NonAnalyzableReason != null)
            log(chainUid, $"  nonAnalyzableReason={raw.NonAnalyzableReason}");
        // Disqualifiers
        if (raw.IsInsideLoop || raw.IsInsideTryCatch || raw.IsCapturedInLambda || raw.IsPassedAsArgument || raw.IsAssignedFromNonQuarryMethod)
            log(chainUid, $"  disqualifiers: loop={raw.IsInsideLoop}, tryCatch={raw.IsInsideTryCatch}, lambdaCapture={raw.IsCapturedInLambda}, passedAsArg={raw.IsPassedAsArgument}, nonQuarryAssign={raw.IsAssignedFromNonQuarryMethod}");

        // ── Binding ──
        log(chainUid, $"[Trace] Binding ({raw.MethodName}):");
        log(chainUid, $"  entity={raw.EntityTypeName}, table={site.Bound.TableName}, schema={site.Bound.SchemaName ?? "(null)"}, dialect={site.Bound.Dialect}");
        log(chainUid, $"  context={site.Bound.ContextClassName}");
        if (site.Bound.Entity != null && site.Bound.Entity.Columns.Count > 0)
            log(chainUid, $"  resolvedColumns=[{string.Join(", ", ColumnNames(site.Bound.Entity.Columns))}]");
        if (site.Bound.JoinedEntity != null)
            log(chainUid, $"  joinedEntity={site.Bound.JoinedEntity.EntityName}, joinedTable={site.Bound.JoinedEntity.TableName}");
        if (site.Bound.JoinedEntities != null)
        {
            foreach (var je in site.Bound.JoinedEntities)
                log(chainUid, $"  joinedEntity: {je.EntityName} -> {je.TableName}");
        }
        if (site.Bound.InsertInfo != null)
            log(chainUid, $"  insertInfo: columns={site.Bound.InsertInfo.Columns.Count}");
        if (site.Bound.UpdateInfo != null)
            log(chainUid, $"  updateInfo: columns={site.Bound.UpdateInfo.Columns.Count}");

        // ── Translation ──
        log(chainUid, $"[Trace] Translation ({raw.MethodName}):");
        if (site.Clause != null)
        {
            log(chainUid, $"  clauseKind={site.Clause.Kind}, isSuccess={site.Clause.IsSuccess}");
            log(chainUid, $"  resolvedExpr={FormatExpr(site.Clause.ResolvedExpression)}");
            if (site.Clause.Parameters.Count > 0)
            {
                foreach (var p in site.Clause.Parameters)
                {
                    var flags = new List<string>();
                    if (p.IsCaptured) flags.Add("captured");
                    if (p.IsCollection) flags.Add("collection");
                    log(chainUid, $"  param[{p.Index}]: type={p.ClrType}, value={p.ValueExpression}, path={p.ExpressionPath ?? "(null)"}{(flags.Count > 0 ? $", flags=[{string.Join(",", flags)}]" : "")}");
                }
            }
            else
            {
                log(chainUid, "  params=none");
            }
            if (site.Clause.JoinKind.HasValue)
                log(chainUid, $"  joinKind={site.Clause.JoinKind.Value}, joinedTable={site.Clause.JoinedTableName}, alias={site.Clause.TableAlias}");
            if (site.Clause.SetAssignments != null)
            {
                foreach (var sa in site.Clause.SetAssignments)
                    log(chainUid, $"  setAssignment: {sa.ColumnSql}={sa.InlinedSqlValue ?? "(param)"}, type={sa.ValueTypeName ?? "?"}");
            }
            if (site.Clause.ErrorMessage != null)
                log(chainUid, $"  error={site.Clause.ErrorMessage}");
        }
        else
        {
            log(chainUid, "  clause=none (non-clause site)");
        }
    }

    /// <summary>
    /// Logs chain-level analysis trace including joins, projections, parameters, and pagination.
    /// </summary>
    private static void LogChainTrace(string chainUid, QueryPlan plan, TranslatedCallSite executionSite)
    {
        var log = IR.TraceCapture.Log;

        log(chainUid, "[Trace] ChainAnalysis:");
        log(chainUid, $"  tier={plan.Tier}, queryKind={plan.Kind}");
        log(chainUid, $"  primaryTable={plan.PrimaryTable.TableName}, schema={plan.PrimaryTable.SchemaName ?? "(null)"}");
        log(chainUid, $"  isDistinct={plan.IsDistinct}");
        if (plan.NotAnalyzableReason != null)
            log(chainUid, $"  notAnalyzableReason={plan.NotAnalyzableReason}");
        if (plan.UnmatchedMethodNames != null)
            log(chainUid, $"  unmatchedMethods=[{string.Join(", ", plan.UnmatchedMethodNames)}]");

        // Joins
        if (plan.Joins.Count > 0)
        {
            foreach (var j in plan.Joins)
                log(chainUid, $"  join: {j.Kind} {j.Table.TableName}{(j.OnCondition != null ? $" ON {FormatExpr(j.OnCondition)}" : "")}{(j.IsNavigationJoin ? " (navigation)" : "")}");
        }

        // WHERE terms
        if (plan.WhereTerms.Count > 0)
        {
            foreach (var w in plan.WhereTerms)
                log(chainUid, $"  where: {FormatExpr(w.Condition)}{(w.BitIndex.HasValue ? $" [bit={w.BitIndex}]" : "")}");
        }

        // ORDER BY terms
        if (plan.OrderTerms.Count > 0)
        {
            foreach (var o in plan.OrderTerms)
                log(chainUid, $"  orderBy: {FormatExpr(o.Expression)} {(o.IsDescending ? "DESC" : "ASC")}{(o.BitIndex.HasValue ? $" [bit={o.BitIndex}]" : "")}");
        }

        // GROUP BY
        if (plan.GroupByExprs.Count > 0)
        {
            foreach (var g in plan.GroupByExprs)
                log(chainUid, $"  groupBy: {FormatExpr(g)}");
        }

        // HAVING
        if (plan.HavingExprs.Count > 0)
        {
            foreach (var h in plan.HavingExprs)
                log(chainUid, $"  having: {FormatExpr(h)}");
        }

        // SET terms
        if (plan.SetTerms.Count > 0)
        {
            foreach (var s in plan.SetTerms)
                log(chainUid, $"  set: {FormatExpr(s.Column)}={FormatExpr(s.Value)}");
        }

        // Projection
        if (plan.Projection != null)
        {
            log(chainUid, $"  projection: kind={plan.Projection.Kind}, resultType={plan.Projection.ResultTypeName}, identity={plan.Projection.IsIdentity}");
            foreach (var c in plan.Projection.Columns)
                log(chainUid, $"    col: {c.PropertyName} -> {c.ColumnName ?? "(null)"} [{c.ClrType}]{(c.SqlExpression != null ? $" expr={c.SqlExpression}" : "")}{(c.IsAggregateFunction ? " (aggregate)" : "")}{(c.TableAlias != null ? $" alias={c.TableAlias}" : "")}");
        }

        // Pagination
        if (plan.Pagination != null)
            log(chainUid, $"  pagination: limit={plan.Pagination.LiteralLimit?.ToString() ?? (plan.Pagination.LimitParamIndex.HasValue ? $"P{plan.Pagination.LimitParamIndex}" : "none")}, offset={plan.Pagination.LiteralOffset?.ToString() ?? (plan.Pagination.OffsetParamIndex.HasValue ? $"P{plan.Pagination.OffsetParamIndex}" : "none")}");

        // Parameters
        if (plan.Parameters.Count > 0)
        {
            log(chainUid, $"  parameters ({plan.Parameters.Count}):");
            foreach (var p in plan.Parameters)
            {
                var flags = new List<string>();
                if (p.IsCaptured) flags.Add("captured");
                if (p.IsCollection) flags.Add($"collection<{p.ElementTypeName ?? "?"}>");
                if (p.IsEnum) flags.Add($"enum({p.EnumUnderlyingType})");
                if (p.NeedsUnsafeAccessor) flags.Add("unsafeAccessor");
                if (p.TypeMappingClass != null) flags.Add($"mapping={p.TypeMappingClass}");
                log(chainUid, $"    P{p.GlobalIndex}: type={p.ClrType}, value={p.ValueExpression}{(flags.Count > 0 ? $", [{string.Join(", ", flags)}]" : "")}");
            }
        }
        else
        {
            log(chainUid, "  parameters=none");
        }

        // Conditional terms + masks
        if (plan.ConditionalTerms.Count > 0)
        {
            foreach (var ct in plan.ConditionalTerms)
                log(chainUid, $"  conditionalTerm: bit={ct.BitIndex}");
        }
        log(chainUid, $"  possibleMasks=[{string.Join(", ", plan.PossibleMasks)}]");
    }

    /// <summary>
    /// Formats a SqlExpr for trace output. Renders to SQL using a generic parameter format
    /// for readability, falling back to type name on failure.
    /// </summary>
    private static string FormatExpr(IR.SqlExpr expr)
    {
        try
        {
            return IR.SqlExprRenderer.Render(expr, Sql.SqlDialect.PostgreSQL, useGenericParamFormat: true, stripOuterParens: true);
        }
        catch
        {
            return expr.GetType().Name;
        }
    }

    private static IEnumerable<string> ColumnNames(IReadOnlyList<Models.ColumnInfo> columns)
    {
        foreach (var c in columns)
            yield return c.PropertyName;
    }
}

/// <summary>
/// A query chain analysis result pairing a QueryPlan with its associated call sites.
/// </summary>
internal sealed class AnalyzedChain
{
    public AnalyzedChain(
        QueryPlan plan,
        TranslatedCallSite executionSite,
        IReadOnlyList<TranslatedCallSite> clauseSites,
        bool isTraced = false,
        IReadOnlyList<TranslatedCallSite>? preparedTerminals = null,
        TranslatedCallSite? prepareSite = null,
        bool isOperandChain = false)
    {
        Plan = plan;
        ExecutionSite = executionSite;
        ClauseSites = clauseSites;
        IsTraced = isTraced;
        PreparedTerminals = preparedTerminals;
        PrepareSite = prepareSite;
        IsOperandChain = isOperandChain;
    }

    /// <summary>The logical query plan.</summary>
    public QueryPlan Plan { get; }

    /// <summary>The execution terminal site (for single-terminal or collapsed single-prepared-terminal).</summary>
    public TranslatedCallSite ExecutionSite { get; }

    /// <summary>All clause sites in the chain (in source order).</summary>
    public IReadOnlyList<TranslatedCallSite> ClauseSites { get; }

    /// <summary>Whether this chain has a .Trace() call and should emit trace comments.</summary>
    public bool IsTraced { get; }

    /// <summary>
    /// Terminal sites called on a PreparedQuery variable. Non-null only for multi-terminal chains (N>1).
    /// When null or empty, this is a standard single-terminal chain.
    /// </summary>
    public IReadOnlyList<TranslatedCallSite>? PreparedTerminals { get; }

    /// <summary>
    /// The .Prepare() call site. Non-null only for multi-terminal chains.
    /// </summary>
    public TranslatedCallSite? PrepareSite { get; }

    /// <summary>
    /// True when this chain is consumed as a set operation operand (no standalone terminal).
    /// The carrier is generated for clause interceptors only — no execution terminal is emitted.
    /// </summary>
    public bool IsOperandChain { get; }
}
