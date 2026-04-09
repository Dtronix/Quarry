namespace Quarry;

/// <summary>
/// Defines the OVER clause for SQL window functions.
/// </summary>
/// <remarks>
/// <para>
/// Used as a lambda parameter in <see cref="Sql"/> window function methods to specify
/// PARTITION BY and ORDER BY clauses. These methods are translated at compile-time
/// by the source generator — they cannot be invoked at runtime.
/// </para>
/// <para>
/// Example usage:
/// <code>
/// .Select(o => (
///     o.Name,
///     RowNum: Sql.RowNumber(over => over.OrderBy(o.Date)),
///     Rank: Sql.Rank(over => over.PartitionBy(o.Category).OrderBy(o.Price))
/// ))
/// </code>
/// </para>
/// </remarks>
public interface IOverClause
{
    /// <summary>
    /// Specifies the PARTITION BY columns for the window function.
    /// </summary>
    /// <typeparam name="T">The column type.</typeparam>
    /// <param name="columns">One or more columns to partition by.</param>
    /// <returns>The over clause for further chaining.</returns>
    IOverClause PartitionBy<T>(params T[] columns);

    /// <summary>
    /// Specifies an ORDER BY column (ascending) for the window function.
    /// </summary>
    /// <typeparam name="T">The column type.</typeparam>
    /// <param name="column">The column to order by.</param>
    /// <returns>The over clause for further chaining.</returns>
    IOverClause OrderBy<T>(T column);

    /// <summary>
    /// Specifies an ORDER BY column (descending) for the window function.
    /// </summary>
    /// <typeparam name="T">The column type.</typeparam>
    /// <param name="column">The column to order by descending.</param>
    /// <returns>The over clause for further chaining.</returns>
    IOverClause OrderByDescending<T>(T column);
}
