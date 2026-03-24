using System.Data.Common;
using System.Linq.Expressions;

namespace Quarry;

/// <summary>
/// Interface for constructing SELECT queries with a two-table join (no projection).
/// </summary>
public interface IJoinedQueryBuilder<T1, T2>
    where T1 : class
    where T2 : class
{
    IJoinedQueryBuilder<T1, T2, TResult> Select<TResult>(Func<T1, T2, TResult> selector);
    IJoinedQueryBuilder<T1, T2> Where(Expression<Func<T1, T2, bool>> predicate);
    IJoinedQueryBuilder<T1, T2> OrderBy<TKey>(Expression<Func<T1, T2, TKey>> keySelector, Direction direction = Direction.Ascending);
    IJoinedQueryBuilder<T1, T2> ThenBy<TKey>(Expression<Func<T1, T2, TKey>> keySelector, Direction direction = Direction.Ascending);
    IJoinedQueryBuilder<T1, T2> Offset(int count);
    IJoinedQueryBuilder<T1, T2> Limit(int count);
    IJoinedQueryBuilder<T1, T2> Distinct();
    IJoinedQueryBuilder3<T1, T2, T3> Join<T3>(Expression<Func<T1, T2, T3, bool>> condition) where T3 : class;
    IJoinedQueryBuilder3<T1, T2, T3> LeftJoin<T3>(Expression<Func<T1, T2, T3, bool>> condition) where T3 : class;
    IJoinedQueryBuilder3<T1, T2, T3> RightJoin<T3>(Expression<Func<T1, T2, T3, bool>> condition) where T3 : class;

    QueryDiagnostics ToDiagnostics();

    /// <summary>
    /// Prepares this query chain for multiple terminal operations.
    /// </summary>
    PreparedQuery<(T1, T2)> Prepare();
}

/// <summary>
/// Interface for constructing SELECT queries with a two-table join and a specified projection.
/// </summary>
public interface IJoinedQueryBuilder<T1, T2, TResult>
    where T1 : class
    where T2 : class
{
    IJoinedQueryBuilder<T1, T2, TResult> Where(Expression<Func<T1, T2, bool>> predicate);
    IJoinedQueryBuilder<T1, T2, TResult> OrderBy<TKey>(Expression<Func<T1, T2, TKey>> keySelector, Direction direction = Direction.Ascending);
    IJoinedQueryBuilder<T1, T2, TResult> ThenBy<TKey>(Expression<Func<T1, T2, TKey>> keySelector, Direction direction = Direction.Ascending);
    IJoinedQueryBuilder<T1, T2, TResult> Offset(int count);
    IJoinedQueryBuilder<T1, T2, TResult> Limit(int count);
    IJoinedQueryBuilder<T1, T2, TResult> Distinct();
    Task<List<TResult>> ExecuteFetchAllAsync(CancellationToken cancellationToken = default);
    Task<TResult> ExecuteFetchFirstAsync(CancellationToken cancellationToken = default);
    Task<TResult?> ExecuteFetchFirstOrDefaultAsync(CancellationToken cancellationToken = default);
    Task<TResult> ExecuteFetchSingleAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<TResult> ToAsyncEnumerable(CancellationToken cancellationToken = default);

    QueryDiagnostics ToDiagnostics();

    /// <summary>
    /// Prepares this query chain for multiple terminal operations.
    /// </summary>
    PreparedQuery<TResult> Prepare();
}

/// <summary>
/// Interface for constructing SELECT queries with a three-table join (no projection).
/// </summary>
public interface IJoinedQueryBuilder3<T1, T2, T3>
    where T1 : class
    where T2 : class
    where T3 : class
{
    IJoinedQueryBuilder3<T1, T2, T3, TResult> Select<TResult>(Func<T1, T2, T3, TResult> selector);
    IJoinedQueryBuilder3<T1, T2, T3> Where(Expression<Func<T1, T2, T3, bool>> predicate);
    IJoinedQueryBuilder3<T1, T2, T3> OrderBy<TKey>(Expression<Func<T1, T2, T3, TKey>> keySelector, Direction direction = Direction.Ascending);
    IJoinedQueryBuilder3<T1, T2, T3> ThenBy<TKey>(Expression<Func<T1, T2, T3, TKey>> keySelector, Direction direction = Direction.Ascending);
    IJoinedQueryBuilder3<T1, T2, T3> Offset(int count);
    IJoinedQueryBuilder3<T1, T2, T3> Limit(int count);
    IJoinedQueryBuilder3<T1, T2, T3> Distinct();
    IJoinedQueryBuilder4<T1, T2, T3, T4> Join<T4>(Expression<Func<T1, T2, T3, T4, bool>> condition) where T4 : class;
    IJoinedQueryBuilder4<T1, T2, T3, T4> LeftJoin<T4>(Expression<Func<T1, T2, T3, T4, bool>> condition) where T4 : class;
    IJoinedQueryBuilder4<T1, T2, T3, T4> RightJoin<T4>(Expression<Func<T1, T2, T3, T4, bool>> condition) where T4 : class;

    QueryDiagnostics ToDiagnostics();

    /// <summary>
    /// Prepares this query chain for multiple terminal operations.
    /// </summary>
    PreparedQuery<(T1, T2, T3)> Prepare();
}

