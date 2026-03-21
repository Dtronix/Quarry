using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.CodeAnalysis;
using Quarry.Generators.Models;
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
}
