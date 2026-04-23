using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.Generation;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Shared.Migration;

namespace Quarry.Generators.Parsing;

/// <summary>
/// Batch processor that enriches all collected RawCallSites with display class names,
/// captured variable types, and RawSql type info. Groups sites by containing method to perform closure
/// analysis once per method instead of once per call site.
/// </summary>
internal static class DisplayClassEnricher
{
    public static ImmutableArray<RawCallSite> EnrichAll(
        ImmutableArray<RawCallSite> sites,
        Compilation compilation,
        EntityRegistry entityRegistry,
        CancellationToken cancellationToken)
    {
        if (sites.Length == 0)
            return sites;

        // Deduplicate sites by InterceptableLocationData. Multiple discovery paths
        // (DiscoverPostCteSites/DiscoverPostJoinSites + normal DiscoverRawCallSites)
        // can produce sites targeting the same call. When both a synthetic site (from
        // post-CTE/post-Join discovery, which sets BuilderTypeName explicitly) and a
        // normal discovery site exist for the same location, prefer the normal site
        // because it has more accurate type information from the SemanticModel.
        if (sites.Length > 1)
        {
            var seen = new Dictionary<string, int>(); // locData → index in deduped
            var deduped = ImmutableArray.CreateBuilder<RawCallSite>(sites.Length);
            foreach (var site in sites)
            {
                var locData = site.InterceptableLocationData;
                if (locData == null)
                {
                    deduped.Add(site);
                    continue;
                }
                if (seen.TryGetValue(locData, out var existingIdx))
                {
                    // Prefer normal discovery (BuilderTypeName == null) over synthetic
                    if (deduped[existingIdx].BuilderTypeName != null && site.BuilderTypeName == null)
                        deduped[existingIdx] = site;
                    // else keep existing
                }
                else
                {
                    seen[locData] = deduped.Count;
                    deduped.Add(site);
                }
            }
            if (deduped.Count < sites.Length)
                sites = deduped.ToImmutable();
        }

        // Build a supplemental compilation that includes generated entity and context
        // source. The original compilation does not contain entity classes or context
        // accessor methods produced by Pipeline A (RegisterSourceOutput), so variables
        // flowing from those methods have TypeKind.Error in the semantic model.
        // Adding the generated source lets Roslyn resolve all types natively.
        if (entityRegistry != null)
            compilation = BuildSupplementalCompilation(compilation, entityRegistry);

        // Cache semantic models per syntax tree
        var semanticModelCache = new Dictionary<SyntaxTree, SemanticModel>();

        // Cache per containing method (keyed by method syntax node via ReferenceEquals)
        var comparer = DisplayClassNameResolver.SyntaxNodeComparer.Instance;
        var methodAnalysisCache = new Dictionary<SyntaxNode, MethodAnalysisResult>(comparer);

        foreach (var site in sites)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (site.EnrichmentLambda == null)
                continue;

            var lambda = site.EnrichmentLambda;
            var syntaxTree = lambda.SyntaxTree;

            if (!semanticModelCache.TryGetValue(syntaxTree, out var semanticModel))
            {
                semanticModel = compilation.GetSemanticModel(syntaxTree);
                semanticModelCache[syntaxTree] = semanticModel;
            }

            // Find the enclosing method symbol
            var enclosing = semanticModel.GetEnclosingSymbol(lambda.SpanStart);
            while (enclosing != null && enclosing is not IMethodSymbol)
                enclosing = enclosing.ContainingSymbol;

            if (enclosing is not IMethodSymbol method)
                continue;

            // Walk up past local functions to find the effective (non-local) method
            var effectiveMethod = method;
            while (effectiveMethod.MethodKind == MethodKind.LocalFunction
                   && effectiveMethod.ContainingSymbol is IMethodSymbol parent)
            {
                effectiveMethod = parent;
            }

            var containingType = effectiveMethod.ContainingType;
            if (containingType == null)
                continue;

            var methodSyntax = effectiveMethod.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
            if (methodSyntax == null)
                continue;

            // Get or create the per-method analysis
            if (!methodAnalysisCache.TryGetValue(methodSyntax, out var analysisResult))
            {
                int methodOrdinal = DisplayClassNameResolver.ComputeMethodOrdinal(containingType, effectiveMethod);
                if (methodOrdinal < 0)
                    continue;

                var closureAnalysis = DisplayClassNameResolver.AnalyzeMethodClosures(methodSyntax, semanticModel);

                var typeName = containingType.ToDisplayString(new SymbolDisplayFormat(
                    typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces));
                var displayClassPrefix = $"{typeName}+<>c__DisplayClass{methodOrdinal}_";

                analysisResult = new MethodAnalysisResult(
                    closureAnalysis, displayClassPrefix, methodSyntax, semanticModel);
                methodAnalysisCache[methodSyntax] = analysisResult;
            }

            var analysis = analysisResult.ClosureAnalysis;

            int closureOrdinal = analysis.DataFlowByNode.ContainsKey(lambda)
                ? DisplayClassNameResolver.LookupClosureOrdinal(analysis, lambda, analysisResult.MethodSyntax)
                : 0;

            site.DisplayClassName = $"{analysisResult.DisplayClassPrefix}{closureOrdinal}";

            // Classify the capture kind and set CapturedVariableTypes for closure locals.
            // Exclude the implicit 'this' parameter — the compiler captures 'this' directly
            // without a display class, so it should be treated as FieldCapture.
            if (analysis.DataFlowByNode.TryGetValue(lambda, out var dataFlow)
                && dataFlow.Succeeded
                && dataFlow.CapturedInside.Any(s => s is ILocalSymbol
                    || (s is IParameterSymbol p && !p.IsThis)))
            {
                site.CaptureKind = CaptureKind.ClosureCapture;
                site.CapturedVariableTypes = DisplayClassNameResolver.CollectCapturedVariableTypes(
                    dataFlow, analysisResult.SemanticModel);
            }
            else
            {
                site.CaptureKind = CaptureKind.FieldCapture;
            }
        }