/// <summary>
/// Interface for constructing SELECT queries with a three-table join and a specified projection.
/// </summary>
public interface IJoinedQueryBuilder3<T1, T2, T3, TResult>
    where T1 : class
    where T2 : class
    where T3 : class
{
    IJoinedQueryBuilder3<T1, T2, T3, TResult> Where(Expression<Func<T1, T2, T3, bool>> predicate);
    IJoinedQueryBuilder3<T1, T2, T3, TResult> OrderBy<TKey>(Expression<Func<T1, T2, T3, TKey>> keySelector, Direction direction = Direction.Ascending);
    IJoinedQueryBuilder3<T1, T2, T3, TResult> ThenBy<TKey>(Expression<Func<T1, T2, T3, TKey>> keySelector, Direction direction = Direction.Ascending);
    IJoinedQueryBuilder3<T1, T2, T3, TResult> Offset(int count);
    IJoinedQueryBuilder3<T1, T2, T3, TResult> Limit(int count);
    IJoinedQueryBuilder3<T1, T2, T3, TResult> Distinct();
    Task<List<TResult>> ExecuteFetchAllAsync(CancellationToken cancellationToken = default);
    Task<TResult> ExecuteFetchFirstAsync(CancellationToken cancellationToken = default);
    Task<TResult?> ExecuteFetchFirstOrDefaultAsync(CancellationToken cancellationToken = default);
    Task<TResult> ExecuteFetchSingleAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<TResult> ToAsyncEnumerable(CancellationToken cancellationToken = default);

    QueryDiagnostics ToDiagnostics();

    /// <summary>
    /// Prepares this query chain for multiple terminal operations.
    /// </summary>
    PreparedQuery<TResult> Prepare();
}

/// <summary>
/// Interface for constructing SELECT queries with a four-table join (no projection).
/// This is the maximum join depth.
/// </summary>
public interface IJoinedQueryBuilder4<T1, T2, T3, T4>
    where T1 : class
    where T2 : class
    where T3 : class
    where T4 : class
{
    IJoinedQueryBuilder4<T1, T2, T3, T4, TResult> Select<TResult>(Func<T1, T2, T3, T4, TResult> selector);
    IJoinedQueryBuilder4<T1, T2, T3, T4> Where(Expression<Func<T1, T2, T3, T4, bool>> predicate);
    IJoinedQueryBuilder4<T1, T2, T3, T4> OrderBy<TKey>(Expression<Func<T1, T2, T3, T4, TKey>> keySelector, Direction direction = Direction.Ascending);
    IJoinedQueryBuilder4<T1, T2, T3, T4> ThenBy<TKey>(Expression<Func<T1, T2, T3, T4, TKey>> keySelector, Direction direction = Direction.Ascending);
    IJoinedQueryBuilder4<T1, T2, T3, T4> Offset(int count);
    IJoinedQueryBuilder4<T1, T2, T3, T4> Limit(int count);
    IJoinedQueryBuilder4<T1, T2, T3, T4> Distinct();

    QueryDiagnostics ToDiagnostics();

    /// <summary>
    /// Prepares this query chain for multiple terminal operations.
    /// </summary>
    PreparedQuery<(T1, T2, T3, T4)> Prepare();
}

/// <summary>
/// Interface for constructing SELECT queries with a four-table join and a specified projection.
/// </summary>
public interface IJoinedQueryBuilder4<T1, T2, T3, T4, TResult>
    where T1 : class
    where T2 : class
    where T3 : class
    where T4 : class
{
    IJoinedQueryBuilder4<T1, T2, T3, T4, TResult> Where(Expression<Func<T1, T2, T3, T4, bool>> predicate);
    IJoinedQueryBuilder4<T1, T2, T3, T4, TResult> OrderBy<TKey>(Expression<Func<T1, T2, T3, T4, TKey>> keySelector, Direction direction = Direction.Ascending);
    IJoinedQueryBuilder4<T1, T2, T3, T4, TResult> ThenBy<TKey>(Expression<Func<T1, T2, T3, T4, TKey>> keySelector, Direction direction = Direction.Ascending);
    IJoinedQueryBuilder4<T1, T2, T3, T4, TResult> Offset(int count);
    IJoinedQueryBuilder4<T1, T2, T3, T4, TResult> Limit(int count);
    IJoinedQueryBuilder4<T1, T2, T3, T4, TResult> Distinct();
    Task<List<TResult>> ExecuteFetchAllAsync(CancellationToken cancellationToken = default);
    Task<TResult> ExecuteFetchFirstAsync(CancellationToken cancellationToken = default);
    Task<TResult?> ExecuteFetchFirstOrDefaultAsync(CancellationToken cancellationToken = default);
    Task<TResult> ExecuteFetchSingleAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<TResult> ToAsyncEnumerable(CancellationToken cancellationToken = default);

    QueryDiagnostics ToDiagnostics();

    /// <summary>
    /// Prepares this query chain for multiple terminal operations.
    /// </summary>
    PreparedQuery<TResult> Prepare();
}
