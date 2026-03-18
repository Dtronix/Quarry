using System.Collections.Generic;
using System.Linq.Expressions;

namespace Quarry.Internal;

public abstract class DeleteCarrierBase<T> : IEntityAccessor<T>, IDeleteBuilder<T>, IExecutableDeleteBuilder<T>
    where T : class
{
    public IQueryExecutionContext? Ctx;

    // ── IEntityAccessor<T> stubs ──

    IQueryBuilder<T> IEntityAccessor<T>.Where(Expression<Func<T, bool>> predicate)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Where is not intercepted in this optimized chain. This indicates a code generation bug.");
    IQueryBuilder<T, TResult> IEntityAccessor<T>.Select<TResult>(Func<T, TResult> selector)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Select is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder<T, TJoined> IEntityAccessor<T>.Join<TJoined>(Expression<Func<T, TJoined, bool>> condition)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Join is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder<T, TJoined> IEntityAccessor<T>.LeftJoin<TJoined>(Expression<Func<T, TJoined, bool>> condition)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.LeftJoin is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder<T, TJoined> IEntityAccessor<T>.RightJoin<TJoined>(Expression<Func<T, TJoined, bool>> condition)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.RightJoin is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder<T, TJoined> IEntityAccessor<T>.Join<TJoined>(Expression<Func<T, NavigationList<TJoined>>> navigation)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Join is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder<T, TJoined> IEntityAccessor<T>.LeftJoin<TJoined>(Expression<Func<T, NavigationList<TJoined>>> navigation)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.LeftJoin is not intercepted in this optimized chain. This indicates a code generation bug.");
    IQueryBuilder<T> IEntityAccessor<T>.Distinct()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Distinct is not intercepted in this optimized chain. This indicates a code generation bug.");
    IQueryBuilder<T> IEntityAccessor<T>.WithTimeout(TimeSpan timeout)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.WithTimeout is not intercepted in this optimized chain. This indicates a code generation bug.");
    string IEntityAccessor<T>.ToSql()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.ToSql is not intercepted in this optimized chain. This indicates a code generation bug.");
    QueryDiagnostics IEntityAccessor<T>.ToDiagnostics()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.ToDiagnostics is not intercepted in this optimized chain. This indicates a code generation bug.");
    IDeleteBuilder<T> IEntityAccessor<T>.Delete()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Delete is not intercepted in this optimized chain. This indicates a code generation bug.");
    IUpdateBuilder<T> IEntityAccessor<T>.Update()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Update is not intercepted in this optimized chain. This indicates a code generation bug.");
    IInsertBuilder<T> IEntityAccessor<T>.Insert(T entity)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Insert is not intercepted in this optimized chain. This indicates a code generation bug.");
    IInsertBuilder<T> IEntityAccessor<T>.InsertMany(IEnumerable<T> entities)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.InsertMany is not intercepted in this optimized chain. This indicates a code generation bug.");

    QueryPlan IEntityAccessor<T>.ToQueryPlan()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.ToQueryPlan is not intercepted in this optimized chain. This indicates a code generation bug.");

    // IDeleteBuilder<T> explicit implementations

    IExecutableDeleteBuilder<T> IDeleteBuilder<T>.Where(Expression<Func<T, bool>> predicate)
        => throw new InvalidOperationException("Carrier method IDeleteBuilder.Where is not intercepted in this optimized chain. This indicates a code generation bug.");

    IExecutableDeleteBuilder<T> IDeleteBuilder<T>.All()
        => throw new InvalidOperationException("Carrier method IDeleteBuilder.All is not intercepted in this optimized chain. This indicates a code generation bug.");

    IDeleteBuilder<T> IDeleteBuilder<T>.WithTimeout(TimeSpan timeout)
        => throw new InvalidOperationException("Carrier method IDeleteBuilder.WithTimeout is not intercepted in this optimized chain. This indicates a code generation bug.");

    // IExecutableDeleteBuilder<T> explicit implementations

    IExecutableDeleteBuilder<T> IExecutableDeleteBuilder<T>.Where(Expression<Func<T, bool>> predicate)
        => throw new InvalidOperationException("Carrier method IExecutableDeleteBuilder.Where is not intercepted in this optimized chain. This indicates a code generation bug.");

    IExecutableDeleteBuilder<T> IExecutableDeleteBuilder<T>.WithTimeout(TimeSpan timeout)
        => throw new InvalidOperationException("Carrier method IExecutableDeleteBuilder.WithTimeout is not intercepted in this optimized chain. This indicates a code generation bug.");

    Task<int> IExecutableDeleteBuilder<T>.ExecuteNonQueryAsync(CancellationToken cancellationToken)
        => throw new InvalidOperationException("Carrier method IExecutableDeleteBuilder.ExecuteNonQueryAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    // ToSql — both interfaces declare it independently, so both need explicit impls.

    string IDeleteBuilder<T>.ToSql()
        => throw new InvalidOperationException("Carrier method IDeleteBuilder.ToSql is not intercepted in this optimized chain. This indicates a code generation bug.");

    QueryDiagnostics IDeleteBuilder<T>.ToDiagnostics()
        => throw new InvalidOperationException("Carrier method IDeleteBuilder.ToDiagnostics is not intercepted in this optimized chain. This indicates a code generation bug.");

    string IExecutableDeleteBuilder<T>.ToSql()
        => throw new InvalidOperationException("Carrier method IExecutableDeleteBuilder.ToSql is not intercepted in this optimized chain. This indicates a code generation bug.");

    QueryDiagnostics IExecutableDeleteBuilder<T>.ToDiagnostics()
        => throw new InvalidOperationException("Carrier method IExecutableDeleteBuilder.ToDiagnostics is not intercepted in this optimized chain. This indicates a code generation bug.");
}

public abstract class UpdateCarrierBase<T> : IEntityAccessor<T>, IUpdateBuilder<T>, IExecutableUpdateBuilder<T>
    where T : class
{
    public IQueryExecutionContext? Ctx;

    // ── IEntityAccessor<T> stubs ──

    IQueryBuilder<T> IEntityAccessor<T>.Where(Expression<Func<T, bool>> predicate)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Where is not intercepted in this optimized chain. This indicates a code generation bug.");
    IQueryBuilder<T, TResult> IEntityAccessor<T>.Select<TResult>(Func<T, TResult> selector)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Select is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder<T, TJoined> IEntityAccessor<T>.Join<TJoined>(Expression<Func<T, TJoined, bool>> condition)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Join is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder<T, TJoined> IEntityAccessor<T>.LeftJoin<TJoined>(Expression<Func<T, TJoined, bool>> condition)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.LeftJoin is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder<T, TJoined> IEntityAccessor<T>.RightJoin<TJoined>(Expression<Func<T, TJoined, bool>> condition)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.RightJoin is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder<T, TJoined> IEntityAccessor<T>.Join<TJoined>(Expression<Func<T, NavigationList<TJoined>>> navigation)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Join is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder<T, TJoined> IEntityAccessor<T>.LeftJoin<TJoined>(Expression<Func<T, NavigationList<TJoined>>> navigation)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.LeftJoin is not intercepted in this optimized chain. This indicates a code generation bug.");
    IQueryBuilder<T> IEntityAccessor<T>.Distinct()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Distinct is not intercepted in this optimized chain. This indicates a code generation bug.");
    IQueryBuilder<T> IEntityAccessor<T>.WithTimeout(TimeSpan timeout)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.WithTimeout is not intercepted in this optimized chain. This indicates a code generation bug.");
    string IEntityAccessor<T>.ToSql()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.ToSql is not intercepted in this optimized chain. This indicates a code generation bug.");
    QueryDiagnostics IEntityAccessor<T>.ToDiagnostics()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.ToDiagnostics is not intercepted in this optimized chain. This indicates a code generation bug.");
    IDeleteBuilder<T> IEntityAccessor<T>.Delete()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Delete is not intercepted in this optimized chain. This indicates a code generation bug.");
    IUpdateBuilder<T> IEntityAccessor<T>.Update()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Update is not intercepted in this optimized chain. This indicates a code generation bug.");
    IInsertBuilder<T> IEntityAccessor<T>.Insert(T entity)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Insert is not intercepted in this optimized chain. This indicates a code generation bug.");
    IInsertBuilder<T> IEntityAccessor<T>.InsertMany(IEnumerable<T> entities)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.InsertMany is not intercepted in this optimized chain. This indicates a code generation bug.");

    QueryPlan IEntityAccessor<T>.ToQueryPlan()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.ToQueryPlan is not intercepted in this optimized chain. This indicates a code generation bug.");

    // IUpdateBuilder<T> explicit implementations

    IUpdateBuilder<T> IUpdateBuilder<T>.Set<TValue>(Expression<Func<T, TValue>> column, TValue value)
        => throw new InvalidOperationException("Carrier method IUpdateBuilder.Set is not intercepted in this optimized chain. This indicates a code generation bug.");

    IUpdateBuilder<T> IUpdateBuilder<T>.Set(T entity)
        => throw new InvalidOperationException("Carrier method IUpdateBuilder.Set is not intercepted in this optimized chain. This indicates a code generation bug.");

    IExecutableUpdateBuilder<T> IUpdateBuilder<T>.Where(Expression<Func<T, bool>> predicate)
        => throw new InvalidOperationException("Carrier method IUpdateBuilder.Where is not intercepted in this optimized chain. This indicates a code generation bug.");

    IExecutableUpdateBuilder<T> IUpdateBuilder<T>.All()
        => throw new InvalidOperationException("Carrier method IUpdateBuilder.All is not intercepted in this optimized chain. This indicates a code generation bug.");

    IUpdateBuilder<T> IUpdateBuilder<T>.WithTimeout(TimeSpan timeout)
        => throw new InvalidOperationException("Carrier method IUpdateBuilder.WithTimeout is not intercepted in this optimized chain. This indicates a code generation bug.");

    // IExecutableUpdateBuilder<T> explicit implementations

    IExecutableUpdateBuilder<T> IExecutableUpdateBuilder<T>.Set<TValue>(Expression<Func<T, TValue>> column, TValue value)
        => throw new InvalidOperationException("Carrier method IExecutableUpdateBuilder.Set is not intercepted in this optimized chain. This indicates a code generation bug.");

    IExecutableUpdateBuilder<T> IExecutableUpdateBuilder<T>.Set(T entity)
        => throw new InvalidOperationException("Carrier method IExecutableUpdateBuilder.Set is not intercepted in this optimized chain. This indicates a code generation bug.");

    IExecutableUpdateBuilder<T> IExecutableUpdateBuilder<T>.Where(Expression<Func<T, bool>> predicate)
        => throw new InvalidOperationException("Carrier method IExecutableUpdateBuilder.Where is not intercepted in this optimized chain. This indicates a code generation bug.");

    IExecutableUpdateBuilder<T> IExecutableUpdateBuilder<T>.WithTimeout(TimeSpan timeout)
        => throw new InvalidOperationException("Carrier method IExecutableUpdateBuilder.WithTimeout is not intercepted in this optimized chain. This indicates a code generation bug.");

    Task<int> IExecutableUpdateBuilder<T>.ExecuteNonQueryAsync(CancellationToken cancellationToken)
        => throw new InvalidOperationException("Carrier method IExecutableUpdateBuilder.ExecuteNonQueryAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    // ToSql — both interfaces declare it independently, so both need explicit impls.

    string IUpdateBuilder<T>.ToSql()
        => throw new InvalidOperationException("Carrier method IUpdateBuilder.ToSql is not intercepted in this optimized chain. This indicates a code generation bug.");

    QueryDiagnostics IUpdateBuilder<T>.ToDiagnostics()
        => throw new InvalidOperationException("Carrier method IUpdateBuilder.ToDiagnostics is not intercepted in this optimized chain. This indicates a code generation bug.");

    string IExecutableUpdateBuilder<T>.ToSql()
        => throw new InvalidOperationException("Carrier method IExecutableUpdateBuilder.ToSql is not intercepted in this optimized chain. This indicates a code generation bug.");

    QueryDiagnostics IExecutableUpdateBuilder<T>.ToDiagnostics()
        => throw new InvalidOperationException("Carrier method IExecutableUpdateBuilder.ToDiagnostics is not intercepted in this optimized chain. This indicates a code generation bug.");
}

public abstract class InsertCarrierBase<T> : IEntityAccessor<T>, IInsertBuilder<T>
    where T : class
{
    public IQueryExecutionContext? Ctx;

    // ── IEntityAccessor<T> stubs ──

    IQueryBuilder<T> IEntityAccessor<T>.Where(Expression<Func<T, bool>> predicate)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Where is not intercepted in this optimized chain. This indicates a code generation bug.");
    IQueryBuilder<T, TResult> IEntityAccessor<T>.Select<TResult>(Func<T, TResult> selector)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Select is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder<T, TJoined> IEntityAccessor<T>.Join<TJoined>(Expression<Func<T, TJoined, bool>> condition)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Join is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder<T, TJoined> IEntityAccessor<T>.LeftJoin<TJoined>(Expression<Func<T, TJoined, bool>> condition)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.LeftJoin is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder<T, TJoined> IEntityAccessor<T>.RightJoin<TJoined>(Expression<Func<T, TJoined, bool>> condition)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.RightJoin is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder<T, TJoined> IEntityAccessor<T>.Join<TJoined>(Expression<Func<T, NavigationList<TJoined>>> navigation)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Join is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder<T, TJoined> IEntityAccessor<T>.LeftJoin<TJoined>(Expression<Func<T, NavigationList<TJoined>>> navigation)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.LeftJoin is not intercepted in this optimized chain. This indicates a code generation bug.");
    IQueryBuilder<T> IEntityAccessor<T>.Distinct()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Distinct is not intercepted in this optimized chain. This indicates a code generation bug.");
    IQueryBuilder<T> IEntityAccessor<T>.WithTimeout(TimeSpan timeout)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.WithTimeout is not intercepted in this optimized chain. This indicates a code generation bug.");
    string IEntityAccessor<T>.ToSql()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.ToSql is not intercepted in this optimized chain. This indicates a code generation bug.");
    QueryDiagnostics IEntityAccessor<T>.ToDiagnostics()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.ToDiagnostics is not intercepted in this optimized chain. This indicates a code generation bug.");
    IDeleteBuilder<T> IEntityAccessor<T>.Delete()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Delete is not intercepted in this optimized chain. This indicates a code generation bug.");
    IUpdateBuilder<T> IEntityAccessor<T>.Update()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Update is not intercepted in this optimized chain. This indicates a code generation bug.");
    IInsertBuilder<T> IEntityAccessor<T>.Insert(T entity)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Insert is not intercepted in this optimized chain. This indicates a code generation bug.");
    IInsertBuilder<T> IEntityAccessor<T>.InsertMany(IEnumerable<T> entities)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.InsertMany is not intercepted in this optimized chain. This indicates a code generation bug.");

    QueryPlan IEntityAccessor<T>.ToQueryPlan()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.ToQueryPlan is not intercepted in this optimized chain. This indicates a code generation bug.");

    // IInsertBuilder<T> explicit implementations

    IInsertBuilder<T> IInsertBuilder<T>.Values(T entity)
        => throw new InvalidOperationException("Carrier method IInsertBuilder.Values is not intercepted in this optimized chain. This indicates a code generation bug.");

    IInsertBuilder<T> IInsertBuilder<T>.WithTimeout(TimeSpan timeout)
        => throw new InvalidOperationException("Carrier method IInsertBuilder.WithTimeout is not intercepted in this optimized chain. This indicates a code generation bug.");

    Task<int> IInsertBuilder<T>.ExecuteNonQueryAsync(CancellationToken cancellationToken)
        => throw new InvalidOperationException("Carrier method IInsertBuilder.ExecuteNonQueryAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    Task<TKey> IInsertBuilder<T>.ExecuteScalarAsync<TKey>(CancellationToken cancellationToken)
        => throw new InvalidOperationException("Carrier method IInsertBuilder.ExecuteScalarAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    string IInsertBuilder<T>.ToSql()
        => throw new InvalidOperationException("Carrier method IInsertBuilder.ToSql is not intercepted in this optimized chain. This indicates a code generation bug.");

    QueryDiagnostics IInsertBuilder<T>.ToDiagnostics()
        => throw new InvalidOperationException("Carrier method IInsertBuilder.ToDiagnostics is not intercepted in this optimized chain. This indicates a code generation bug.");
}
