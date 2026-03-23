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

        for (int i = 0; i < maxHops; i++)
        {
            if (root is not IdentifierNameSyntax ident)
                break;

            var declarator = TryResolveDeclarator(ident, semanticModel, ct);
            if (declarator == null)
                break;

            var initializer = GetInitializerExpression(declarator);
            if (initializer == null)
                break;

            // Record the first variable name we trace through
            if (firstVariableName == null)
                firstVariableName = declarator.Identifier.Text;

            root = WalkFluentChainRoot(initializer);
            hops++;
        }

        return new TraceResult(root, hops, hops > 0, firstVariableName);
    }

    /// <summary>
    /// Checks if a type name (display string) is a known Quarry builder type.
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

        public TraceResult(ExpressionSyntax root, int hops, bool traced, string? firstVariableName)
        {
            Root = root;
            Hops = hops;
            Traced = traced;
            FirstVariableName = firstVariableName;
        }
    }
}
