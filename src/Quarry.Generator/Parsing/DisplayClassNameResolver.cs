using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Quarry.Generators.Parsing;

/// <summary>
/// Provides utilities for predicting compiler-generated display class names
/// and analyzing closure captures within methods. Used by
/// <see cref="DisplayClassEnricher"/> for batch enrichment.
/// Display class naming convention:
///   ContainingType+&lt;&gt;c__DisplayClass{methodOrdinal}_{closureOrdinal}
/// </summary>
internal static class DisplayClassNameResolver
{
    /// <summary>
    /// Computes the method ordinal: the index of the method in the containing type's
    /// GetMembers() array. ALL members count (backing fields, properties, accessor
    /// methods, events, fields, methods) because the C# compiler uses the same ordering.
    /// For local functions, matches against the synthesized method name pattern
    /// (e.g., &lt;&lt;Main&gt;$&gt;g__MethodName|N_M).
    /// </summary>
    internal static int ComputeMethodOrdinal(INamedTypeSymbol containingType, IMethodSymbol methodSymbol)
    {
        var members = containingType.GetMembers();

        // Direct match (regular methods, constructors, accessors)
        for (int i = 0; i < members.Length; i++)
        {
            if (SymbolEqualityComparer.Default.Equals(members[i], methodSymbol))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Attempts to resolve the type of a captured variable when the semantic model
    /// reports TypeKind.Error (typically because the variable is assigned from a
    /// generator-produced method whose return type isn't yet resolved).
    /// Handles patterns like: var x = await something.Method&lt;T&gt;()
    /// </summary>
    private static string? TryResolveErrorType(ISymbol symbol, SemanticModel semanticModel)
    {
        var declRef = symbol.DeclaringSyntaxReferences.FirstOrDefault();
        if (declRef == null)
            return null;

        var declSyntax = declRef.GetSyntax();
        if (declSyntax is not VariableDeclaratorSyntax { Initializer.Value: { } initValue })
            return null;

        // Unwrap await expressions: var x = await expr → analyze expr
        if (initValue is AwaitExpressionSyntax awaitExpr)
            initValue = awaitExpr.Expression;

        // Look for generic method invocations: something.Method<T>()
        // Extract T from the type argument list.
        if (initValue is InvocationExpressionSyntax invocation)
        {
            GenericNameSyntax? genericName = null;
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess
                && memberAccess.Name is GenericNameSyntax g1)
            {
                genericName = g1;
            }
            else if (invocation.Expression is GenericNameSyntax g2)
            {
                genericName = g2;
            }

            if (genericName != null && genericName.TypeArgumentList.Arguments.Count == 1)
            {
                var typeArg = genericName.TypeArgumentList.Arguments[0];
                var typeInfo = semanticModel.GetTypeInfo(typeArg);
                if (typeInfo.Type != null && typeInfo.Type.TypeKind != TypeKind.Error)
                    return typeInfo.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }
        }

        return null;
    }

    private static void AssignOrdinalsPreOrder(
        SyntaxNode node,
        HashSet<SyntaxNode> scopesWithCaptures,
        Dictionary<SyntaxNode, int> scopeOrdinals,
        ref int nextOrdinal)
    {
        if (scopesWithCaptures.Contains(node) && !scopeOrdinals.ContainsKey(node))
            scopeOrdinals[node] = nextOrdinal++;

        foreach (var child in node.ChildNodes())
        {
            AssignOrdinalsPreOrder(child, scopesWithCaptures, scopeOrdinals, ref nextOrdinal);
        }
    }

    private static SyntaxNode FindDeclaringScope(ISymbol variable, SyntaxNode methodRoot)
    {
        var declRef = variable.DeclaringSyntaxReferences.FirstOrDefault();
        if (declRef == null)
            return methodRoot;

        var declNode = declRef.GetSyntax();
        var current = declNode.Parent;
        while (current != null)
        {
            if (current is BlockSyntax)
                return current;
            if (current == methodRoot)
                return methodRoot;
            current = current.Parent;
        }
        return methodRoot;
    }

    /// <summary>
    /// Builds the scope-ordinal map and dataflow cache for all closures in a method.
    /// Called once per method by DisplayClassEnricher.
    /// </summary>
    internal static MethodClosureAnalysis AnalyzeMethodClosures(
        SyntaxNode methodSyntax,
        SemanticModel semanticModel)
    {
        var scopesWithCaptures = new HashSet<SyntaxNode>(SyntaxNodeComparer.Instance);
        var dataFlowByNode = new Dictionary<SyntaxNode, DataFlowAnalysis>(SyntaxNodeComparer.Instance);

        var allClosures = methodSyntax.DescendantNodes()
            .Where(n => n is LambdaExpressionSyntax || n is LocalFunctionStatementSyntax)
            .ToArray();

        foreach (var closure in allClosures)
        {
            DataFlowAnalysis? dataFlow = null;

            if (closure is LambdaExpressionSyntax lambda)
                dataFlow = semanticModel.AnalyzeDataFlow(lambda);
            else if (closure is LocalFunctionStatementSyntax localFunc && localFunc.Body != null)
                dataFlow = semanticModel.AnalyzeDataFlow(localFunc.Body);

            if (dataFlow == null || !dataFlow.Succeeded)
                continue;

            if (closure is LambdaExpressionSyntax lam)
                dataFlowByNode[lam] = dataFlow;

            foreach (var capturedVar in dataFlow.CapturedInside)
            {
                if (capturedVar is ILocalSymbol || capturedVar is IParameterSymbol)
                {
                    var declScope = FindDeclaringScope(capturedVar, methodSyntax);
                    if (declScope != null)
                        scopesWithCaptures.Add(declScope);
                }
            }
        }

        var scopeOrdinals = new Dictionary<SyntaxNode, int>(SyntaxNodeComparer.Instance);
        int nextOrdinal = 0;
        AssignOrdinalsPreOrder(methodSyntax, scopesWithCaptures, scopeOrdinals, ref nextOrdinal);

        return new MethodClosureAnalysis(dataFlowByNode, scopeOrdinals);
    }

    /// <summary>
    /// Looks up the closure ordinal for a lambda using pre-computed analysis.
    /// Returns 0 if the lambda has no captured local/parameter variables.
    /// </summary>
    internal static int LookupClosureOrdinal(
        MethodClosureAnalysis analysis,
        LambdaExpressionSyntax lambda,
        SyntaxNode methodSyntax)
    {
        if (!analysis.DataFlowByNode.TryGetValue(lambda, out var dataFlow))
            return 0;

        foreach (var capturedVar in dataFlow.CapturedInside)
        {
            if (capturedVar is ILocalSymbol || capturedVar is IParameterSymbol)
            {
                var declScope = FindDeclaringScope(capturedVar, methodSyntax);
                if (declScope != null && analysis.ScopeOrdinals.TryGetValue(declScope, out int ordinal))
                    return ordinal;
            }
        }

        return 0;
    }

    /// <summary>
    /// Overload that uses a pre-computed DataFlowAnalysis instead of calling AnalyzeDataFlow.
    /// </summary>
    public static Dictionary<string, string>? CollectCapturedVariableTypes(
        DataFlowAnalysis dataFlow,
        SemanticModel semanticModel)
    {
        if (!dataFlow.Succeeded)
            return null;

        var captured = dataFlow.CapturedInside
            .Where(s => s is ILocalSymbol || s is IParameterSymbol)
            .Distinct<ISymbol>(SymbolEqualityComparer.Default)
            .ToArray();

        if (captured.Length == 0)
            return null;

        var result = new Dictionary<string, string>();
        foreach (var symbol in captured)
        {
            string varName;
            ITypeSymbol? typeSymbol;
            if (symbol is ILocalSymbol local)
            {
                varName = local.Name;
                typeSymbol = local.Type;
            }
            else if (symbol is IParameterSymbol param)
            {
                varName = param.Name;
                typeSymbol = param.Type;
            }
            else continue;

            var varType = typeSymbol.TypeKind == TypeKind.Error
                ? (TryResolveErrorType(symbol, semanticModel) ?? "object")
                : typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (string.IsNullOrWhiteSpace(varType))
                varType = "object";

            result[varName] = varType;
        }

        return result.Count > 0 ? result : null;
    }

    internal sealed class MethodClosureAnalysis
    {
        public MethodClosureAnalysis(
            Dictionary<SyntaxNode, DataFlowAnalysis> dataFlowByNode,
            Dictionary<SyntaxNode, int> scopeOrdinals)
        {
            DataFlowByNode = dataFlowByNode;
            ScopeOrdinals = scopeOrdinals;
        }

        public Dictionary<SyntaxNode, DataFlowAnalysis> DataFlowByNode { get; }
        public Dictionary<SyntaxNode, int> ScopeOrdinals { get; }
    }

    internal sealed class SyntaxNodeComparer : IEqualityComparer<SyntaxNode>
    {
        public static readonly SyntaxNodeComparer Instance = new SyntaxNodeComparer();
        public bool Equals(SyntaxNode x, SyntaxNode y) => ReferenceEquals(x, y);
        public int GetHashCode(SyntaxNode obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
