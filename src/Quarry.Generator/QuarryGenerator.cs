using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.CodeGen;
using Quarry.Generators.Generation;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;
using Quarry;
using Quarry.Generators.Parsing;
using Quarry.Generators.Utilities;
using Quarry.Shared.Migration;

namespace Quarry.Generators;

/// <summary>
/// Incremental source generator for Quarry schema, interceptor, and migration generation.
/// </summary>
/// <remarks>
/// Three pipelines, split between design-time (IDE) and build-time (<c>dotnet build</c>):
///
/// <b>Design-time (RegisterSourceOutput):</b>
/// Pipeline 1 — Schema/Context (per-context, no Collect):
/// 1. Discovers classes decorated with [QuarryContext] attribute
/// 2. Extracts dialect and database schema configuration
/// 3. Discovers entities via partial QueryBuilder&lt;T&gt; properties
/// 4. Parses schema classes to extract column metadata
/// 5. Generates entity classes, context partials, and column metadata
/// Reports QRY003, QRY017, QRY026, QRY027, QRY028 immediately.
///
/// <b>Build-time only (RegisterImplementationSourceOutput):</b>
/// Pipeline 1 cross-context check — Duplicate TypeMapping detection (QRY016).
/// Pipeline 2 — Interceptors (6-stage IR: discovery → binding → translation →
/// chain analysis → assembly → emission). Produces [InterceptsLocation] methods
/// and carrier classes. Reports QRY001/014-016/019/029-036.
/// Pipeline 3 — Migrations. Discovers [Migration]/[MigrationSnapshot] classes
/// and generates MigrateAsync. Reports QRY050–055.
///
/// Design-time profile: one CreateSyntaxProvider + one RegisterSourceOutput,
/// zero Collect() calls. Pipeline 2's syntax predicates are registered but
/// never pulled because no design-time consumer exists downstream.
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
        // Pipeline 1: Register a syntax provider to find class declarations with [QuarryContext] attribute
        var contextDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsContextCandidate(node),
                transform: static (ctx, ct) => GetContextInfo(ctx, ct))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        // Pipeline 1 output: Per-context entity/context/metadata generation (no Collect needed)
        // Each context is independently cached — changing one context doesn't regenerate others.
        context.RegisterSourceOutput(contextDeclarations,
            static (spc, contextInfo) => GenerateEntityAndContextCode(contextInfo, spc));

        // Pipeline 1 diagnostics: Cross-context duplicate TypeMapping check (needs Collect)
        // Build-time only — rare edge case, not needed for IntelliSense.
        context.RegisterImplementationSourceOutput(contextDeclarations.Collect(),
            static (spc, contexts) =>
            {
                CheckDuplicateTypeMappings(contexts, spc);
                ValidateHasManyThroughNavigations(contexts, spc);
            });

        // === NEW: Build EntityRegistry from collected contexts ===
        var entityRegistry = contextDeclarations.Collect()
            .Select(static (contexts, ct) => IR.EntityRegistry.Build(contexts, ct));

        // === Stage 2: Raw Call Site Discovery (returns RawCallSite, no display class enrichment) ===
        var rawCallSites = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => UsageSiteDiscovery.IsQuarryMethodCandidate(node),
                transform: static (ctx, ct) => DiscoverRawCallSites(ctx, ct))
            .SelectMany(static (sites, _) => sites);

        // === Stage 2.5: Batch display class enrichment (build-time only via downstream consumers) ===
        // Collect all raw sites, then enrich display class names and captured variable types
        // in batch — computing closure analysis once per method instead of once per call site.
        var enrichedCallSites = rawCallSites.Collect()
            .Combine(context.CompilationProvider)
            .Combine(entityRegistry)
            .SelectMany(static (data, ct) =>
                DisplayClassEnricher.EnrichAll(data.Left.Left, data.Left.Right, data.Right, ct));

        // === Stage 3: Per-Site Binding (individually cached) ===
        var boundCallSites = enrichedCallSites
            .Combine(entityRegistry)
            .SelectMany(static (pair, ct) =>
            {
                try { return IR.CallSiteBinder.Bind(pair.Left, pair.Right, ct); }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Quarry] Bind failed: {ex}");
                    var raw = pair.Left;
                    IR.PipelineErrorBag.Report(raw.FilePath, raw.Line, raw.Column,
                        $"Bind: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                    return ImmutableArray<IR.BoundCallSite>.Empty;
                }
            });

        // === Stage 4: Per-Site Translation (individually cached) ===
        var translatedCallSites = boundCallSites
            .Combine(entityRegistry)
            .Select(static (pair, ct) =>
            {
                try { return IR.CallSiteTranslator.Translate(pair.Left, pair.Right, ct); }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Quarry] Translate failed: {ex}");
                    return new IR.TranslatedCallSite(pair.Left, pipelineError: $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                }
            });

        // === Stage 5: Collected Analysis + File Grouping (new pipeline) ===
        var perFileGroups = entityRegistry
            .Combine(translatedCallSites.Collect())
            .SelectMany(static (data, ct) =>
                IR.PipelineOrchestrator.AnalyzeAndGroupTranslated(data.Right, data.Left, ct));

        // Per-file output — only regenerates files whose group changed.
        // Build-time only — interceptors replace builder interface implementations
        // at compile time via [InterceptsLocation]; not needed for IntelliSense.
        // Combine with CompilationProvider so EmitFileInterceptors can reconstruct
        // source locations for diagnostics (Location.Create needs SyntaxTree).
        context.RegisterImplementationSourceOutput(
            perFileGroups.Combine(context.CompilationProvider),
            static (spc, pair) => EmitFileInterceptors(spc, pair.Left, pair.Right));

        // === SQL Manifest Emission (opt-in via QuarrySqlManifestPath MSBuild property) ===
        var manifestConfig = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) =>
            {
                provider.GlobalOptions.TryGetValue(
                    "build_property.QuarrySqlManifestPath", out var manifestRel);
                if (string.IsNullOrWhiteSpace(manifestRel))
                    return default((string?, string?));

                provider.GlobalOptions.TryGetValue(
                    "build_property.ProjectDir", out var projectDir);
                return (manifestRel, projectDir);
            });

        context.RegisterImplementationSourceOutput(
            perFileGroups.Collect().Combine(manifestConfig),
            static (spc, pair) =>
            {
                var (groups, config) = pair;
                var (manifestRel, projectDir) = config;
                if (string.IsNullOrWhiteSpace(manifestRel))
                    return;

                ManifestEmitter.Emit(groups, manifestRel!, projectDir, spc);
            });

        // Pipeline 3: Migration class discovery for MigrateAsync generation
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

        // Build-time only — MigrateAsync is a partial method called at runtime,
        // not needed for IntelliSense.
        context.RegisterImplementationSourceOutput(migrationData,
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
    /// Discovers raw call sites from an invocation expression for the new pipeline.
    /// Returns multiple sites for navigation joins (the join site + post-join sites).
    /// </summary>
    private static ImmutableArray<IR.RawCallSite> DiscoverRawCallSites(GeneratorSyntaxContext context, CancellationToken ct)
    {
        if (context.Node is not InvocationExpressionSyntax invocation)
            return ImmutableArray<IR.RawCallSite>.Empty;

        try
        {
            return UsageSiteDiscovery.DiscoverRawCallSites(invocation, context.SemanticModel, ct);
        }
        catch
        {
            return ImmutableArray<IR.RawCallSite>.Empty;
        }
    }

    /// <summary>
    /// Processes translated call sites into per-file output groups.
    /// The new pipeline (Stages 2-4) has already performed discovery, binding, and
    /// translation. This method delegates to PipelineOrchestrator for chain analysis
    /// and file grouping.
    /// No re-enrichment occurs — the data is already fully enriched.
    /// </summary>
    // Old GroupByFileAndProcessTranslated removed — pipeline calls
    // PipelineOrchestrator.AnalyzeAndGroupTranslated directly.

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

                // Report navigation diagnostics (QRY040-045) collected during schema parsing
                foreach (var navDiag in entity.Diagnostics)
                    context.ReportDiagnostic(navDiag);

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
                $"Failed to generate code for context '{contextInfo.ClassName}': {ex.Message}\n{ex.StackTrace}");

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
    /// Cross-entity validation for HasManyThrough navigations.
    /// Validates that junction navigation names reference actual Many&lt;T&gt; / One&lt;T&gt; properties.
    /// </summary>
    private static void ValidateHasManyThroughNavigations(
        ImmutableArray<ContextInfo> contexts,
        SourceProductionContext spc)
    {
        if (contexts.IsDefaultOrEmpty)
            return;

        // Build entity lookup from all contexts
        var allEntities = new Dictionary<string, EntityInfo>(StringComparer.Ordinal);
        foreach (var contextInfo in contexts)
        {
            foreach (var entity in contextInfo.Entities)
            {
                if (!allEntities.ContainsKey(entity.EntityName))
                    allEntities[entity.EntityName] = entity;
            }
        }

        // Validate through-navigations against junction entity structure.
        // Iterate allEntities (deduplicated) rather than all contexts to avoid
        // reporting the same diagnostic multiple times for entities shared across contexts.
        foreach (var entity in allEntities.Values)
        {
                foreach (var throughNav in entity.ThroughNavigations)
                {
                    if (!allEntities.TryGetValue(throughNav.JunctionEntityName, out var junctionEntity))
                        continue; // Junction entity not found — separate concern

                    // QRY044: junction navigation must be a Many<T> on the source entity
                    bool foundJunctionNav = false;
                    foreach (var nav in entity.Navigations)
                    {
                        if (nav.PropertyName == throughNav.JunctionNavigationName)
                        {
                            foundJunctionNav = true;
                            break;
                        }
                    }
                    if (!foundJunctionNav)
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(
                            DiagnosticDescriptors.HasManyThroughInvalidJunction,
                            entity.Location,
                            throughNav.JunctionNavigationName));
                    }

                    // QRY045: target navigation must be a One<T> on the junction entity
                    bool foundTargetNav = false;
                    foreach (var singleNav in junctionEntity.SingleNavigations)
                    {
                        if (singleNav.PropertyName == throughNav.TargetNavigationName)
                        {
                            foundTargetNav = true;
                            break;
                        }
                    }
                    if (!foundTargetNav)
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(
                            DiagnosticDescriptors.HasManyThroughInvalidTarget,
                            entity.Location,
                            throughNav.TargetNavigationName, throughNav.JunctionEntityName));
                    }
                }
        }
    }

    /// <summary>
    /// Groups all usage sites by (context, source file) and processes them into FileInterceptorGroups.
    /// Replaces the old Execute()+GenerateInterceptors() flow with per-file incremental output.
    /// </summary>
    // Old GroupByFileAndProcess removed — replaced by GroupByFileAndProcessTranslated

    /// <summary>
    /// Emits interceptor source and reports diagnostics for a single (context, file) group.
    /// </summary>
    private static void EmitFileInterceptors(SourceProductionContext spc, FileInterceptorGroup group, Compilation compilation)
    {
        // Find the SyntaxTree for this file so diagnostics get proper source locations
        SyntaxTree? syntaxTree = null;
        foreach (var tree in compilation.SyntaxTrees)
        {
            if (tree.FilePath == group.SourceFilePath)
            {
                syntaxTree = tree;
                break;
            }
        }

        // Report pipeline errors captured during binding/translation
        // Check both Sites and ChainMemberSites — either can carry pipeline errors
        foreach (var site in group.Sites.Concat(group.ChainMemberSites))
        {
            if (site.PipelineError != null)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.InternalError,
                    CreateLineLocation(site.FilePath, site.Line, site.Column),
                    site.PipelineError));
            }
        }

        // Drain side-channel errors from Stage 3 (Bind failures that couldn't attach to a site)
        foreach (var err in IR.PipelineErrorBag.DrainErrors())
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.InternalError,
                CreateLineLocation(err.SourceFilePath, err.Line, err.Column),
                err.Error));
        }

        // Report all deferred diagnostics
        foreach (var diag in group.Diagnostics)
        {
            var descriptor = GetDescriptorById(diag.DiagnosticId);
            if (descriptor == null) continue;

            Location location;
            if (syntaxTree != null && diag.Location.Span.Length > 0)
            {
                location = Location.Create(syntaxTree, diag.Location.Span);
            }
            else
            {
                location = CreateLineLocation(diag.Location.FilePath, diag.Location.Line, diag.Location.Column);
            }

            spc.ReportDiagnostic(Diagnostic.Create(descriptor, location, diag.MessageArgs));
        }

        try
        {
            EmitFileInterceptorsNewPipeline(spc, group, compilation);
        }
        catch (Exception ex)
        {
            var loc = syntaxTree != null ? Location.Create(syntaxTree, default) : Location.None;
            spc.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.InternalError,
                loc,
                $"Failed to emit interceptors for '{group.SourceFilePath}': {ex.Message}\n{ex.StackTrace}"));
        }
    }

    private static void EmitFileInterceptorsNewPipeline(SourceProductionContext spc, FileInterceptorGroup group, Compilation compilation)
    {
        var hasQuarryTrace = HasQuarryTrace(compilation);

        // Build enriched projection lookup from chains (keyed by Select clause site UniqueId)
        // Enrich AssembledPlans with ProjectionInfo, JoinedTableInfos, TraceLines, ReaderDelegateCode
        for (int i = 0; i < group.AssembledPlans.Count; i++)
        {
            var assembled = group.AssembledPlans[i];

            // Resolve ProjectionInfo: prefer QueryPlan.Projection (enriched by ChainAnalyzer with entity
            // metadata), then fall back to Select clause site's raw ProjectionInfo or execution site's.
            ProjectionInfo? projInfo = null;
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
                    var entityRef = assembled.ExecutionSite.Bound.Entity;
                    if (entityRef != null && entityRef.Columns.Count > 0)
                        projInfo = BuildEntityProjectionFromEntityRef(entityRef);
                }
            }
            if (projInfo == null)
            {
                foreach (var cs in assembled.ClauseSites)
                {
                    if (cs.Kind == InterceptorKind.Select && cs.ProjectionInfo != null)
                    {
                        projInfo = cs.ProjectionInfo;
                        break;
                    }
                }
            }
            if (projInfo == null)
                projInfo = assembled.ExecutionSite.ProjectionInfo;
            assembled.ProjectionInfo = projInfo;

            // Generate reader delegate code for SELECT queries (skip if projection failed)
            if (assembled.ReaderDelegateCode == null && assembled.Plan.Kind == QueryKind.Select
                && projInfo != null && projInfo.Kind != ProjectionKind.Unknown)
            {
                var entityType = InterceptorCodeGenerator.GetShortTypeName(assembled.EntityTypeName);
                assembled.ReaderDelegateCode = Projection.ReaderCodeGenerator.GenerateReaderDelegate(projInfo, entityType);
            }

            // Resolve joined table infos
            if (assembled.ExecutionSite.JoinedEntityTypeNames != null)
            {
                var joinedTableInfos = new List<(string, string?)>();
                foreach (var join in assembled.Plan.Joins)
                    joinedTableInfos.Add((join.Table.TableName, join.Table.SchemaName));
                assembled.JoinedTableInfos = joinedTableInfos;
            }

            // Collect trace lines from TraceCapture side-channel
            if (assembled.IsTraced && hasQuarryTrace)
            {
                var execUid = assembled.ExecutionSite.UniqueId;
                var execTrace = IR.TraceCapture.Get(execUid);
                if (execTrace != null && execTrace.Count > 0)
                    assembled.TraceLines = execTrace;
            }

            // Post-process SQL to tokenize collection parameter placeholders for carrier expansion
            var isCarrierEligible = i < group.CarrierPlans.Count && group.CarrierPlans[i].IsEligible;
            if (isCarrierEligible && assembled.Plan.Parameters.Count > 0)
                TokenizeCollectionParameters(assembled.SqlVariants, assembled.Plan.Parameters, assembled.Dialect);
        }

        // Merge sites and chain member sites
        var mergedSites = new List<IR.TranslatedCallSite>(
            group.Sites.Count + group.ChainMemberSites.Count);
        mergedSites.AddRange(group.Sites);
        mergedSites.AddRange(group.ChainMemberSites);

        if (mergedSites.Count == 0) return;

        // Filter AssembledPlans: skip RuntimeBuild and incomplete tuple types
        var filteredPlans = new List<IR.AssembledPlan>();
        var filteredCarrierPlans = new List<CodeGen.CarrierPlan>();
        for (int i = 0; i < group.AssembledPlans.Count; i++)
        {
            var assembled = group.AssembledPlans[i];
            if (assembled.Plan.Tier == OptimizationTier.RuntimeBuild)
                continue;

            if (assembled.ResultTypeName != null
                && assembled.ResultTypeName.StartsWith("(")
                && assembled.ResultTypeName.Contains(",")
                && System.Text.RegularExpressions.Regex.IsMatch(assembled.ResultTypeName, @"\(\s\w"))
            {
                var hasSelectClause = assembled.ClauseSites.Any(cs => cs.Kind == InterceptorKind.Select);
                if (!hasSelectClause) continue;
            }

            filteredPlans.Add(assembled);
            if (i < group.CarrierPlans.Count)
                filteredCarrierPlans.Add(group.CarrierPlans[i]);
        }

        // Emit QRY030-032 diagnostics from chain analysis
        foreach (var assembled in group.AssembledPlans)
        {
            var execRaw = assembled.ExecutionSite.Bound.Raw;
            var location = execRaw.Location;
            var locationDisplay = $"{GetRelativePath(execRaw.FilePath)}:{execRaw.Line}";

            switch (assembled.Plan.Tier)
            {
                case OptimizationTier.PrebuiltDispatch:
                    spc.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.ChainOptimized,
                        Location.Create(execRaw.FilePath, location.Span, new Microsoft.CodeAnalysis.Text.LinePositionSpan(
                            new Microsoft.CodeAnalysis.Text.LinePosition(execRaw.Line - 1, execRaw.Column - 1),
                            new Microsoft.CodeAnalysis.Text.LinePosition(execRaw.Line - 1, execRaw.Column - 1))),
                        locationDisplay, assembled.Plan.PossibleMasks.Count.ToString()));
                    break;
                case OptimizationTier.RuntimeBuild:
                    if (assembled.Plan.ForkedVariableName != null)
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(
                            DiagnosticDescriptors.ForkedQueryChain,
                            Location.Create(execRaw.FilePath, location.Span, new Microsoft.CodeAnalysis.Text.LinePositionSpan(
                                new Microsoft.CodeAnalysis.Text.LinePosition(execRaw.Line - 1, execRaw.Column - 1),
                                new Microsoft.CodeAnalysis.Text.LinePosition(execRaw.Line - 1, execRaw.Column - 1))),
                            assembled.Plan.ForkedVariableName));
                    }
                    else
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(
                            DiagnosticDescriptors.ChainNotAnalyzable,
                            Location.Create(execRaw.FilePath, location.Span, new Microsoft.CodeAnalysis.Text.LinePositionSpan(
                                new Microsoft.CodeAnalysis.Text.LinePosition(execRaw.Line - 1, execRaw.Column - 1),
                                new Microsoft.CodeAnalysis.Text.LinePosition(execRaw.Line - 1, execRaw.Column - 1))),
                            locationDisplay, assembled.Plan.NotAnalyzableReason ?? "unknown"));
                    }
                    break;
            }
        }

        // Report QRY034 warning for traced chains when QUARRY_TRACE is not defined
        if (!hasQuarryTrace)
        {
            foreach (var assembled in group.AssembledPlans)
            {
                if (!assembled.IsTraced) continue;
                var execRaw = assembled.ExecutionSite.Bound.Raw;
                spc.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.TraceWithoutFlag,
                    Location.Create(execRaw.FilePath, execRaw.Location.Span, new Microsoft.CodeAnalysis.Text.LinePositionSpan(
                        new Microsoft.CodeAnalysis.Text.LinePosition(execRaw.Line - 1, execRaw.Column - 1),
                        new Microsoft.CodeAnalysis.Text.LinePosition(execRaw.Line - 1, execRaw.Column - 1))),
                    $"{GetRelativePath(execRaw.FilePath)}:{execRaw.Line}"));
            }
        }

        try
        {
            var emitter = new CodeGen.FileEmitter(
                group.ContextClassName,
                group.ContextNamespace,
                group.FileTag,
                mergedSites,
                filteredPlans,
                filteredCarrierPlans);
            var interceptorsSource = emitter.Emit();
            var fileName = $"{group.ContextClassName}.Interceptors.{group.FileTag}.g.cs";
            spc.AddSource(fileName, interceptorsSource);

            // Report diagnostics collected during emission (e.g., QRY041 for unresolvable columns)
            foreach (var diag in emitter.EmitDiagnostics)
            {
                var descriptor = GetDescriptorById(diag.DiagnosticId);
                if (descriptor == null) continue;
                var location = CreateLineLocation(diag.Location.FilePath, diag.Location.Line, diag.Location.Column);
                spc.ReportDiagnostic(Diagnostic.Create(descriptor, location, diag.MessageArgs));
            }
        }
        catch (Exception ex)
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.InternalError,
                Location.None,
                $"Failed to generate interceptors: {ex.Message}\n{ex.StackTrace}"));
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
        DiagnosticDescriptors.ChainOptimized,
        DiagnosticDescriptors.UnresolvableRawSqlTypeParameter,
        DiagnosticDescriptors.ChainNotAnalyzable,
        DiagnosticDescriptors.ForkedQueryChain,
        DiagnosticDescriptors.SqlRawPlaceholderMismatch,
        DiagnosticDescriptors.PreparedQueryEscapesScope,
        DiagnosticDescriptors.PreparedQueryNoTerminals,
        DiagnosticDescriptors.RawSqlUnresolvableColumn,
        DiagnosticDescriptors.CteInnerChainNotAnalyzable,
        DiagnosticDescriptors.FromCteWithoutWith,
        DiagnosticDescriptors.DuplicateCteName,
    }.ToDictionary(d => d.Id);

    private static DiagnosticDescriptor? GetDescriptorById(string id) =>
        s_deferredDescriptors.TryGetValue(id, out var descriptor) ? descriptor : null;

    private static Location CreateLineLocation(string? filePath, int line, int column)
    {
        if (filePath == null || line <= 0) return Location.None;
        var pos = new Microsoft.CodeAnalysis.Text.LinePosition(line - 1, column - 1);
        return Location.Create(filePath, default, new Microsoft.CodeAnalysis.Text.LinePositionSpan(pos, pos));
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
                    isEnum: col.IsEnum,
                    isJoinNullable: col.IsJoinNullable));
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
    /// Replaces collection parameter placeholders in pre-built SQL with expansion tokens.
    /// For example, <c>IN (@p0)</c> becomes <c>IN ({__COL_P0__})</c> when P0 is a collection.
    /// The carrier terminal expands these tokens at runtime based on the actual collection size.
    /// </summary>
    private static void TokenizeCollectionParameters(
        Dictionary<int, IR.AssembledSqlVariant> sqlMap,
        IReadOnlyList<IR.QueryParameter> chainParams,
        SqlDialect dialect)
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
        var pendingUpdates = new List<(int Key, string Sql, int ParamCount)>();
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
            sqlMap[key] = new IR.AssembledSqlVariant(sql, paramCount);
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
    /// Gets a relative file path for display in diagnostics.
    /// </summary>
    private static string GetRelativePath(string filePath)
    {
        // Simple: just return the filename
        var lastSlash = filePath.LastIndexOfAny(new[] { '/', '\\' });
        return lastSlash >= 0 ? filePath.Substring(lastSlash + 1) : filePath;
    }

    /// <summary>
    /// Checks if the consumer project defines the QUARRY_TRACE preprocessor symbol.
    /// </summary>
    private static bool HasQuarryTrace(Compilation compilation)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            if (((Microsoft.CodeAnalysis.CSharp.CSharpParseOptions)tree.Options)
                .PreprocessorSymbolNames.Contains("QUARRY_TRACE"))
                return true;
        }
        return false;
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

    // ─── Pipeline 3: Migration helpers ─────────────────────────────────

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
                    ctx.Dialect.ToString(),
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
