namespace Quarry;

/// <summary>
/// Unified entry point for all operations on an entity table.
/// Provides chain-starting methods for queries, deletes, updates, and inserts.
/// </summary>
/// <remarks>
/// This is a slim interface — it does NOT extend <see cref="IQueryBuilder{T}"/>.
/// Chain-continuation methods (OrderBy, ThenBy, Limit, Offset, Having, set operations)
/// are exposed here so the natural fluent syntax compiles in chains where the static
/// type is <see cref="IEntityAccessor{T}"/> (e.g., directly off <c>FromCte&lt;T&gt;()</c>).
/// Calling any of these transitions the chain to <see cref="IQueryBuilder{T}"/>.
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
    IQueryBuilder<T> Where(Func<T, bool> predicate)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Where is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Specifies the columns or projection to select.
    /// </summary>
    IQueryBuilder<T, TResult> Select<TResult>(Func<T, TResult> selector)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Select is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Adds an INNER JOIN with another table using an explicit condition.
    /// </summary>
    IJoinedQueryBuilder<T, TJoined> Join<TJoined>(Func<T, TJoined, bool> condition) where TJoined : class
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Join is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Adds a LEFT JOIN with another table using an explicit condition.
    /// </summary>
    IJoinedQueryBuilder<T, TJoined> LeftJoin<TJoined>(Func<T, TJoined, bool> condition) where TJoined : class
        => throw new InvalidOperationException("Carrier method IEntityAccessor.LeftJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Adds a RIGHT JOIN with another table using an explicit condition.
    /// </summary>
    IJoinedQueryBuilder<T, TJoined> RightJoin<TJoined>(Func<T, TJoined, bool> condition) where TJoined : class
        => throw new InvalidOperationException("Carrier method IEntityAccessor.RightJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Adds a CROSS JOIN with another table (cartesian product, no condition).
    /// </summary>
    IJoinedQueryBuilder<T, TJoined> CrossJoin<TJoined>() where TJoined : class
        => throw new InvalidOperationException("Carrier method IEntityAccessor.CrossJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Adds a FULL OUTER JOIN with another table using an explicit condition.
    /// </summary>
    IJoinedQueryBuilder<T, TJoined> FullOuterJoin<TJoined>(Func<T, TJoined, bool> condition) where TJoined : class
        => throw new InvalidOperationException("Carrier method IEntityAccessor.FullOuterJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Adds an INNER JOIN via a navigation property relationship.
    /// </summary>
    IJoinedQueryBuilder<T, TJoined> Join<TJoined>(Func<T, NavigationList<TJoined>> navigation) where TJoined : class
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Join is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Adds a LEFT JOIN via a navigation property relationship.
    /// </summary>
    IJoinedQueryBuilder<T, TJoined> LeftJoin<TJoined>(Func<T, NavigationList<TJoined>> navigation) where TJoined : class
        => throw new InvalidOperationException("Carrier method IEntityAccessor.LeftJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Groups the results by the specified key expression.
    /// Returns <see cref="IQueryBuilder{T}"/> for further query building (e.g., Select with aggregates).
    /// </summary>
    IQueryBuilder<T> GroupBy<TKey>(Func<T, TKey> keySelector)
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

    // ── Chain-continuation transitions to IQueryBuilder<T> ──

    /// <summary>
    /// Specifies the primary sort order. Returns <see cref="IQueryBuilder{T}"/> for further query building.
    /// </summary>
    IQueryBuilder<T> OrderBy<TKey>(Func<T, TKey> keySelector, Direction direction = Direction.Ascending)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.OrderBy is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Specifies an additional sort order. Returns <see cref="IQueryBuilder{T}"/> for further query building.
    /// </summary>
    IQueryBuilder<T> ThenBy<TKey>(Func<T, TKey> keySelector, Direction direction = Direction.Ascending)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.ThenBy is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Limits the result to the specified number of rows. Returns <see cref="IQueryBuilder{T}"/> for further query building.
    /// </summary>
    IQueryBuilder<T> Limit(int count)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Limit is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Skips the specified number of rows. Returns <see cref="IQueryBuilder{T}"/> for further query building.
    /// </summary>
    IQueryBuilder<T> Offset(int count)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Offset is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Filters groups using the specified predicate. Returns <see cref="IQueryBuilder{T}"/> for further query building.
    /// </summary>
    IQueryBuilder<T> Having(Func<T, bool> predicate)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Having is not intercepted in this optimized chain. This indicates a code generation bug.");

    // ── Set operations ──

    /// <summary>
    /// Combines this query with another, removing duplicate rows.
    /// </summary>
    IQueryBuilder<T> Union(IQueryBuilder<T> other)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Union is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Combines this query with another, keeping all rows including duplicates.
    /// </summary>
    IQueryBuilder<T> UnionAll(IQueryBuilder<T> other)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.UnionAll is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Returns only rows present in both this query and another.
    /// </summary>
    IQueryBuilder<T> Intersect(IQueryBuilder<T> other)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Intersect is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Returns only rows present in both this query and another, keeping duplicates.
    /// </summary>
    IQueryBuilder<T> IntersectAll(IQueryBuilder<T> other)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.IntersectAll is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Returns rows from this query that are not present in another.
    /// </summary>
    IQueryBuilder<T> Except(IQueryBuilder<T> other)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Except is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Returns rows from this query that are not present in another, keeping duplicates.
    /// </summary>
    IQueryBuilder<T> ExceptAll(IQueryBuilder<T> other)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.ExceptAll is not intercepted in this optimized chain. This indicates a code generation bug.");

    // ── Set operations (lambda form) ──

    /// <summary>
    /// Combines this query with another built via lambda, removing duplicate rows.
    /// </summary>
    IQueryBuilder<T> Union(Func<IEntityAccessor<T>, IQueryBuilder<T>> other)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Union is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Combines this query with another built via lambda, keeping all rows including duplicates.
    /// </summary>
    IQueryBuilder<T> UnionAll(Func<IEntityAccessor<T>, IQueryBuilder<T>> other)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.UnionAll is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Returns only rows present in both this query and another built via lambda.
    /// </summary>
    IQueryBuilder<T> Intersect(Func<IEntityAccessor<T>, IQueryBuilder<T>> other)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Intersect is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Returns only rows present in both this query and another built via lambda, keeping duplicates.
    /// </summary>
    IQueryBuilder<T> IntersectAll(Func<IEntityAccessor<T>, IQueryBuilder<T>> other)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.IntersectAll is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Returns rows from this query that are not present in another built via lambda.
    /// </summary>
    IQueryBuilder<T> Except(Func<IEntityAccessor<T>, IQueryBuilder<T>> other)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Except is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Returns rows from this query that are not present in another built via lambda, keeping duplicates.
    /// </summary>
    IQueryBuilder<T> ExceptAll(Func<IEntityAccessor<T>, IQueryBuilder<T>> other)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.ExceptAll is not intercepted in this optimized chain. This indicates a code generation bug.");

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
