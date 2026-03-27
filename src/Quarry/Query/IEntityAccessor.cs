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
/// Default implementations throw <see cref="InvalidOperationException"/> so that
/// generated carrier classes can implement this interface without providing stubs
/// for every method — only intercepted methods need real implementations.
/// </remarks>
/// <typeparam name="T">The entity type.</typeparam>
public interface IEntityAccessor<T> where T : class
{
    // ── Query chain starters ──

    /// <summary>
    /// Adds a WHERE clause to filter rows. Returns <see cref="IQueryBuilder{T}"/> for further query building.
    /// </summary>
    IQueryBuilder<T> Where(Expression<Func<T, bool>> predicate)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Where is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Specifies the columns or projection to select.
    /// </summary>
    IQueryBuilder<T, TResult> Select<TResult>(Func<T, TResult> selector)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Select is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Adds an INNER JOIN with another table using an explicit condition.
    /// </summary>
    IJoinedQueryBuilder<T, TJoined> Join<TJoined>(Expression<Func<T, TJoined, bool>> condition) where TJoined : class
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Join is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Adds a LEFT JOIN with another table using an explicit condition.
    /// </summary>
    IJoinedQueryBuilder<T, TJoined> LeftJoin<TJoined>(Expression<Func<T, TJoined, bool>> condition) where TJoined : class
        => throw new InvalidOperationException("Carrier method IEntityAccessor.LeftJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Adds a RIGHT JOIN with another table using an explicit condition.
    /// </summary>
    IJoinedQueryBuilder<T, TJoined> RightJoin<TJoined>(Expression<Func<T, TJoined, bool>> condition) where TJoined : class
        => throw new InvalidOperationException("Carrier method IEntityAccessor.RightJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Adds an INNER JOIN via a navigation property relationship.
    /// </summary>
    IJoinedQueryBuilder<T, TJoined> Join<TJoined>(Expression<Func<T, NavigationList<TJoined>>> navigation) where TJoined : class
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Join is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Adds a LEFT JOIN via a navigation property relationship.
    /// </summary>
    IJoinedQueryBuilder<T, TJoined> LeftJoin<TJoined>(Expression<Func<T, NavigationList<TJoined>>> navigation) where TJoined : class
        => throw new InvalidOperationException("Carrier method IEntityAccessor.LeftJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Groups the results by the specified key expression.
    /// Returns <see cref="IQueryBuilder{T}"/> for further query building (e.g., Select with aggregates).
    /// </summary>
    IQueryBuilder<T> GroupBy<TKey>(Expression<Func<T, TKey>> keySelector)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.GroupBy is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Removes duplicate rows from the result.
    /// </summary>
    IQueryBuilder<T> Distinct()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Distinct is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Sets a custom timeout for this query.
    /// </summary>
    IQueryBuilder<T> WithTimeout(TimeSpan timeout)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.WithTimeout is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Returns a <see cref="QueryDiagnostics"/> containing the generated SQL,
    /// bound parameters, and optimization metadata for this query chain.
    /// </summary>
    QueryDiagnostics ToDiagnostics()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.ToDiagnostics is not intercepted in this optimized chain. This indicates a code generation bug.");

    // ── Modification entry points ──

    /// <summary>
    /// Starts a DELETE operation on this entity's table.
    /// </summary>
    IDeleteBuilder<T> Delete()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Delete is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Starts an UPDATE operation on this entity's table.
    /// </summary>
    IUpdateBuilder<T> Update()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Update is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Starts an INSERT operation with the specified entity.
    /// </summary>
    IInsertBuilder<T> Insert(T entity)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Insert is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Starts a batch INSERT operation with explicit column selection.
    /// </summary>
    IBatchInsertBuilder<T> InsertBatch<TColumns>(Func<T, TColumns> columnSelector)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.InsertBatch is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Returns a diagnostic query plan showing the optimization tier and SQL.
    /// </summary>
    QueryPlan ToQueryPlan()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.ToQueryPlan is not intercepted in this optimized chain. This indicates a code generation bug.");
}
