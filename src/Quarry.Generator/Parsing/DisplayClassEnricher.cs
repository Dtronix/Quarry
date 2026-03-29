using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.IR;

namespace Quarry.Generators.Parsing;

/// <summary>
/// Batch processor that enriches all collected RawCallSites with display class names
/// and captured variable types. Groups sites by containing method to perform closure
/// analysis once per method instead of once per call site.
/// </summary>
internal static class DisplayClassEnricher
{
    public static ImmutableArray<RawCallSite> EnrichAll(
        ImmutableArray<RawCallSite> sites,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        if (sites.Length == 0)
            return sites;

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

        return sites;
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
