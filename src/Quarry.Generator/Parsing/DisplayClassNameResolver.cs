using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Quarry.Generators.Parsing;

/// <summary>
/// Predicts the compiler-generated display class name for a lambda's closure.
/// The C# compiler names display classes as:
///   ContainingType+&lt;&gt;c__DisplayClass{methodOrdinal}_{closureOrdinal}
/// where methodOrdinal is the index of the method in GetMembers()
/// and closureOrdinal is assigned during pre-order scope tree traversal.
/// </summary>
internal static class DisplayClassNameResolver
{
    /// <summary>
    /// Resolves the full display class name for a lambda expression's closure.
    /// Returns null if the display class name cannot be determined.
    /// </summary>
    public static string? Resolve(
        IMethodSymbol containingMethod,
        LambdaExpressionSyntax lambda,
        SemanticModel semanticModel)
    {
        // For local functions, the display class is generated under the containing
        // non-local method's ordinal. Walk up past local functions.
        var effectiveMethod = containingMethod;
        while (effectiveMethod.MethodKind == MethodKind.LocalFunction
               && effectiveMethod.ContainingSymbol is IMethodSymbol parent)
        {
            effectiveMethod = parent;
        }

        var containingType = effectiveMethod.ContainingType;
        if (containingType == null)
            return null;

        int methodOrdinal = ComputeMethodOrdinal(containingType, effectiveMethod);
        if (methodOrdinal < 0)
            return null;

        // Use the effective (non-local) method's syntax so the scope walk covers
        // local function bodies for closure ordinal computation.
        var methodSyntax = effectiveMethod.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        if (methodSyntax == null)
            return null;

        int closureOrdinal = ComputeClosureOrdinal(methodSyntax, lambda, semanticModel);

        // Use a format that produces a CLR-resolvable type name:
        // Namespace.Outer+Inner (using + for nested types) without global:: prefix.
        var typeName = containingType.ToDisplayString(new SymbolDisplayFormat(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces));
        return $"{typeName}+<>c__DisplayClass{methodOrdinal}_{closureOrdinal}";
    }

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
    /// Computes the closure ordinal for a lambda within a method.
    /// Roslyn assigns display class ordinals in scope-tree pre-order: the method body
    /// scope (if it has captured variables) gets the first ordinal, then child scopes
    /// in source order. Only scopes that declare captured variables get an ordinal.
    /// </summary>
    internal static int ComputeClosureOrdinal(
        SyntaxNode methodSyntax,
        LambdaExpressionSyntax targetLambda,
        SemanticModel semanticModel)
    {
        // Step 1: Find ALL scopes that declare captured variables.
        var scopesWithCaptures = new HashSet<SyntaxNode>(SyntaxNodeComparer.Instance);

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

        // Step 2: Walk scopes in pre-order, assign ordinals to those with captures.
        var scopeOrdinals = new Dictionary<SyntaxNode, int>(SyntaxNodeComparer.Instance);
        int nextOrdinal = 0;
        AssignOrdinalsPreOrder(methodSyntax, scopesWithCaptures, scopeOrdinals, ref nextOrdinal);

        // Step 3: Find the target lambda's scope ordinal.
        var targetDataFlow = semanticModel.AnalyzeDataFlow(targetLambda);
        if (targetDataFlow != null && targetDataFlow.Succeeded)
        {
            foreach (var capturedVar in targetDataFlow.CapturedInside)
            {
                if (capturedVar is ILocalSymbol || capturedVar is IParameterSymbol)
                {
                    var declScope = FindDeclaringScope(capturedVar, methodSyntax);
                    if (declScope != null && scopeOrdinals.TryGetValue(declScope, out int ordinal))
                        return ordinal;
                }
            }
        }

        return 0;
    }

    /// <summary>
    /// Collects captured variable names and their CLR types from a lambda expression.
    /// Returns a dictionary mapping field name → fully-qualified CLR type.
    /// </summary>
    public static Dictionary<string, string>? CollectCapturedVariableTypes(
        LambdaExpressionSyntax lambda,
        SemanticModel semanticModel)
    {
        var dataFlow = semanticModel.AnalyzeDataFlow(lambda);
        if (dataFlow == null || !dataFlow.Succeeded)
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

            // Handle unresolved types (error types from generator ordering)
            var varType = typeSymbol.TypeKind == TypeKind.Error
                ? "object"
                : typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            // Guard against empty/null type strings
            if (string.IsNullOrWhiteSpace(varType))
                varType = "object";

            result[varName] = varType;
        }

        return result.Count > 0 ? result : null;
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

    private sealed class SyntaxNodeComparer : IEqualityComparer<SyntaxNode>
    {
        public static readonly SyntaxNodeComparer Instance = new SyntaxNodeComparer();
        public bool Equals(SyntaxNode x, SyntaxNode y) => ReferenceEquals(x, y);
        public int GetHashCode(SyntaxNode obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
