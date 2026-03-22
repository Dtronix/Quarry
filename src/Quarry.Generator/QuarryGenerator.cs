using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.Generation;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;
using Quarry;
using Quarry.Generators.Parsing;
using Quarry.Generators.Utilities;
using Quarry.Shared.Migration;

namespace Quarry.Generators;

/// <summary>
/// Incremental source generator for Quarry schema and entity generation.
/// </summary>
/// <remarks>
/// This generator implements a two-phase scanning approach:
///
/// Phase 1 (Context &amp; Schema Discovery):
/// 1. Discovers classes decorated with [QuarryContext] attribute
/// 2. Extracts dialect and database schema configuration
/// 3. Discovers entities via partial QueryBuilder&lt;T&gt; properties
/// 4. Parses schema classes to extract column metadata
/// 5. Generates entity classes and column metadata
///
/// Phase 2 (Usage Site Discovery):
/// 1. Scans for InvocationExpressionSyntax nodes
/// 2. Filters for method calls on Quarry builder types
/// 3. Extracts location info for [InterceptsLocation] attributes
/// 4. Determines analyzability for optimal vs fallback path
/// 5. Generates interceptor methods for each usage site
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class QuarryGenerator : IIncrementalGenerator
{
    /// <summary>
    /// Compiled regex for matching SQL parameter placeholders (@p0, @p1, etc.).
    /// </summary>
    private static readonly Regex ParameterPlaceholderRegex = new(@"@p(\d+)", RegexOptions.Compiled);

    /// <summary>
    /// Initializes the generator.
    /// </summary>
    /// <param name="context">The initialization context.</param>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Phase 1: Register a syntax provider to find class declarations with [QuarryContext] attribute
        var contextDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsContextCandidate(node),
                transform: static (ctx, ct) => GetContextInfo(ctx, ct))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        // Phase 1 output: Per-context entity/context/metadata generation (no Collect needed)
        // Each context is independently cached — changing one context doesn't regenerate others.
        context.RegisterSourceOutput(contextDeclarations,
            static (spc, contextInfo) => GenerateEntityAndContextCode(contextInfo, spc));

        // Phase 1 diagnostics: Cross-context duplicate TypeMapping check (needs Collect)
        context.RegisterSourceOutput(contextDeclarations.Collect(),
            static (spc, contexts) => CheckDuplicateTypeMappings(contexts, spc));

        // === NEW: Build EntityRegistry from collected contexts ===
        var entityRegistry = contextDeclarations.Collect()
            .Select(static (contexts, ct) => IR.EntityRegistry.Build(contexts, ct));

        // === Stage 2: Raw Call Site Discovery (returns RawCallSite) ===
        var rawCallSites = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => UsageSiteDiscovery.IsQuarryMethodCandidate(node),
                transform: static (ctx, ct) => DiscoverRawCallSite(ctx, ct))
            .Where(static site => site is not null)
            .Select(static (site, _) => site!);

        // === Stage 3: Per-Site Binding (individually cached) ===
        var boundCallSites = rawCallSites
            .Combine(entityRegistry)
            .SelectMany(static (pair, ct) =>
            {
                try { return IR.CallSiteBinder.Bind(pair.Left, pair.Right, ct); }
                catch { return ImmutableArray<IR.BoundCallSite>.Empty; }
            });

        // === Stage 4: Per-Site Translation (individually cached) ===
        var translatedCallSites = boundCallSites
            .Select(static (bound, ct) =>
            {
                try { return IR.CallSiteTranslator.Translate(bound, ct); }
                catch { return new IR.TranslatedCallSite(bound); }
            });

        // === Stage 5: Collected Analysis + File Grouping (new pipeline) ===
        var perFileGroups = entityRegistry
            .Combine(translatedCallSites.Collect())
            .SelectMany(static (data, ct) =>
                IR.PipelineOrchestrator.AnalyzeAndGroupTranslated(data.Right, data.Left, ct));

        // Per-file output — only regenerates files whose group changed
        context.RegisterSourceOutput(perFileGroups,
            static (spc, group) => EmitFileInterceptors(spc, group));

        // Phase 3: Migration class discovery for MigrateAsync generation
        var migrationClasses = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Quarry.MigrationAttribute",
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => ExtractMigrationInfo(ctx))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        var snapshotClasses = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Quarry.MigrationSnapshotAttribute",
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => ExtractSnapshotInfo(ctx))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        // Combine migration info with context info to generate MigrateAsync
        var migrationData = contextDeclarations.Collect()
            .Combine(migrationClasses.Collect())
            .Combine(snapshotClasses.Collect());

        context.RegisterSourceOutput(migrationData,
            static (spc, source) => GenerateMigrateAsync(source.Left.Left, source.Left.Right, source.Right, spc));
    }

    /// <summary>
    /// Checks if a syntax node is a potential QuarryContext class.
    /// </summary>
    private static bool IsContextCandidate(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax classDeclaration
            && ContextParser.HasQuarryContextAttribute(classDeclaration);
    }

    /// <summary>
    /// Transforms a syntax node into context information.
    /// </summary>
    private static ContextInfo? GetContextInfo(GeneratorSyntaxContext context, CancellationToken ct)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        return ContextParser.ParseContext(classDeclaration, context.SemanticModel, ct);
    }

    /// <summary>
    /// Transforms an invocation syntax node into usage site information.
    /// </summary>
    private static UsageSiteInfo? GetUsageSiteInfo(GeneratorSyntaxContext context, CancellationToken ct)
    {
        if (context.Node is not InvocationExpressionSyntax invocation)
            return null;

        return UsageSiteDiscovery.DiscoverUsageSite(invocation, context.SemanticModel, ct);
    }

    /// <summary>
    /// Discovers a raw call site from an invocation expression for the new pipeline.
    /// </summary>
    private static IR.RawCallSite? DiscoverRawCallSite(GeneratorSyntaxContext context, CancellationToken ct)
    {
        if (context.Node is not InvocationExpressionSyntax invocation)
            return null;

        try
        {
            return UsageSiteDiscovery.DiscoverRawCallSite(invocation, context.SemanticModel, ct);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Processes translated call sites into per-file output groups.
    /// The new pipeline (Stages 2-4) has already performed discovery, binding, and
    /// translation. This method converts TranslatedCallSite to UsageSiteInfo at the
    /// boundary for ChainAnalyzer and emitter compatibility, then delegates to
    /// PipelineOrchestrator for chain analysis and file grouping.
    /// No re-enrichment occurs — the data is already fully enriched.
    /// </summary>
    private static ImmutableArray<FileInterceptorGroup> GroupByFileAndProcessTranslated(
        Compilation compilation,
        ImmutableArray<ContextInfo> contexts,
        ImmutableArray<IR.TranslatedCallSite> translatedSites,
        CancellationToken ct)
    {
        if (translatedSites.IsDefaultOrEmpty)
            return ImmutableArray<FileInterceptorGroup>.Empty;

        ct.ThrowIfCancellationRequested();

        // Build EntityRegistry (needed by PipelineOrchestrator for chain analysis)
        var registry = IR.EntityRegistry.Build(contexts, ct);

        // Build a lookup of syntax nodes by method-name location for InvocationSyntax
        // reconstruction. ChainAnalyzer needs InvocationSyntax to walk the syntax tree
        // for dataflow analysis. The collected stage is short-lived, so SyntaxNode refs are ok.
        var syntaxNodeLookup = new Dictionary<string, SyntaxNode>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            ct.ThrowIfCancellationRequested();
            var root = tree.GetRoot(ct);
            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var methodLocation = Parsing.UsageSiteDiscovery.GetMethodLocation(invocation);
                if (methodLocation == null) continue;
                var (filePath, line, col) = methodLocation.Value;
                var key = $"{filePath}:{line}:{col}";
                syntaxNodeLookup[key] = invocation;
            }
        }

        // Convert TranslatedCallSite → UsageSiteInfo with reconstructed SyntaxNode.
        // This is a thin boundary conversion — all enrichment was done in Stages 3-4.
        var usageSites = ImmutableArray.CreateBuilder<UsageSiteInfo>(translatedSites.Length);
        foreach (var ts in translatedSites)
        {
            var raw = ts.Bound.Raw;
            var key = $"{raw.FilePath}:{raw.Line}:{raw.Column}";
            syntaxNodeLookup.TryGetValue(key, out var syntaxNode);

            var adapted = UsageSiteInfo.FromTranslatedCallSite(ts, syntaxNode);
            usageSites.Add(adapted);
        }

        var allUsageSites = usageSites.ToImmutable();

        // Collect pre-analysis diagnostics
        var diagnostics = new List<DiagnosticInfo>();
        var sitesToSkip = new HashSet<UsageSiteInfo>();
        foreach (var site in allUsageSites)
        {
            if (site.ProjectionInfo?.FailureReason == ProjectionFailureReason.AnonymousTypeNotSupported)
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.AnonymousTypeNotSupported.Id,
                    site.InvocationSyntax != null
                        ? DiagnosticLocation.FromSyntaxNode(site.InvocationSyntax)
                        : new DiagnosticLocation(site.FilePath, site.Line, site.Column, default)));
                sitesToSkip.Add(site);
                continue;
            }

            if (!site.IsAnalyzable && site.NonAnalyzableReason != null)
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.QueryNotAnalyzable.Id,
                    site.InvocationSyntax != null
                        ? DiagnosticLocation.FromSyntaxNode(site.InvocationSyntax)
                        : new DiagnosticLocation(site.FilePath, site.Line, site.Column, default),
                    site.NonAnalyzableReason));
            }
        }

        ct.ThrowIfCancellationRequested();

        // Sites are already enriched by the new pipeline — no re-enrichment needed.
        var enrichedSites = allUsageSites
            .Where(s => s.IsAnalyzable && !sitesToSkip.Contains(s))
            .ToList();

        // Discover navigation join chain members (still needs syntax tree walk)
        var discoveredLocationKeys = new HashSet<string>(
            allUsageSites.Select(s => $"{s.FilePath}:{s.Line}:{s.Column}"));
        var navJoinChainSites = DiscoverNavigationJoinChainMembers(
            enrichedSites, discoveredLocationKeys, registry, compilation);
        if (navJoinChainSites.Count > 0)
            enrichedSites.AddRange(navJoinChainSites);

        // Delegate to PipelineOrchestrator — enrichSite is identity (already enriched)
        var orchestrator = new IR.PipelineOrchestrator(compilation, registry, allUsageSites, ct);
        return orchestrator.AnalyzeAndGroup(
            enrichedSites,
            navJoinChainSites,
            sitesToSkip,
            diagnostics,
            enrichSite: s => s, // Already enriched by new pipeline
            getNonTranslatableClauseKind: GetNonTranslatableClauseKind,
            analyzeChains: AnalyzeExecutionChainsWithDiagnostics);
    }

    /// <summary>
    /// Generates entity classes, context class, and metadata for a single context.
    /// Registered per-context (no Collect) so that changing one context doesn't regenerate others.
    /// </summary>
    private static void GenerateEntityAndContextCode(
        ContextInfo contextInfo,
        SourceProductionContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();

        try
        {
            // Generate entity classes and validate column types
            foreach (var entity in contextInfo.Entities)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                // Report QRY003 for columns with unsupported types and no mapping,
                // and QRY017 for TypeMapping TCustom mismatches
                foreach (var column in entity.Columns)
                {
                    if (column.CustomTypeMappingClass == null &&
                        !column.IsEnum &&
                        column.ReaderMethodName == "GetValue" &&
                        !IsKnownFallbackType(column.FullClrType))
                    {
                        var diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.InvalidColumnType,
                            entity.Location,
                            column.PropertyName,
                            column.FullClrType);
                        context.ReportDiagnostic(diagnostic);
                    }

                    if (column.MappingMismatchExpectedType != null)
                    {
                        var diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.TypeMappingMismatch,
                            entity.Location,
                            column.PropertyName,
                            column.FullClrType,
                            column.CustomTypeMappingClass,
                            column.MappingMismatchExpectedType);
                        context.ReportDiagnostic(diagnostic);
                    }
                }

                // Report QRY026 (info) for valid custom entity reader
                if (entity.CustomEntityReaderClass != null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.CustomEntityReaderActive,
                        entity.Location,
                        entity.CustomEntityReaderClass,
                        entity.EntityName));
                }

                // Report QRY027 (error) for invalid custom entity reader
                if (entity.InvalidEntityReaderClass != null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.InvalidEntityReaderType,
                        entity.Location,
                        entity.InvalidEntityReaderClass,
                        entity.SchemaClassName,
                        entity.EntityName));
                }

                // Report QRY028 (warning) for column-level Unique() overlapping with explicit unique Index()
                foreach (var column in entity.Columns)
                {
                    if (!column.Modifiers.IsUnique) continue;

                    foreach (var index in entity.Indexes)
                    {
                        if (index.IsUnique && index.Columns.Count == 1 &&
                            index.Columns[0].PropertyName == column.PropertyName)
                        {
                            context.ReportDiagnostic(Diagnostic.Create(
                                DiagnosticDescriptors.UniqueColumnOverlapsIndex,
                                entity.Location,
                                column.PropertyName,
                                index.Name));
                        }
                    }
                }

                var entitySource = EntityCodeGenerator.GenerateEntityClass(entity, contextInfo.Namespace);
                var entityFileName = $"{contextInfo.Namespace}.{entity.EntityName}.g.cs";
                context.AddSource(entityFileName, entitySource);
            }

            // Generate context class with constructors and query builder properties
            var contextSource = ContextCodeGenerator.GenerateContextClass(contextInfo);
            var contextFileName = $"{contextInfo.ClassName}.g.cs";
            context.AddSource(contextFileName, contextSource);
        }
        catch (Exception ex)
        {
            // Report diagnostic for generation failure
            var diagnostic = Diagnostic.Create(
                DiagnosticDescriptors.InternalError,
                contextInfo.Location,
                $"Failed to generate code for context '{contextInfo.ClassName}': {ex.Message}");

            context.ReportDiagnostic(diagnostic);
        }
    }

    /// <summary>
    /// Checks for duplicate TypeMappings across all contexts.
    /// Registered on collected contexts since it needs cross-context comparison.
    /// </summary>
    private static void CheckDuplicateTypeMappings(
        ImmutableArray<ContextInfo> contexts,
        SourceProductionContext context)
    {
        if (contexts.IsDefaultOrEmpty)
            return;

        // Track customType → (mappingClass, location) to detect conflicts
        var seenMappings = new Dictionary<string, (string MappingClass, Location? Location)>();

        foreach (var contextInfo in contexts)
        {
            foreach (var entity in contextInfo.Entities)
            {
                foreach (var column in entity.Columns)
                {
                    if (column.CustomTypeMappingClass == null)
                        continue;

                    if (seenMappings.TryGetValue(column.FullClrType, out var existing))
                    {
                        if (existing.MappingClass != column.CustomTypeMappingClass)
                        {
                            context.ReportDiagnostic(Diagnostic.Create(
                                DiagnosticDescriptors.DuplicateTypeMapping,
                                entity.Location,
                                column.FullClrType,
                                existing.MappingClass,
                                column.CustomTypeMappingClass));
                        }
                    }
                    else
                    {
                        seenMappings[column.FullClrType] = (column.CustomTypeMappingClass, entity.Location);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Groups all usage sites by (context, source file) and processes them into FileInterceptorGroups.
    /// Replaces the old Execute()+GenerateInterceptors() flow with per-file incremental output.
    /// </summary>
    private static ImmutableArray<FileInterceptorGroup> GroupByFileAndProcess(
        Compilation compilation,
        ImmutableArray<ContextInfo> contexts,
        ImmutableArray<UsageSiteInfo> usageSites,
        CancellationToken ct)
    {
        if (usageSites.IsDefaultOrEmpty)
            return ImmutableArray<FileInterceptorGroup>.Empty;

        ct.ThrowIfCancellationRequested();

        // Build EntityRegistry from all contexts — single source of entity metadata
        var registry = IR.EntityRegistry.Build(contexts, ct);

        // Collect pre-enrichment diagnostics and identify sites to skip
        var diagnostics = new List<DiagnosticInfo>();
        var sitesToSkip = new HashSet<UsageSiteInfo>();
        foreach (var site in usageSites)
        {
            if (site.ProjectionInfo?.FailureReason == ProjectionFailureReason.AnonymousTypeNotSupported)
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.AnonymousTypeNotSupported.Id,
                    DiagnosticLocation.FromSyntaxNode(site.InvocationSyntax)));
                sitesToSkip.Add(site);
                continue;
            }

            if (!site.IsAnalyzable && site.NonAnalyzableReason != null)
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.QueryNotAnalyzable.Id,
                    DiagnosticLocation.FromSyntaxNode(site.InvocationSyntax),
                    site.NonAnalyzableReason));
            }
        }

        ct.ThrowIfCancellationRequested();

        // Enrich usage sites with entity metadata
        var enrichedSites = usageSites
            .Where(s => s.IsAnalyzable && !sitesToSkip.Contains(s))
            .Select(s => EnrichUsageSiteWithEntityInfo(s, registry, compilation))
            .ToList();

        // Discover and enrich missing chain members from resolved navigation joins
        var discoveredLocationKeys = new HashSet<string>(
            usageSites.Select(s => $"{s.FilePath}:{s.Line}:{s.Column}"));
        var navJoinChainSites = DiscoverNavigationJoinChainMembers(
            enrichedSites, discoveredLocationKeys, registry, compilation);
        if (navJoinChainSites.Count > 0)
            enrichedSites.AddRange(navJoinChainSites);

        // Delegate chain analysis, diagnostic collection, and file grouping to PipelineOrchestrator
        var orchestrator = new IR.PipelineOrchestrator(compilation, registry, usageSites, ct);
        return orchestrator.AnalyzeAndGroup(
            enrichedSites,
            navJoinChainSites,
            sitesToSkip,
            diagnostics,
            enrichSite: s => EnrichUsageSiteWithEntityInfo(s, registry, compilation),
            getNonTranslatableClauseKind: GetNonTranslatableClauseKind,
            analyzeChains: AnalyzeExecutionChainsWithDiagnostics);
    }

    /// <summary>
    /// Emits interceptor source and reports diagnostics for a single (context, file) group.
    /// </summary>
    private static void EmitFileInterceptors(SourceProductionContext spc, FileInterceptorGroup group)
    {
        // Report all deferred diagnostics
        foreach (var diag in group.Diagnostics)
        {
            var descriptor = GetDescriptorById(diag.DiagnosticId);
            if (descriptor == null) continue;

            var location = diag.Location.FilePath != null
                ? Location.Create(diag.Location.FilePath,
                    default,
                    new Microsoft.CodeAnalysis.Text.LinePositionSpan(
                        new Microsoft.CodeAnalysis.Text.LinePosition(diag.Location.Line - 1, diag.Location.Column - 1),
                        new Microsoft.CodeAnalysis.Text.LinePosition(diag.Location.Line - 1, diag.Location.Column - 1)))
                : Location.None;

            spc.ReportDiagnostic(Diagnostic.Create(descriptor, location, diag.MessageArgs));
        }

        if (group.IsNewPipeline)
        {
            EmitFileInterceptorsNewPipeline(spc, group);
        }
        else
        {
            EmitFileInterceptorsLegacy(spc, group);
        }
    }

    private static void EmitFileInterceptorsNewPipeline(SourceProductionContext spc, FileInterceptorGroup group)
    {
        var trace = IR.PipelineOrchestrator.TraceLog ?? new System.Text.StringBuilder();
        trace.AppendLine($"// === EmitFileInterceptorsNewPipeline: {group.ContextClassName} / {System.IO.Path.GetFileName(group.SourceFilePath)} ===");
        trace.AppendLine($"//   TranslatedSites={group.TranslatedSites.Count} TranslatedChainMemberSites={group.TranslatedChainMemberSites.Count} AssembledPlans={group.AssembledPlans.Count} SourceFilePath={System.IO.Path.GetFileName(group.SourceFilePath)}");

        // Build enriched projection lookup from chains (keyed by Select clause site UniqueId)
        // During discovery, entity types are generated by the same source generator and invisible
        // to the semantic model, so ProjectionInfo has broken column types. ChainAnalyzer enriches
        // these using EntityRef.Columns. We propagate the enriched projections to the clause sites.
        var enrichedProjections = new Dictionary<string, ProjectionInfo>(StringComparer.Ordinal);
        foreach (var assembled in group.AssembledPlans)
        {
            if (assembled.Plan.Projection != null && assembled.Plan.Projection.Columns.Count > 0)
            {
                // Strip SqlExpression from non-aggregate columns for bridge compatibility
                var bridgeCols = StripNonAggregateSqlExpressions(assembled.Plan.Projection.Columns);
                var enrichedProj = new ProjectionInfo(
                    assembled.Plan.Projection.Kind,
                    assembled.Plan.Projection.ResultTypeName,
                    bridgeCols,
                    customEntityReaderClass: assembled.Plan.Projection.CustomEntityReaderClass);

                foreach (var cs in assembled.ClauseSites)
                {
                    if (cs.Bound.Raw.Kind == InterceptorKind.Select)
                        enrichedProjections[cs.Bound.Raw.UniqueId] = enrichedProj;
                }
            }
        }

        // Convert TranslatedCallSite → UsageSiteInfo for emitter compatibility (temporary bridge)
        var mergedSites = new List<UsageSiteInfo>(
            group.TranslatedSites.Count + group.TranslatedChainMemberSites.Count);
        foreach (var ts in group.TranslatedSites)
        {
            var enrichedProj = ResolveEnrichedProjection(ts, enrichedProjections);
            mergedSites.Add(UsageSiteInfo.FromTranslatedCallSite(ts, projectionOverride: enrichedProj));
        }
        foreach (var ts in group.TranslatedChainMemberSites)
        {
            var enrichedProj = ResolveEnrichedProjection(ts, enrichedProjections);
            mergedSites.Add(UsageSiteInfo.FromTranslatedCallSite(ts, projectionOverride: enrichedProj));
        }

        trace.AppendLine($"//   mergedSites={mergedSites.Count}");
        foreach (var ms in mergedSites)
            trace.AppendLine($"//     MergedSite: Kind={ms.Kind} UniqueId={ms.UniqueId} IsAnalyzable={ms.IsAnalyzable} Method={ms.MethodName}");

        if (mergedSites.Count == 0) return;

        // Convert AssembledPlan → PrebuiltChainInfo for emitter compatibility (temporary bridge)
        var chains = new List<PrebuiltChainInfo>();
        foreach (var assembled in group.AssembledPlans)
        {
            if (assembled.Plan.Tier == OptimizationTier.RuntimeBuild)
            {
                trace.AppendLine($"//   SKIPPED RuntimeBuild chain: ExecUniqueId={assembled.ExecutionSite.Bound.Raw.UniqueId} ExecMethod={assembled.ExecutionSite.Bound.Raw.MethodName}");
                continue;
            }

            // Skip chains with incomplete tuple result types (e.g., "( Name,  Total)" instead of
            // "(string Name, decimal Total)"). This happens when a joined query with Select projection
            // is stored in a variable and executed on a separate statement — the chain can't see the
            // Select clause, and the semantic model can't resolve generated entity types in tuple elements.
            if (assembled.ResultTypeName != null
                && assembled.ResultTypeName.StartsWith("(")
                && assembled.ResultTypeName.Contains(",")
                && System.Text.RegularExpressions.Regex.IsMatch(assembled.ResultTypeName, @"\(\s\w"))
            {
                trace.AppendLine($"//   SKIPPED incomplete tuple result: ExecUniqueId={assembled.ExecutionSite.Bound.Raw.UniqueId} ResultType={assembled.ResultTypeName}");
                continue;
            }

            // Convert AssembledSqlVariant → PrebuiltSqlResult
            var sqlMap = new Dictionary<ulong, Sql.PrebuiltSqlResult>();
            foreach (var kvp in assembled.SqlVariants)
                sqlMap[kvp.Key] = new Sql.PrebuiltSqlResult(kvp.Value.Sql, kvp.Value.ParameterCount);

            // Build ChainAnalysisResult from QueryPlan
            var executionUsageSite = UsageSiteInfo.FromTranslatedCallSite(assembled.ExecutionSite);

            // Build a lookup from clause site UniqueId → conditional bit index
            // ConditionalTerms are ordered matching the conditional clause sites
            var conditionalBitLookup = new Dictionary<string, int>(StringComparer.Ordinal);
            {
                int condIdx = 0;
                foreach (var cs in assembled.ClauseSites)
                {
                    if (cs.Bound.Raw.ConditionalInfo != null && condIdx < assembled.Plan.ConditionalTerms.Count)
                    {
                        conditionalBitLookup[cs.Bound.Raw.UniqueId] = assembled.Plan.ConditionalTerms[condIdx].BitIndex;
                        condIdx++;
                    }
                }
            }

            var clauseChainedSites = new List<ChainedClauseSite>();
            foreach (var cs in assembled.ClauseSites)
            {
                var role = ChainAnalyzer.MapInterceptorKindToClauseRole(cs.Bound.Raw.Kind);
                if (role != null)
                {
                    conditionalBitLookup.TryGetValue(cs.Bound.Raw.UniqueId, out var bitIdx);
                    var hasBit = conditionalBitLookup.ContainsKey(cs.Bound.Raw.UniqueId);
                    clauseChainedSites.Add(new ChainedClauseSite(
                        UsageSiteInfo.FromTranslatedCallSite(cs),
                        isConditional: cs.Bound.Raw.ConditionalInfo != null,
                        bitIndex: hasBit ? (int?)bitIdx : null,
                        role: role.Value));
                }
            }

            var conditionalClauses = new List<ConditionalClause>();
            foreach (var ct in assembled.Plan.ConditionalTerms)
            {
                // Find the matching site for this conditional term
                UsageSiteInfo? matchingSite = null;
                foreach (var ccs in clauseChainedSites)
                {
                    if (ccs.BitIndex == ct.BitIndex)
                    {
                        matchingSite = ccs.Site;
                        break;
                    }
                }
                conditionalClauses.Add(new ConditionalClause(ct.BitIndex, matchingSite ?? executionUsageSite, BranchKind.Independent));
            }

            var analysis = new ChainAnalysisResult(
                assembled.Plan.Tier,
                clauseChainedSites,
                executionUsageSite,
                conditionalClauses,
                assembled.Plan.PossibleMasks);

            // Build chain parameters from QueryPlan.Parameters
            var chainParams = new List<ChainParameterInfo>();
            foreach (var p in assembled.Plan.Parameters)
            {
                chainParams.Add(new ChainParameterInfo(
                    index: p.GlobalIndex,
                    typeName: p.ClrType,
                    valueExpression: p.ValueExpression,
                    typeMapping: p.TypeMappingClass,
                    isSensitive: p.IsSensitive,
                    isEnum: p.IsEnum,
                    enumUnderlyingType: p.EnumUnderlyingType,
                    needsFieldInfoCache: p.NeedsFieldInfoCache,
                    isCollection: p.IsCollection,
                    elementTypeName: p.ElementTypeName,
                    isDirectAccessible: p.IsDirectAccessible,
                    collectionAccessExpression: p.CollectionAccessExpression,
                    entityPropertyExpression: p.EntityPropertyExpression));
            }

            // Resolve joined table infos
            List<(string, string?)>? joinedTableInfos = null;
            if (assembled.ExecutionSite.JoinedEntityTypeNames != null)
            {
                joinedTableInfos = new List<(string, string?)>();
                foreach (var join in assembled.Plan.Joins)
                    joinedTableInfos.Add((join.Table.TableName, join.Table.SchemaName));
            }

            // Resolve ProjectionInfo: prefer QueryPlan.Projection (enriched by ChainAnalyzer with entity
            // metadata), then fall back to Select clause site's raw ProjectionInfo or execution site's.
            // The discovery-time ProjectionInfo has broken columns because the source generator can't see
            // its own generated entity types. ChainAnalyzer enriches these using EntityRef.Columns.
            ProjectionInfo? projInfo = null;
            // First try: QueryPlan.Projection (enriched by ChainAnalyzer with entity column metadata)
            if (assembled.Plan.Projection != null)
            {
                if (assembled.Plan.Projection.Columns.Count > 0)
                {
                    var bridgeColumns = StripNonAggregateSqlExpressions(assembled.Plan.Projection.Columns);
                    projInfo = new ProjectionInfo(
                        assembled.Plan.Projection.Kind,
                        assembled.Plan.Projection.ResultTypeName,
                        bridgeColumns,
                        customEntityReaderClass: assembled.Plan.Projection.CustomEntityReaderClass);
                }
                else if (assembled.Plan.Projection.IsIdentity)
                {
                    // Identity projection (no Select clause) — build from entity columns
                    var entityRef = assembled.ExecutionSite.Bound.Entity;
                    if (entityRef != null && entityRef.Columns.Count > 0)
                    {
                        projInfo = BuildEntityProjectionFromEntityRef(entityRef);
                    }
                }
            }
            // Second try: Select clause site's raw ProjectionInfo
            if (projInfo == null)
            {
                foreach (var cs in assembled.ClauseSites)
                {
                    if (cs.Bound.Raw.Kind == InterceptorKind.Select && cs.Bound.Raw.ProjectionInfo != null)
                    {
                        projInfo = cs.Bound.Raw.ProjectionInfo;
                        break;
                    }
                }
            }
            // Third try: execution site's ProjectionInfo
            if (projInfo == null)
                projInfo = assembled.ExecutionSite.ProjectionInfo;

            // Generate reader delegate code for SELECT queries (the old pipeline did this via ReaderCodeGenerator)
            string? readerCode = assembled.ReaderDelegateCode;
            if (readerCode == null && assembled.Plan.Kind == QueryKind.Select && projInfo != null)
            {
                var entityType = InterceptorCodeGenerator.GetShortTypeName(assembled.EntityTypeName);
                readerCode = Projection.ReaderCodeGenerator.GenerateReaderDelegate(projInfo, entityType);
            }

            var chain = new PrebuiltChainInfo(
                analysis: analysis,
                sqlMap: sqlMap,
                readerDelegateCode: readerCode,
                entityTypeName: assembled.EntityTypeName,
                resultTypeName: assembled.ResultTypeName,
                dialect: assembled.Dialect,
                tableName: assembled.ExecutionSite.TableName,
                schemaName: assembled.ExecutionSite.SchemaName,
                queryKind: assembled.Plan.Kind,
                projectionInfo: projInfo,
                joinedEntityTypeNames: assembled.ExecutionSite.JoinedEntityTypeNames,
                joinedTableInfos: joinedTableInfos,
                chainParameters: chainParams,
                isCarrierEligible: group.CarrierPlans.Count > 0 && group.CarrierPlans.Any(cp => cp.IsEligible),
                entitySchemaNamespace: assembled.EntitySchemaNamespace);

            chains.Add(chain);
        }

        trace.AppendLine($"//   chains={chains.Count}");
        foreach (var ch in chains)
        {
            trace.AppendLine($"//     Chain: Tier={ch.Analysis.Tier} ExecUniqueId={ch.Analysis.ExecutionSite.UniqueId} ExecMethod={ch.Analysis.ExecutionSite.MethodName} Clauses={ch.Analysis.Clauses.Count}");
            foreach (var cl in ch.Analysis.Clauses)
                trace.AppendLine($"//       Clause: UniqueId={cl.Site.UniqueId} Kind={cl.Site.Kind} Role={cl.Role}");
        }

        // Check for UniqueId mismatches between chains and mergedSites
        var mergedSiteIds = new HashSet<string>(mergedSites.Select(s => s.UniqueId));
        foreach (var ch in chains)
        {
            if (!mergedSiteIds.Contains(ch.Analysis.ExecutionSite.UniqueId))
                trace.AppendLine($"//   WARNING: Chain exec site {ch.Analysis.ExecutionSite.UniqueId} ({ch.Analysis.ExecutionSite.MethodName}) NOT in mergedSites!");
            foreach (var cl in ch.Analysis.Clauses)
            {
                if (!mergedSiteIds.Contains(cl.Site.UniqueId))
                    trace.AppendLine($"//   WARNING: Chain clause site {cl.Site.UniqueId} ({cl.Site.Kind}) NOT in mergedSites!");
            }
        }

        // Emit trace file
        try
        {
            var traceFileName = $"__Trace.{group.ContextClassName}.{group.FileTag}.g.cs";
            spc.AddSource(traceFileName, trace.ToString());
        }
        catch { }

        try
        {
            var emitter = new CodeGen.FileEmitter(
                group.ContextClassName,
                group.ContextNamespace,
                group.FileTag,
                mergedSites,
                chains);
            var interceptorsSource = emitter.Emit();
            var fileName = $"{group.ContextClassName}.Interceptors.{group.FileTag}.g.cs";
            spc.AddSource(fileName, interceptorsSource);
        }
        catch (Exception ex)
        {
            // Include stack trace in trace log for diagnosis
            trace.AppendLine($"// EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            trace.AppendLine($"// StackTrace: {ex.StackTrace?.Replace("\r\n", " | ").Replace("\n", " | ")}");
            try { spc.AddSource($"__Trace.{group.ContextClassName}.{group.FileTag}.g.cs", trace.ToString()); } catch { }

            spc.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.InternalError,
                Location.None,
                $"Failed to generate interceptors: {ex.Message}"));
        }
    }

    private static void EmitFileInterceptorsLegacy(SourceProductionContext spc, FileInterceptorGroup group)
    {
        // Merge sites and chain member sites
        var mergedSites = new List<UsageSiteInfo>(group.Sites.Count + group.ChainMemberSites.Count);
        mergedSites.AddRange(group.Sites);
        mergedSites.AddRange(group.ChainMemberSites);

        if (mergedSites.Count == 0)
            return;

        try
        {
            var emitter = new CodeGen.FileEmitter(
                group.ContextClassName,
                group.ContextNamespace,
                group.FileTag,
                mergedSites,
                group.Chains as IReadOnlyList<PrebuiltChainInfo>);
            var interceptorsSource = emitter.Emit();

            var fileName = $"{group.ContextClassName}.Interceptors.{group.FileTag}.g.cs";
            spc.AddSource(fileName, interceptorsSource);
        }
        catch (Exception ex)
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.InternalError,
                Location.None,
                $"Failed to generate interceptors: {ex.Message}"));
        }
    }

    /// <summary>
    /// Maps a diagnostic ID to its descriptor for deferred reporting.
    /// </summary>
    private static readonly Dictionary<string, DiagnosticDescriptor> s_deferredDescriptors = new[]
    {
        DiagnosticDescriptors.QueryNotAnalyzable,
        DiagnosticDescriptors.AnonymousTypeNotSupported,
        DiagnosticDescriptors.AmbiguousContextResolution,
        DiagnosticDescriptors.UnboundParameterPlaceholder,
        DiagnosticDescriptors.ClauseNotTranslatable,
        DiagnosticDescriptors.ChainOptimizedTier1,
        DiagnosticDescriptors.ChainOptimizedTier2,
        DiagnosticDescriptors.ChainNotAnalyzable,
        DiagnosticDescriptors.ForkedQueryChain,
    }.ToDictionary(d => d.Id);

    private static DiagnosticDescriptor? GetDescriptorById(string id) =>
        s_deferredDescriptors.TryGetValue(id, out var descriptor) ? descriptor : null;

    /// <summary>
    /// Analyzes execution chains for pre-built SQL optimization.
    /// Identifies execution call sites, performs dataflow analysis on their query chains,
    /// builds pre-built SQL maps for tier 1 chains, and collects QRY030-032 diagnostics.
    /// </summary>
    private static (IReadOnlyList<PrebuiltChainInfo> Chains, List<DiagnosticInfo> Diagnostics) AnalyzeExecutionChainsWithDiagnostics(
        Compilation compilation,
        List<UsageSiteInfo> enrichedSites,
        IR.EntityRegistry registry,
        CancellationToken ct)
    {
        var prebuiltChains = new List<PrebuiltChainInfo>();
        var diagnostics = new List<DiagnosticInfo>();

        // Find execution sites (including joined builders)
        var executionSites = enrichedSites
            .Where(s => ChainAnalyzer.IsExecutionKind(s.Kind))
            .ToList();

        if (executionSites.Count == 0)
            return (prebuiltChains, diagnostics);

        // Group all sites by syntax tree for efficient lookup
        var sitesByTree = enrichedSites
            .Where(s => s.InvocationSyntax?.SyntaxTree != null)
            .GroupBy(s => s.InvocationSyntax!.SyntaxTree!)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<UsageSiteInfo>)g.ToList());

        foreach (var executionSite in executionSites)
        {
            ct.ThrowIfCancellationRequested();

            var tree = executionSite.InvocationSyntax?.SyntaxTree;
            if (tree == null)
                continue;

            // Get the semantic model for this tree
            SemanticModel semanticModel;
            try
            {
                semanticModel = compilation.GetSemanticModel(tree);
            }
            catch
            {
                continue;
            }

            // Get all sites in the same syntax tree
            if (!sitesByTree.TryGetValue(tree, out var sitesInTree))
                continue;

            var result = ChainAnalyzer.AnalyzeChain(
                executionSite, sitesInTree, semanticModel, ct);

            if (result == null)
                continue;

            // Collect diagnostic based on tier
            var diagLocation = executionSite.InvocationSyntax != null
                ? DiagnosticLocation.FromSyntaxNode(executionSite.InvocationSyntax)
                : new DiagnosticLocation(executionSite.FilePath, executionSite.Line, executionSite.Column, default);
            var locationDisplay = $"{GetRelativePath(executionSite.FilePath)}:{executionSite.Line}";

            switch (result.Tier)
            {
                case OptimizationTier.PrebuiltDispatch:
                    diagnostics.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.ChainOptimizedTier1.Id,
                        diagLocation,
                        locationDisplay,
                        result.PossibleMasks.Count.ToString()));
                    break;

                case OptimizationTier.PrequotedFragments:
                    diagnostics.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.ChainOptimizedTier2.Id,
                        diagLocation,
                        locationDisplay,
                        result.ConditionalClauses.Count.ToString()));
                    break;

                case OptimizationTier.RuntimeBuild:
                    if (result.ForkedVariableName != null)
                    {
                        // QRY033: Forked chain — error severity
                        diagnostics.Add(new DiagnosticInfo(
                            DiagnosticDescriptors.ForkedQueryChain.Id,
                            diagLocation,
                            result.ForkedVariableName));
                    }
                    else
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            DiagnosticDescriptors.ChainNotAnalyzable.Id,
                            diagLocation,
                            locationDisplay,
                            result.NotAnalyzableReason ?? "unknown reason"));
                    }
                    break;
            }

            // Build PrebuiltChainInfo for tier 1 chains.
            if (result.Tier == OptimizationTier.PrebuiltDispatch)
            {
                PrebuiltChainInfo? chainInfo;
                if (executionSite.JoinedEntityTypeNames != null && executionSite.JoinedEntityTypeNames.Count >= 2)
                    chainInfo = BuildPrebuiltChainInfoForJoin(result, executionSite, registry);
                else
                    chainInfo = BuildPrebuiltChainInfo(result, executionSite, registry);
                if (chainInfo != null)
                    prebuiltChains.Add(chainInfo);
            }
        }

        return (prebuiltChains, diagnostics);
    }

    /// <summary>
    /// Builds a <see cref="PrebuiltChainInfo"/> for a tier 1 chain by resolving entity metadata
    /// and building the compile-time SQL map.
    /// </summary>
    private static PrebuiltChainInfo? BuildPrebuiltChainInfo(
        ChainAnalysisResult result,
        UsageSiteInfo executionSite,
        IR.EntityRegistry registry)
    {
        var resolved = registry.Resolve(executionSite.EntityTypeName, executionSite.ContextClassName);
        if (resolved == null)
            return null;

        var entity = resolved.Entity;
        var ctx = resolved.Context;
        var dialect = ctx.Dialect;
        var tableName = entity.TableName;
        var schemaName = ctx.Schema;

        // Determine query kind from execution site
        var queryKind = DetermineQueryKind(executionSite);
        if (queryKind == null)
            return null;

        // Build SQL map using CompileTimeSqlBuilder
        Dictionary<ulong, PrebuiltSqlResult>? sqlMap;
        try
        {
            if (queryKind.Value == QueryKind.Insert)
            {
                // Insert SQL is built from entity metadata, not from clause analysis
                var insertInfo = executionSite.InsertInfo;
                if (insertInfo == null || insertInfo.Columns.Count == 0)
                    return null;
                var columns = insertInfo.Columns.Select(c => c.QuotedColumnName).ToList();
                // ExecuteScalar and ToDiagnostics include RETURNING clause; ExecuteNonQuery does not.
                // Pass unquoted name — BuildInsertSql applies dialect-specific quoting.
                var identityCol = executionSite.Kind is InterceptorKind.InsertExecuteScalar or InterceptorKind.InsertToDiagnostics
                    ? insertInfo.IdentityColumnName : null;
                var insertResult = CompileTimeSqlBuilder.BuildInsertSql(
                    dialect, tableName, schemaName, columns, columns.Count, identityCol);
                sqlMap = new Dictionary<ulong, PrebuiltSqlResult>
                {
                    [0UL] = new PrebuiltSqlResult(insertResult.Sql, columns.Count)
                };
            }
            else
            {
                // For ToDiagnostics terminals, extract constant Limit/Offset values to inline as literals.
                // If any Limit/Offset clause has a non-constant value, skip prebuilt chain
                // (the runtime ToDiagnostics path handles variable values correctly).
                var pagination = ExtractLiteralPagination(result, executionSite.Kind);
                if (pagination.HasVariablePagination)
                    return null;

                sqlMap = queryKind.Value switch
                {
                    QueryKind.Select => CompileTimeSqlBuilder.BuildSelectSqlMap(
                        result.PossibleMasks, result.Clauses, dialect, tableName, schemaName,
                        literalLimit: pagination.Limit, literalOffset: pagination.Offset),
                    QueryKind.Delete => CompileTimeSqlBuilder.BuildDeleteSqlMap(
                        result.PossibleMasks, result.Clauses, dialect, tableName, schemaName),
                    QueryKind.Update => CompileTimeSqlBuilder.BuildUpdateSqlMap(
                        result.PossibleMasks, result.Clauses, dialect, tableName, schemaName),
                    _ => null
                };
            }
        }
        catch
        {
            // If SQL building fails for any reason, skip this chain
            return null;
        }

        if (sqlMap == null || sqlMap.Count == 0)
            return null;

        // Bail out if any clause has captured parameters that can't generate direct extraction paths.
        // Without extraction, the BindParam(p0) reference is unresolved, causing compile errors.
        // Exception: UpdateSetAction captures use delegate.Target + FieldInfo (not expression tree
        // paths), so they don't need CanGenerateDirectPath.
        foreach (var clause in result.Clauses)
        {
            if (clause.Site.Kind == InterceptorKind.UpdateSetAction)
                continue;

            if (clause.Site.ClauseInfo?.Parameters is { Count: > 0 } clauseParams)
            {
                if (clauseParams.Any(p => p.IsCaptured && !p.CanGenerateDirectPath))
                    return null;
            }
        }

        // Find the Select clause for reader delegate (SELECT queries only)
        string? readerCode = null;
        ProjectionInfo? projInfo = null;
        if (queryKind.Value == QueryKind.Select)
        {
            var selectClause = result.Clauses
                .Where(c => c.Role == ClauseRole.Select)
                .Select(c => c.Site)
                .FirstOrDefault(s => s.ProjectionInfo != null);

            if (selectClause?.ProjectionInfo != null)
            {
                projInfo = selectClause.ProjectionInfo;
                var entityType = InterceptorCodeGenerator.GetShortTypeName(executionSite.EntityTypeName);
                readerCode = Quarry.Generators.Projection.ReaderCodeGenerator.GenerateReaderDelegate(projInfo, entityType);
            }
            else
            {
                // No Select clause — identity projection (SELECT *).
                // Build a ProjectionInfo from EntityInfo for the reader delegate.
                projInfo = BuildEntityProjectionFromEntityInfo(entity);
                if (projInfo != null)
                {
                    var entityType = InterceptorCodeGenerator.GetShortTypeName(executionSite.EntityTypeName);
                    readerCode = Quarry.Generators.Projection.ReaderCodeGenerator.GenerateReaderDelegate(projInfo, entityType);
                }
            }
        }

        // Build chain parameter info for carrier optimization.
        // Carrier eligibility mirrors execution terminal checks — if the terminal would be
        // skipped by GenerateInterceptorMethod, the chain must NOT be carrier-eligible,
        // otherwise clause interceptors create carriers with no terminal to consume them.
        var chainParams = BuildChainParameters(result);
        var isCarrierEligible = chainParams != null
            && result.UnmatchedMethodNames == null;

        if (isCarrierEligible && queryKind.Value == QueryKind.Select)
        {
            // SELECT terminal checks: result type resolution, reader delegate, ambiguous columns
            // ToDiagnostics doesn't read rows, so skip the reader check for it.
            var isToDiag = executionSite.Kind == InterceptorKind.ToDiagnostics;
            var resolvedResult = InterceptorCodeGenerator.ResolveExecutionResultTypePublic(
                executionSite.ResultTypeName, executionSite.ResultTypeName, projInfo);
            if (string.IsNullOrEmpty(resolvedResult))
                isCarrierEligible = false;
            else if (readerCode == null && !isToDiag)
                isCarrierEligible = false;
            else if (projInfo != null && projInfo.Columns.Any(c =>
                c.SqlExpression != null && !string.IsNullOrEmpty(c.ColumnName)))
                isCarrierEligible = false;
        }
        else if (isCarrierEligible && queryKind.Value is QueryKind.Delete or QueryKind.Update)
        {
            // NonQuery terminal checks: SQL variants must be non-empty and well-formed
            if (sqlMap.Values.Any(v => string.IsNullOrWhiteSpace(v.Sql)
                || (queryKind.Value == QueryKind.Update && v.Sql.Contains("SET  "))))
                isCarrierEligible = false;
        }
        else if (isCarrierEligible && queryKind.Value == QueryKind.Insert)
        {
            // Insert terminal checks: SQL must be non-empty
            if (sqlMap.Values.Any(v => string.IsNullOrWhiteSpace(v.Sql)))
                isCarrierEligible = false;
            // MySQL ExecuteScalar requires a separate SELECT LAST_INSERT_ID() query,
            // which can't be done with a single carrier DbCommand.
            if (executionSite.Kind == InterceptorKind.InsertExecuteScalar && dialect == SqlDialect.MySQL)
                isCarrierEligible = false;
        }

        // Post-process SQL to tokenize collection parameter placeholders for carrier expansion.
        if (isCarrierEligible && chainParams != null)
            TokenizeCollectionParameters(sqlMap, chainParams, dialect);

        return new PrebuiltChainInfo(
            result, sqlMap, readerCode,
            executionSite.EntityTypeName, executionSite.ResultTypeName,
            dialect, tableName, schemaName, queryKind.Value, projInfo,
            chainParameters: chainParams,
            isCarrierEligible: isCarrierEligible,
            entitySchemaNamespace: entity.SchemaNamespace);
    }

    /// <summary>
    /// Builds an entity projection from EntityInfo for identity projections (no Select clause).
    /// Used when the chain has no explicit <c>.Select()</c> call, meaning the result type is
    /// the entity itself and all columns should be read.
    /// </summary>
    private static ProjectionInfo? BuildEntityProjectionFromEntityInfo(EntityInfo entity)
    {
        if (entity.Columns.Count == 0)
            return null;

        var columns = new List<ProjectedColumn>();
        var ordinal = 0;
        foreach (var col in entity.Columns)
        {
            columns.Add(new ProjectedColumn(
                propertyName: col.PropertyName,
                columnName: col.ColumnName,
                clrType: col.ClrType,
                fullClrType: col.FullClrType,
                isNullable: col.IsNullable,
                ordinal: ordinal++,
                customTypeMapping: col.CustomTypeMappingClass,
                isValueType: col.IsValueType,
                readerMethodName: col.DbReaderMethodName ?? col.ReaderMethodName,
                isForeignKey: col.Kind == ColumnKind.ForeignKey,
                foreignKeyEntityName: col.ReferencedEntityName,
                isEnum: col.IsEnum));
        }

        return new ProjectionInfo(ProjectionKind.Entity, entity.EntityName, columns,
            customEntityReaderClass: entity.CustomEntityReaderClass);
    }

    /// <summary>
    /// Builds an entity projection from an EntityRef for identity projections in the new pipeline bridge.
    /// Resolves an enriched ProjectionInfo for a translated call site.
    /// First checks the chain-level enrichment lookup, then enriches standalone Select sites
    /// using EntityRef column metadata.
    /// </summary>
    private static ProjectionInfo? ResolveEnrichedProjection(
        IR.TranslatedCallSite ts,
        Dictionary<string, ProjectionInfo> enrichedProjections)
    {
        // Check chain-level enrichment first
        if (enrichedProjections.TryGetValue(ts.Bound.Raw.UniqueId, out var enrichedProj))
            return enrichedProj;

        // For standalone Select sites, enrich the raw ProjectionInfo using entity column metadata
        if (ts.Bound.Raw.Kind == InterceptorKind.Select && ts.Bound.Raw.ProjectionInfo != null)
        {
            var rawProj = ts.Bound.Raw.ProjectionInfo;
            var entityRef = ts.Bound.Entity;
            var isJoined = ts.Bound.JoinedEntities != null && ts.Bound.JoinedEntities.Count >= 2;

            // Build per-alias lookups for joined queries, flat lookup for single-entity
            Dictionary<string, Dictionary<string, ColumnInfo>>? perAliasLookup = null;
            var colLookup = new Dictionary<string, ColumnInfo>(System.StringComparer.Ordinal);

            if (isJoined)
            {
                perAliasLookup = new Dictionary<string, Dictionary<string, ColumnInfo>>(System.StringComparer.Ordinal);
                for (int i = 0; i < ts.Bound.JoinedEntities!.Count; i++)
                {
                    var je = ts.Bound.JoinedEntities[i];
                    var alias = $"t{i}";
                    var lookup = new Dictionary<string, ColumnInfo>(System.StringComparer.Ordinal);
                    foreach (var ec in je.Columns)
                        lookup[ec.PropertyName] = ec;
                    perAliasLookup[alias] = lookup;
                    // Also add to flat lookup for aggregate resolution
                    foreach (var ec in je.Columns)
                        colLookup[ec.PropertyName] = ec;
                }
            }
            else if (entityRef != null && entityRef.Columns.Count > 0)
            {
                foreach (var ec in entityRef.Columns)
                    colLookup[ec.PropertyName] = ec;
            }

            if ((colLookup.Count > 0 || perAliasLookup != null) && rawProj.Columns.Count > 0)
            {

                var enrichedColumns = new List<ProjectedColumn>();
                bool anyEnriched = false;
                foreach (var col in rawProj.Columns)
                {
                    // Aggregate columns with unresolved type: resolve from referenced entity column
                    if (col.IsAggregateFunction && (col.ClrType == "object" || string.IsNullOrWhiteSpace(col.ClrType))
                        && col.SqlExpression != null)
                    {
                        var resolvedType = Parsing.ChainAnalyzer.TryResolveAggregateTypeFromSqlPublic(
                            col.SqlExpression, colLookup);
                        if (resolvedType != null)
                        {
                            enrichedColumns.Add(new ProjectedColumn(
                                propertyName: col.PropertyName,
                                columnName: col.ColumnName,
                                clrType: resolvedType,
                                fullClrType: resolvedType,
                                isNullable: col.IsNullable,
                                ordinal: col.Ordinal,
                                alias: col.Alias,
                                sqlExpression: col.SqlExpression,
                                isAggregateFunction: true,
                                isValueType: true,
                                readerMethodName: Parsing.ChainAnalyzer.GetReaderMethodForTypePublic(resolvedType),
                                tableAlias: col.TableAlias));
                            anyEnriched = true;
                            continue;
                        }
                    }

                    // Regular columns with missing types
                    ColumnInfo? entityCol = null;
                    if (string.IsNullOrWhiteSpace(col.ClrType))
                    {
                        // Multi-entity: match by TableAlias + PropertyName
                        if (isJoined && perAliasLookup != null && col.TableAlias != null)
                        {
                            if (perAliasLookup.TryGetValue(col.TableAlias, out var aliasLookup))
                                aliasLookup.TryGetValue(col.PropertyName, out entityCol);
                        }
                        else
                        {
                            colLookup.TryGetValue(col.PropertyName, out entityCol);
                        }
                    }
                    if (entityCol != null)
                    {
                        enrichedColumns.Add(new ProjectedColumn(
                            propertyName: col.PropertyName,
                            columnName: entityCol.ColumnName,
                            clrType: string.IsNullOrWhiteSpace(col.ClrType) ? entityCol.ClrType : col.ClrType,
                            fullClrType: string.IsNullOrWhiteSpace(col.FullClrType) ? entityCol.FullClrType : col.FullClrType,
                            isNullable: entityCol.IsNullable,
                            ordinal: col.Ordinal,
                            alias: col.Alias,
                            sqlExpression: col.SqlExpression,
                            isAggregateFunction: col.IsAggregateFunction,
                            customTypeMapping: entityCol.CustomTypeMappingClass ?? col.CustomTypeMapping,
                            isValueType: entityCol.IsValueType,
                            readerMethodName: entityCol.DbReaderMethodName ?? entityCol.ReaderMethodName,
                            tableAlias: col.TableAlias,
                            isForeignKey: entityCol.Kind == Quarry.Shared.Migration.ColumnKind.ForeignKey,
                            foreignKeyEntityName: entityCol.ReferencedEntityName,
                            isEnum: entityCol.IsEnum));
                        anyEnriched = true;
                    }
                    else
                    {
                        enrichedColumns.Add(col);
                    }
                }

                if (anyEnriched)
                {
                    // Rebuild result type name
                    var resultTypeName = rawProj.ResultTypeName;
                    if (enrichedColumns.Count == 1)
                    {
                        var col = enrichedColumns[0];
                        var colType = !string.IsNullOrWhiteSpace(col.ClrType) ? col.ClrType : col.FullClrType;
                        if (!string.IsNullOrWhiteSpace(colType) && colType != "?")
                            resultTypeName = col.IsNullable && !colType.EndsWith("?") ? colType + "?" : colType;
                    }
                    else if (rawProj.Kind == ProjectionKind.Tuple)
                    {
                        var parts = new List<string>();
                        foreach (var c in enrichedColumns)
                        {
                            var t = !string.IsNullOrWhiteSpace(c.ClrType) ? c.ClrType : c.FullClrType;
                            if (string.IsNullOrWhiteSpace(t)) t = "object";
                            if (c.IsNullable && !t.EndsWith("?")) t += "?";
                            var isDefault = c.PropertyName.StartsWith("Item") &&
                                int.TryParse(c.PropertyName.Substring(4), out var idx) && idx == c.Ordinal + 1;
                            parts.Add(isDefault ? t : $"{t} {c.PropertyName}");
                        }
                        resultTypeName = $"({string.Join(", ", parts)})";
                    }

                    return new ProjectionInfo(rawProj.Kind, resultTypeName, enrichedColumns,
                        rawProj.IsOptimalPath, rawProj.NonOptimalReason, rawProj.FailureReason,
                        entityRef.CustomEntityReaderClass);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Builds an entity projection from an EntityRef for identity projections in the new pipeline bridge.
    /// Similar to <see cref="BuildEntityProjectionFromEntityInfo"/> but uses the lightweight EntityRef.
    /// </summary>
    /// <summary>
    /// Strip SqlExpression from non-aggregate columns for bridge compatibility.
    /// The old pipeline emitters treat SqlExpression as "computed/aggregate expression",
    /// but the new pipeline sets it for ALL columns (e.g., "u.UserId"). Leaving it set
    /// triggers the "ambiguous columns" guard in FileEmitter which skips terminal generation.
    /// </summary>
    private static IReadOnlyList<ProjectedColumn> StripNonAggregateSqlExpressions(
        IReadOnlyList<ProjectedColumn> columns)
    {
        var result = new List<ProjectedColumn>(columns.Count);
        foreach (var col in columns)
        {
            if (col.SqlExpression != null && !col.IsAggregateFunction)
            {
                result.Add(new ProjectedColumn(
                    col.PropertyName, col.ColumnName, col.ClrType, col.FullClrType,
                    col.IsNullable, col.Ordinal, col.Alias,
                    sqlExpression: null,
                    isAggregateFunction: false,
                    customTypeMapping: col.CustomTypeMapping,
                    isValueType: col.IsValueType,
                    readerMethodName: col.ReaderMethodName,
                    tableAlias: col.TableAlias,
                    isForeignKey: col.IsForeignKey,
                    foreignKeyEntityName: col.ForeignKeyEntityName,
                    isEnum: col.IsEnum));
            }
            else
            {
                result.Add(col);
            }
        }
        return result;
    }

    private static ProjectionInfo? BuildEntityProjectionFromEntityRef(IR.EntityRef entityRef)
    {
        if (entityRef.Columns.Count == 0)
            return null;

        var columns = new List<ProjectedColumn>();
        var ordinal = 0;
        foreach (var col in entityRef.Columns)
        {
            columns.Add(new ProjectedColumn(
                propertyName: col.PropertyName,
                columnName: col.ColumnName,
                clrType: col.ClrType,
                fullClrType: col.FullClrType,
                isNullable: col.IsNullable,
                ordinal: ordinal++,
                customTypeMapping: col.CustomTypeMappingClass,
                isValueType: col.IsValueType,
                readerMethodName: col.DbReaderMethodName ?? col.ReaderMethodName,
                isForeignKey: col.Kind == ColumnKind.ForeignKey,
                foreignKeyEntityName: col.ReferencedEntityName,
                isEnum: col.IsEnum));
        }

        return new ProjectionInfo(ProjectionKind.Entity, entityRef.EntityName, columns,
            customEntityReaderClass: entityRef.CustomEntityReaderClass);
    }

    /// <summary>
    /// Builds a <see cref="PrebuiltChainInfo"/> for a tier 1 joined chain by resolving
    /// all joined entity metadata and building the compile-time SQL map.
    /// </summary>
    private static PrebuiltChainInfo? BuildPrebuiltChainInfoForJoin(
        ChainAnalysisResult result,
        UsageSiteInfo executionSite,
        IR.EntityRegistry registry)
    {
        var joinedNames = executionSite.JoinedEntityTypeNames!;
        var resolvedEntities = new List<EntityInfo>(joinedNames.Count);
        ContextInfo? sharedContext = null;

        foreach (var name in joinedNames)
        {
            var resolved = registry.Resolve(name, executionSite.ContextClassName);
            if (resolved == null)
                return null;

            var entity = resolved.Entity;
            var ctx = resolved.Context;

            // All entities must share the same context (same dialect, schema)
            if (sharedContext == null)
                sharedContext = ctx;
            else if (sharedContext.Dialect != ctx.Dialect)
                return null;

            resolvedEntities.Add(entity);
        }

        if (sharedContext == null || resolvedEntities.Count < 2)
            return null;

        var dialect = sharedContext.Dialect;

        // Joined chains must have an explicit Select clause — no identity projection
        var selectClause = result.Clauses
            .Where(c => c.Role == ClauseRole.Select)
            .Select(c => c.Site)
            .FirstOrDefault(s => s.ProjectionInfo != null);

        if (selectClause?.ProjectionInfo == null)
            return null;

        var projInfo = selectClause.ProjectionInfo;
        var entityType = InterceptorCodeGenerator.GetShortTypeName(executionSite.EntityTypeName);
        var readerCode = Quarry.Generators.Projection.ReaderCodeGenerator.GenerateReaderDelegate(projInfo, entityType);

        // Build table info list for join SQL generation
        var tables = new List<(string TableName, string? SchemaName)>(resolvedEntities.Count);
        foreach (var e in resolvedEntities)
            tables.Add((e.TableName, sharedContext.Schema));

        // Build SQL map using CompileTimeSqlBuilder with join support
        // Join chains need "t0" alias on the primary table (matches runtime QueryState.WithJoin)
        // For ToDiagnostics terminals, extract constant Limit/Offset to inline as literals.
        var joinPagination = ExtractLiteralPagination(result, executionSite.Kind);
        if (joinPagination.HasVariablePagination)
            return null;

        Dictionary<ulong, PrebuiltSqlResult>? sqlMap;
        try
        {
            sqlMap = CompileTimeSqlBuilder.BuildSelectSqlMap(
                result.PossibleMasks, result.Clauses, dialect, tables[0].TableName, tables[0].SchemaName, "t0",
                literalLimit: joinPagination.Limit, literalOffset: joinPagination.Offset);
        }
        catch
        {
            return null;
        }

        if (sqlMap == null || sqlMap.Count == 0)
            return null;

        // Build joined entity type names and table infos for the chain info
        var joinedTypeNames = joinedNames.ToList();
        var joinedTableInfos = tables;

        // Build chain parameter info for carrier optimization
        var chainParams = BuildChainParameters(result);
        var isCarrierEligible = chainParams != null;

        if (isCarrierEligible)
        {
            // ToDiagnostics doesn't read rows, so skip the reader check for it.
            var isToDiag = executionSite.Kind == InterceptorKind.ToDiagnostics;
            if (readerCode == null && !isToDiag)
                isCarrierEligible = false;
        }

        // Post-process SQL to tokenize collection parameter placeholders for carrier expansion.
        if (isCarrierEligible && chainParams != null)
            TokenizeCollectionParameters(sqlMap, chainParams, dialect);

        return new PrebuiltChainInfo(
            result, sqlMap, readerCode,
            executionSite.EntityTypeName, executionSite.ResultTypeName,
            dialect, tables[0].TableName, tables[0].SchemaName,
            QueryKind.Select, projInfo,
            joinedTypeNames, joinedTableInfos,
            chainParameters: chainParams,
            isCarrierEligible: isCarrierEligible,
            entitySchemaNamespace: resolvedEntities[0].SchemaNamespace);
    }

    /// <summary>
    /// Builds a list of <see cref="ChainParameterInfo"/> for carrier class field generation
    /// by walking clause sites in chain order and collecting their parameters with global indices.
    /// Returns null if carrier parameter extraction fails (e.g., collection parameters that
    /// cannot be represented as typed carrier fields).
    /// </summary>
    private static IReadOnlyList<ChainParameterInfo>? BuildChainParameters(ChainAnalysisResult result)
    {
        var chainParams = new List<ChainParameterInfo>();
        var globalIndex = 0;

        foreach (var clause in result.Clauses)
        {
            // OrderBy/ThenBy/GroupBy require resolved KeyTypeName for carrier path.
            // Without it, the clause interceptor falls back to non-carrier (open-generic),
            // creating a mismatch where the terminal expects a carrier but gets a real builder.
            if (clause.Role is ClauseRole.OrderBy or ClauseRole.ThenBy or ClauseRole.GroupBy)
            {
                if (clause.Site.KeyTypeName == null)
                    return null;
            }

            // Set/UpdateSet: carrier eligibility depends on parameter type resolution.
            // ValueTypeName (needed for concrete interceptor signatures) is resolved during
            // enrichment — here we only check that parameter types are available.
            if (clause.Role is ClauseRole.Set or ClauseRole.UpdateSet)
            {
                // UpdateSetPoco: entity-sourced parameters — one per column from UpdateInfo
                if (clause.Site.Kind == InterceptorKind.UpdateSetPoco && clause.Site.UpdateInfo != null)
                {
                    foreach (var col in clause.Site.UpdateInfo.Columns)
                    {
                        // Build the entity property access expression for terminal parameter extraction
                        var propExpr = col.IsForeignKey
                            ? $"__c.Entity.{col.PropertyName}.Id"
                            : $"__c.Entity.{col.PropertyName}";
                        if (col.CustomTypeMappingClass != null)
                            propExpr = $"{InterceptorCodeGenerator.GetMappingFieldName(col.CustomTypeMappingClass)}.ToDb({propExpr})";

                        chainParams.Add(new ChainParameterInfo(
                            index: globalIndex,
                            typeName: col.FullClrType,
                            valueExpression: propExpr,
                            typeMapping: col.CustomTypeMappingClass,
                            isSensitive: col.IsSensitive,
                            entityPropertyExpression: propExpr));

                        globalIndex++;
                    }
                    continue;
                }

                // UpdateSetAction: multiple assignments from Action<T> lambda.
                // Fall through to normal parameter processing — each assignment's parameter
                // is already in ClauseInfo.Parameters with resolved types.
                if (clause.Site.ClauseInfo is SetActionClauseInfo)
                {
                    // Validated — fall through to standard parameter processing
                }
                // Scalar Set<TValue>: require SetClauseInfo with resolvable parameter types.
                // Fall through to normal parameter processing — the standard ClrType checks
                // will validate that carrier fields can be typed.
                else if (clause.Site.ClauseInfo is not SetClauseInfo)
                    return null;
            }

            // InsertTransition has no expression parameters — skip parameter extraction
            if (clause.Role is ClauseRole.InsertTransition)
                continue;

            if (clause.Site.ClauseInfo == null)
                continue;

            foreach (var param in clause.Site.ClauseInfo.Parameters)
            {
                // Collection parameters need a resolved element type for carrier fields
                if (param.IsCollection)
                {
                    if (string.IsNullOrWhiteSpace(param.CollectionElementType))
                        return null;

                    // Classify the receiver for direct-access vs runtime-helper extraction.
                    // Public static fields/properties can be accessed directly in generated code.
                    var isDirectAccessible = false;
                    string? collectionAccessExpression = null;

                    if (param.CollectionReceiverSymbol is IFieldSymbol { IsStatic: true, DeclaredAccessibility: Accessibility.Public } publicField)
                    {
                        isDirectAccessible = true;
                        collectionAccessExpression = $"global::{publicField.ContainingType.ToDisplayString()}.{publicField.Name}";
                    }
                    else if (param.CollectionReceiverSymbol is IPropertySymbol { IsStatic: true, DeclaredAccessibility: Accessibility.Public, GetMethod: not null } publicProp)
                    {
                        isDirectAccessible = true;
                        collectionAccessExpression = $"global::{publicProp.ContainingType.ToDisplayString()}.{publicProp.Name}";
                    }

                    chainParams.Add(new ChainParameterInfo(
                        index: globalIndex,
                        typeName: param.ClrType,
                        valueExpression: param.ValueExpression,
                        isCollection: true,
                        elementTypeName: param.CollectionElementType,
                        isDirectAccessible: isDirectAccessible,
                        collectionAccessExpression: collectionAccessExpression));

                    globalIndex++;
                    continue;
                }

                // Parameters with unresolved types cannot be carrier fields
                if (string.IsNullOrWhiteSpace(param.ClrType) || param.ClrType == "?" || param.ClrType == "object?")
                    return null;

                // For Set clauses, the CustomTypeMappingClass lives on SetClauseInfo
                // rather than on the individual ParameterInfo — propagate it.
                var typeMapping = param.CustomTypeMappingClass;
                if (typeMapping == null
                    && clause.Role is ClauseRole.Set or ClauseRole.UpdateSet
                    && clause.Site.ClauseInfo is SetClauseInfo sci)
                {
                    typeMapping = sci.CustomTypeMappingClass;
                }
                // For SetAction assignments, propagate type mapping from the assignment
                if (typeMapping == null
                    && clause.Site.Kind == InterceptorKind.UpdateSetAction
                    && clause.Site.ClauseInfo is SetActionClauseInfo saci)
                {
                    var localIdx = param.Index; // Index within this clause's parameters
                    if (localIdx < saci.Assignments.Count)
                        typeMapping = saci.Assignments[localIdx].CustomTypeMappingClass;
                }

                // For Action<T> captured params, needsFieldInfoCache is true because we use
                // delegate.Target + cached FieldInfo (no expression tree path, but still needs cache).
                var needsFieldInfoCache = param.IsCaptured
                    && (param.CanGenerateDirectPath || clause.Site.Kind == InterceptorKind.UpdateSetAction);

                chainParams.Add(new ChainParameterInfo(
                    index: globalIndex,
                    typeName: param.ClrType,
                    valueExpression: param.ValueExpression,
                    typeMapping: typeMapping,
                    isEnum: param.IsEnum,
                    enumUnderlyingType: param.EnumUnderlyingType,
                    needsFieldInfoCache: needsFieldInfoCache));

                globalIndex++;
            }
        }

        return chainParams;
    }

    /// <summary>
    /// Replaces collection parameter placeholders in pre-built SQL with expansion tokens.
    /// For example, <c>IN (@p0)</c> becomes <c>IN ({__COL_P0__})</c> when P0 is a collection.
    /// The carrier terminal expands these tokens at runtime based on the actual collection size.
    /// </summary>
    private static void TokenizeCollectionParameters(
        Dictionary<ulong, Sql.PrebuiltSqlResult> sqlMap,
        IReadOnlyList<ChainParameterInfo> chainParams,
        SqlDialect dialect)
    {
        // Find collection parameter indices and their tokens
        var collectionParams = new List<(int Index, string Token)>();
        foreach (var param in chainParams)
        {
            if (!param.IsCollection) continue;
            collectionParams.Add((param.Index, $"{{__COL_P{param.Index}__}}"));
        }

        if (collectionParams.Count == 0) return;

        // Replace placeholders with tokens in all SQL variants.
        // Collect updates and apply after iteration to avoid allocating a key list copy.
        var pendingUpdates = new List<(ulong Key, string Sql, int ParamCount)>();
        foreach (var kvp in sqlMap)
        {
            var sql = kvp.Value.Sql;

            if (dialect == SqlDialect.MySQL)
            {
                // MySQL uses positional '?' — replace the Nth '?' by ordinal index.
                // Walk through '?' occurrences and replace the one at the collection param's position.
                foreach (var (paramIdx, token) in collectionParams)
                {
                    sql = ReplaceNthOccurrence(sql, '?', paramIdx, token);
                }
            }
            else
            {
                var sbSql = new System.Text.StringBuilder(sql);
                foreach (var (paramIdx, token) in collectionParams)
                {
                    var placeholder = dialect switch
                    {
                        SqlDialect.PostgreSQL => $"${paramIdx + 1}",
                        _ => $"@p{paramIdx}"
                    };
                    sbSql.Replace(placeholder, token);
                }
                sql = sbSql.ToString();
            }

            if (sql != kvp.Value.Sql)
            {
                pendingUpdates.Add((kvp.Key, sql, kvp.Value.ParameterCount));
            }
        }
        foreach (var (key, sql, paramCount) in pendingUpdates)
        {
            sqlMap[key] = new Sql.PrebuiltSqlResult(sql, paramCount);
        }
    }

    /// <summary>
    /// Replaces the Nth occurrence (0-based) of a character in a string with a replacement string.
    /// </summary>
    private static string ReplaceNthOccurrence(string input, char target, int n, string replacement)
    {
        var count = 0;
        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] == target)
            {
                if (count == n)
                {
                    return input.Substring(0, i) + replacement + input.Substring(i + 1);
                }
                count++;
            }
        }
        return input; // Nth occurrence not found
    }

    /// <summary>
    /// Extracts constant Limit/Offset values from a chain's clause sites for ToDiagnostics terminals.
    /// Returns <c>hasVariablePagination = true</c> if any Limit/Offset clause has a non-constant
    /// value, meaning the chain should fall back to the runtime ToDiagnostics path.
    /// </summary>
    private static (int? Limit, int? Offset, bool HasVariablePagination) ExtractLiteralPagination(
        ChainAnalysisResult result, InterceptorKind terminalKind)
    {
        if (terminalKind is not InterceptorKind.ToDiagnostics)
            return (null, null, false);

        int? literalLimit = null;
        int? literalOffset = null;

        foreach (var clause in result.Clauses)
        {
            if (clause.Role == ClauseRole.Limit)
            {
                if (clause.Site.ConstantIntValue == null)
                    return (null, null, true);
                literalLimit = clause.Site.ConstantIntValue;
            }
            else if (clause.Role == ClauseRole.Offset)
            {
                if (clause.Site.ConstantIntValue == null)
                    return (null, null, true);
                literalOffset = clause.Site.ConstantIntValue;
            }
        }

        return (literalLimit, literalOffset, false);
    }

    /// <summary>
    /// Determines the <see cref="QueryKind"/> from an execution site's builder type.
    /// </summary>
    private static QueryKind? DetermineQueryKind(UsageSiteInfo executionSite)
    {
        switch (executionSite.Kind)
        {
            case InterceptorKind.ExecuteFetchAll:
            case InterceptorKind.ExecuteFetchFirst:
            case InterceptorKind.ExecuteFetchFirstOrDefault:
            case InterceptorKind.ExecuteFetchSingle:
            case InterceptorKind.ExecuteScalar:
            case InterceptorKind.ToAsyncEnumerable:
                return QueryKind.Select;

            case InterceptorKind.ExecuteNonQuery:
                // Determine by builder kind
                if (executionSite.BuilderKind is BuilderKind.Delete or BuilderKind.ExecutableDelete)
                    return QueryKind.Delete;
                if (executionSite.BuilderKind is BuilderKind.Update or BuilderKind.ExecutableUpdate)
                    return QueryKind.Update;
                // QueryBuilder ExecuteNonQuery is a SELECT-like operation
                if (executionSite.BuilderKind is BuilderKind.Query or BuilderKind.JoinedQuery)
                    return QueryKind.Select;
                return null;

            case InterceptorKind.ToDiagnostics:
                if (executionSite.BuilderKind is BuilderKind.Delete or BuilderKind.ExecutableDelete)
                    return QueryKind.Delete;
                if (executionSite.BuilderKind is BuilderKind.Update or BuilderKind.ExecutableUpdate)
                    return QueryKind.Update;
                if (executionSite.BuilderKind is BuilderKind.Query or BuilderKind.JoinedQuery)
                    return QueryKind.Select;
                return null; // IEntityAccessor — trivial db.Users().ToDiagnostics(), no clauses to optimize

            case InterceptorKind.InsertExecuteNonQuery:
            case InterceptorKind.InsertExecuteScalar:
            case InterceptorKind.InsertToDiagnostics:
                return QueryKind.Insert;

            default:
                return null;
        }
    }

    /// <summary>
    /// Gets a relative file path for display in diagnostics.
    /// </summary>
    private static string GetRelativePath(string filePath)
    {
        // Simple: just return the filename
        var lastSlash = filePath.LastIndexOfAny(new[] { '/', '\\' });
        return lastSlash >= 0 ? filePath.Substring(lastSlash + 1) : filePath;
    }

    /// <summary>
    /// Enriches a usage site with entity metadata if available.
    /// This fixes the empty column name issue by using schema-derived column info,
    /// and translates pending clauses when entity metadata becomes available.
    /// </summary>
    private static UsageSiteInfo EnrichUsageSiteWithEntityInfo(
        UsageSiteInfo site,
        IR.EntityRegistry registry,
        Compilation? compilation = null)
    {
        // Check if this site needs any enrichment
        var needsProjectionEnrichment = site.Kind == InterceptorKind.Select && site.ProjectionInfo != null;
        var needsClauseEnrichment = site.PendingClauseInfo != null;
        var needsInsertEnrichment = site.Kind is InterceptorKind.InsertExecuteNonQuery
                                                or InterceptorKind.InsertExecuteScalar
                                                or InterceptorKind.InsertToDiagnostics;
        var needsUpdatePocoEnrichment = site.Kind == InterceptorKind.UpdateSetPoco;
        var needsJoinEnrichment = site.Kind is InterceptorKind.Join or InterceptorKind.LeftJoin or InterceptorKind.RightJoin
                                  && site.ClauseInfo == null
                                  && site.JoinedEntityTypeName != null;
        // Joined clause methods (Where/OrderBy on JoinedQueryBuilder) need multi-entity enrichment
        var needsJoinedClauseEnrichment = site.JoinedEntityTypeNames != null
                                          && site.JoinedEntityTypeNames.Count >= 2
                                          && site.ClauseInfo == null
                                          && IsClauseOrSelectMethod(site.Kind);
        // RawSql sites: enrich type info when T matches a known entity
        var needsRawSqlEnrichment = site.Kind is InterceptorKind.RawSqlAsync or InterceptorKind.RawSqlScalarAsync
                                    && site.RawSqlTypeInfo != null
                                    && site.RawSqlTypeInfo.TypeKind == Models.RawSqlTypeKind.Dto;
        // Set/UpdateSet: resolve ValueTypeName from enriched entity columns when discovery couldn't
        var needsSetValueTypeEnrichment = site.Kind is InterceptorKind.Set or InterceptorKind.UpdateSet
                                          && site.ClauseInfo is SetClauseInfo sci && sci.ValueTypeName == null;
        // SetAction: enrich assignments with custom type mappings from EntityInfo
        var needsSetActionEnrichment = site.Kind is InterceptorKind.UpdateSetAction
                                       && site.ClauseInfo is SetActionClauseInfo;

        if (!needsProjectionEnrichment && !needsClauseEnrichment && !needsInsertEnrichment
            && !needsUpdatePocoEnrichment && !needsJoinEnrichment && !needsJoinedClauseEnrichment
            && !needsRawSqlEnrichment && !needsSetValueTypeEnrichment && !needsSetActionEnrichment)
            return site;

        // Try to find matching entity, using contextClassName to disambiguate
        var resolved = registry.Resolve(site.EntityTypeName, site.ContextClassName);
        if (resolved == null)
            return site;

        var entityContext = (resolved.Entity, resolved.Context);

        // Enrich projection if needed
        var enrichedProjection = site.ProjectionInfo;
        if (needsProjectionEnrichment && site.ProjectionInfo != null)
        {
            enrichedProjection = EnrichProjectionWithEntityInfo(
                site.ProjectionInfo,
                entityContext.Entity,
                entityContext.Context.Dialect);
        }

        // Translate pending clause if needed.
        // Skip single-entity pending clause enrichment when joined clause enrichment is
        // needed — the single-entity path cannot resolve columns from secondary entities
        // (e.g., "o.Status" in (u, o) => statuses.Contains(o.Status)) and would produce a
        // non-null failure that blocks the multi-entity TryTranslateJoinedClause path.
        var enrichedClauseInfo = site.ClauseInfo;
        PendingClauseInfo? clearedPendingClause = null; // Will be null after enrichment
        if (needsClauseEnrichment && !needsJoinedClauseEnrichment && site.PendingClauseInfo != null)
        {
            enrichedClauseInfo = TranslatePendingClause(
                site.PendingClauseInfo,
                entityContext.Entity,
                entityContext.Context.Dialect,
                site.InvocationSyntax as InvocationExpressionSyntax,
                registry,
                compilation);
            // Clear pending clause info since it's been translated
            clearedPendingClause = null;
        }
        else
        {
            clearedPendingClause = site.PendingClauseInfo;
        }

        // Create insert info if needed
        InsertInfo? enrichedInsertInfo = site.InsertInfo;
        if (needsInsertEnrichment)
        {
            enrichedInsertInfo = InsertInfo.FromEntityInfo(
                entityContext.Entity,
                entityContext.Context.Dialect,
                site.InitializedPropertyNames);
        }

        // Create update info if needed (POCO Set)
        InsertInfo? enrichedUpdateInfo = site.UpdateInfo;
        if (needsUpdatePocoEnrichment)
        {
            enrichedUpdateInfo = InsertInfo.FromEntityInfo(
                entityContext.Entity,
                entityContext.Context.Dialect,
                site.InitializedPropertyNames);
        }

        // Enrich join clause if needed
        string? resolvedJoinedEntityTypeName = null;
        if (needsJoinEnrichment && enrichedClauseInfo == null)
        {
            // For navigation joins with unresolved type parameters, resolve from navigation metadata
            if (site.IsNavigationJoin && site.JoinedEntityTypeName != null
                && !registry.Contains(site.JoinedEntityTypeName))
            {
                resolvedJoinedEntityTypeName = ResolveNavigationJoinEntityType(
                    site, entityContext.Entity, registry);
            }

            var siteForJoin = resolvedJoinedEntityTypeName != null
                ? new UsageSiteInfo(
                    methodName: site.MethodName, filePath: site.FilePath,
                    line: site.Line, column: site.Column,
                    builderTypeName: site.BuilderTypeName, entityTypeName: site.EntityTypeName,
                    isAnalyzable: site.IsAnalyzable, kind: site.Kind,
                    invocationSyntax: site.InvocationSyntax, uniqueId: site.UniqueId,
                    joinedEntityTypeName: resolvedJoinedEntityTypeName,
                    isNavigationJoin: site.IsNavigationJoin,
                    builderKind: site.BuilderKind)
                : site;
            enrichedClauseInfo = TryTranslateJoinClause(siteForJoin, entityContext, registry);
        }

        // Enrich joined clause methods (Where/OrderBy/GroupBy on joined builders)
        // Note: Joined Select projection is analyzed in Phase 2 (UsageSiteDiscovery) and
        // may be further enriched via EnrichJoinedProjectionWithEntityInfo below
        if (needsJoinedClauseEnrichment && enrichedClauseInfo == null && site.Kind != InterceptorKind.Select)
        {
            enrichedClauseInfo = TryTranslateJoinedClause(site, entityContext, registry);
            if (enrichedClauseInfo != null)
                clearedPendingClause = null; // Clear pending clause — joined translation succeeded
        }

        // Analyze joined Select projection using entity metadata
        if (needsJoinedClauseEnrichment && site.Kind == InterceptorKind.Select
            && site.InvocationSyntax is InvocationExpressionSyntax selectInvocation)
        {
            enrichedProjection = TryAnalyzeJoinedProjection(
                selectInvocation, site.JoinedEntityTypeNames!, registry, entityContext.Context.Dialect);
        }

        // Enrich Set clause with custom type mapping info from schema
        if (site.Kind == InterceptorKind.Set && enrichedClauseInfo is SetClauseInfo setClause && setClause.CustomTypeMappingClass == null)
        {
            enrichedClauseInfo = EnrichSetClauseWithMapping(setClause, entityContext.Entity);
        }

        // Enrich Set/UpdateSet with resolved ValueTypeName from entity column metadata.
        // During discovery, the entity's generated properties may not be available yet
        // (same generator pipeline), so ValueTypeName can be null. Resolve it here using
        // the enriched EntityInfo from Pipeline 1.
        if (needsSetValueTypeEnrichment)
        {
            var setClauseToEnrich = (enrichedClauseInfo ?? site.ClauseInfo) as SetClauseInfo;
            if (setClauseToEnrich != null && setClauseToEnrich.ValueTypeName == null)
            {
                var resolvedValueType = ResolveSetValueTypeFromEntity(setClauseToEnrich, entityContext.Entity);
                if (resolvedValueType != null)
                {
                    enrichedClauseInfo = new SetClauseInfo(
                        setClauseToEnrich.ColumnSql,
                        setClauseToEnrich.ParameterIndex,
                        setClauseToEnrich.Parameters,
                        setClauseToEnrich.CustomTypeMappingClass,
                        resolvedValueType);
                }
            }
        }

        // Also enrich UpdateSet mapping (same as InterceptorKind.Set above)
        if (site.Kind == InterceptorKind.UpdateSet && enrichedClauseInfo is SetClauseInfo updateSetClause && updateSetClause.CustomTypeMappingClass == null)
        {
            enrichedClauseInfo = EnrichSetClauseWithMapping(updateSetClause, entityContext.Entity);
        }

        // Enrich UpdateSetAction: resolve custom type mappings, value types, and proper column quoting
        if (needsSetActionEnrichment && enrichedClauseInfo is SetActionClauseInfo actionClause)
        {
            enrichedClauseInfo = EnrichSetActionClauseWithMapping(actionClause, entityContext.Entity, entityContext.Context.Dialect);
        }

        // Enrich RawSql type info when T matches a known entity from Pipeline 1
        var enrichedRawSqlTypeInfo = site.RawSqlTypeInfo;
        if (needsRawSqlEnrichment && site.RawSqlTypeInfo != null)
        {
            enrichedRawSqlTypeInfo = EnrichRawSqlTypeInfoWithEntity(site.RawSqlTypeInfo, entityContext.Entity);
        }

        // Create updated usage site with enriched data
        return new UsageSiteInfo(
            methodName: site.MethodName,
            filePath: site.FilePath,
            line: site.Line,
            column: site.Column,
            builderTypeName: site.BuilderTypeName,
            entityTypeName: site.EntityTypeName,
            isAnalyzable: site.IsAnalyzable,
            kind: site.Kind,
            invocationSyntax: site.InvocationSyntax,
            uniqueId: site.UniqueId,
            resultTypeName: site.ResultTypeName,
            nonAnalyzableReason: site.NonAnalyzableReason,
            contextClassName: entityContext.Context.ClassName,
            contextNamespace: entityContext.Context.Namespace,
            projectionInfo: enrichedProjection,
            clauseInfo: enrichedClauseInfo,
            interceptableLocationData: site.InterceptableLocationData,
            interceptableLocationVersion: site.InterceptableLocationVersion,
            pendingClauseInfo: clearedPendingClause,
            insertInfo: enrichedInsertInfo,
            joinedEntityTypeName: resolvedJoinedEntityTypeName ?? site.JoinedEntityTypeName,
            joinedEntityTypeNames: site.JoinedEntityTypeNames,
            dialect: entityContext.Context.Dialect,
            initializedPropertyNames: site.InitializedPropertyNames,
            updateInfo: enrichedUpdateInfo,
            keyTypeName: (enrichedClauseInfo is Models.OrderByClauseInfo orderByInfo && orderByInfo.KeyTypeName != null)
                ? orderByInfo.KeyTypeName : site.KeyTypeName,
            rawSqlTypeInfo: enrichedRawSqlTypeInfo,
            isNavigationJoin: site.IsNavigationJoin,
            builderKind: site.BuilderKind,
            valueTypeName: (enrichedClauseInfo is SetClauseInfo setClauseForValue && setClauseForValue.ValueTypeName != null)
                ? setClauseForValue.ValueTypeName
                : (site.ClauseInfo is SetClauseInfo origSetClause && origSetClause.ValueTypeName != null)
                    ? origSetClause.ValueTypeName
                    : site.ValueTypeName);
    }

    /// <summary>
    /// Attempts to translate a join condition using enriched entity metadata.
    /// </summary>
    private static ClauseInfo? TryTranslateJoinClause(
        UsageSiteInfo site,
        (EntityInfo Entity, ContextInfo Context) leftEntityContext,
        IR.EntityRegistry registry)
    {
        if (site.JoinedEntityTypeName == null || site.InvocationSyntax is not Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax invocation)
            return null;

        // Find the right (newly joined) entity info
        var rightEntry = registry.Resolve(site.JoinedEntityTypeName);
        if (rightEntry == null)
            return null;

        var joinKind = site.Kind switch
        {
            InterceptorKind.LeftJoin => Models.JoinClauseKind.Left,
            InterceptorKind.RightJoin => Models.JoinClauseKind.Right,
            _ => Models.JoinClauseKind.Inner
        };

        try
        {
            // Check if this is a navigation-based join (u => u.Orders)
            if (site.IsNavigationJoin)
            {
                return Translation.ClauseTranslator.TranslateNavigationJoin(
                    invocation,
                    leftEntityContext.Entity,
                    rightEntry.Entity,
                    leftEntityContext.Context.Dialect,
                    joinKind);
            }

            // Check if this is a chained join (from JoinedQueryBuilder/3)
            if (site.JoinedEntityTypeNames != null && site.JoinedEntityTypeNames.Count >= 2)
            {
                // Resolve all prior entity infos
                var priorEntities = new List<(string TypeName, EntityInfo Entity)>();
                foreach (var typeName in site.JoinedEntityTypeNames)
                {
                    var entry = registry.Resolve(typeName);
                    if (entry == null)
                        return null;
                    priorEntities.Add((typeName, entry.Entity));
                }

                return Translation.ClauseTranslator.TranslateChainedJoinFromEntityInfo(
                    invocation,
                    priorEntities.Select(e => e.Entity).ToList(),
                    rightEntry.Entity,
                    leftEntityContext.Context.Dialect,
                    joinKind);
            }

            return Translation.ClauseTranslator.TranslateJoinFromEntityInfo(
                invocation,
                leftEntityContext.Entity,
                rightEntry.Entity,
                leftEntityContext.Context.Dialect,
                joinKind);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Discovers missing chain members (Select, Execute) from navigation join chains where
    /// the semantic model couldn't resolve downstream calls due to unresolved TJoined.
    /// Walks up the syntax tree from each enriched navigation join to find undiscovered calls.
    /// </summary>
    private static List<UsageSiteInfo> DiscoverNavigationJoinChainMembers(
        List<UsageSiteInfo> enrichedSites,
        HashSet<string> discoveredLocations,
        IR.EntityRegistry registry,
        Compilation compilation)
    {
        var result = new List<UsageSiteInfo>();

        foreach (var site in enrichedSites)
        {
            if (!site.IsNavigationJoin || site.JoinedEntityTypeName == null)
                continue;

            // Skip if the enrichment didn't resolve the entity type (still unresolved)
            if (!registry.Contains(site.JoinedEntityTypeName))
                continue;

            // Walk up the syntax tree from the Join invocation to find parent method calls
            var joinInvocation = site.InvocationSyntax;
            if (joinInvocation == null)
                continue;
            var current = joinInvocation.Parent;

            // The join invocation is part of a fluent chain: .Join(...).Select(...).Execute(...)
            // Walk up through MemberAccessExpression → InvocationExpression pairs
            while (current != null)
            {
                // Pattern: joinInvocation is the Expression of a MemberAccessExpression
                if (current is MemberAccessExpressionSyntax memberAccess
                    && memberAccess.Parent is InvocationExpressionSyntax parentInvocation)
                {
                    var methodName = memberAccess.Name.Identifier.ValueText;

                    // Check if this call was already discovered
                    var location = Parsing.UsageSiteDiscovery.GetMethodLocation(parentInvocation);
                    if (location == null)
                    {
                        current = parentInvocation.Parent;
                        continue;
                    }
                    var (filePath, line, column) = location.Value;
                    var locationKey = $"{filePath}:{line}:{column}";

                    if (discoveredLocations.Contains(locationKey))
                    {
                        current = parentInvocation.Parent;
                        continue;
                    }

                    // Determine the interceptor kind
                    if (!Parsing.UsageSiteDiscovery.InterceptableMethods.TryGetValue(methodName, out var kind))
                    {
                        current = parentInvocation.Parent;
                        continue;
                    }

                    // Build the joined entity type names list
                    var joinedEntityTypeNames = new List<string> { site.EntityTypeName, site.JoinedEntityTypeName };

                    var uniqueId = Parsing.UsageSiteDiscovery.GenerateUniqueId(filePath, line, column, methodName);

                    // Get interceptable location
                    string? interceptableLocationData = null;
                    int interceptableLocationVersion = 1;
                    var semanticModel = compilation.GetSemanticModel(parentInvocation.SyntaxTree);
#if QUARRY_GENERATOR
                    try
                    {
#pragma warning disable RSEXPERIMENTAL002
                        var interceptableLocation = semanticModel.GetInterceptableLocation(parentInvocation, default);
#pragma warning restore RSEXPERIMENTAL002
                        if (interceptableLocation != null)
                        {
                            interceptableLocationData = interceptableLocation.Data;
                            interceptableLocationVersion = interceptableLocation.Version;
                        }
                    }
                    catch { }
#endif

                    var newSite = new UsageSiteInfo(
                        methodName: methodName,
                        filePath: filePath,
                        line: line,
                        column: column,
                        builderTypeName: "IJoinedQueryBuilder",
                        entityTypeName: site.EntityTypeName,
                        isAnalyzable: true,
                        kind: kind,
                        invocationSyntax: parentInvocation,
                        uniqueId: uniqueId,
                        contextClassName: site.ContextClassName,
                        contextNamespace: site.ContextNamespace,
                        joinedEntityTypeNames: joinedEntityTypeNames,
                        dialect: site.Dialect,
                        interceptableLocationData: interceptableLocationData,
                        interceptableLocationVersion: interceptableLocationVersion,
                        builderKind: BuilderKind.JoinedQuery);

                    // Enrich the new site
                    var enriched = EnrichUsageSiteWithEntityInfo(newSite, registry, compilation);
                    result.Add(enriched);
                    discoveredLocations.Add(locationKey);

                    current = parentInvocation.Parent;
                }
                else
                {
                    break;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Resolves the joined entity type name for a navigation join by extracting the
    /// navigation property name from the lambda syntax and looking it up in entity metadata.
    /// For example, u => u.Orders → NavigationInfo.PropertyName == "Orders" → RelatedEntityName == "Order".
    /// </summary>
    private static string? ResolveNavigationJoinEntityType(
        UsageSiteInfo site,
        EntityInfo leftEntity,
        IR.EntityRegistry registry)
    {
        // Extract navigation property name from the lambda: u => u.Orders → "Orders"
        if (site.InvocationSyntax is not Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax invocation)
            return null;

        if (invocation.ArgumentList.Arguments.Count == 0)
            return null;

        var arg = invocation.ArgumentList.Arguments[0].Expression;
        string? navPropertyName = null;

        if (arg is Microsoft.CodeAnalysis.CSharp.Syntax.SimpleLambdaExpressionSyntax simpleLambda
            && simpleLambda.Body is Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax memberAccess)
        {
            navPropertyName = memberAccess.Name.Identifier.Text;
        }
        else if (arg is Microsoft.CodeAnalysis.CSharp.Syntax.ParenthesizedLambdaExpressionSyntax parenLambda
            && parenLambda.Body is Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax parenMemberAccess)
        {
            navPropertyName = parenMemberAccess.Name.Identifier.Text;
        }

        if (navPropertyName == null)
            return null;

        // Find matching navigation in entity metadata
        var nav = leftEntity.Navigations.FirstOrDefault(n => n.PropertyName == navPropertyName);
        if (nav == null)
            return null;

        // Verify the related entity exists in the registry
        if (registry.Contains(nav.RelatedEntityName))
            return nav.RelatedEntityName;

        return null;
    }

    /// <summary>
    /// Checks if an interceptor kind is a clause method or Select.
    /// </summary>
    private static bool IsClauseOrSelectMethod(InterceptorKind kind)
    {
        return kind switch
        {
            InterceptorKind.Where => true,
            InterceptorKind.OrderBy => true,
            InterceptorKind.ThenBy => true,
            InterceptorKind.GroupBy => true,
            InterceptorKind.Select => true,
            _ => false
        };
    }

    /// <summary>
    /// Attempts to translate a clause (Where/OrderBy) on a joined builder using multi-entity metadata.
    /// </summary>
    private static ClauseInfo? TryTranslateJoinedClause(
        UsageSiteInfo site,
        (EntityInfo Entity, ContextInfo Context) primaryEntityContext,
        IR.EntityRegistry registry)
    {
        if (site.JoinedEntityTypeNames == null || site.InvocationSyntax is not Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax invocation)
            return null;

        // Resolve all entity infos
        var entities = new List<EntityInfo>();
        foreach (var typeName in site.JoinedEntityTypeNames)
        {
            var entry = registry.Resolve(typeName);
            if (entry == null)
                return null;
            entities.Add(entry.Entity);
        }

        var dialect = primaryEntityContext.Context.Dialect;

        try
        {
            if (site.Kind == InterceptorKind.Where)
            {
                return Translation.ClauseTranslator.TranslateJoinedWhere(
                    invocation, entities, dialect);
            }
            else if (site.Kind == InterceptorKind.OrderBy || site.Kind == InterceptorKind.ThenBy)
            {
                return Translation.ClauseTranslator.TranslateJoinedOrderBy(
                    invocation, entities, dialect);
            }
            else if (site.Kind == InterceptorKind.GroupBy)
            {
                return Translation.ClauseTranslator.TranslateJoinedGroupBy(
                    invocation, entities, dialect);
            }
        }
        catch
        {
            // Translation failed - will use fallback
        }

        return null;
    }

    /// <summary>
    /// Analyzes a joined Select projection using entity metadata from schema definitions.
    /// Called during enrichment when EntityInfo is available.
    /// </summary>
    private static ProjectionInfo? TryAnalyzeJoinedProjection(
        InvocationExpressionSyntax invocation,
        IReadOnlyList<string> entityTypeNames,
        IR.EntityRegistry registry,
        SqlDialect dialect)
    {
        var entities = new List<EntityInfo>();
        foreach (var typeName in entityTypeNames)
        {
            var entry = registry.Resolve(typeName);
            if (entry == null)
                return null;
            entities.Add(entry.Entity);
        }

        try
        {
            return Projection.ProjectionAnalyzer.AnalyzeJoined(invocation, entities, dialect);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Translates a pending clause to SQL using entity metadata.
    /// Uses the SqlExpr IR pipeline (Bind → Render). For subqueries that the IR
    /// can't handle, falls back to the semantic path which has access to the
    /// full expression tree.
    /// </summary>
    private static ClauseInfo? TranslatePendingClause(
        PendingClauseInfo pendingClause,
        EntityInfo entity,
        SqlDialect dialect,
        InvocationExpressionSyntax? originalInvocation = null,
        IR.EntityRegistry? registry = null,
        Compilation? compilation = null)
    {
        var translator = new IR.SqlExprClauseTranslator(entity, dialect);
        var result = translator.Translate(pendingClause);

        if (result != null && result.IsSuccess)
            return result;

        // IR translation failed — try semantic path with entity registry (handles subqueries)
        if (originalInvocation != null && registry != null && pendingClause.Kind == ClauseKind.Where)
        {
            try
            {
                var semanticResult = Translation.ClauseTranslator.TranslateWhereWithEntityInfo(
                    originalInvocation, null!, entity, dialect, registry.ByEntityName, compilation);
                if (semanticResult.IsSuccess)
                    return semanticResult;
            }
            catch
            {
                // Return IR failure
            }
        }

        return result;
    }

    /// <summary>
    /// Enriches projection columns with proper column names from entity metadata.
    /// </summary>
    private static ProjectionInfo EnrichProjectionWithEntityInfo(
        ProjectionInfo projection,
        EntityInfo entity,
        SqlDialect dialect)
    {
        // For entity projections (Select(u => u)), the discovery phase may have produced
        // 0 columns because the entity type is generated by this same source generator and
        // wasn't available in the semantic model yet. Rebuild from the full EntityInfo.
        if (projection.Kind == ProjectionKind.Entity && projection.Columns.Count < entity.Columns.Count)
        {
            var columns = new List<ProjectedColumn>();
            var ordinal = 0;
            foreach (var col in entity.Columns)
            {
                columns.Add(new ProjectedColumn(
                    propertyName: col.PropertyName,
                    columnName: col.ColumnName,
                    clrType: col.ClrType,
                    fullClrType: col.FullClrType,
                    isNullable: col.IsNullable,
                    ordinal: ordinal++,
                    customTypeMapping: col.CustomTypeMappingClass,
                    isValueType: col.IsValueType,
                    readerMethodName: col.DbReaderMethodName ?? col.ReaderMethodName,
                    isForeignKey: col.Kind == ColumnKind.ForeignKey,
                    foreignKeyEntityName: col.ReferencedEntityName,
                    isEnum: col.IsEnum));
            }
            return new ProjectionInfo(ProjectionKind.Entity, entity.EntityName, columns,
                customEntityReaderClass: entity.CustomEntityReaderClass);
        }

        // Build column lookup from entity
        var columnLookup = entity.Columns.ToDictionary(c => c.PropertyName, StringComparer.Ordinal);

        // Update each projected column with the correct column name and type metadata from schema
        var updatedColumns = new List<ProjectedColumn>();
        foreach (var column in projection.Columns)
        {
            if (columnLookup.TryGetValue(column.PropertyName, out var entityColumn))
            {
                // Update with correct column name and type metadata from schema
                updatedColumns.Add(new ProjectedColumn(
                    propertyName: column.PropertyName,
                    columnName: entityColumn.ColumnName,
                    clrType: entityColumn.ClrType,
                    fullClrType: entityColumn.FullClrType,
                    isNullable: entityColumn.IsNullable,
                    ordinal: column.Ordinal,
                    alias: column.Alias,
                    sqlExpression: column.IsAggregateFunction || (column.SqlExpression != null && column.SqlExpression.Contains('('))
                        ? column.SqlExpression   // Keep intentional SQL expressions (aggregates, functions)
                        : null,                  // Clear fallback syntax like "u.UserId" — ColumnName is authoritative after enrichment
                    isAggregateFunction: column.IsAggregateFunction,
                    customTypeMapping: entityColumn.CustomTypeMappingClass,
                    isValueType: entityColumn.IsValueType,
                    readerMethodName: entityColumn.DbReaderMethodName ?? entityColumn.ReaderMethodName,
                    isForeignKey: entityColumn.Kind == ColumnKind.ForeignKey,
                    foreignKeyEntityName: entityColumn.ReferencedEntityName,
                    isEnum: entityColumn.IsEnum));
            }
            else if (column.IsAggregateFunction && !string.IsNullOrEmpty(column.SqlExpression!))
            {
                // Re-translate aggregate SQL expressions using entity metadata.
                // During discovery, property names may have been used as column names
                // (e.g., SUM("Total") instead of SUM("total")) because the entity type
                // was generated and invisible. Replace with correct DB column names.
                var fixedSql = FixAggregateSqlExpression(column.SqlExpression!, columnLookup, dialect);

                // Fix aggregate CLR type when it was unresolved during discovery.
                // For Min/Max/Avg/Sum, the return type depends on the argument column's type.
                // During discovery, the semantic model may return error types for generated entities,
                // causing the CLR type to fall back to "object". Resolve from entity column info.
                var fixedClrType = column.ClrType;
                var fixedFullClrType = column.FullClrType;
                var fixedIsValueType = column.IsValueType;
                var fixedReaderMethod = column.ReaderMethodName;
                if (fixedClrType == "object" || string.IsNullOrWhiteSpace(fixedClrType) || fixedClrType == "?")
                {
                    var resolvedType = ResolveAggregateClrTypeFromSql(column.SqlExpression!, columnLookup);
                    if (resolvedType != null)
                    {
                        fixedClrType = resolvedType.ClrType;
                        fixedFullClrType = resolvedType.FullClrType;
                        fixedIsValueType = resolvedType.IsValueType;
                        fixedReaderMethod = resolvedType.DbReaderMethodName ?? resolvedType.ReaderMethodName;
                    }
                }

                updatedColumns.Add(new ProjectedColumn(
                    propertyName: column.PropertyName,
                    columnName: column.ColumnName,
                    clrType: fixedClrType,
                    fullClrType: fixedFullClrType,
                    isNullable: column.IsNullable,
                    ordinal: column.Ordinal,
                    alias: column.Alias,
                    sqlExpression: fixedSql,
                    isAggregateFunction: true,
                    isValueType: fixedIsValueType,
                    readerMethodName: fixedReaderMethod));
            }
            else if (string.IsNullOrEmpty(column.ColumnName) && !column.IsAggregateFunction)
            {
                // Column not found - keep original but log warning
                updatedColumns.Add(column);
            }
            else
            {
                // Keep column as-is (already has name)
                updatedColumns.Add(column);
            }
        }

        // Rebuild the result type name from the enriched columns when the original is unresolved
        var resultTypeName = projection.ResultTypeName;
        if (projection.Kind == ProjectionKind.Tuple && updatedColumns.Count > 0)
        {
            resultTypeName = BuildTupleTypeName(updatedColumns);
        }
        else if (projection.Kind == ProjectionKind.SingleColumn && updatedColumns.Count == 1)
        {
            // Fix scalar Select result type: the discovery phase may produce "?" when
            // the entity type is generated and the semantic model can't resolve the return type.
            var col = updatedColumns[0];
            var colType = !string.IsNullOrWhiteSpace(col.ClrType) && col.ClrType != "?" ? col.ClrType : col.FullClrType;
            if (!string.IsNullOrWhiteSpace(colType) && colType != "?")
            {
                var fixedType = col.IsNullable && !colType.EndsWith("?") ? $"{colType}?" : colType;
                resultTypeName = fixedType;
            }
        }

        // Propagate custom entity reader only for Entity projections
        var entityReaderClass = projection.Kind == ProjectionKind.Entity
            ? entity.CustomEntityReaderClass
            : null;

        return new ProjectionInfo(projection.Kind, resultTypeName, updatedColumns,
            customEntityReaderClass: entityReaderClass);
    }

    /// <summary>
    /// Fixes aggregate SQL expressions by replacing property-name-based column references
    /// with the correct database column names from entity metadata.
    /// </summary>
    /// <remarks>
    /// During discovery, generated entity types may be invisible, so aggregate functions like
    /// SUM("Total") use the property name as column name. This method replaces those with
    /// the actual DB column names from EntityInfo (e.g., SUM("total")).
    /// </remarks>
    /// <summary>
    /// Resolves aggregate CLR type from the SQL expression by extracting the referenced column
    /// and looking up its type in the entity column metadata.
    /// For MIN/MAX/SUM, the return type matches the argument column type.
    /// For AVG, the return type depends on the argument type (but decimal is a safe default).
    /// For COUNT, the return type is always int (handled separately during discovery).
    /// </summary>
    private static ColumnInfo? ResolveAggregateClrTypeFromSql(
        string sqlExpression,
        Dictionary<string, ColumnInfo> columnLookup)
    {
        // Extract the column name from patterns like: MIN("Total"), MAX("column_name"), AVG("Total")
        // The SQL expression is in the form FUNC("ColumnName") or FUNC(quoted_col)
        foreach (var kvp in columnLookup)
        {
            // Check if the SQL expression references this column (by property name or column name)
            if (sqlExpression.Contains($"\"{kvp.Key}\"") || sqlExpression.Contains($"\"{kvp.Value.ColumnName}\""))
            {
                return kvp.Value;
            }
        }

        return null;
    }

    private static string FixAggregateSqlExpression(
        string sqlExpression,
        Dictionary<string, ColumnInfo> columnLookup,
        SqlDialect dialect)
    {
        // For each column in the lookup, replace quoted property names with quoted DB column names
        var result = sqlExpression;
        foreach (var kvp in columnLookup)
        {
            if (kvp.Key == kvp.Value.ColumnName)
                continue; // No change needed

            // Replace quoted property name references: "PropertyName" → "column_name"
            var quotedProperty = QuoteIdentifier(kvp.Key, dialect);
            var quotedColumn = QuoteIdentifier(kvp.Value.ColumnName, dialect);

            if (result.Contains(quotedProperty))
            {
                result = result.Replace(quotedProperty, quotedColumn);
            }
        }

        return result;
    }

    /// <summary>
    /// Quotes an identifier according to the SQL dialect.
    /// </summary>
    private static string QuoteIdentifier(string identifier, SqlDialect dialect)
    {
        return dialect switch
        {
            SqlDialect.MySQL => $"`{identifier}`",
            SqlDialect.SqlServer => $"[{identifier}]",
            _ => $"\"{identifier}\"" // SQLite, PostgreSQL
        };
    }

    /// <summary>
    /// Builds a tuple type name from projected columns.
    /// </summary>
    private static string BuildTupleTypeName(List<ProjectedColumn> columns)
    {
        var elements = columns.Select(c =>
        {
            var typeName = c.ClrType;

            // Check for empty, whitespace, or unresolved type names
            if (string.IsNullOrWhiteSpace(typeName) || typeName == "?")
            {
                typeName = c.FullClrType;
            }

            if (string.IsNullOrWhiteSpace(typeName) || typeName == "?")
            {
                typeName = "object";
            }

            // Add nullable suffix if needed and not already present
            if (c.IsNullable && !typeName.EndsWith("?"))
            {
                typeName += "?";
            }

            // Omit default ItemN names — they cause CS9154 warnings when the
            // original tuple has unnamed elements (e.g., (string, int) vs (string, int Item2))
            var isDefaultName = c.PropertyName.StartsWith("Item") &&
                                int.TryParse(c.PropertyName.Substring(4), out var idx) &&
                                idx == c.Ordinal + 1;
            return isDefaultName ? typeName : $"{typeName} {c.PropertyName}";
        });
        return $"({string.Join(", ", elements)})";
    }

    /// <summary>
    /// Extracts the namespace from a fully qualified type name.
    /// </summary>
    private static string? GetNamespaceFromEntityType(string fullTypeName)
    {
        // Remove global:: prefix if present
        if (fullTypeName.StartsWith("global::"))
        {
            fullTypeName = fullTypeName.Substring(8);
        }

        var lastDot = fullTypeName.LastIndexOf('.');
        if (lastDot <= 0)
            return null;

        return fullTypeName.Substring(0, lastDot);
    }

    /// <summary>
    /// Resolves the CLR value type from the SET column SQL by matching against entity column metadata.
    /// Returns the column's FullClrType, or null if the column cannot be matched.
    /// </summary>
    private static string? ResolveSetValueTypeFromEntity(SetClauseInfo setClause, EntityInfo entity)
    {
        var rawColumnName = setClause.ColumnSql.Trim('"', '[', ']', '`');

        foreach (var column in entity.Columns)
        {
            if (column.ColumnName == rawColumnName)
                return column.FullClrType;
        }

        return null;
    }

    /// <summary>
    /// Enriches a SetClauseInfo with custom type mapping info by matching the column SQL
    /// back to a column in the entity schema.
    /// </summary>
    private static ClauseInfo EnrichSetClauseWithMapping(SetClauseInfo setClause, EntityInfo entity)
    {
        // The columnSql is a quoted column name like "Amount" or [Amount] or `Amount`
        // Strip quoting to match against column names
        var rawColumnName = setClause.ColumnSql
            .Trim('"', '[', ']', '`');

        foreach (var column in entity.Columns)
        {
            if (column.ColumnName == rawColumnName && column.CustomTypeMappingClass != null)
            {
                return new SetClauseInfo(
                    setClause.ColumnSql,
                    setClause.ParameterIndex,
                    setClause.Parameters,
                    column.CustomTypeMappingClass,
                    setClause.ValueTypeName);
            }
        }

        return setClause;
    }

    /// <summary>
    /// Enriches a SetActionClauseInfo with custom type mappings from entity schema columns,
    /// resolves value types, and applies correct dialect-specific column quoting.
    /// </summary>
    private static ClauseInfo EnrichSetActionClauseWithMapping(SetActionClauseInfo actionClause, EntityInfo entity, SqlDialect dialect)
    {
        var enrichedAssignments = new List<SetActionAssignment>(actionClause.Assignments.Count);
        var enrichedParameters = new List<Translation.ParameterInfo>(actionClause.Parameters.Count);
        var paramEnrichIdx = 0;

        for (int i = 0; i < actionClause.Assignments.Count; i++)
        {
            var assignment = actionClause.Assignments[i];

            // The ColumnSql from discovery is the unquoted property name.
            // Resolve the actual column name and quote it with the correct dialect.
            var propertyName = assignment.ColumnSql.Trim('"', '[', ']', '`');
            string? mapping = null;
            string? valueType = assignment.ValueTypeName;
            string? resolvedColumnName = null;
            string? resolvedClrType = null;

            foreach (var column in entity.Columns)
            {
                if (column.PropertyName == propertyName || column.ColumnName == propertyName)
                {
                    resolvedColumnName = column.ColumnName;
                    resolvedClrType = column.FullClrType;
                    if (column.CustomTypeMappingClass != null)
                        mapping = column.CustomTypeMappingClass;
                    if (valueType == null)
                        valueType = column.FullClrType;
                    break;
                }
            }

            // Quote the column name with the correct dialect
            var quotedColumn = QuoteIdentifier(resolvedColumnName ?? propertyName, dialect);

            // Re-format boolean literals for the target dialect.
            // During discovery, inlined SQL values are formatted with DefaultDiscoveryDialect (PostgreSQL).
            // Boolean formatting is dialect-sensitive, so we must re-format here.
            var inlinedSqlValue = ReformatInlinedBooleanForDialect(assignment.InlinedSqlValue, dialect);

            enrichedAssignments.Add(new SetActionAssignment(quotedColumn, valueType, mapping,
                inlinedSqlValue: inlinedSqlValue, inlinedCSharpExpression: assignment.InlinedCSharpExpression));

            // Enrich the corresponding parameter's ClrType from the column metadata.
            // Inlined assignments have no parameter entry — skip them.
            if (!assignment.IsInlined && paramEnrichIdx < actionClause.Parameters.Count)
            {
                var param = actionClause.Parameters[paramEnrichIdx++];
                if (resolvedClrType != null
                    && (string.IsNullOrWhiteSpace(param.ClrType) || param.ClrType == "?" || param.ClrType == "object" || param.ClrType == "object?"))
                {
                    enrichedParameters.Add(new Translation.ParameterInfo(
                        param.Index, param.Name, resolvedClrType, param.ValueExpression,
                        isCaptured: param.IsCaptured, expressionPath: param.ExpressionPath));
                }
                else
                {
                    enrichedParameters.Add(param);
                }
            }
        }

        return new SetActionClauseInfo(enrichedAssignments, enrichedParameters);
    }

    /// <summary>
    /// Re-formats a boolean SQL literal for the target dialect.
    /// During discovery, boolean values are formatted as PostgreSQL ("TRUE"/"FALSE").
    /// Non-PostgreSQL dialects need "1"/"0" instead. Returns the original value unchanged
    /// for non-boolean tokens.
    /// </summary>
    private static string? ReformatInlinedBooleanForDialect(string? sqlValue, SqlDialect dialect)
    {
        if (sqlValue == null)
            return null;

        if (dialect == SqlDialect.PostgreSQL)
        {
            // Already in PostgreSQL format from discovery — pass through.
            return sqlValue;
        }

        // Discovery uses PostgreSQL format, so we only need to convert TRUE/FALSE → 1/0.
        return sqlValue switch
        {
            "TRUE" => "1",
            "FALSE" => "0",
            _ => sqlValue
        };
    }

    /// <summary>
    /// Returns the clause kind name if the site has a non-translatable clause that would
    /// have silently dropped the clause in older versions. Returns null if the site is fine.
    /// </summary>
    private static string? GetNonTranslatableClauseKind(UsageSiteInfo site)
    {
        switch (site.Kind)
        {
            case InterceptorKind.Where:
            case InterceptorKind.DeleteWhere:
            case InterceptorKind.UpdateWhere:
                if (site.ClauseInfo == null || !site.ClauseInfo.IsSuccess)
                    return "Where";
                break;

            case InterceptorKind.OrderBy:
            case InterceptorKind.ThenBy:
                if (site.ClauseInfo is OrderByClauseInfo orderByInfo && orderByInfo.IsSuccess)
                    break;
                if (site.ClauseInfo == null || !site.ClauseInfo.IsSuccess)
                    return site.Kind == InterceptorKind.OrderBy ? "OrderBy" : "ThenBy";
                break;

            case InterceptorKind.GroupBy:
                if (site.ClauseInfo == null || !site.ClauseInfo.IsSuccess)
                    return "GroupBy";
                break;

            case InterceptorKind.Having:
                if (site.ClauseInfo == null || !site.ClauseInfo.IsSuccess)
                    return "Having";
                break;

            case InterceptorKind.Set:
            case InterceptorKind.UpdateSet:
                if (site.ClauseInfo is SetClauseInfo setInfo && setInfo.IsSuccess)
                    break;
                if (site.ClauseInfo == null || !site.ClauseInfo.IsSuccess)
                    return "Set";
                break;

            case InterceptorKind.UpdateSetAction:
                if (site.ClauseInfo is SetActionClauseInfo actionInfo && actionInfo.IsSuccess)
                    break;
                if (site.ClauseInfo == null || !site.ClauseInfo.IsSuccess)
                    return "Set";
                break;

            case InterceptorKind.UpdateSetPoco:
                if (site.UpdateInfo == null || site.UpdateInfo.Columns.Count == 0)
                    return "Set";
                break;
        }

        return null;
    }

    /// <summary>
    /// Enriches a RawSqlTypeInfo with column metadata from a known entity.
    /// Promotes the type kind to Entity and populates TypeMapping, Ref&lt;&gt;, and enum info
    /// from the Pipeline 1 schema.
    /// </summary>
    private static Models.RawSqlTypeInfo EnrichRawSqlTypeInfoWithEntity(
        Models.RawSqlTypeInfo original,
        Models.EntityInfo entity)
    {
        // Build a lookup from property name to column info
        var columnLookup = new Dictionary<string, Models.ColumnInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in entity.Columns)
        {
            columnLookup[column.PropertyName] = column;
        }

        // Re-create properties with enriched metadata from entity columns
        var enrichedProperties = new List<Models.RawSqlPropertyInfo>();
        foreach (var prop in original.Properties)
        {
            if (columnLookup.TryGetValue(prop.PropertyName, out var column))
            {
                // Enriched with entity column metadata
                enrichedProperties.Add(new Models.RawSqlPropertyInfo(
                    propertyName: column.PropertyName,
                    clrType: column.ClrType,
                    readerMethodName: column.ReaderMethodName,
                    isNullable: column.IsNullable,
                    isEnum: column.IsEnum,
                    fullClrType: column.FullClrType,
                    customTypeMappingClass: column.CustomTypeMappingClass,
                    dbReaderMethodName: column.DbReaderMethodName,
                    isForeignKey: column.Kind == ColumnKind.ForeignKey,
                    referencedEntityName: column.ReferencedEntityName));
            }
            else
            {
                // Property not found in entity schema — keep original
                enrichedProperties.Add(prop);
            }
        }

        return new Models.RawSqlTypeInfo(
            original.ResultTypeName,
            Models.RawSqlTypeKind.Entity,
            enrichedProperties,
            original.HasCancellationToken,
            original.ScalarReaderMethod);
    }

    /// <summary>
    /// Checks if a type is a known type that uses GetValue fallback but is still valid
    /// (e.g. DateTimeOffset, TimeSpan). These types legitimately use GetValue and should
    /// not trigger QRY003.
    /// </summary>
    private static bool IsKnownFallbackType(string fullClrType)
    {
        var baseType = fullClrType.TrimEnd('?');
        return baseType is
            "System.DateTimeOffset" or "DateTimeOffset" or
            "System.TimeSpan" or "TimeSpan" or
            "System.DateOnly" or "DateOnly" or
            "System.TimeOnly" or "TimeOnly" or
            "byte[]" or "System.Byte[]";
    }

    // ─── Phase 3: Migration helpers ──────────────────────────────────

    private static MigrationInfo? ExtractMigrationInfo(GeneratorAttributeSyntaxContext ctx)
    {
        var symbol = ctx.TargetSymbol as INamedTypeSymbol;
        if (symbol == null) return null;

        int? version = null;
        string? name = null;

        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.Name != "MigrationAttribute") continue;
            foreach (var arg in attr.NamedArguments)
            {
                if (arg.Key == "Version" && arg.Value.Value is int v) version = v;
                if (arg.Key == "Name" && arg.Value.Value is string n) name = n;
            }
        }

        if (!version.HasValue) return null;

        // Scan method bodies for destructive patterns, backup presence, and references
        var hasDestructive = false;
        var hasBackup = false;
        var hasSql = false;
        var hasAlterColumnNotNull = false;
        var tableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var columnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Methods that take (tableName, ...) as first arg
        var tableOnlyMethods = new HashSet<string> { "CreateTable", "DropTable", "RenameTable" };
        // Methods that take (tableName, columnName, ...) as first two args
        var tableColumnMethods = new HashSet<string> { "AddColumn", "DropColumn", "RenameColumn", "AlterColumn" };

        foreach (var member in symbol.GetMembers())
        {
            if (member is not IMethodSymbol method) continue;

            var syntaxRefs = method.DeclaringSyntaxReferences;
            if (syntaxRefs.Length == 0) continue;

            var methodSyntax = syntaxRefs[0].GetSyntax() as MethodDeclarationSyntax;
            if (methodSyntax?.Body == null) continue;

            var bodyText = methodSyntax.Body.ToString();

            if (method.Name == "Upgrade" || method.Name == "Downgrade")
            {
                if (method.Name == "Upgrade")
                {
                    hasDestructive = bodyText.Contains("DropTable") || bodyText.Contains("DropColumn");
                    hasSql = bodyText.Contains(".Sql(");
                    hasAlterColumnNotNull = bodyText.Contains("AlterColumn") && bodyText.Contains(".NotNull()");
                }

                // Extract table/column references from invocations
                foreach (var invocation in methodSyntax.Body.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    var methodName = invocation.Expression switch
                    {
                        MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
                        _ => null
                    };
                    if (methodName == null) continue;

                    var args = invocation.ArgumentList.Arguments;
                    if (args.Count == 0) continue;

                    if (tableOnlyMethods.Contains(methodName) || tableColumnMethods.Contains(methodName))
                    {
                        if (args[0].Expression is LiteralExpressionSyntax lit0 && lit0.Token.Value is string tbl)
                            tableNames.Add(tbl);
                    }

                    if (tableColumnMethods.Contains(methodName) && args.Count >= 2)
                    {
                        if (args[0].Expression is LiteralExpressionSyntax litT && litT.Token.Value is string tblName
                            && args[1].Expression is LiteralExpressionSyntax litC && litC.Token.Value is string colName)
                        {
                            columnNames.Add(tblName + "." + colName);
                        }
                    }
                }
            }
            else if (method.Name == "Backup")
            {
                hasBackup = methodSyntax.Body.Statements.Count > 0;
            }
        }

        return new MigrationInfo(
            version.Value,
            name ?? "",
            symbol.Name,
            symbol.ContainingNamespace?.ToDisplayString() ?? "",
            hasDestructive,
            hasBackup,
            hasSql,
            hasAlterColumnNotNull,
            tableNames.ToList(),
            columnNames.ToList());
    }

    private static SnapshotInfo? ExtractSnapshotInfo(GeneratorAttributeSyntaxContext ctx)
    {
        var symbol = ctx.TargetSymbol as INamedTypeSymbol;
        if (symbol == null) return null;

        int? version = null;
        string? name = null;
        string? schemaHash = null;

        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.Name != "MigrationSnapshotAttribute") continue;
            foreach (var arg in attr.NamedArguments)
            {
                if (arg.Key == "Version" && arg.Value.Value is int v) version = v;
                if (arg.Key == "Name" && arg.Value.Value is string n) name = n;
                if (arg.Key == "SchemaHash" && arg.Value.Value is string h) schemaHash = h;
            }
        }

        if (!version.HasValue) return null;

        return new SnapshotInfo(
            version.Value,
            name ?? "",
            symbol.Name,
            symbol.ContainingNamespace?.ToDisplayString() ?? "",
            schemaHash ?? "");
    }

    private static void GenerateMigrateAsync(
        ImmutableArray<ContextInfo> contexts,
        ImmutableArray<MigrationInfo> migrations,
        ImmutableArray<SnapshotInfo> snapshots,
        SourceProductionContext spc)
    {
        if (contexts.Length == 0) return;

        if (migrations.Length > 0)
        {
            foreach (var ctx in contexts)
            {
                var source = MigrateAsyncCodeGenerator.Generate(
                    ctx.ClassName,
                    ctx.Namespace,
                    migrations.OrderBy(m => m.Version).ToList());

                spc.AddSource($"{ctx.ClassName}.MigrateAsync.g.cs", source);
            }
        }

        // QRY052: Duplicate migration versions
        var versions = new HashSet<int>();
        foreach (var m in migrations)
        {
            if (!versions.Add(m.Version))
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.MigrationVersionError,
                    Location.None,
                    $"Duplicate migration version {m.Version}"));
            }
        }

        // QRY050: Schema drift detection
        if (snapshots.Length > 0 && contexts.Length > 0)
        {
            var latestSnapshot = snapshots.OrderByDescending(s => s.Version).First();
            if (!string.IsNullOrEmpty(latestSnapshot.SchemaHash))
            {
                // Compute current schema hash from context entities
                foreach (var ctx in contexts)
                {
                    var tableNames = new List<string>(ctx.Entities.Count);
                    var columnSigs = new List<IReadOnlyList<string>>(ctx.Entities.Count);

                    foreach (var entity in ctx.Entities)
                    {
                        tableNames.Add(entity.TableName);
                        var sigs = new List<string>(entity.Columns.Count);
                        foreach (var col in entity.Columns)
                        {
                            sigs.Add(col.ColumnName + "\0" + col.ClrType + "\0"
                                + (col.IsNullable ? "1" : "0") + "\0" + (int)col.Kind
                                + "\0" + (col.Modifiers.IsIdentity ? "1" : "0")
                                + "\0" + (col.Modifiers.IsComputed ? "1" : "0")
                                + "\0" + (col.Modifiers.MaxLength?.ToString() ?? "")
                                + "\0" + (col.Modifiers.Precision?.ToString() ?? "")
                                + "\0" + (col.Modifiers.Scale?.ToString() ?? "")
                                + "\0" + (col.Modifiers.HasDefault ? "1" : "0")
                                + "\0" + (col.Modifiers.MappedName ?? ""));
                        }
                        columnSigs.Add(sigs);
                    }

                    var currentHash = Quarry.Shared.Migration.SchemaHasher.ComputeHashFromEntities(
                        tableNames, columnSigs);

                    if (currentHash != latestSnapshot.SchemaHash)
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(
                            DiagnosticDescriptors.SchemaChangedSinceSnapshot,
                            ctx.Location));
                    }
                }
            }
        }

        // QRY053: Pending migrations count
        if (snapshots.Length > 0 && migrations.Length > 0)
        {
            var latestSnapshotVersion = snapshots.Max(s => s.Version);
            var pendingCount = migrations.Count(m => m.Version > latestSnapshotVersion);
            if (pendingCount > 0)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.PendingMigrations,
                    Location.None,
                    pendingCount));
            }
        }

        // QRY054: Destructive steps without backup
        foreach (var m in migrations)
        {
            if (m.HasDestructiveSteps && !m.HasBackup)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.DestructiveWithoutBackup,
                    Location.None,
                    m.ClassName));
            }
        }

        // QRY051: Migration references unknown table/column
        if (contexts.Length > 0)
        {
            // Build set of known table names and table.column names from all contexts
            var knownTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var knownColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ctx in contexts)
            {
                foreach (var entity in ctx.Entities)
                {
                    knownTables.Add(entity.TableName);
                    foreach (var col in entity.Columns)
                        knownColumns.Add(entity.TableName + "." + col.ColumnName);
                }
            }

            foreach (var m in migrations)
            {
                foreach (var tbl in m.ReferencedTableNames)
                {
                    if (!knownTables.Contains(tbl))
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(
                            DiagnosticDescriptors.MigrationReferencesUnknown,
                            Location.None,
                            tbl));
                    }
                }
                foreach (var col in m.ReferencedColumnNames)
                {
                    if (!knownColumns.Contains(col))
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(
                            DiagnosticDescriptors.MigrationReferencesUnknown,
                            Location.None,
                            col));
                    }
                }
            }
        }

        // QRY055: Nullable→non-null change without data migration
        foreach (var m in migrations)
        {
            if (m.HasAlterColumnNotNull && !m.HasSqlStep)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.NullableToNonNullWithoutMigration,
                    Location.None,
                    m.ClassName));
            }
        }
    }
}
