using System.Linq.Expressions;

namespace Quarry;

/// <summary>
/// Unified entry point for all operations on an entity table.
/// Provides chain-starting methods for queries, deletes, updates, and inserts.
/// </summary>
/// <remarks>
/// This is a slim interface — it does NOT extend <see cref="IQueryBuilder{T}"/>.
/// Chain-continuation methods (OrderBy, Limit, GroupBy, Having, Offset, ThenBy)
/// only appear after the first clause transitions the chain to <see cref="IQueryBuilder{T}"/>.
/// </remarks>
/// <typeparam name="T">The entity type.</typeparam>
public interface IEntityAccessor<T> where T : class
{
    // ── Query chain starters ──

    /// <summary>
    /// Adds a WHERE clause to filter rows. Returns <see cref="IQueryBuilder{T}"/> for further query building.
    /// </summary>
    IQueryBuilder<T> Where(Expression<Func<T, bool>> predicate);

    /// <summary>
    /// Specifies the columns or projection to select.
    /// </summary>
    IQueryBuilder<T, TResult> Select<TResult>(Func<T, TResult> selector);

    /// <summary>
    /// Adds an INNER JOIN with another table using an explicit condition.
    /// </summary>
    IJoinedQueryBuilder<T, TJoined> Join<TJoined>(Expression<Func<T, TJoined, bool>> condition) where TJoined : class;

    /// <summary>
    /// Adds a LEFT JOIN with another table using an explicit condition.
    /// </summary>
    IJoinedQueryBuilder<T, TJoined> LeftJoin<TJoined>(Expression<Func<T, TJoined, bool>> condition) where TJoined : class;

    /// <summary>
    /// Adds a RIGHT JOIN with another table using an explicit condition.
    /// </summary>
    IJoinedQueryBuilder<T, TJoined> RightJoin<TJoined>(Expression<Func<T, TJoined, bool>> condition) where TJoined : class;

    /// <summary>
    /// Adds an INNER JOIN via a navigation property relationship.
    /// </summary>
    IJoinedQueryBuilder<T, TJoined> Join<TJoined>(Expression<Func<T, NavigationList<TJoined>>> navigation) where TJoined : class;

    /// <summary>
    /// Adds a LEFT JOIN via a navigation property relationship.
    /// </summary>
    IJoinedQueryBuilder<T, TJoined> LeftJoin<TJoined>(Expression<Func<T, NavigationList<TJoined>>> navigation) where TJoined : class;

    /// <summary>
    /// Removes duplicate rows from the result.
    /// </summary>
    IQueryBuilder<T> Distinct();

    /// <summary>
    /// Sets a custom timeout for this query.
    /// </summary>
    IQueryBuilder<T> WithTimeout(TimeSpan timeout);

    /// <summary>
    /// Returns the generated SQL without executing the query.
    /// </summary>
    string ToSql();

    /// <summary>
    /// Returns a <see cref="QueryDiagnostics"/> containing the generated SQL,
    /// bound parameters, and optimization metadata for this query chain.
    /// </summary>
    QueryDiagnostics ToDiagnostics();

    // ── Modification entry points ──

    /// <summary>
    /// Starts a DELETE operation on this entity's table.
    /// </summary>
    IDeleteBuilder<T> Delete();

    /// <summary>
    /// Starts an UPDATE operation on this entity's table.
    /// </summary>
    IUpdateBuilder<T> Update();

    /// <summary>
    /// Starts an INSERT operation with the specified entity.
    /// </summary>
    IInsertBuilder<T> Insert(T entity);

    /// <summary>
    /// Starts a batch INSERT operation with explicit column selection.
    /// </summary>
    IBatchInsertBuilder<T> InsertBatch<TColumns>(Func<T, TColumns> columnSelector);

    /// <summary>
    /// Returns a diagnostic query plan showing the optimization tier and SQL.
    /// </summary>
    QueryPlan ToQueryPlan();
}
