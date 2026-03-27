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
        var analyzedChains = ChainAnalyzer.Analyze(translatedSites, registry, ct, diagnostics);

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

        // Resolve unresolved result types from chain projections (e.g., tuple types
        // that the semantic model couldn't resolve during discovery due to reassignment).
        var resultTypePatches = BuildResultTypePatches(assembledPlans);

        // Propagate chain-updated sites (e.g., JoinedEntityTypeNames from ChainAnalyzer)
        // back into the main site array so downstream code sees a single consistent view.
        // This eliminates the need for FileEmitter to conditionally select between original
        // and chain-updated sites.
        var updatedSites = PropagateChainUpdatedSites(translatedSites, assembledPlans, resultTypePatches);

        // Group into files
        return GroupTranslatedIntoFiles(updatedSites, assembledPlans, carrierPlans, diagnostics);
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
            if (resolvedType == null || resolvedType.Length == 0 || IsUnresolvedResultType(resolvedType))
                continue;

            // Patch execution site if its result type is unresolved
            if (IsUnresolvedResultType(plan.ExecutionSite.ResultTypeName))
                patches[plan.ExecutionSite.UniqueId] = resolvedType;

            // Patch clause sites with unresolved result types
            foreach (var cs in plan.ClauseSites)
            {
                if (IsUnresolvedResultType(cs.ResultTypeName))
                    patches[cs.UniqueId] = resolvedType;
            }
        }

        return patches;
    }

    /// <summary>
    /// Determines whether a non-null ResultTypeName is unresolved and needs patching.
    /// A null ResultTypeName means "no result type" (entity-only query), which is valid.
    /// </summary>
    private static bool IsUnresolvedResultType(string? resultTypeName)
    {
        if (resultTypeName == null)
            return false;
        if (resultTypeName.Length == 0 || resultTypeName == "?" || resultTypeName == "object")
            return true;

        // Tuple types with unresolved elements: "object" type parts, "?" type parts,
        // or missing type parts (e.g., "( OrderId,  Total)" where types are empty)
        if (resultTypeName.StartsWith("(") && resultTypeName.EndsWith(")"))
        {
            var inner = resultTypeName.Substring(1, resultTypeName.Length - 2);
            foreach (var element in inner.Split(','))
            {
                var trimmed = element.Trim();
                if (trimmed.Length == 0)
                    return true;

                // Named tuple element: "type name" format. Check the type part.
                var spaceIdx = trimmed.LastIndexOf(' ');
                if (spaceIdx >= 0)
                {
                    var typePart = trimmed.Substring(0, spaceIdx).Trim();
                    // Empty type part means unresolved (e.g., " OrderId" → type is empty, name is "OrderId")
                    if (typePart.Length == 0 || typePart == "object" || typePart == "?")
                        return true;
                }
                else
                {
                    // Single token — no space. Could be a type-only element like "int"
                    // or a bare "object"/"?" error type.
                    if (trimmed == "object" || trimmed == "?")
                        return true;
                }

            }

            // If the inner string starts with a space, the first element's type is empty
            // e.g., "( OrderId, decimal Total)" → first element has no type
            if (inner.Length > 0 && inner[0] == ' ')
                return true;
        }

        return false;
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

}
