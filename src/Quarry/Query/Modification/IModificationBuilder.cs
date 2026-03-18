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
    string ToSql();
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
    string ToSql();
    QueryDiagnostics ToDiagnostics();
}

/// <summary>
/// Interface for constructing UPDATE operations (before WHERE/All).
/// </summary>
public interface IUpdateBuilder<T> where T : class
{
    IUpdateBuilder<T> Set<TValue>(Expression<Func<T, TValue>> column, TValue value);
    IUpdateBuilder<T> Set(T entity);
    IExecutableUpdateBuilder<T> Where(Expression<Func<T, bool>> predicate);
    IExecutableUpdateBuilder<T> All();
    IUpdateBuilder<T> WithTimeout(TimeSpan timeout);
    string ToSql();
    QueryDiagnostics ToDiagnostics();
}

/// <summary>
/// Interface for constructing UPDATE operations that can be executed (after WHERE/All).
/// </summary>
public interface IExecutableUpdateBuilder<T> where T : class
{
    IExecutableUpdateBuilder<T> Set<TValue>(Expression<Func<T, TValue>> column, TValue value);
    IExecutableUpdateBuilder<T> Set(T entity);
    IExecutableUpdateBuilder<T> Where(Expression<Func<T, bool>> predicate);
    IExecutableUpdateBuilder<T> WithTimeout(TimeSpan timeout);
    Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default);
    string ToSql();
    QueryDiagnostics ToDiagnostics();
}

/// <summary>
/// Interface for constructing INSERT operations.
/// </summary>
public interface IInsertBuilder<T> where T : class
{
    IInsertBuilder<T> Values(T entity);
    IInsertBuilder<T> WithTimeout(TimeSpan timeout);
    Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default);
    Task<TKey> ExecuteScalarAsync<TKey>(CancellationToken cancellationToken = default);
    string ToSql();
    QueryDiagnostics ToDiagnostics();
}
