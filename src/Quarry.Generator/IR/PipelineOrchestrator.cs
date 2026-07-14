using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.CodeAnalysis;
using Quarry.Generators.CodeGen;
using Quarry.Generators.Models;
using Quarry.Generators.Parsing;
using Quarry.Generators.Utilities;

namespace Quarry.Generators.IR;

/// <summary>
/// Orchestrates the post-enrichment pipeline: diagnostic collection, chain analysis,
/// and file grouping. Takes TranslatedCallSites and produces per-file output groups
/// ready for code generation.
/// </summary>
internal static class PipelineOrchestrator
{

    private static string? GetNamespaceFromEntityType(string entityTypeName)
    {
        // Remove global:: prefix if present
        if (entityTypeName.StartsWith("global::"))
            entityTypeName = entityTypeName.Substring(8);

        var lastDot = entityTypeName.LastIndexOf('.');
        return lastDot > 0 ? entityTypeName.Substring(0, lastDot) : null;
    }

    /// <summary>
    /// New pipeline entry point: takes TranslatedCallSites and orchestrates
    /// ChainAnalyzer → SqlAssembler → CarrierAnalyzer → file grouping.
    /// </summary>
    public static ImmutableArray<FileInterceptorGroup> AnalyzeAndGroupTranslated(
        ImmutableArray<TranslatedCallSite> translatedSites,
        EntityRegistry registry,
        CancellationToken ct)
    {
        // Trace state is produced (ChainAnalyzer, SqlAssembler) and consumed (captured
        // onto AssembledPlan.TraceLines) entirely within this call, so the ThreadStatic
        // never has to survive a pipeline-node or thread boundary (#311). The finally
        // ensures a cancellation or failure cannot leak lines into a later run.
        TraceCapture.Clear();
        try
        {
            return AnalyzeAndGroupTranslatedCore(translatedSites, registry, ct);
        }
        finally
        {
            TraceCapture.Clear();
        }
    }

