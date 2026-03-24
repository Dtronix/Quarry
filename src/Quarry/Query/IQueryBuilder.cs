using System.Data.Common;
using System.Linq.Expressions;

namespace Quarry;

/// <summary>
/// Interface for constructing SELECT queries.
/// Exposes only the user-facing fluent API, hiding internal builder methods.
/// </summary>
/// <typeparam name="T">The entity type being queried.</typeparam>
public interface IQueryBuilder<T> where T : class
{
    /// <summary>
    /// Adds a WHERE clause to filter rows.
    /// </summary>
    IQueryBuilder<T> Where(Expression<Func<T, bool>> predicate);

    /// <summary>
    /// Specifies the columns or projection to select.
    /// </summary>
    IQueryBuilder<T, TResult> Select<TResult>(Func<T, TResult> selector);

    /// <summary>
    /// Specifies the primary sort order.
    /// </summary>
    IQueryBuilder<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector, Direction direction = Direction.Ascending);

    /// <summary>
    /// Specifies an additional sort order.
    /// </summary>
    IQueryBuilder<T> ThenBy<TKey>(Expression<Func<T, TKey>> keySelector, Direction direction = Direction.Ascending);

    /// <summary>
    /// Skips the specified number of rows.
    /// </summary>
    IQueryBuilder<T> Offset(int count);

    /// <summary>
    /// Limits the result to the specified number of rows.
    /// </summary>
    IQueryBuilder<T> Limit(int count);

    /// <summary>
    /// Removes duplicate rows from the result.
    /// </summary>
    IQueryBuilder<T> Distinct();

    /// <summary>
    /// Sets a custom timeout for this query.
    /// </summary>
    IQueryBuilder<T> WithTimeout(TimeSpan timeout);

    /// <summary>
    /// Groups rows by the specified key selector.
    /// </summary>
    IQueryBuilder<T> GroupBy<TKey>(Expression<Func<T, TKey>> keySelector);

    /// <summary>
    /// Filters groups using the specified predicate.
    /// </summary>
    IQueryBuilder<T> Having(Expression<Func<T, bool>> predicate);

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
    /// Returns a <see cref="QueryDiagnostics"/> containing the generated SQL,
    /// bound parameters, and optimization metadata for this query chain.
    /// </summary>
    QueryDiagnostics ToDiagnostics();
}

/// <summary>
/// Interface for constructing SELECT queries with a specified projection.
/// Exposes only the user-facing fluent API, hiding internal builder methods.
/// </summary>
/// <typeparam name="TEntity">The entity type being queried.</typeparam>
/// <typeparam name="TResult">The result type after projection.</typeparam>
public interface IQueryBuilder<TEntity, TResult> where TEntity : class
{
    /// <summary>
    /// Adds a WHERE clause to filter rows.
    /// </summary>
    IQueryBuilder<TEntity, TResult> Where(Expression<Func<TEntity, bool>> predicate);

    /// <summary>
    /// Specifies the primary sort order.
    /// </summary>
    IQueryBuilder<TEntity, TResult> OrderBy<TKey>(Expression<Func<TEntity, TKey>> keySelector, Direction direction = Direction.Ascending);

    /// <summary>
    /// Specifies an additional sort order.
    /// </summary>
    IQueryBuilder<TEntity, TResult> ThenBy<TKey>(Expression<Func<TEntity, TKey>> keySelector, Direction direction = Direction.Ascending);

    /// <summary>
    /// Skips the specified number of rows.
    /// </summary>
    IQueryBuilder<TEntity, TResult> Offset(int count);

    /// <summary>
    /// Limits the result to the specified number of rows.
    /// </summary>
    IQueryBuilder<TEntity, TResult> Limit(int count);

    /// <summary>
    /// Removes duplicate rows from the result.
    /// </summary>
    IQueryBuilder<TEntity, TResult> Distinct();

    /// <summary>
    /// Sets a custom timeout for this query.
    /// </summary>
    IQueryBuilder<TEntity, TResult> WithTimeout(TimeSpan timeout);

    /// <summary>
    /// Groups rows by the specified key selector.
    /// </summary>
    IQueryBuilder<TEntity, TResult> GroupBy<TKey>(Expression<Func<TEntity, TKey>> keySelector);

    /// <summary>
    /// Filters groups using the specified predicate.
    /// </summary>
    IQueryBuilder<TEntity, TResult> Having(Expression<Func<TEntity, bool>> predicate);

    /// <summary>
    /// Executes the query and returns all results as a list.
    /// </summary>
    Task<List<TResult>> ExecuteFetchAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the query and returns the first result.
    /// </summary>
    Task<TResult> ExecuteFetchFirstAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the query and returns the first result, or default if no results.
    /// </summary>
    Task<TResult?> ExecuteFetchFirstOrDefaultAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the query and returns exactly one result.
    /// </summary>
    Task<TResult> ExecuteFetchSingleAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the query and returns the scalar result.
    /// </summary>
    Task<TScalar> ExecuteScalarAsync<TScalar>(CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the query and returns results as an async enumerable for streaming.
    /// </summary>
    IAsyncEnumerable<TResult> ToAsyncEnumerable(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a <see cref="QueryDiagnostics"/> containing the generated SQL,
    /// bound parameters, and optimization metadata for this query chain.
    /// </summary>
    QueryDiagnostics ToDiagnostics();
}
