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

    /// <summary>
    /// Returns the closure scope a captured variable belongs to, keyed so that two variables the
    /// compiler places on the same display class always produce the same key.
    /// <para>
    /// A scope is a <see cref="BlockSyntax"/>. Parameters are the subtle case: a lambda's (or local
    /// function's, or method's) parameters live on the SAME display class as the top-level locals of
    /// its body — verified against emitted IL, where <c>src.Select(p =&gt; { var bodyLocal = …; … })</c>
    /// yields a single <c>&lt;&gt;c__DisplayClass0_0 { p, bodyLocal }</c>. So a parameter resolves to
    /// its owner's body block, NOT to the block enclosing the owner. Walking a lambda parameter up to
    /// the enclosing block instead (the pre-#333 behaviour) merged the lambda's scope into its
    /// parent's and shifted every later closure ordinal down by one.
    /// </para>
    /// <para>
    /// Expression-bodied owners have no body block; they key on the owner node itself, which is
    /// still unique and still visited in the same pre-order position.
    /// </para>
    /// </summary>
    private static SyntaxNode FindDeclaringScope(ISymbol variable, SyntaxNode methodRoot)
    {
        var declRef = variable.DeclaringSyntaxReferences.FirstOrDefault();
        if (declRef == null)
            return methodRoot;

        // Start at the declaration node itself, not its parent: a foreach variable's declaring
        // syntax IS the ForEachStatement, so starting at .Parent would walk straight past the
        // scope that owns it and land in the enclosing block.
        var current = declRef.GetSyntax();
        while (current != null)
        {
            // A local's scope is the innermost block that contains its declaration.
            if (current is BlockSyntax)
                return current;

            // Statements that own a scope for the variable they declare, distinct from both the
            // enclosing block and their own body block.
            if (IsOwnScopeStatement(current))
                return current;

            // A parameter's scope is its OWNER's body, reached before any enclosing block.
            var ownerScope = TryGetOwnerBodyScope(current);
            if (ownerScope != null)
                return ownerScope;

            if (current == methodRoot)
                return methodRoot;
            current = current.Parent;
        }
        return methodRoot;
    }

    /// <summary>
    /// True for statements whose declared variable lives on its OWN display class, separate from the
    /// enclosing block and separate from the statement's own body block.
    /// <para>
    /// Verified against emitted IL — <c>foreach (var name in names) { var body = …; … }</c> yields
    /// <c>_0 { name }</c> and <c>_1 { body, CS$&lt;&gt;8__locals1 → _0 }</c>, i.e. TWO display classes, not
    /// one. <c>for</c> and <c>using</c> behave identically; a <c>switch</c> section holds all of its own
    /// locals in a single class. Resolving any of these to the enclosing block (the pre-fix behaviour)
    /// merged the loop scope into the method scope and shifted every later closure ordinal.
    /// </para>
    /// <para>
    /// Pre-order ordinal assignment visits the statement before its body block, which is the order the
    /// compiler numbers them in.
    /// </para>
    /// </summary>
    private static bool IsOwnScopeStatement(SyntaxNode node)
        => node is ForEachStatementSyntax
            or ForEachVariableStatementSyntax
            or ForStatementSyntax
            or UsingStatementSyntax
            or SwitchSectionSyntax;

    /// <summary>
    /// If <paramref name="node"/> declares a parameter scope (lambda, anonymous method, local
    /// function, or method), returns its body block — or the node itself when expression-bodied.
    /// Returns null for any other node so the caller keeps walking.
    /// </summary>
    private static SyntaxNode? TryGetOwnerBodyScope(SyntaxNode node)
    {
        switch (node)
        {
            case AnonymousFunctionExpressionSyntax anon:
                return anon.Block ?? (SyntaxNode)anon;
            case LocalFunctionStatementSyntax localFunc:
                return localFunc.Body ?? (SyntaxNode)localFunc;
            case BaseMethodDeclarationSyntax baseMethod:
                return baseMethod.Body ?? (SyntaxNode)baseMethod;
            case AccessorDeclarationSyntax accessor:
                return accessor.Body ?? (SyntaxNode)accessor;
            default:
                return null;
        }
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
                if (capturedVar is ILocalSymbol || (capturedVar is IParameterSymbol p && !p.IsThis))
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
    /// Counts the DISTINCT closure scopes a lambda captures locals/parameters from.
    /// <para>
    /// A count above 1 means the delegate's <c>Target</c> is the innermost of those display classes and
    /// the outer ones are only reachable through the compiler's <c>CS$&lt;&gt;8__locals</c> link fields —
    /// which cannot be read, because a field accessor must return byref and a byref return cannot name an
    /// inaccessible type (dotnet/runtime#119664, open). Those chains are disqualified rather than emitted
    /// wrongly; see the guard in <c>ChainAnalyzer.CheckDisqualifiers</c>.
    /// </para>
    /// <para>
    /// <c>this</c> is excluded (as everywhere else here), so a clause mixing an instance field with a
    /// local counts as ONE scope and is correctly not disqualified — that case is handled by reading
    /// <c>&lt;&gt;4__this</c> off the display class instead.
    /// </para>
    /// </summary>
    internal static int CountCaptureScopes(
        MethodClosureAnalysis analysis,
        LambdaExpressionSyntax lambda,
        SyntaxNode methodSyntax)
    {
        if (!analysis.DataFlowByNode.TryGetValue(lambda, out var dataFlow) || !dataFlow.Succeeded)
            return 0;

        var scopes = new HashSet<SyntaxNode>(SyntaxNodeComparer.Instance);
        foreach (var capturedVar in dataFlow.CapturedInside)
        {
            if (capturedVar is not ILocalSymbol && !(capturedVar is IParameterSymbol p && !p.IsThis))
                continue;

            // Only variables declared OUTSIDE this lambda are read out of a display class. A nested
            // subquery lambda inside the clause (u => u.Orders.Any(o => …)) contributes its own
            // parameters and locals to CapturedInside, but those live inside the clause and are
            // handled by the SQL translator, never extracted. Counting them made the guard fire on
            // working nested-subquery and set-operation chains.
            var declRef = capturedVar.DeclaringSyntaxReferences.FirstOrDefault();
            if (declRef != null && lambda.Span.Contains(declRef.Span))
                continue;

            var declScope = FindDeclaringScope(capturedVar, methodSyntax);
            if (declScope != null)
                scopes.Add(declScope);
        }
        return scopes.Count;
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
            if (capturedVar is ILocalSymbol || (capturedVar is IParameterSymbol p && !p.IsThis))
            {
                var declScope = FindDeclaringScope(capturedVar, methodSyntax);
                if (declScope != null && analysis.ScopeOrdinals.TryGetValue(declScope, out int ordinal))
                    return ordinal;
            }
        }

        return 0;
    }

    /// <summary>
    /// Collects names and fully-qualified types of captured variables using the semantic model.
    /// The calling compilation is expected to include generated entity and context source
    /// (supplemental compilation), so all types resolve natively without manual fallbacks.
    /// </summary>
    public static Dictionary<string, string>? CollectCapturedVariableTypes(
        DataFlowAnalysis dataFlow,
        SemanticModel semanticModel)
    {
        if (!dataFlow.Succeeded)
            return null;

        var captured = dataFlow.CapturedInside
            .Where(s => s is ILocalSymbol || (s is IParameterSymbol p && !p.IsThis))
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
                ? "object"
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
