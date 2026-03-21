using System;
using Quarry.Generators.IR;

namespace Quarry.Generators.Models;

/// <summary>
/// Holds deferred clause analysis data when semantic analysis fails.
/// This allows clause translation to be completed during the enrichment phase
/// when EntityInfo is available.
/// </summary>
internal sealed class PendingClauseInfo : IEquatable<PendingClauseInfo>
{
    /// <summary>
    /// The kind of clause (Where, OrderBy, etc.).
    /// </summary>
    public ClauseKind Kind { get; }

    /// <summary>
    /// The lambda parameter name (e.g., "u" in u => u.IsActive).
    /// </summary>
    public string LambdaParameterName { get; }

    /// <summary>
    /// The syntactically parsed expression tree (legacy path).
    /// </summary>
    public SyntacticExpression Expression { get; }

    /// <summary>
    /// The parsed SqlExpr tree (new IR path). When set, SqlExprClauseTranslator
    /// uses this directly instead of converting via SyntacticExpressionAdapter.
    /// </summary>
    public SqlExpr? ParsedSqlExpr { get; }

    /// <summary>
    /// For OrderBy clauses, whether the direction is descending.
    /// </summary>
    public bool IsDescending { get; }

    public PendingClauseInfo(
        ClauseKind kind,
        string lambdaParameterName,
        SyntacticExpression expression,
        bool isDescending = false,
        SqlExpr? parsedSqlExpr = null)
    {
        Kind = kind;
        LambdaParameterName = lambdaParameterName;
        Expression = expression;
        IsDescending = isDescending;
        ParsedSqlExpr = parsedSqlExpr;
    }

    public bool Equals(PendingClauseInfo? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (Kind != other.Kind
            || LambdaParameterName != other.LambdaParameterName
            || IsDescending != other.IsDescending)
            return false;

        // Prefer SqlExpr equality when both sides have it
        if (ParsedSqlExpr != null && other.ParsedSqlExpr != null)
            return ParsedSqlExpr.Equals(other.ParsedSqlExpr);

        return SyntacticExpression.DeepEquals(Expression, other.Expression);
    }

    public override bool Equals(object? obj) => Equals(obj as PendingClauseInfo);

    public override int GetHashCode()
    {
        return HashCode.Combine(Kind, LambdaParameterName, IsDescending);
    }
}
