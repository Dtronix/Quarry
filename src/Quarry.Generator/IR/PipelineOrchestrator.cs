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
/// and file grouping. Takes enriched usage sites and produces per-file output groups
/// ready for code generation.
/// </summary>
internal sealed class PipelineOrchestrator
{
    private static readonly Regex ParameterPlaceholderRegex = new(@"\{(\d+)\}", RegexOptions.Compiled);

    private readonly Compilation _compilation;
    private readonly EntityRegistry _registry;
    private readonly ImmutableArray<UsageSiteInfo> _originalUsageSites;
    private readonly CancellationToken _ct;

    public PipelineOrchestrator(
        Compilation compilation,
        EntityRegistry registry,
        ImmutableArray<UsageSiteInfo> originalUsageSites,
        CancellationToken ct)
    {
        _compilation = compilation;
        _registry = registry;
        _originalUsageSites = originalUsageSites;
        _ct = ct;
    }

    /// <summary>
    /// Collects diagnostics, analyzes chains, and groups enriched sites into per-file groups.
    /// </summary>
    public ImmutableArray<FileInterceptorGroup> AnalyzeAndGroup(
        List<UsageSiteInfo> enrichedSites,
        List<UsageSiteInfo> navJoinChainSites,
        HashSet<UsageSiteInfo> sitesToSkip,
        List<DiagnosticInfo> diagnostics,
        Func<UsageSiteInfo, UsageSiteInfo> enrichSite,
        Func<UsageSiteInfo, string?> getNonTranslatableClauseKind,
        Func<Compilation, List<UsageSiteInfo>, EntityRegistry, CancellationToken,
            (IReadOnlyList<PrebuiltChainInfo> Chains, List<DiagnosticInfo> Diagnostics)> analyzeChains)
    {
        _ct.ThrowIfCancellationRequested();

        // QRY015: ambiguous context resolution
        CollectAmbiguityDiagnostics(enrichedSites, diagnostics);

        // QRY016: unbound parameter placeholders
        CollectUnboundParameterDiagnostics(enrichedSites, diagnostics);

        // QRY019: clause not translatable
        CollectClauseNotTranslatableDiagnostics(enrichedSites, diagnostics, getNonTranslatableClauseKind);

        _ct.ThrowIfCancellationRequested();

        // Analyze execution chains
        var allSitesForChainAnalysis = _originalUsageSites
            .Where(s => !sitesToSkip.Contains(s))
            .Select(enrichSite)
            .ToList();
        if (navJoinChainSites.Count > 0)
            allSitesForChainAnalysis.AddRange(navJoinChainSites);
        var (prebuiltChains, chainDiagnostics) = analyzeChains(
            _compilation, allSitesForChainAnalysis, _registry, _ct);
        diagnostics.AddRange(chainDiagnostics);

        // Merge non-analyzable chain member sites
        var chainMemberUniqueIds = new HashSet<string>();
        foreach (var chain in prebuiltChains)
        {
            foreach (var clause in chain.Analysis.Clauses)
                chainMemberUniqueIds.Add(clause.Site.UniqueId);
            chainMemberUniqueIds.Add(chain.Analysis.ExecutionSite.UniqueId);
        }

        var enrichedSiteIds = new HashSet<string>(enrichedSites.Select(s => s.UniqueId));
        var additionalChainSites = allSitesForChainAnalysis
            .Where(s => chainMemberUniqueIds.Contains(s.UniqueId) && !enrichedSiteIds.Contains(s.UniqueId))
            .ToList();

        var allSitesForGeneration = enrichedSites.Concat(additionalChainSites).ToList();

        _ct.ThrowIfCancellationRequested();

        return GroupIntoFiles(allSitesForGeneration, prebuiltChains, diagnostics);
    }

