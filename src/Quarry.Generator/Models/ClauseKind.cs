namespace Quarry.Generators.Models;

/// <summary>
/// Specifies the kind of clause.
/// </summary>
internal enum ClauseKind
{
    /// <summary>
    /// WHERE clause for filtering.
    /// </summary>
    Where,

    /// <summary>
    /// ORDER BY clause for sorting.
    /// </summary>
    OrderBy,

    /// <summary>
    /// GROUP BY clause for grouping.
    /// </summary>
    GroupBy,

    /// <summary>
    /// HAVING clause for filtering groups.
    /// </summary>
    Having,

    /// <summary>
    /// SET clause for Update operations.
    /// </summary>
    Set,

    /// <summary>
    /// JOIN clause for joining tables.
    /// </summary>
    Join
}

/// <summary>
/// The kind of join operation for clause translation.
/// </summary>
internal enum JoinClauseKind
{
    Inner,
    Left,
    Right,
    Cross,
    FullOuter
}
