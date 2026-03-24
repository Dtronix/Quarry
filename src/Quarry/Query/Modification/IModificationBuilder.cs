using System.Linq.Expressions;

namespace Quarry;

/// <summary>
/// Interface for constructing DELETE operations (before WHERE/All).
/// </summary>
public interface IDeleteBuilder<T> where T : class
{
    IExecutableDeleteBuilder<T> Where(Expression<Func<T, bool>> predicate);
    IExecutableDeleteBuilder<T> All();
    IDeleteBuilder<T> WithTimeout(TimeSpan timeout);

    QueryDiagnostics ToDiagnostics();
}

/// <summary>
/// Interface for constructing DELETE operations that can be executed (after WHERE/All).
/// </summary>
public interface IExecutableDeleteBuilder<T> where T : class
{
    IExecutableDeleteBuilder<T> Where(Expression<Func<T, bool>> predicate);
    IExecutableDeleteBuilder<T> WithTimeout(TimeSpan timeout);
    Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default);

    QueryDiagnostics ToDiagnostics();
}

/// <summary>
/// Interface for constructing UPDATE operations (before WHERE/All).
/// </summary>
public interface IUpdateBuilder<T> where T : class
{
    IUpdateBuilder<T> Set(T entity);
    IUpdateBuilder<T> Set(Action<T> assignment);
    IExecutableUpdateBuilder<T> Where(Expression<Func<T, bool>> predicate);
    IExecutableUpdateBuilder<T> All();
    IUpdateBuilder<T> WithTimeout(TimeSpan timeout);

    QueryDiagnostics ToDiagnostics();
}

/// <summary>
/// Interface for constructing UPDATE operations that can be executed (after WHERE/All).
/// </summary>
public interface IExecutableUpdateBuilder<T> where T : class
{
    IExecutableUpdateBuilder<T> Set(T entity);
    IExecutableUpdateBuilder<T> Set(Action<T> assignment);
    IExecutableUpdateBuilder<T> Where(Expression<Func<T, bool>> predicate);
    IExecutableUpdateBuilder<T> WithTimeout(TimeSpan timeout);
    Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default);

    QueryDiagnostics ToDiagnostics();
}

/// <summary>
/// Interface for constructing single-entity INSERT operations.
/// </summary>
public interface IInsertBuilder<T> where T : class
{
    IInsertBuilder<T> WithTimeout(TimeSpan timeout);
    Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default);
    Task<TKey> ExecuteScalarAsync<TKey>(CancellationToken cancellationToken = default);

    QueryDiagnostics ToDiagnostics();
}

/// <summary>
/// Interface for constructing batch INSERT operations after column selection.
/// Returned by the column-selector <c>InsertBatch(lambda)</c> overload.
/// </summary>
public interface IBatchInsertBuilder<T> where T : class
{
    IExecutableBatchInsert<T> Values(IEnumerable<T> entities);
    IBatchInsertBuilder<T> WithTimeout(TimeSpan timeout);
}

/// <summary>
/// Terminal interface for batch INSERT operations after <c>Values()</c>.
/// Supports execution and diagnostics.
/// </summary>
public interface IExecutableBatchInsert<T> where T : class
{
    Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default);
    Task<TKey> ExecuteScalarAsync<TKey>(CancellationToken cancellationToken = default);

    QueryDiagnostics ToDiagnostics();
}