    private void CollectAmbiguityDiagnostics(List<UsageSiteInfo> enrichedSites, List<DiagnosticInfo> diagnostics)
    {
        foreach (var site in enrichedSites)
        {
            if (site.ContextClassName == null && _registry.GetEntryCount(site.EntityTypeName) > 1)
            {
                var chosen = _registry.GetFirstEntry(site.EntityTypeName);
                if (chosen != null)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.AmbiguousContextResolution.Id,
                        DiagnosticLocation.FromSyntaxNode(site.InvocationSyntax),
                        site.EntityTypeName,
                        chosen.Context.ClassName,
                        chosen.Context.Dialect.ToString()));
                }
            }
        }
    }

    private static void CollectUnboundParameterDiagnostics(List<UsageSiteInfo> enrichedSites, List<DiagnosticInfo> diagnostics)
    {
        foreach (var site in enrichedSites)
        {
            if (site.ClauseInfo is { IsSuccess: true } clause)
            {
                var boundIndices = new HashSet<int>(
                    clause.Parameters.Select(p => p.Index));

                foreach (Match match in ParameterPlaceholderRegex.Matches(clause.SqlFragment))
                {
                    var index = int.Parse(match.Groups[1].Value);
                    if (!boundIndices.Contains(index))
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            DiagnosticDescriptors.UnboundParameterPlaceholder.Id,
                            DiagnosticLocation.FromSyntaxNode(site.InvocationSyntax),
                            match.Value));
                    }
                }
            }
        }
    }

    private static void CollectClauseNotTranslatableDiagnostics(
        List<UsageSiteInfo> enrichedSites,
        List<DiagnosticInfo> diagnostics,
        Func<UsageSiteInfo, string?> getNonTranslatableClauseKind)
    {
        foreach (var site in enrichedSites)
        {
            var clauseKind = getNonTranslatableClauseKind(site);
            if (clauseKind != null)
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.ClauseNotTranslatable.Id,
                    DiagnosticLocation.FromSyntaxNode(site.InvocationSyntax),
                    clauseKind));
            }
        }
    }

    private static ImmutableArray<FileInterceptorGroup> GroupIntoFiles(
        List<UsageSiteInfo> allSitesForGeneration,
        IReadOnlyList<PrebuiltChainInfo> prebuiltChains,
        List<DiagnosticInfo> diagnostics)
    {
        var fileGroups = allSitesForGeneration
            .GroupBy(s => (ContextClassName: s.ContextClassName ?? "Quarry", FilePath: s.FilePath))
            .ToList();

        var result = ImmutableArray.CreateBuilder<FileInterceptorGroup>(fileGroups.Count);

        foreach (var group in fileGroups)
        {
            var sites = group.ToList();
            if (sites.Count == 0)
                continue;

            var contextClassName = group.Key.ContextClassName;
            var filePath = group.Key.FilePath;
            var fileTag = FileHasher.ComputeFileTag(filePath);

            var namespaceName = sites.Select(s => s.ContextNamespace)
                                    .FirstOrDefault(ns => !string.IsNullOrEmpty(ns))
                                ?? GetNamespaceFromEntityType(sites[0].EntityTypeName);

            var fileChains = prebuiltChains
                .Where(c => c.Analysis.ExecutionSite.ContextClassName == contextClassName
                         && c.Analysis.ExecutionSite.FilePath == filePath)
                .ToList();

            var fileChainMemberIds = new HashSet<string>();
            foreach (var chain in fileChains)
            {
                foreach (var clause in chain.Analysis.Clauses)
                    fileChainMemberIds.Add(clause.Site.UniqueId);
                fileChainMemberIds.Add(chain.Analysis.ExecutionSite.UniqueId);
            }

            var fileSites = new List<UsageSiteInfo>();
            var fileChainMemberSites = new List<UsageSiteInfo>();
            foreach (var s in sites)
            {
                if (s.IsAnalyzable || !fileChainMemberIds.Contains(s.UniqueId))
                    fileSites.Add(s);
                if (!s.IsAnalyzable && fileChainMemberIds.Contains(s.UniqueId))
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
                fileChains,
                fileChainMemberSites,
                fileDiagnostics));
        }

        // Diagnostic-only groups for files with diagnostics but no enriched sites
        var coveredFilePaths = new HashSet<string>(fileGroups.Select(g => g.Key.FilePath));
        var orphanedDiagnostics = diagnostics
            .Where(d => !coveredFilePaths.Contains(d.Location.FilePath))
            .GroupBy(d => d.Location.FilePath)
            .ToList();

        foreach (var orphanGroup in orphanedDiagnostics)
        {
            var filePath = orphanGroup.Key;
            var fileTag = FileHasher.ComputeFileTag(filePath);

            result.Add(new FileInterceptorGroup(
                "Quarry",
                null,
                filePath,
                fileTag,
                Array.Empty<UsageSiteInfo>(),
                Array.Empty<PrebuiltChainInfo>(),
                Array.Empty<UsageSiteInfo>(),
                orphanGroup.ToList()));
        }

        return result.ToImmutable();
    }

    private static string? GetNamespaceFromEntityType(string entityTypeName)
    {
        // Remove global:: prefix if present
        if (entityTypeName.StartsWith("global::"))
            entityTypeName = entityTypeName.Substring(8);

        var lastDot = entityTypeName.LastIndexOf('.');
        return lastDot > 0 ? entityTypeName.Substring(0, lastDot) : null;
    }

    #region New pipeline (TranslatedCallSite → FileInterceptorGroup)

    /// <summary>
    /// New pipeline entry point: takes TranslatedCallSites and orchestrates
    /// ChainAnalyzer → SqlAssembler → CarrierAnalyzer → file grouping.
    /// </summary>
    // Trace log buffer — flushed into a generated .g.cs file per group
    [ThreadStatic]
    internal static System.Text.StringBuilder? TraceLog;

    public static ImmutableArray<FileInterceptorGroup> AnalyzeAndGroupTranslated(
        ImmutableArray<TranslatedCallSite> translatedSites,
        EntityRegistry registry,
        CancellationToken ct)
    {
        TraceLog = new System.Text.StringBuilder();
        TraceLog.AppendLine($"// === AnalyzeAndGroupTranslated: {translatedSites.Length} sites ===");
        TraceCapture.Clear();

        ct.ThrowIfCancellationRequested();

        // Log all incoming sites
        foreach (var s in translatedSites)
        {
            var raw = s.Bound.Raw;
            TraceLog.AppendLine($"//   Site: Kind={raw.Kind} Method={raw.MethodName} UniqueId={raw.UniqueId} IsAnalyzable={raw.IsAnalyzable} ChainId={raw.ChainId} ContextClass={s.Bound.ContextClassName ?? "(null)"} File={System.IO.Path.GetFileName(raw.FilePath)}:{raw.Line}");
        }

        // Collect diagnostics from TranslatedCallSite properties
        var diagnostics = new List<DiagnosticInfo>();
        CollectTranslatedDiagnostics(translatedSites, diagnostics, registry);

        ct.ThrowIfCancellationRequested();

        // Chain analysis: TranslatedCallSite[] → AnalyzedChain[]
        var analyzedChains = ChainAnalyzer.Analyze(translatedSites, registry, ct);

        TraceLog.AppendLine($"// === ChainAnalyzer produced {analyzedChains.Count} chains ===");
        foreach (var chain in analyzedChains)
        {
            TraceLog.AppendLine($"//   Chain: Tier={chain.Plan.Tier} Kind={chain.Plan.Kind} ExecUniqueId={chain.ExecutionSite.Bound.Raw.UniqueId} ExecMethod={chain.ExecutionSite.Bound.Raw.MethodName} ClauseSites={chain.ClauseSites.Count}");
            foreach (var cs in chain.ClauseSites)
                TraceLog.AppendLine($"//     Clause: Kind={cs.Bound.Raw.Kind} UniqueId={cs.Bound.Raw.UniqueId}");
        }

        ct.ThrowIfCancellationRequested();

        // SQL assembly: AnalyzedChain → AssembledPlan
        var assembledPlans = new List<AssembledPlan>(analyzedChains.Count);
        foreach (var chain in analyzedChains)
        {
            var assembled = SqlAssembler.Assemble(chain, registry);
            assembledPlans.Add(assembled);
            TraceLog.AppendLine($"//   Assembled: Tier={assembled.Plan.Tier} ExecUniqueId={assembled.ExecutionSite.Bound.Raw.UniqueId} SqlVariants={assembled.SqlVariants.Count} Params={assembled.Plan.Parameters.Count} ResultType={assembled.ResultTypeName ?? "(null)"} EntityType={assembled.EntityTypeName}");
            foreach (var p in assembled.Plan.Parameters)
                TraceLog.AppendLine($"//     Param: Idx={p.GlobalIndex} Type={p.ClrType} NeedsFieldInfo={p.NeedsFieldInfoCache} IsCaptured={p.ValueExpression}");
        }

        ct.ThrowIfCancellationRequested();

        // Carrier analysis: AssembledPlan → CarrierPlan
        var carrierPlans = new List<CarrierPlan>(assembledPlans.Count);
        foreach (var assembled in assembledPlans)
        {
            var carrier = CarrierAnalyzer.AnalyzeNew(assembled);
            carrierPlans.Add(carrier);
        }

        ct.ThrowIfCancellationRequested();

        // Group into files
        return GroupTranslatedIntoFiles(translatedSites, assembledPlans, carrierPlans, diagnostics);
    }

    private static void CollectTranslatedDiagnostics(
        ImmutableArray<TranslatedCallSite> sites,
        List<DiagnosticInfo> diagnostics,
        EntityRegistry registry)
    {
        foreach (var site in sites)
        {
            var raw = site.Bound.Raw;

            // QRY001: query not analyzable (parameter receiver, variable receiver, etc.)
            if (!raw.IsAnalyzable && raw.NonAnalyzableReason != null)
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
        }
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
        var droppedSites = allSites.Where(s => string.IsNullOrEmpty(s.Bound.ContextClassName)).ToList();
        if (TraceLog != null && droppedSites.Count > 0)
        {
            TraceLog.AppendLine($"// === GroupTranslatedIntoFiles: DROPPED {droppedSites.Count} sites with empty ContextClassName ===");
            foreach (var ds in droppedSites)
                TraceLog.AppendLine($"//   Dropped: Kind={ds.Bound.Raw.Kind} UniqueId={ds.Bound.Raw.UniqueId} Entity={ds.Bound.Raw.EntityTypeName}");
        }
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

            if (TraceLog != null)
            {
                TraceLog.AppendLine($"// === FileGroup: {contextClassName} / {System.IO.Path.GetFileName(filePath)} ===");
                TraceLog.AppendLine($"//   fileSites={fileSites.Count} fileChainMemberSites={fileChainMemberSites.Count} fileAssembledPlans={fileAssembledPlans.Count} fileCarrierPlans={fileCarrierPlans.Count}");
                foreach (var s in fileSites)
                    TraceLog.AppendLine($"//   FileSite: Kind={s.Bound.Raw.Kind} UniqueId={s.Bound.Raw.UniqueId} IsAnalyzable={s.Bound.Raw.IsAnalyzable} Method={s.Bound.Raw.MethodName}");
                foreach (var s in fileChainMemberSites)
                    TraceLog.AppendLine($"//   ChainMemberSite: Kind={s.Bound.Raw.Kind} UniqueId={s.Bound.Raw.UniqueId} IsAnalyzable={s.Bound.Raw.IsAnalyzable} Method={s.Bound.Raw.MethodName}");
                foreach (var p in fileAssembledPlans)
                    TraceLog.AppendLine($"//   AssembledPlan: Tier={p.Plan.Tier} ExecUniqueId={p.ExecutionSite.Bound.Raw.UniqueId} ExecMethod={p.ExecutionSite.Bound.Raw.MethodName}");
            }

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

    #endregion
}
