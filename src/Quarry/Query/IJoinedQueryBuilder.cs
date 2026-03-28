using System.Data.Common;

namespace Quarry;

/// <summary>
/// Interface for constructing SELECT queries with a two-table join (no projection).
/// </summary>
public interface IJoinedQueryBuilder<T1, T2>
    where T1 : class
    where T2 : class
{
    IJoinedQueryBuilder<T1, T2, TResult> Select<TResult>(Func<T1, T2, TResult> selector)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.Select is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder<T1, T2> Where(Func<T1, T2, bool> predicate)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.Where is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder<T1, T2> OrderBy<TKey>(Func<T1, T2, TKey> keySelector, Direction direction = Direction.Ascending)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.OrderBy is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder<T1, T2> ThenBy<TKey>(Func<T1, T2, TKey> keySelector, Direction direction = Direction.Ascending)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.ThenBy is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder<T1, T2> Offset(int count)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.Offset is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder<T1, T2> Limit(int count)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.Limit is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder<T1, T2> Distinct()
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.Distinct is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder3<T1, T2, T3> Join<T3>(Func<T1, T2, T3, bool> condition) where T3 : class
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.Join is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder3<T1, T2, T3> LeftJoin<T3>(Func<T1, T2, T3, bool> condition) where T3 : class
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.LeftJoin is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder3<T1, T2, T3> RightJoin<T3>(Func<T1, T2, T3, bool> condition) where T3 : class
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.RightJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    QueryDiagnostics ToDiagnostics()
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.ToDiagnostics is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Prepares this query chain for multiple terminal operations.
    /// </summary>
    PreparedQuery<(T1, T2)> Prepare()
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.Prepare is not intercepted in this optimized chain. This indicates a code generation bug.");
}

