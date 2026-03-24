using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Quarry.Generators.Parsing;

/// <summary>
/// Reusable primitives for walking variable declarations and fluent chain receivers.
/// Used by ComputeChainId, ExtractBatchInsertColumnNamesFromChain, and AnalyzabilityChecker
/// to trace through variable assignments and find the original chain root.
/// </summary>
internal static class VariableTracer
{
    /// <summary>
    /// Given an identifier usage, resolves the symbol to an ILocalSymbol and returns
    /// its VariableDeclaratorSyntax. Returns null if the symbol is not a local,
    /// has multiple declarations, or the syntax reference doesn't resolve.
    /// </summary>
    internal static VariableDeclaratorSyntax? TryResolveDeclarator(
        IdentifierNameSyntax identifier,
        SemanticModel semanticModel,
        CancellationToken ct)
    {
        var symbol = semanticModel.GetSymbolInfo(identifier, ct).Symbol;
        if (symbol is not ILocalSymbol local)
            return null;

        if (local.DeclaringSyntaxReferences.Length != 1)
            return null;

        var declSyntax = local.DeclaringSyntaxReferences[0].GetSyntax(ct);
        return declSyntax as VariableDeclaratorSyntax;
    }

    /// <summary>
    /// Returns the initializer expression from a variable declarator, or null.
    /// </summary>
    internal static ExpressionSyntax? GetInitializerExpression(
        VariableDeclaratorSyntax declarator)
    {
        return declarator.Initializer?.Value;
    }

    /// <summary>
    /// Walks through nested InvocationExpression → MemberAccessExpression → .Expression
    /// until the receiver is no longer an invocation. Returns the deepest non-invocation
    /// receiver (the fluent chain root).
    /// </summary>
    internal static ExpressionSyntax WalkFluentChainRoot(ExpressionSyntax expression)
    {
        var current = expression;
        while (current is InvocationExpressionSyntax invoc)
        {
            if (invoc.Expression is MemberAccessExpressionSyntax ma)
                current = ma.Expression;
            else
                break;
        }
        return current;
    }

    /// <summary>
    /// Traces from a fluent chain root receiver through variable declarations
    /// to find the original chain origin. Walks up to <paramref name="maxHops"/>
    /// variable assignments.
    /// </summary>
    internal static TraceResult TraceToChainRoot(
        ExpressionSyntax receiver,
        SemanticModel semanticModel,
        CancellationToken ct,
        int maxHops = 2)
    {
        var root = receiver;
        string? firstVariableName = null;
        int hops = 0;
        List<ExpressionSyntax>? initializers = null;

        for (int i = 0; i < maxHops; i++)
        {
            if (root is not IdentifierNameSyntax ident)
                break;

            // Only trace through builder-type variables. Non-builder locals
            // (e.g., context variables like `db`) must not be traced — doing so
            // would collapse independent chains from the same context into one.
            var symbol = semanticModel.GetSymbolInfo(ident, ct).Symbol;
            if (symbol is not ILocalSymbol local || !IsBuilderType(local.Type.ToDisplayString()))
                break;

            var declarator = TryResolveDeclarator(ident, semanticModel, ct);
            if (declarator == null)
                break;

            var initializer = GetInitializerExpression(declarator);
            if (initializer == null)
                break;

            // Record the deepest variable name (closest to the chain origin).
            // For exec → batch → Lite, this yields "batch" — matching the
            // ChainId that GetAssignedVariableName produces for the root statement.
            firstVariableName = declarator.Identifier.Text;

            initializers ??= new List<ExpressionSyntax>();
            initializers.Add(initializer);

            root = WalkFluentChainRoot(initializer);
            hops++;
        }

        return new TraceResult(root, hops, hops > 0, firstVariableName, initializers);
    }

    /// <summary>
    /// Checks if a type display string is a known Quarry builder type.
    /// Use for ILocalSymbol.Type.ToDisplayString() checks where generic forms appear.
    /// </summary>
    internal static bool IsBuilderType(string typeName)
    {
        return typeName.Contains("IQueryBuilder") || typeName.Contains("QueryBuilder<")
            || typeName.Contains("IEntityAccessor") || typeName.Contains("EntityAccessor<")
            || typeName.Contains("IDeleteBuilder") || typeName.Contains("IExecutableDeleteBuilder")
            || typeName.Contains("DeleteBuilder<")
            || typeName.Contains("IUpdateBuilder") || typeName.Contains("IExecutableUpdateBuilder")
            || typeName.Contains("UpdateBuilder<")
            || typeName.Contains("IInsertBuilder")
            || typeName.Contains("IBatchInsertBuilder") || typeName.Contains("IExecutableBatchInsert")
            || typeName.Contains("InsertBuilder<");
    }

    /// <summary>
    /// Checks if a short type name (INamedTypeSymbol.Name) is a known Quarry builder type.
    /// Use for IMethodSymbol.ReturnType.Name or IPropertySymbol.Type.Name checks.
    /// </summary>
    internal static bool IsBuilderTypeName(string name)
    {
        return name is "IQueryBuilder" or "QueryBuilder"
            or "IJoinedQueryBuilder" or "IJoinedQueryBuilder3" or "IJoinedQueryBuilder4"
            or "IEntityAccessor" or "EntityAccessor"
            or "IDeleteBuilder" or "IExecutableDeleteBuilder" or "DeleteBuilder"
            or "IUpdateBuilder" or "IExecutableUpdateBuilder" or "UpdateBuilder"
            or "IInsertBuilder" or "InsertBuilder"
            or "IBatchInsertBuilder" or "IExecutableBatchInsert";
    }

    /// <summary>
    /// Result of tracing through variable declarations to find the chain root.
    /// </summary>
    internal readonly struct TraceResult
    {
        public ExpressionSyntax Root { get; }
        public int Hops { get; }
        public bool Traced { get; }
        /// <summary>
        /// Name of the first builder variable encountered during tracing.
        /// Used by ComputeChainId to link all sites in a variable-split chain.
        /// </summary>
        public string? FirstVariableName { get; }
        /// <summary>
        /// The initializer expressions encountered at each hop during tracing, in order.
        /// Allows callers to inspect intermediate initializers without re-resolving declarators.
        /// Null when no hops were taken.
        /// </summary>
        public IReadOnlyList<ExpressionSyntax>? Initializers { get; }

        public TraceResult(ExpressionSyntax root, int hops, bool traced, string? firstVariableName,
            IReadOnlyList<ExpressionSyntax>? initializers = null)
        {
            Root = root;
            Hops = hops;
            Traced = traced;
            FirstVariableName = firstVariableName;
            Initializers = initializers;
        }
    }
}
