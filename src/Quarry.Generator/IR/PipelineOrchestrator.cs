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
        TraceCapture.Clear();

        ct.ThrowIfCancellationRequested();

        // Collect diagnostics from TranslatedCallSite properties
        var diagnostics = new List<DiagnosticInfo>();
        CollectTranslatedDiagnostics(translatedSites, diagnostics, registry);

        ct.ThrowIfCancellationRequested();

        // Chain analysis: TranslatedCallSite[] → AnalyzedChain[]
        var analyzedChains = ChainAnalyzer.Analyze(translatedSites, registry, ct);

        ct.ThrowIfCancellationRequested();

        // SQL assembly: AnalyzedChain → AssembledPlan
        var assembledPlans = new List<AssembledPlan>(analyzedChains.Count);
        foreach (var chain in analyzedChains)
        {
            var assembled = SqlAssembler.Assemble(chain, registry);
            assembledPlans.Add(assembled);
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

}