/// <summary>
/// Interface for constructing SELECT queries with a two-table join and a specified projection.
/// </summary>
public interface IJoinedQueryBuilder<T1, T2, TResult>
    where T1 : class
    where T2 : class
{
    IJoinedQueryBuilder<T1, T2, TResult> Where(Func<T1, T2, bool> predicate)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.Where is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder<T1, T2, TResult> OrderBy<TKey>(Func<T1, T2, TKey> keySelector, Direction direction = Direction.Ascending)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.OrderBy is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder<T1, T2, TResult> ThenBy<TKey>(Func<T1, T2, TKey> keySelector, Direction direction = Direction.Ascending)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.ThenBy is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder<T1, T2, TResult> Offset(int count)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.Offset is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder<T1, T2, TResult> Limit(int count)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.Limit is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder<T1, T2, TResult> Distinct()
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.Distinct is not intercepted in this optimized chain. This indicates a code generation bug.");
    Task<List<TResult>> ExecuteFetchAllAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.ExecuteFetchAllAsync is not intercepted in this optimized chain. This indicates a code generation bug.");
    Task<TResult> ExecuteFetchFirstAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.ExecuteFetchFirstAsync is not intercepted in this optimized chain. This indicates a code generation bug.");
    Task<TResult?> ExecuteFetchFirstOrDefaultAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.ExecuteFetchFirstOrDefaultAsync is not intercepted in this optimized chain. This indicates a code generation bug.");
    Task<TResult> ExecuteFetchSingleAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.ExecuteFetchSingleAsync is not intercepted in this optimized chain. This indicates a code generation bug.");
    IAsyncEnumerable<TResult> ToAsyncEnumerable(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.ToAsyncEnumerable is not intercepted in this optimized chain. This indicates a code generation bug.");
    Task<TScalar> ExecuteScalarAsync<TScalar>(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.ExecuteScalarAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    QueryDiagnostics ToDiagnostics()
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.ToDiagnostics is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Prepares this query chain for multiple terminal operations.
    /// </summary>
    PreparedQuery<TResult> Prepare()
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.Prepare is not intercepted in this optimized chain. This indicates a code generation bug.");
}

/// <summary>
/// Interface for constructing SELECT queries with a three-table join (no projection).
/// </summary>
public interface IJoinedQueryBuilder3<T1, T2, T3>
    where T1 : class
    where T2 : class
    where T3 : class
{
    IJoinedQueryBuilder3<T1, T2, T3, TResult> Select<TResult>(Func<T1, T2, T3, TResult> selector)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.Select is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder3<T1, T2, T3> Where(Func<T1, T2, T3, bool> predicate)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.Where is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder3<T1, T2, T3> OrderBy<TKey>(Func<T1, T2, T3, TKey> keySelector, Direction direction = Direction.Ascending)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.OrderBy is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder3<T1, T2, T3> ThenBy<TKey>(Func<T1, T2, T3, TKey> keySelector, Direction direction = Direction.Ascending)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.ThenBy is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder3<T1, T2, T3> Offset(int count)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.Offset is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder3<T1, T2, T3> Limit(int count)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.Limit is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder3<T1, T2, T3> Distinct()
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.Distinct is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder4<T1, T2, T3, T4> Join<T4>(Func<T1, T2, T3, T4, bool> condition) where T4 : class
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.Join is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder4<T1, T2, T3, T4> LeftJoin<T4>(Func<T1, T2, T3, T4, bool> condition) where T4 : class
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.LeftJoin is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder4<T1, T2, T3, T4> RightJoin<T4>(Func<T1, T2, T3, T4, bool> condition) where T4 : class
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.RightJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    QueryDiagnostics ToDiagnostics()
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.ToDiagnostics is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Prepares this query chain for multiple terminal operations.
    /// </summary>
    PreparedQuery<(T1, T2, T3)> Prepare()
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.Prepare is not intercepted in this optimized chain. This indicates a code generation bug.");
}

/// <summary>
/// Interface for constructing SELECT queries with a three-table join and a specified projection.
/// </summary>
public interface IJoinedQueryBuilder3<T1, T2, T3, TResult>
    where T1 : class
    where T2 : class
    where T3 : class
{
    IJoinedQueryBuilder3<T1, T2, T3, TResult> Where(Func<T1, T2, T3, bool> predicate)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.Where is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder3<T1, T2, T3, TResult> OrderBy<TKey>(Func<T1, T2, T3, TKey> keySelector, Direction direction = Direction.Ascending)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.OrderBy is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder3<T1, T2, T3, TResult> ThenBy<TKey>(Func<T1, T2, T3, TKey> keySelector, Direction direction = Direction.Ascending)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.ThenBy is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder3<T1, T2, T3, TResult> Offset(int count)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.Offset is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder3<T1, T2, T3, TResult> Limit(int count)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.Limit is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder3<T1, T2, T3, TResult> Distinct()
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.Distinct is not intercepted in this optimized chain. This indicates a code generation bug.");
    Task<List<TResult>> ExecuteFetchAllAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.ExecuteFetchAllAsync is not intercepted in this optimized chain. This indicates a code generation bug.");
    Task<TResult> ExecuteFetchFirstAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.ExecuteFetchFirstAsync is not intercepted in this optimized chain. This indicates a code generation bug.");
    Task<TResult?> ExecuteFetchFirstOrDefaultAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.ExecuteFetchFirstOrDefaultAsync is not intercepted in this optimized chain. This indicates a code generation bug.");
    Task<TResult> ExecuteFetchSingleAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.ExecuteFetchSingleAsync is not intercepted in this optimized chain. This indicates a code generation bug.");
    IAsyncEnumerable<TResult> ToAsyncEnumerable(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.ToAsyncEnumerable is not intercepted in this optimized chain. This indicates a code generation bug.");
    Task<TScalar> ExecuteScalarAsync<TScalar>(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.ExecuteScalarAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    QueryDiagnostics ToDiagnostics()
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.ToDiagnostics is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Prepares this query chain for multiple terminal operations.
    /// </summary>
    PreparedQuery<TResult> Prepare()
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.Prepare is not intercepted in this optimized chain. This indicates a code generation bug.");
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
    IJoinedQueryBuilder4<T1, T2, T3, T4, TResult> Select<TResult>(Func<T1, T2, T3, T4, TResult> selector)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder4.Select is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder4<T1, T2, T3, T4> Where(Func<T1, T2, T3, T4, bool> predicate)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder4.Where is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder4<T1, T2, T3, T4> OrderBy<TKey>(Func<T1, T2, T3, T4, TKey> keySelector, Direction direction = Direction.Ascending)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder4.OrderBy is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder4<T1, T2, T3, T4> ThenBy<TKey>(Func<T1, T2, T3, T4, TKey> keySelector, Direction direction = Direction.Ascending)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder4.ThenBy is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder4<T1, T2, T3, T4> Offset(int count)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder4.Offset is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder4<T1, T2, T3, T4> Limit(int count)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder4.Limit is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder4<T1, T2, T3, T4> Distinct()
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder4.Distinct is not intercepted in this optimized chain. This indicates a code generation bug.");

    QueryDiagnostics ToDiagnostics()
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder4.ToDiagnostics is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Prepares this query chain for multiple terminal operations.
    /// </summary>
    PreparedQuery<(T1, T2, T3, T4)> Prepare()
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder4.Prepare is not intercepted in this optimized chain. This indicates a code generation bug.");
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
    IJoinedQueryBuilder4<T1, T2, T3, T4, TResult> Where(Func<T1, T2, T3, T4, bool> predicate)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder4.Where is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder4<T1, T2, T3, T4, TResult> OrderBy<TKey>(Func<T1, T2, T3, T4, TKey> keySelector, Direction direction = Direction.Ascending)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder4.OrderBy is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder4<T1, T2, T3, T4, TResult> ThenBy<TKey>(Func<T1, T2, T3, T4, TKey> keySelector, Direction direction = Direction.Ascending)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder4.ThenBy is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder4<T1, T2, T3, T4, TResult> Offset(int count)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder4.Offset is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder4<T1, T2, T3, T4, TResult> Limit(int count)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder4.Limit is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder4<T1, T2, T3, T4, TResult> Distinct()
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder4.Distinct is not intercepted in this optimized chain. This indicates a code generation bug.");
    Task<List<TResult>> ExecuteFetchAllAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder4.ExecuteFetchAllAsync is not intercepted in this optimized chain. This indicates a code generation bug.");
    Task<TResult> ExecuteFetchFirstAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder4.ExecuteFetchFirstAsync is not intercepted in this optimized chain. This indicates a code generation bug.");
    Task<TResult?> ExecuteFetchFirstOrDefaultAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder4.ExecuteFetchFirstOrDefaultAsync is not intercepted in this optimized chain. This indicates a code generation bug.");
    Task<TResult> ExecuteFetchSingleAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder4.ExecuteFetchSingleAsync is not intercepted in this optimized chain. This indicates a code generation bug.");
    IAsyncEnumerable<TResult> ToAsyncEnumerable(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder4.ToAsyncEnumerable is not intercepted in this optimized chain. This indicates a code generation bug.");
    Task<TScalar> ExecuteScalarAsync<TScalar>(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder4.ExecuteScalarAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    QueryDiagnostics ToDiagnostics()
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder4.ToDiagnostics is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Prepares this query chain for multiple terminal operations.
    /// </summary>
    PreparedQuery<TResult> Prepare()
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder4.Prepare is not intercepted in this optimized chain. This indicates a code generation bug.");
}