        // === RawSql type resolution pass ===
        // Resolve RawSqlTypeInfo for RawSql call sites using the supplemental compilation.
        // This must happen here (after BuildSupplementalCompilation) because generated entity
        // types are not yet in the compilation during Stage 2 discovery.
        EnrichRawSqlTypeInfo(sites, compilation, entityRegistry, semanticModelCache, cancellationToken);

        return sites;
    }

    /// <summary>
    /// Resolves RawSqlTypeInfo for RawSql call sites that stored their invocation syntax
    /// for deferred enrichment. Uses the supplemental compilation's semantic model so
    /// generated entity type members are visible. For entity types found in the registry,
    /// patches properties with schema-level metadata (custom type mappings, foreign keys).
    /// </summary>
    private static void EnrichRawSqlTypeInfo(
        ImmutableArray<RawCallSite> sites,
        Compilation compilation,
        EntityRegistry? entityRegistry,
        Dictionary<SyntaxTree, SemanticModel> semanticModelCache,
        CancellationToken cancellationToken)
    {
        foreach (var site in sites)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (site.EnrichmentInvocation == null)
                continue;

            var invocation = site.EnrichmentInvocation;
            var syntaxTree = invocation.SyntaxTree;

            if (!semanticModelCache.TryGetValue(syntaxTree, out var semanticModel))
            {
                semanticModel = compilation.GetSemanticModel(syntaxTree);
                semanticModelCache[syntaxTree] = semanticModel;
            }

            // Re-resolve the method symbol from the supplemental compilation
            var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
            if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
                continue;
            if (!methodSymbol.IsGenericMethod || methodSymbol.TypeArguments.Length == 0)
                continue;

            var typeArgSymbol = methodSymbol.TypeArguments[0];
            if (typeArgSymbol.TypeKind == TypeKind.TypeParameter || typeArgSymbol.TypeKind == TypeKind.Error)
                continue;

            var hasCancellationToken = methodSymbol.Parameters.Length >= 3
                && methodSymbol.Parameters[1].Type.Name == "CancellationToken";

            var rawSqlTypeInfo = UsageSiteDiscovery.ResolveRawSqlTypeInfo(typeArgSymbol, hasCancellationToken);

            // For entity types in the registry, patch properties with schema-level metadata
            // (custom type mappings, foreign keys) that the type symbol doesn't carry.
            if (entityRegistry != null
                && rawSqlTypeInfo.TypeKind != RawSqlTypeKind.Scalar
                && rawSqlTypeInfo.Properties.Count > 0)
            {
                var entry = entityRegistry.Resolve(site.EntityTypeName, site.ContextClassName);
                if (entry != null)
                    rawSqlTypeInfo = PatchWithColumnMetadata(rawSqlTypeInfo, entry.Entity.Columns);
            }

            // Extract SQL string literal from the first argument for compile-time column resolution.
            // Only captures compile-time constant strings; variables and interpolated strings are skipped.
            string? sqlLiteral = null;
            if (invocation.ArgumentList?.Arguments.Count > 0)
            {
                var firstArg = invocation.ArgumentList.Arguments[0].Expression;
                if (firstArg is LiteralExpressionSyntax literal
                    && literal.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    sqlLiteral = literal.Token.ValueText;
                }
            }

            if (sqlLiteral != null)
            {
                rawSqlTypeInfo = new RawSqlTypeInfo(
                    rawSqlTypeInfo.ResultTypeName,
                    rawSqlTypeInfo.TypeKind,
                    rawSqlTypeInfo.Properties,
                    rawSqlTypeInfo.HasCancellationToken,
                    rawSqlTypeInfo.ScalarReaderMethod,
                    sqlLiteral);
            }

            site.RawSqlTypeInfo = rawSqlTypeInfo;

            // Materializability check (QRY043): the row reader calls `new T()` and assigns each
            // column to a public settable property. Positional records have no parameterless
            // constructor; init-only properties are rejected by the C# compiler when assigned
            // outside an object initializer. Only check DTO row types — scalars and entity types
            // in the registry have their own code paths.
            if (rawSqlTypeInfo.TypeKind == RawSqlTypeKind.Dto
                && typeArgSymbol is INamedTypeSymbol namedType)
            {
                var materializabilityError = CheckRowEntityMaterializability(namedType);
                if (materializabilityError != null)
                    site.MaterializabilityError = materializabilityError;
            }

            // Clear transient reference — no longer needed after enrichment
            site.EnrichmentInvocation = null;
        }
    }

    /// <summary>
    /// Returns null if the type can be materialized by the source-generated row reader,
    /// or a human-readable reason why not. Verifies the type has a public parameterless
    /// constructor and that no public settable property is init-only.
    /// </summary>
    private static string? CheckRowEntityMaterializability(INamedTypeSymbol type)
    {
        // Structs always have an implicit parameterless constructor.
        var isStruct = type.TypeKind == TypeKind.Struct;
        if (!isStruct)
        {
            var hasParameterlessCtor = false;
            foreach (var ctor in type.InstanceConstructors)
            {
                if (ctor.Parameters.Length == 0
                    && ctor.DeclaredAccessibility == Accessibility.Public)
                {
                    hasParameterlessCtor = true;
                    break;
                }
            }
            if (!hasParameterlessCtor)
                return "no accessible parameterless constructor (positional records are not supported)";
        }

        foreach (var member in type.GetMembers())
        {
            if (member is not IPropertySymbol prop)
                continue;
            if (prop.DeclaredAccessibility != Accessibility.Public)
                continue;
            if (prop.IsStatic || prop.IsIndexer)
                continue;
            if (prop.SetMethod is { IsInitOnly: true })
                return $"property '{prop.Name}' is init-only and cannot be assigned by the generated reader";
        }

        return null;
    }

    /// <summary>
    /// Patches RawSqlPropertyInfo entries with schema-level column metadata from Pipeline 1.
    /// Copies CustomTypeMappingClass, DbReaderMethodName, IsForeignKey, and ReferencedEntityName
    /// from the matching ColumnInfo. Returns a new RawSqlTypeInfo if any property was patched.
    /// </summary>
    private static RawSqlTypeInfo PatchWithColumnMetadata(
        RawSqlTypeInfo typeInfo,
        IReadOnlyList<ColumnInfo> columns)
    {
        // Build a name→ColumnInfo lookup for O(1) matching
        var columnByName = new Dictionary<string, ColumnInfo>(columns.Count, System.StringComparer.Ordinal);
        foreach (var col in columns)
            columnByName[col.PropertyName] = col;

        var patched = false;
        var newProperties = new RawSqlPropertyInfo[typeInfo.Properties.Count];

        for (int i = 0; i < typeInfo.Properties.Count; i++)
        {
            var prop = typeInfo.Properties[i];
            if (columnByName.TryGetValue(prop.PropertyName, out var col)
                && (col.CustomTypeMappingClass != null || col.Kind == ColumnKind.ForeignKey))
            {
                newProperties[i] = new RawSqlPropertyInfo(
                    propertyName: prop.PropertyName,
                    clrType: prop.ClrType,
                    readerMethodName: col.ReaderMethodName,
                    isNullable: prop.IsNullable,
                    isEnum: prop.IsEnum,
                    fullClrType: prop.FullClrType,
                    customTypeMappingClass: col.CustomTypeMappingClass,
                    dbReaderMethodName: col.DbReaderMethodName,
                    isForeignKey: col.Kind == ColumnKind.ForeignKey,
                    referencedEntityName: col.ReferencedEntityName);
                patched = true;
            }
            else
            {
                newProperties[i] = prop;
            }
        }

        if (!patched)
            return typeInfo;

        return new RawSqlTypeInfo(
            typeInfo.ResultTypeName,
            typeInfo.TypeKind,
            newProperties,
            typeInfo.HasCancellationToken,
            typeInfo.ScalarReaderMethod);
    }

    /// <summary>
    /// Builds a compilation that includes the generated entity classes and context
    /// partial classes so the semantic model can resolve all generated types natively.
    /// <c>Compilation.AddSyntaxTrees()</c> is cheap — Roslyn compilations are immutable
    /// and share state, so the new compilation reuses almost everything from the original.
    /// </summary>
    private static Compilation BuildSupplementalCompilation(
        Compilation compilation, EntityRegistry entityRegistry)
    {
        var generatedTrees = new List<SyntaxTree>();

        // Parse options must match the existing compilation to avoid
        // "Inconsistent syntax tree features" when adding trees.
        var parseOptions = compilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions;

        foreach (var contextInfo in entityRegistry.AllContexts)
        {
            // Entity classes
            foreach (var entity in contextInfo.Entities)
            {
                var src = EntityCodeGenerator.GenerateEntityClass(entity, contextInfo.Namespace);
                generatedTrees.Add(CSharpSyntaxTree.ParseText(src, parseOptions));
            }

            // Context partial class with accessor methods
            var ctxSrc = ContextCodeGenerator.GenerateContextClass(contextInfo);
            generatedTrees.Add(CSharpSyntaxTree.ParseText(ctxSrc, parseOptions));
        }

        return generatedTrees.Count > 0
            ? compilation.AddSyntaxTrees(generatedTrees)
            : compilation;
    }

    private sealed class MethodAnalysisResult
    {
        public MethodAnalysisResult(
            DisplayClassNameResolver.MethodClosureAnalysis closureAnalysis,
            string displayClassPrefix,
            SyntaxNode methodSyntax,
            SemanticModel semanticModel)
        {
            ClosureAnalysis = closureAnalysis;
            DisplayClassPrefix = displayClassPrefix;
            MethodSyntax = methodSyntax;
            SemanticModel = semanticModel;
        }

        public DisplayClassNameResolver.MethodClosureAnalysis ClosureAnalysis { get; }
        public string DisplayClassPrefix { get; }
        public SyntaxNode MethodSyntax { get; }
        public SemanticModel SemanticModel { get; }
    }
}