    private static ImmutableArray<FileInterceptorGroup> AnalyzeAndGroupTranslatedCore(
        ImmutableArray<TranslatedCallSite> translatedSites,
        EntityRegistry registry,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Collect diagnostics from TranslatedCallSite properties
        var diagnostics = new List<DiagnosticInfo>();
        CollectTranslatedDiagnostics(translatedSites, diagnostics, registry);

        ct.ThrowIfCancellationRequested();

        // Chain analysis: TranslatedCallSite[] → AnalyzedChain[]
        var analyzedChains = ChainAnalyzer.Analyze(translatedSites, registry, ct, diagnostics);

        ct.ThrowIfCancellationRequested();

        // SQL assembly: AnalyzedChain → AssembledPlan
        var assembledPlans = new List<AssembledPlan>(analyzedChains.Count);
        foreach (var chain in analyzedChains)
        {
            var assembled = SqlAssembler.Assemble(chain, registry);
            assembledPlans.Add(assembled);
        }

        // Capture trace lines onto the plan itself so traced chains keep their
        // .Trace() output when their file group is cached on incremental runs (#311).
        // TraceLines is excluded from AssembledPlan equality (derived data), so this
        // never churns the cache. All trace producers have run by this point:
        // ChainAnalyzer's retroactive site/chain traces and SqlAssembler's per-mask
        // assembly traces, both keyed by the execution site's UniqueId.
        foreach (var assembled in assembledPlans)
        {
            if (!assembled.IsTraced) continue;
            var trace = TraceCapture.Get(assembled.ExecutionSite.UniqueId);
            if (trace is { Count: > 0 })
                assembled.TraceLines = trace;
        }

        ct.ThrowIfCancellationRequested();

        // Post-analysis diagnostics (require assembled plans with resolved projections)
        CollectPostAnalysisDiagnostics(assembledPlans, diagnostics);

        ct.ThrowIfCancellationRequested();

        // Carrier analysis: AssembledPlan → CarrierPlan
        var carrierPlans = new List<CarrierPlan>(assembledPlans.Count);
        foreach (var assembled in assembledPlans)
        {
            var carrier = CarrierAnalyzer.AnalyzeNew(assembled);
            carrierPlans.Add(carrier);
        }

        ct.ThrowIfCancellationRequested();

        // SQL post-processing: collection tokenization + MySQL bind-order extraction.
        // Runs here — before file grouping — so both RegisterImplementationSourceOutput
        // consumers (interceptor emission and the SQL manifest) see final SQL with no
        // dependence on cross-output execution ordering, and so incremental equality
        // always compares post-processed plans (a fresh recompute and a cached group
        // carry identical SQL strings).
        for (int i = 0; i < assembledPlans.Count; i++)
        {
            var assembled = assembledPlans[i];
            var isCarrierEligible = carrierPlans[i].IsEligible;
            if (assembled.Dialect == Sql.SqlDialect.MySQL)
            {
                // A single pass rewrites bind-order markers to '?' (or collection
                // expansion tokens) and extracts the SQL-text bind order for the carrier
                // bind loop (#303). Runs for ALL MySQL plans — markers must never leak
                // into manifests or generated SQL — with collection tokenization gated
                // on carrier eligibility exactly like the non-MySQL path below.
                var failure = RewriteMySqlBindMarkers(assembled, isCarrierEligible);
                if (failure != null)
                {
                    // QRY048: binding stays in chain order, which may not match the SQL
                    // text's '?' positions — surface the potential misbind loudly
                    // instead of shipping it silently (the pre-#303 failure mode).
                    diagnostics.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.MySqlBindOrderFallback.Id,
                        assembled.ExecutionSite.Bound.Raw.Location,
                        failure));
                }
            }
            else if (isCarrierEligible && assembled.Plan.Parameters.Count > 0)
            {
                TokenizeCollectionParameters(assembled.SqlVariants, assembled.Plan.Parameters, assembled.Dialect);
            }
        }

        ct.ThrowIfCancellationRequested();

        // Resolve unresolved result types from chain projections (e.g., tuple types
        // that the semantic model couldn't resolve during discovery due to reassignment).
        var resultTypePatches = BuildResultTypePatches(assembledPlans);

        // Propagate chain-updated sites (e.g., JoinedEntityTypeNames from ChainAnalyzer)
        // back into the main site array so downstream code sees a single consistent view.
        // This eliminates the need for FileEmitter to conditionally select between original
        // and chain-updated sites.
        var updatedSites = PropagateChainUpdatedSites(translatedSites, assembledPlans, resultTypePatches);

        // Filter out lambda inner chain sites before file grouping — their SQL is embedded
        // in the outer chain's CTE/set-op clause at compile time. Passing them through to
        // file grouping would create interceptor files under wrong contexts (the entity type
        // may be registered in multiple contexts, and without a concrete chain root the
        // pipeline can't disambiguate).
        var consumedIds = Parsing.ChainAnalyzer.ConsumedLambdaInnerSiteIds;
        var filteredSites = updatedSites;
        if (consumedIds != null && consumedIds.Count > 0)
        {
            filteredSites = updatedSites
                .Where(s => !consumedIds.Contains(s.UniqueId))
                .ToImmutableArray();
            consumedIds.Clear(); // avoid stale state across incremental runs
        }

        // Group into files
        return GroupTranslatedIntoFiles(filteredSites, assembledPlans, carrierPlans, diagnostics);
    }

    private static void CollectTranslatedDiagnostics(
        ImmutableArray<TranslatedCallSite> sites,
        List<DiagnosticInfo> diagnostics,
        EntityRegistry registry)
    {
        foreach (var site in sites)
        {
            var raw = site.Bound.Raw;

            // QRY031: unresolvable RawSqlAsync type parameter (Error — must fix before compiling)
            if (!raw.IsAnalyzable
                && raw.Kind is InterceptorKind.RawSqlAsync or InterceptorKind.RawSqlScalarAsync
                && raw.NonAnalyzableReason != null)
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.UnresolvableRawSqlTypeParameter.Id,
                    raw.Location,
                    raw.NonAnalyzableReason));
                continue;
            }

            // QRY043: RawSqlAsync row entity type is not materializable (positional record,
            // init-only property, no parameterless constructor). Reported as Error so consumers
            // see the real problem instead of a CS7036/CS8852 against the generated interceptor.
            if (raw.MaterializabilityError != null
                && raw.Kind is InterceptorKind.RawSqlAsync or InterceptorKind.RawSqlScalarAsync)
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.RowEntityNotMaterializable.Id,
                    raw.Location,
                    raw.ResultTypeName ?? raw.EntityTypeName,
                    raw.MaterializabilityError));
                continue;
            }

            // QRY001: query not analyzable (parameter receiver, variable receiver, etc.)
            // Lambda inner chain sites are expected to be non-analyzable (their receiver
            // is a lambda parameter) and are handled by ChainAnalyzer's recursive analysis.
            if (!raw.IsAnalyzable && raw.NonAnalyzableReason != null
                && (raw.ChainId == null || !raw.ChainId.Contains(":lambda-inner:")))
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.QueryNotAnalyzable.Id,
                    raw.Location,
                    raw.NonAnalyzableReason));
            }

            // QRY015: ambiguous context resolution
            if (site.Bound.ContextClassName == null && registry.GetEntryCount(raw.EntityTypeName) > 1)
            {
                var chosen = registry.GetFirstEntry(raw.EntityTypeName);
                if (chosen != null)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.AmbiguousContextResolution.Id,
                        raw.Location,
                        raw.EntityTypeName,
                        chosen.Context.ClassName,
                        chosen.Context.Dialect.ToString()));
                }
            }

            // QRY019: clause not translatable
            if (site.Clause != null && !site.Clause.IsSuccess && site.Clause.ErrorMessage != null)
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.ClauseNotTranslatable.Id,
                    raw.Location,
                    site.Clause.ErrorMessage));
            }

            // QRY029: Sql.Raw template placeholder mismatch
            if (raw.Expression is RawCallExpr rawCallExpr)
            {
                var validationError = rawCallExpr.Validate();
                if (validationError != null)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.SqlRawPlaceholderMismatch.Id,
                        raw.Location,
                        validationError));
                }
            }

            // QRY029: Sql.Raw template placeholder mismatch inside a Select projection.
            // The projection analyzer builds a transient RawCallExpr for validation in
            // IsRawTemplateValid; failures are accumulated per projection as
            // ProjectionInfo.SqlRawValidationErrors so the pipeline can emit them here.
            // Attaches to the Select call's location (raw.Location) — less precise than the
            // Where-path (which has the Sql.Raw site directly), but sufficient for the user
            // to locate the bad template in the Select lambda.
            if (raw.ProjectionInfo?.SqlRawValidationErrors is { Count: > 0 } projectionRawErrors)
            {
                foreach (var error in projectionRawErrors)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.SqlRawPlaceholderMismatch.Id,
                        raw.Location,
                        error));
                }
            }

            // QRY070/QRY071: INTERSECT ALL / EXCEPT ALL not supported on non-PostgreSQL dialects
            if (raw.Kind == InterceptorKind.IntersectAll && site.Bound.Dialect != Sql.SqlDialect.PostgreSQL)
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.IntersectAllNotSupported.Id,
                    raw.Location,
                    site.Bound.Dialect.ToString()));
            }
            else if (raw.Kind == InterceptorKind.ExceptAll && site.Bound.Dialect != Sql.SqlDialect.PostgreSQL)
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.ExceptAllNotSupported.Id,
                    raw.Location,
                    site.Bound.Dialect.ToString()));
            }
        }
    }

    /// <summary>
    /// Collects diagnostics that require post-analysis information (assembled plans with resolved projections).
    /// </summary>
    private static void CollectPostAnalysisDiagnostics(
        List<AssembledPlan> assembledPlans,
        List<DiagnosticInfo> diagnostics)
    {
        foreach (var assembled in assembledPlans)
        {
            var plan = assembled.Plan;
            if (plan.SetOperations.Count == 0) continue;

            var mainColumnCount = plan.Projection?.Columns.Count ?? 0;

            var mainTable = plan.PrimaryTable.TableName;

            for (int i = 0; i < plan.SetOperations.Count; i++)
            {
                var setOp = plan.SetOperations[i];
                var setOpSite = FindSetOperationSite(assembled.ClauseSites, i);
                var location = setOpSite?.Bound.Raw.Location ?? assembled.ExecutionSite.Bound.Raw.Location;

                // QRY072: Set operation projection mismatch
                var operandColumnCount = setOp.Operand.Projection?.Columns.Count ?? 0;
                if (mainColumnCount > 0 && operandColumnCount > 0 && mainColumnCount != operandColumnCount)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.SetOperationProjectionMismatch.Id,
                        location,
                        operandColumnCount.ToString(),
                        mainColumnCount.ToString()));
                }
            }
        }
    }

    /// <summary>
    /// Finds the Nth set operation call site in a chain's clause sites.
    /// </summary>
    private static TranslatedCallSite? FindSetOperationSite(IReadOnlyList<TranslatedCallSite> clauseSites, int setOpIndex)
    {
        int found = 0;
        foreach (var site in clauseSites)
        {
            if (ChainAnalyzer.IsSetOperationKind(site.Kind))
            {
                if (found == setOpIndex) return site;
                found++;
            }
        }
        return null;
    }

    /// <summary>
    /// Builds a dictionary of result type patches for clause/execution sites whose
    /// ResultTypeName is unresolved (e.g., tuple types rendered as (object, object, object)
    /// due to Roslyn semantic model limitations on reassigned variables).
    /// The resolved type comes from the chain's SelectProjection after BuildProjection enrichment.
    /// </summary>
    private static Dictionary<string, string> BuildResultTypePatches(List<AssembledPlan> assembledPlans)
    {
        var patches = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var plan in assembledPlans)
        {
            var resolvedType = plan.Plan.Projection?.ResultTypeName;
            if (resolvedType == null || resolvedType.Length == 0 || TypeClassification.IsUnresolvedResultType(resolvedType))
                continue;

            // Patch execution site if its result type is unresolved
            if (TypeClassification.IsUnresolvedResultType(plan.ExecutionSite.ResultTypeName))
                patches[plan.ExecutionSite.UniqueId] = resolvedType;

            // Patch clause sites with unresolved result types
            foreach (var cs in plan.ClauseSites)
            {
                if (TypeClassification.IsUnresolvedResultType(cs.ResultTypeName))
                    patches[cs.UniqueId] = resolvedType;
            }
        }

        return patches;
    }


    /// <summary>
    /// Replaces sites in the main array with chain-updated versions from AssembledPlans.
    /// ChainAnalyzer may update sites (e.g., propagating JoinedEntityTypeNames to post-join
    /// and execution sites). Also applies result type patches for unresolved tuple types.
    /// This ensures all downstream code sees the enriched sites.
    /// </summary>
    private static ImmutableArray<TranslatedCallSite> PropagateChainUpdatedSites(
        ImmutableArray<TranslatedCallSite> allSites,
        List<AssembledPlan> assembledPlans,
        Dictionary<string, string> resultTypePatches)
    {
        // Build lookup of chain-updated sites by UniqueId
        var chainUpdatedSites = new Dictionary<string, TranslatedCallSite>(StringComparer.Ordinal);
        foreach (var plan in assembledPlans)
        {
            chainUpdatedSites[plan.ExecutionSite.UniqueId] = plan.ExecutionSite;
            foreach (var cs in plan.ClauseSites)
                chainUpdatedSites[cs.UniqueId] = cs;
        }

        // Apply result type patches on top of chain-updated sites
        foreach (var kvp in resultTypePatches)
        {
            if (chainUpdatedSites.TryGetValue(kvp.Key, out var site))
                chainUpdatedSites[kvp.Key] = site.WithResolvedResultType(kvp.Value);
            else
            {
                // Site not in any chain's updated set — find it in allSites
                foreach (var s in allSites)
                {
                    if (s.UniqueId == kvp.Key)
                    {
                        chainUpdatedSites[kvp.Key] = s.WithResolvedResultType(kvp.Value);
                        break;
                    }
                }
            }
        }

        if (chainUpdatedSites.Count == 0)
            return allSites;

        // Replace sites that were updated during chain analysis
        var builder = ImmutableArray.CreateBuilder<TranslatedCallSite>(allSites.Length);
        foreach (var site in allSites)
        {
            builder.Add(chainUpdatedSites.TryGetValue(site.UniqueId, out var updated) ? updated : site);
        }
        return builder.MoveToImmutable();
    }

    private static ImmutableArray<FileInterceptorGroup> GroupTranslatedIntoFiles(
        ImmutableArray<TranslatedCallSite> allSites,
        List<AssembledPlan> assembledPlans,
        List<CarrierPlan> carrierPlans,
        List<DiagnosticInfo> diagnostics)
    {
        // Collect chain member IDs
        var chainMemberIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var plan in assembledPlans)
        {
            chainMemberIds.Add(plan.ExecutionSite.Bound.Raw.UniqueId);
            foreach (var cs in plan.ClauseSites)
                chainMemberIds.Add(cs.Bound.Raw.UniqueId);
        }

        // Filter out sites without a valid context (e.g., direct QueryBuilder usage not from a QuarryContext)
        var contextSites = allSites.Where(s => !string.IsNullOrEmpty(s.Bound.ContextClassName)).ToImmutableArray();

        // Group by (context, filePath)
        var fileGroups = contextSites
            .GroupBy(s => (
                ContextClassName: s.Bound.ContextClassName!,
                FilePath: s.Bound.Raw.FilePath))
            .ToList();

        var result = ImmutableArray.CreateBuilder<FileInterceptorGroup>(fileGroups.Count);

        foreach (var group in fileGroups)
        {
            var sites = group.ToList();
            if (sites.Count == 0) continue;

            var contextClassName = group.Key.ContextClassName;
            var filePath = group.Key.FilePath;
            var fileTag = FileHasher.ComputeFileTag(filePath);

            var namespaceName = sites.Select(s => s.Bound.ContextNamespace)
                .FirstOrDefault(ns => !string.IsNullOrEmpty(ns))
                ?? GetNamespaceFromEntityType(sites[0].Bound.Raw.EntityTypeName);

            // Find chains whose execution terminal is in this file
            var fileAssembledPlans = new List<AssembledPlan>();
            var fileCarrierPlans = new List<CarrierPlan>();
            for (int i = 0; i < assembledPlans.Count; i++)
            {
                var plan = assembledPlans[i];
                if (plan.ExecutionSite.Bound.ContextClassName == contextClassName
                    && plan.ExecutionSite.Bound.Raw.FilePath == filePath)
                {
                    fileAssembledPlans.Add(plan);
                    fileCarrierPlans.Add(carrierPlans[i]);
                }
            }

            // Separate analyzable and chain-member-only sites
            var fileSites = new List<TranslatedCallSite>();
            var fileChainMemberSites = new List<TranslatedCallSite>();
            foreach (var s in sites)
            {
                if (s.Bound.Raw.IsAnalyzable || !chainMemberIds.Contains(s.Bound.Raw.UniqueId))
                    fileSites.Add(s);
                if (!s.Bound.Raw.IsAnalyzable && chainMemberIds.Contains(s.Bound.Raw.UniqueId))
                    fileChainMemberSites.Add(s);
            }

            var fileDiagnostics = diagnostics
                .Where(d => d.Location.FilePath == filePath)
                .ToList();

            result.Add(new FileInterceptorGroup(
                contextClassName,
                namespaceName,
                filePath,
                fileTag,
                fileSites,
                fileAssembledPlans,
                fileChainMemberSites,
                fileDiagnostics,
                fileCarrierPlans));
        }

        return result.ToImmutable();
    }

    /// <summary>
    /// Replaces collection parameter placeholders in pre-built SQL with expansion tokens.
    /// For example, <c>IN (@p0)</c> becomes <c>IN ({__COL_P0__})</c> when P0 is a collection.
    /// The carrier terminal expands these tokens at runtime based on the actual collection size.
    /// </summary>
    private static void TokenizeCollectionParameters(
        Dictionary<int, AssembledSqlVariant> sqlMap,
        IReadOnlyList<QueryParameter> chainParams,
        Sql.SqlDialect dialect)
    {
        // Find collection parameter indices and their tokens
        var collectionParams = new List<(int Index, string Token)>();
        foreach (var param in chainParams)
        {
            if (!param.IsCollection) continue;
            collectionParams.Add((param.GlobalIndex, $"{{__COL_P{param.GlobalIndex}__}}"));
        }

        if (collectionParams.Count == 0) return;

        // Replace placeholders with tokens in all SQL variants.
        // Collect updates and apply after iteration to avoid allocating a key list copy.
        // MySQL never reaches this method — its collection tokenization happens inside
        // RewriteMySqlBindMarkers' single marker pass (#303), which replaced the old
        // positional Nth-'?' substitution (and its miscount hazard when a SQL string
        // literal contains '?').
        var pendingUpdates = new List<(int Key, string Sql, int ParamCount)>();
        foreach (var kvp in sqlMap)
        {
            var sbSql = new System.Text.StringBuilder(kvp.Value.Sql);
            foreach (var (paramIdx, token) in collectionParams)
            {
                var placeholder = dialect switch
                {
                    Sql.SqlDialect.PostgreSQL => $"${paramIdx + 1}",
                    _ => $"@p{paramIdx}"
                };
                sbSql.Replace(placeholder, token);
            }
            var sql = sbSql.ToString();

            if (sql != kvp.Value.Sql)
            {
                pendingUpdates.Add((kvp.Key, sql, kvp.Value.ParameterCount));
            }
        }
        foreach (var (key, sql, paramCount) in pendingUpdates)
        {
            sqlMap[key] = new AssembledSqlVariant(sql, paramCount);
        }
    }

    /// <summary>
    /// MySQL assembly post-pass (#303). A single scan per SQL variant rewrites
    /// <c>{__Q{n}__}</c> bind-order markers to <c>?</c> — or to <c>{__COL_P{n}__}</c>
    /// expansion tokens for collection parameters on carrier-eligible chains — and
    /// extracts the variant's SQL-text slot order. Per-variant orders are validated
    /// against the mask's expected active-parameter set and merged into a single chain
    /// ranking, stored on <see cref="AssembledPlan.MySqlBindOrder"/> when it differs
    /// from GlobalIndex (identity) order.
    /// </summary>
    /// <returns>
    /// Null on success (order stored or proven identity); otherwise a short reason
    /// string describing why extraction/validation failed. The caller reports it as
    /// QRY048 — binding then falls back to identity (the pre-#303 behavior), which may
    /// misalign on chains whose text order genuinely diverges. The rewritten
    /// (marker-free) SQL is always applied regardless of the outcome.
    /// </returns>
    internal static string? RewriteMySqlBindMarkers(AssembledPlan assembled, bool isCarrierEligible)
    {
        var chainParams = assembled.Plan.Parameters;
        HashSet<int>? collectionSlots = null;
        if (isCarrierEligible)
        {
            foreach (var p in chainParams)
            {
                if (p.IsCollection)
                    (collectionSlots ??= new HashSet<int>()).Add(p.GlobalIndex);
            }
        }
        Func<int, bool>? isCollectionSlot = collectionSlots != null ? collectionSlots.Contains : null;

        // Slot layout mirrors the carrier bind loop: chain params 0..N-1, then the
        // parameterized limit/offset slots. Pagination slots come straight from the
        // plan (ChainAnalyzer allocates them last) — the same source AppendPagination's
        // marker emission uses — rather than being derived from paramCount arithmetic.
        var paramCount = chainParams.Count;
        var pag = assembled.Plan.Pagination;
        int? limitSlot = pag?.LimitParamIndex;
        int? offsetSlot = pag?.OffsetParamIndex;
        var totalSlots = paramCount;
        if (limitSlot != null) totalSlots = Math.Max(totalSlots, limitSlot.Value + 1);
        if (offsetSlot != null) totalSlots = Math.Max(totalSlots, offsetSlot.Value + 1);

        var condMap = assembled.GetParamConditionalMap();

        // Deterministic mask order keeps the merge (and thus the generated bind order)
        // stable regardless of dictionary enumeration order.
        var maskKeys = new List<int>(assembled.SqlVariants.Keys);
        maskKeys.Sort();

        List<int[]>? sequences = null;
        List<(int Key, string Sql, int ParamCount)>? updates = null;
        string? failure = null;
        var seen = new HashSet<int>();
        var textOrder = new List<int>();

        // The bind loop indexes ChainParameters by list position while marker slots carry
        // GlobalIndex values; the two coincide for every chain ChainAnalyzer produces.
        // Guard the invariant — on violation, the marker rewrite below still runs (markers
        // must never leak) but the bind order stays identity rather than reordering
        // against the wrong axis.
        for (int i = 0; i < paramCount; i++)
        {
            if (chainParams[i].GlobalIndex != i)
            {
                failure = $"parameter list position {i} carries GlobalIndex {chainParams[i].GlobalIndex}";
                break;
            }
        }

        foreach (var mask in maskKeys)
        {
            var variant = assembled.SqlVariants[mask];
            textOrder.Clear();
            var rewritten = MySqlBindMarkers.RewriteAndExtract(variant.Sql, isCollectionSlot, textOrder);
            if (!ReferenceEquals(rewritten, variant.Sql))
                (updates ??= new List<(int, string, int)>()).Add((mask, rewritten, variant.ParameterCount));

            if (failure != null)
                continue;

            // Per-variant validation: no duplicates, all slots in range, and the slot set
            // must equal this mask's expected active set. A mismatch means a renderer
            // skipped or duplicated a bound slot (e.g. an ORDER BY term whose params are
            // reserved but textually elided) — bind order is then unreliable for this
            // chain, so leave it identity. Marker-free variants are NOT exempt: a variant
            // with active parameters but zero extracted markers means an entire render
            // surface missed marker emission, which must be loud (QRY048), not silent.
            seen.Clear();
            foreach (var slot in textOrder)
            {
                if (slot < 0 || slot >= totalSlots || !seen.Add(slot))
                {
                    failure = $"duplicate or out-of-range placeholder slot {slot} in the SQL variant for mask {mask}";
                    break;
                }
            }
            if (failure != null) continue;

            for (int i = 0; i < paramCount && failure == null; i++)
            {
                var active = !condMap.TryGetValue(i, out var ci)
                    || !ci.IsConditional
                    || ci.BitIndex == null
                    || (mask & (1 << ci.BitIndex.Value)) != 0;
                if (active != seen.Contains(i))
                    failure = $"placeholder slot set does not match the active parameter set for mask {mask} (parameter {i})";
            }
            // Conditional pagination: the placeholder must appear exactly in the variants
            // whose mask has the site's bit set (unconditional == always active).
            if (failure == null && limitSlot != null)
            {
                var limitActive = pag!.LimitBitIndex == null || (mask & (1 << pag.LimitBitIndex.Value)) != 0;
                if (limitActive != seen.Contains(limitSlot.Value))
                    failure = $"limit placeholder presence does not match its conditional bit for mask {mask}";
            }
            if (failure == null && offsetSlot != null)
            {
                var offsetActive = pag!.OffsetBitIndex == null || (mask & (1 << pag.OffsetBitIndex.Value)) != 0;
                if (offsetActive != seen.Contains(offsetSlot.Value))
                    failure = $"offset placeholder presence does not match its conditional bit for mask {mask}";
            }
            if (failure != null) continue;

            if (textOrder.Count > 0)
                (sequences ??= new List<int[]>()).Add(textOrder.ToArray());
        }

        // Apply the rewritten SQL regardless of order validity — markers must never
        // survive into manifests or generated source.
        if (updates != null)
        {
            foreach (var (key, sql, pc) in updates)
                assembled.SqlVariants[key] = new AssembledSqlVariant(sql, pc);
        }

        if (failure != null)
            return failure;
        if (sequences == null)
            return null; // no parameterized variants — nothing to order

        if (!TryMergeTextOrders(sequences, totalSlots, out var master))
            return "contradictory placeholder order across SQL variants";

        // Pagination binds after the chain-param loop in the emitter; verify the ranking
        // agrees (LIMIT/OFFSET is textually last in every MySQL statement Quarry emits,
        // LIMIT before OFFSET), then drop those slots — the emitter's bind-after-loop
        // structure already handles them.
        var tail = master.Count;
        if (offsetSlot != null && (tail == 0 || master[--tail] != offsetSlot.Value))
            return "offset placeholder is not in trailing position";
        if (limitSlot != null && (tail == 0 || master[--tail] != limitSlot.Value))
            return "limit placeholder is not in trailing position";
        if (tail != paramCount)
            return "a parameter never appears in any SQL variant";

        var identity = true;
        for (int i = 0; i < tail; i++)
        {
            if (master[i] != i) { identity = false; break; }
        }
        if (identity)
            return null;

        master.RemoveRange(tail, master.Count - tail);
        assembled.MySqlBindOrder = master;
        return null;
    }

    /// <summary>
    /// Merges the per-variant SQL-text slot sequences into the single chain ranking via
    /// a topological sort over the pairwise order constraints each variant contributes.
    /// Slots that co-occur in two variants must agree on relative order — renderers
    /// traverse clauses in a fixed structural order, so a cycle indicates a bug and
    /// aborts the merge (the caller reports QRY048). Slots never seen together
    /// (mutually exclusive conditional branches) carry no constraint against each other;
    /// among unconstrained candidates the smallest slot is emitted first (GlobalIndex
    /// tiebreak), so the ranking is deterministic regardless of variant enumeration
    /// order. An incremental anchor-insertion merge is NOT equivalent: fed the mask
    /// variants in ascending order (e.g. [0], [1], [0,1] from two independently
    /// conditional parameters), it guesses a placement for slots it has not yet seen
    /// co-occur and then reports a false contradiction when the combined variant
    /// arrives. Internal for unit testing.
    /// </summary>
    internal static bool TryMergeTextOrders(List<int[]> sequences, int totalSlots, out List<int> master)
    {
        master = new List<int>();
        var present = new bool[totalSlots];
        var edge = new bool[totalSlots * totalSlots];
        var indegree = new int[totalSlots];
        var nodeCount = 0;

        foreach (var seq in sequences)
        {
            for (int i = 0; i < seq.Length; i++)
            {
                var slot = seq[i];
                if (!present[slot])
                {
                    present[slot] = true;
                    nodeCount++;
                }
                if (i > 0)
                {
                    var prev = seq[i - 1];
                    if (!edge[prev * totalSlots + slot])
                    {
                        edge[prev * totalSlots + slot] = true;
                        indegree[slot]++;
                    }
                }
            }
        }

        // Kahn's algorithm; slot counts are tiny, so the O(slots²) ready-scan keeps the
        // smallest-slot-first tiebreak without a priority queue.
        var emitted = new bool[totalSlots];
        while (master.Count < nodeCount)
        {
            var pick = -1;
            for (int slot = 0; slot < totalSlots; slot++)
            {
                if (present[slot] && !emitted[slot] && indegree[slot] == 0)
                {
                    pick = slot;
                    break;
                }
            }
            if (pick < 0)
                return false; // cycle — contradictory relative order across variants

            emitted[pick] = true;
            master.Add(pick);
            for (int next = 0; next < totalSlots; next++)
            {
                if (edge[pick * totalSlots + next])
                    indegree[next]--;
            }
        }
        return true;
    }
}
