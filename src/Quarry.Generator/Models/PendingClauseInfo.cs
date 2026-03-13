namespace Quarry.Generators.Models;

/// <summary>
/// Holds deferred clause analysis data when semantic analysis fails.
/// This allows clause translation to be completed during the enrichment phase
/// when EntityInfo is available.
/// </summary>
internal sealed class PendingClauseInfo
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
    /// The syntactically parsed expression tree.
    /// </summary>
    public SyntacticExpression Expression { get; }

    /// <summary>
    /// For OrderBy clauses, whether the direction is descending.
    /// </summary>
    public bool IsDescending { get; }

    public PendingClauseInfo(
        ClauseKind kind,
        string lambdaParameterName,
        SyntacticExpression expression,
        bool isDescending = false)
    {
        Kind = kind;
        LambdaParameterName = lambdaParameterName;
        Expression = expression;
        IsDescending = isDescending;
    }
}
