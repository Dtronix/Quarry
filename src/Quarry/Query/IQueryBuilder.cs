using System.Data.Common;

namespace Quarry;

/// <summary>
/// Interface for constructing SELECT queries.
/// Exposes only the user-facing fluent API, hiding internal builder methods.
/// Default implementations throw so that generated carrier classes satisfy the
/// interface contract without providing stubs for every method.
/// </summary>
/// <typeparam name="T">The entity type being queried.</typeparam>
public interface IQueryBuilder<T> where T : class
{
    /// <summary>
    /// Adds a WHERE clause to filter rows.
    /// </summary>
    IQueryBuilder<T> Where(Func<T, bool> predicate)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Where is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Specifies the columns or projection to select.
    /// </summary>
    IQueryBuilder<T, TResult> Select<TResult>(Func<T, TResult> selector)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Select is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Specifies the primary sort order.
    /// </summary>
    IQueryBuilder<T> OrderBy<TKey>(Func<T, TKey> keySelector, Direction direction = Direction.Ascending)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.OrderBy is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Specifies an additional sort order.
    /// </summary>
    IQueryBuilder<T> ThenBy<TKey>(Func<T, TKey> keySelector, Direction direction = Direction.Ascending)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.ThenBy is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Skips the specified number of rows.
    /// </summary>
    IQueryBuilder<T> Offset(int count)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Offset is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Limits the result to the specified number of rows.
    /// </summary>
    IQueryBuilder<T> Limit(int count)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Limit is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Removes duplicate rows from the result.
    /// </summary>
    IQueryBuilder<T> Distinct()
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Distinct is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Sets a custom timeout for this query.
    /// </summary>
    IQueryBuilder<T> WithTimeout(TimeSpan timeout)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.WithTimeout is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Groups rows by the specified key selector.
    /// </summary>
    IQueryBuilder<T> GroupBy<TKey>(Func<T, TKey> keySelector)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.GroupBy is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Filters groups using the specified predicate.
    /// </summary>
    IQueryBuilder<T> Having(Func<T, bool> predicate)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Having is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Adds an INNER JOIN with another table using an explicit condition.
    /// </summary>
    IJoinedQueryBuilder<T, TJoined> Join<TJoined>(Func<T, TJoined, bool> condition) where TJoined : class
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Join is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Adds a LEFT JOIN with another table using an explicit condition.
    /// </summary>
    IJoinedQueryBuilder<T, TJoined> LeftJoin<TJoined>(Func<T, TJoined, bool> condition) where TJoined : class
        => throw new InvalidOperationException("Carrier method IQueryBuilder.LeftJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Adds a RIGHT JOIN with another table using an explicit condition.
    /// </summary>
    IJoinedQueryBuilder<T, TJoined> RightJoin<TJoined>(Func<T, TJoined, bool> condition) where TJoined : class
        => throw new InvalidOperationException("Carrier method IQueryBuilder.RightJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Adds a CROSS JOIN with another table (cartesian product, no condition).
    /// </summary>
    IJoinedQueryBuilder<T, TJoined> CrossJoin<TJoined>() where TJoined : class
        => throw new InvalidOperationException("Carrier method IQueryBuilder.CrossJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Adds a FULL OUTER JOIN with another table using an explicit condition.
    /// </summary>
    IJoinedQueryBuilder<T, TJoined> FullOuterJoin<TJoined>(Func<T, TJoined, bool> condition) where TJoined : class
        => throw new InvalidOperationException("Carrier method IQueryBuilder.FullOuterJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Adds an INNER JOIN via a navigation property relationship.
    /// </summary>
    IJoinedQueryBuilder<T, TJoined> Join<TJoined>(Func<T, NavigationList<TJoined>> navigation) where TJoined : class
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Join is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Adds a LEFT JOIN via a navigation property relationship.
    /// </summary>
    IJoinedQueryBuilder<T, TJoined> LeftJoin<TJoined>(Func<T, NavigationList<TJoined>> navigation) where TJoined : class
        => throw new InvalidOperationException("Carrier method IQueryBuilder.LeftJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    // ── Set operations ──

    /// <summary>
    /// Combines this query with another, removing duplicate rows.
    /// </summary>
    IQueryBuilder<T> Union(IQueryBuilder<T> other)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Union is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Combines this query with another, keeping all rows including duplicates.
    /// </summary>
    IQueryBuilder<T> UnionAll(IQueryBuilder<T> other)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.UnionAll is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Returns only rows present in both this query and another.
    /// </summary>
    IQueryBuilder<T> Intersect(IQueryBuilder<T> other)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Intersect is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Returns only rows present in both this query and another, keeping duplicates.
    /// </summary>
    IQueryBuilder<T> IntersectAll(IQueryBuilder<T> other)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.IntersectAll is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Returns rows from this query that are not present in another.
    /// </summary>
    IQueryBuilder<T> Except(IQueryBuilder<T> other)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Except is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Returns rows from this query that are not present in another, keeping duplicates.
    /// </summary>
    IQueryBuilder<T> ExceptAll(IQueryBuilder<T> other)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.ExceptAll is not intercepted in this optimized chain. This indicates a code generation bug.");

    // ── Execution terminals (no Select — result type is the entity type T) ──

    /// <summary>
    /// Executes the query and returns all results as a list.
    /// </summary>
    Task<List<T>> ExecuteFetchAllAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.ExecuteFetchAllAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Executes the query and returns the first result.
    /// </summary>
    Task<T> ExecuteFetchFirstAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.ExecuteFetchFirstAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Executes the query and returns the first result, or default if no results.
    /// </summary>
    Task<T?> ExecuteFetchFirstOrDefaultAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.ExecuteFetchFirstOrDefaultAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Executes the query and returns exactly one result.
    /// </summary>
    Task<T> ExecuteFetchSingleAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.ExecuteFetchSingleAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Executes the query and returns exactly one result, or default if no results.
    /// Throws if more than one result.
    /// </summary>
    Task<T?> ExecuteFetchSingleOrDefaultAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.ExecuteFetchSingleOrDefaultAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Executes the query and returns the scalar result.
    /// </summary>
    Task<TScalar> ExecuteScalarAsync<TScalar>(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.ExecuteScalarAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Executes the query and returns results as an async enumerable for streaming.
    /// </summary>
    IAsyncEnumerable<T> ToAsyncEnumerable(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.ToAsyncEnumerable is not intercepted in this optimized chain. This indicates a code generation bug.");

    // ── Diagnostics and preparation ──

    /// <summary>
    /// Returns a <see cref="QueryDiagnostics"/> containing the generated SQL,
    /// bound parameters, and optimization metadata for this query chain.
    /// </summary>
    QueryDiagnostics ToDiagnostics()
        => throw new InvalidOperationException("Carrier method IQueryBuilder.ToDiagnostics is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Prepares this query chain for multiple terminal operations.
    /// </summary>
    PreparedQuery<T> Prepare()
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Prepare is not intercepted in this optimized chain. This indicates a code generation bug.");
}

/// <summary>
/// Interface for constructing SELECT queries with a specified projection.
/// Exposes only the user-facing fluent API, hiding internal builder methods.
/// Default implementations throw so that generated carrier classes satisfy the
/// interface contract without providing stubs for every method.
/// </summary>
/// <typeparam name="TEntity">The entity type being queried.</typeparam>
/// <typeparam name="TResult">The result type after projection.</typeparam>
public interface IQueryBuilder<TEntity, TResult> where TEntity : class
{
    /// <summary>
    /// Adds a WHERE clause to filter rows.
    /// </summary>
    IQueryBuilder<TEntity, TResult> Where(Func<TEntity, bool> predicate)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Where is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Specifies the primary sort order.
    /// </summary>
    IQueryBuilder<TEntity, TResult> OrderBy<TKey>(Func<TEntity, TKey> keySelector, Direction direction = Direction.Ascending)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.OrderBy is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Specifies an additional sort order.
    /// </summary>
    IQueryBuilder<TEntity, TResult> ThenBy<TKey>(Func<TEntity, TKey> keySelector, Direction direction = Direction.Ascending)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.ThenBy is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Skips the specified number of rows.
    /// </summary>
    IQueryBuilder<TEntity, TResult> Offset(int count)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Offset is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Limits the result to the specified number of rows.
    /// </summary>
    IQueryBuilder<TEntity, TResult> Limit(int count)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Limit is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Removes duplicate rows from the result.
    /// </summary>
    IQueryBuilder<TEntity, TResult> Distinct()
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Distinct is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Sets a custom timeout for this query.
    /// </summary>
    IQueryBuilder<TEntity, TResult> WithTimeout(TimeSpan timeout)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.WithTimeout is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Groups rows by the specified key selector.
    /// </summary>
    IQueryBuilder<TEntity, TResult> GroupBy<TKey>(Func<TEntity, TKey> keySelector)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.GroupBy is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Filters groups using the specified predicate.
    /// </summary>
    IQueryBuilder<TEntity, TResult> Having(Func<TEntity, bool> predicate)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Having is not intercepted in this optimized chain. This indicates a code generation bug.");

    // ── Set operations ──

    /// <summary>
    /// Combines this query with another, removing duplicate rows.
    /// </summary>
    IQueryBuilder<TEntity, TResult> Union(IQueryBuilder<TEntity, TResult> other)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Union is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Combines this query with another from a different entity type, removing duplicate rows.
    /// Both queries must project to the same result type.
    /// </summary>
    IQueryBuilder<TEntity, TResult> Union<TOther>(IQueryBuilder<TOther, TResult> other) where TOther : class
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Union is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Combines this query with another, keeping all rows including duplicates.
    /// </summary>
    IQueryBuilder<TEntity, TResult> UnionAll(IQueryBuilder<TEntity, TResult> other)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.UnionAll is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Combines this query with another from a different entity type, keeping all rows including duplicates.
    /// Both queries must project to the same result type.
    /// </summary>
    IQueryBuilder<TEntity, TResult> UnionAll<TOther>(IQueryBuilder<TOther, TResult> other) where TOther : class
        => throw new InvalidOperationException("Carrier method IQueryBuilder.UnionAll is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Returns only rows present in both this query and another.
    /// </summary>
    IQueryBuilder<TEntity, TResult> Intersect(IQueryBuilder<TEntity, TResult> other)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Intersect is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Returns only rows present in both this query and another from a different entity type.
    /// Both queries must project to the same result type.
    /// </summary>
    IQueryBuilder<TEntity, TResult> Intersect<TOther>(IQueryBuilder<TOther, TResult> other) where TOther : class
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Intersect is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Returns only rows present in both this query and another, keeping duplicates.
    /// </summary>
    IQueryBuilder<TEntity, TResult> IntersectAll(IQueryBuilder<TEntity, TResult> other)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.IntersectAll is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Returns only rows present in both this query and another from a different entity type, keeping duplicates.
    /// Both queries must project to the same result type.
    /// </summary>
    IQueryBuilder<TEntity, TResult> IntersectAll<TOther>(IQueryBuilder<TOther, TResult> other) where TOther : class
        => throw new InvalidOperationException("Carrier method IQueryBuilder.IntersectAll is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Returns rows from this query that are not present in another.
    /// </summary>
    IQueryBuilder<TEntity, TResult> Except(IQueryBuilder<TEntity, TResult> other)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Except is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Returns rows from this query that are not present in another from a different entity type.
    /// Both queries must project to the same result type.
    /// </summary>
    IQueryBuilder<TEntity, TResult> Except<TOther>(IQueryBuilder<TOther, TResult> other) where TOther : class
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Except is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Returns rows from this query that are not present in another, keeping duplicates.
    /// </summary>
    IQueryBuilder<TEntity, TResult> ExceptAll(IQueryBuilder<TEntity, TResult> other)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.ExceptAll is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Returns rows from this query that are not present in another from a different entity type, keeping duplicates.
    /// Both queries must project to the same result type.
    /// </summary>
    IQueryBuilder<TEntity, TResult> ExceptAll<TOther>(IQueryBuilder<TOther, TResult> other) where TOther : class
        => throw new InvalidOperationException("Carrier method IQueryBuilder.ExceptAll is not intercepted in this optimized chain. This indicates a code generation bug.");

    // ── Execution terminals ──

    /// <summary>
    /// Executes the query and returns all results as a list.
    /// </summary>
    Task<List<TResult>> ExecuteFetchAllAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.ExecuteFetchAllAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Executes the query and returns the first result.
    /// </summary>
    Task<TResult> ExecuteFetchFirstAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.ExecuteFetchFirstAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Executes the query and returns the first result, or default if no results.
    /// </summary>
    Task<TResult?> ExecuteFetchFirstOrDefaultAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.ExecuteFetchFirstOrDefaultAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Executes the query and returns exactly one result.
    /// </summary>
    Task<TResult> ExecuteFetchSingleAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.ExecuteFetchSingleAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Executes the query and returns exactly one result, or default if no results.
    /// Throws if more than one result.
    /// </summary>
    Task<TResult?> ExecuteFetchSingleOrDefaultAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.ExecuteFetchSingleOrDefaultAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Executes the query and returns the scalar result.
    /// </summary>
    Task<TScalar> ExecuteScalarAsync<TScalar>(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.ExecuteScalarAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Executes the query and returns results as an async enumerable for streaming.
    /// </summary>
    IAsyncEnumerable<TResult> ToAsyncEnumerable(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.ToAsyncEnumerable is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Returns a <see cref="QueryDiagnostics"/> containing the generated SQL,
    /// bound parameters, and optimization metadata for this query chain.
    /// </summary>
    QueryDiagnostics ToDiagnostics()
        => throw new InvalidOperationException("Carrier method IQueryBuilder.ToDiagnostics is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Prepares this query chain for multiple terminal operations.
    /// </summary>
    PreparedQuery<TResult> Prepare()
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Prepare is not intercepted in this optimized chain. This indicates a code generation bug.");
}
