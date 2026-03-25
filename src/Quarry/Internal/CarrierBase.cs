using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace Quarry.Internal;

public abstract class CarrierBase<T> : IEntityAccessor<T>, IQueryBuilder<T>
    where T : class
{
    public IQueryExecutionContext? Ctx;

    // ── IEntityAccessor<T> query chain starters ──

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

    IQueryBuilder<T> IEntityAccessor<T>.GroupBy<TKey>(Expression<Func<T, TKey>> keySelector)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.GroupBy is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T> IEntityAccessor<T>.Distinct()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Distinct is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T> IEntityAccessor<T>.WithTimeout(TimeSpan timeout)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.WithTimeout is not intercepted in this optimized chain. This indicates a code generation bug.");

    QueryDiagnostics IEntityAccessor<T>.ToDiagnostics()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.ToDiagnostics is not intercepted in this optimized chain. This indicates a code generation bug.");

    // IEntityAccessor<T> modification methods:
    IDeleteBuilder<T> IEntityAccessor<T>.Delete()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Delete is not intercepted in this optimized chain. This indicates a code generation bug.");

    IUpdateBuilder<T> IEntityAccessor<T>.Update()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Update is not intercepted in this optimized chain. This indicates a code generation bug.");

    IInsertBuilder<T> IEntityAccessor<T>.Insert(T entity)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Insert is not intercepted in this optimized chain. This indicates a code generation bug.");

    IBatchInsertBuilder<T> IEntityAccessor<T>.InsertBatch<TColumns>(Func<T, TColumns> columnSelector)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Insert(columnSelector) is not intercepted in this optimized chain. This indicates a code generation bug.");

    QueryPlan IEntityAccessor<T>.ToQueryPlan()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.ToQueryPlan is not intercepted in this optimized chain. This indicates a code generation bug.");

    // ── IQueryBuilder<T> methods ──

    IQueryBuilder<T> IQueryBuilder<T>.Where(Expression<Func<T, bool>> predicate)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Where is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T, TResult> IQueryBuilder<T>.Select<TResult>(Func<T, TResult> selector)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Select is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T> IQueryBuilder<T>.OrderBy<TKey>(Expression<Func<T, TKey>> keySelector, Direction direction)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.OrderBy is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T> IQueryBuilder<T>.ThenBy<TKey>(Expression<Func<T, TKey>> keySelector, Direction direction)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.ThenBy is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T> IQueryBuilder<T>.Offset(int count)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Offset is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T> IQueryBuilder<T>.Limit(int count)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Limit is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T> IQueryBuilder<T>.Distinct()
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Distinct is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T> IQueryBuilder<T>.WithTimeout(TimeSpan timeout)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.WithTimeout is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T> IQueryBuilder<T>.GroupBy<TKey>(Expression<Func<T, TKey>> keySelector)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.GroupBy is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T> IQueryBuilder<T>.Having(Expression<Func<T, bool>> predicate)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Having is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder<T, TJoined> IQueryBuilder<T>.Join<TJoined>(Expression<Func<T, TJoined, bool>> condition)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Join is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder<T, TJoined> IQueryBuilder<T>.LeftJoin<TJoined>(Expression<Func<T, TJoined, bool>> condition)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.LeftJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder<T, TJoined> IQueryBuilder<T>.RightJoin<TJoined>(Expression<Func<T, TJoined, bool>> condition)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.RightJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder<T, TJoined> IQueryBuilder<T>.Join<TJoined>(Expression<Func<T, NavigationList<TJoined>>> navigation)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Join is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder<T, TJoined> IQueryBuilder<T>.LeftJoin<TJoined>(Expression<Func<T, NavigationList<TJoined>>> navigation)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.LeftJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    QueryDiagnostics IQueryBuilder<T>.ToDiagnostics()
        => throw new InvalidOperationException("Carrier method IQueryBuilder.ToDiagnostics is not intercepted in this optimized chain. This indicates a code generation bug.");

    PreparedQuery<T> IQueryBuilder<T>.Prepare()
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Prepare is not intercepted in this optimized chain. This indicates a code generation bug.");
}

public abstract class CarrierBase<T, TResult> : IEntityAccessor<T>, IQueryBuilder<T>, IQueryBuilder<T, TResult>
    where T : class
{
    public IQueryExecutionContext? Ctx;

    // ── IEntityAccessor<T> stubs ──

    IQueryBuilder<T> IEntityAccessor<T>.Where(Expression<Func<T, bool>> predicate)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Where is not intercepted in this optimized chain. This indicates a code generation bug.");
    IQueryBuilder<T, TResult1> IEntityAccessor<T>.Select<TResult1>(Func<T, TResult1> selector)
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
    IQueryBuilder<T> IEntityAccessor<T>.GroupBy<TKey>(Expression<Func<T, TKey>> keySelector)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.GroupBy is not intercepted in this optimized chain. This indicates a code generation bug.");
    IQueryBuilder<T> IEntityAccessor<T>.Distinct()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Distinct is not intercepted in this optimized chain. This indicates a code generation bug.");
    IQueryBuilder<T> IEntityAccessor<T>.WithTimeout(TimeSpan timeout)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.WithTimeout is not intercepted in this optimized chain. This indicates a code generation bug.");
    QueryDiagnostics IEntityAccessor<T>.ToDiagnostics()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.ToDiagnostics is not intercepted in this optimized chain. This indicates a code generation bug.");
    IDeleteBuilder<T> IEntityAccessor<T>.Delete()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Delete is not intercepted in this optimized chain. This indicates a code generation bug.");
    IUpdateBuilder<T> IEntityAccessor<T>.Update()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Update is not intercepted in this optimized chain. This indicates a code generation bug.");
    IInsertBuilder<T> IEntityAccessor<T>.Insert(T entity)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Insert is not intercepted in this optimized chain. This indicates a code generation bug.");
    IBatchInsertBuilder<T> IEntityAccessor<T>.InsertBatch<TColumns>(Func<T, TColumns> columnSelector)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Insert(columnSelector) is not intercepted in this optimized chain. This indicates a code generation bug.");

    QueryPlan IEntityAccessor<T>.ToQueryPlan()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.ToQueryPlan is not intercepted in this optimized chain. This indicates a code generation bug.");

    // IQueryBuilder<T> explicit implementations

    IQueryBuilder<T> IQueryBuilder<T>.Where(Expression<Func<T, bool>> predicate)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Where is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T, TResult1> IQueryBuilder<T>.Select<TResult1>(Func<T, TResult1> selector)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Select is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T> IQueryBuilder<T>.OrderBy<TKey>(Expression<Func<T, TKey>> keySelector, Direction direction)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.OrderBy is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T> IQueryBuilder<T>.ThenBy<TKey>(Expression<Func<T, TKey>> keySelector, Direction direction)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.ThenBy is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T> IQueryBuilder<T>.Offset(int count)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Offset is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T> IQueryBuilder<T>.Limit(int count)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Limit is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T> IQueryBuilder<T>.Distinct()
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Distinct is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T> IQueryBuilder<T>.WithTimeout(TimeSpan timeout)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.WithTimeout is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T> IQueryBuilder<T>.GroupBy<TKey>(Expression<Func<T, TKey>> keySelector)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.GroupBy is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T> IQueryBuilder<T>.Having(Expression<Func<T, bool>> predicate)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Having is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder<T, TJoined> IQueryBuilder<T>.Join<TJoined>(Expression<Func<T, TJoined, bool>> condition)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Join is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder<T, TJoined> IQueryBuilder<T>.LeftJoin<TJoined>(Expression<Func<T, TJoined, bool>> condition)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.LeftJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder<T, TJoined> IQueryBuilder<T>.RightJoin<TJoined>(Expression<Func<T, TJoined, bool>> condition)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.RightJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder<T, TJoined> IQueryBuilder<T>.Join<TJoined>(Expression<Func<T, NavigationList<TJoined>>> navigation)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Join is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder<T, TJoined> IQueryBuilder<T>.LeftJoin<TJoined>(Expression<Func<T, NavigationList<TJoined>>> navigation)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.LeftJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    QueryDiagnostics IQueryBuilder<T>.ToDiagnostics()
        => throw new InvalidOperationException("Carrier method IQueryBuilder.ToDiagnostics is not intercepted in this optimized chain. This indicates a code generation bug.");

    PreparedQuery<T> IQueryBuilder<T>.Prepare()
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Prepare is not intercepted in this optimized chain. This indicates a code generation bug.");

    // IQueryBuilder<T, TResult> explicit implementations

    IQueryBuilder<T, TResult> IQueryBuilder<T, TResult>.Where(Expression<Func<T, bool>> predicate)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Where is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T, TResult> IQueryBuilder<T, TResult>.OrderBy<TKey>(Expression<Func<T, TKey>> keySelector, Direction direction)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.OrderBy is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T, TResult> IQueryBuilder<T, TResult>.ThenBy<TKey>(Expression<Func<T, TKey>> keySelector, Direction direction)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.ThenBy is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T, TResult> IQueryBuilder<T, TResult>.Offset(int count)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Offset is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T, TResult> IQueryBuilder<T, TResult>.Limit(int count)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Limit is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T, TResult> IQueryBuilder<T, TResult>.Distinct()
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Distinct is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T, TResult> IQueryBuilder<T, TResult>.WithTimeout(TimeSpan timeout)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.WithTimeout is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T, TResult> IQueryBuilder<T, TResult>.GroupBy<TKey>(Expression<Func<T, TKey>> keySelector)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.GroupBy is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T, TResult> IQueryBuilder<T, TResult>.Having(Expression<Func<T, bool>> predicate)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Having is not intercepted in this optimized chain. This indicates a code generation bug.");

    Task<List<TResult>> IQueryBuilder<T, TResult>.ExecuteFetchAllAsync(CancellationToken cancellationToken)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.ExecuteFetchAllAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    Task<TResult> IQueryBuilder<T, TResult>.ExecuteFetchFirstAsync(CancellationToken cancellationToken)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.ExecuteFetchFirstAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    Task<TResult?> IQueryBuilder<T, TResult>.ExecuteFetchFirstOrDefaultAsync(CancellationToken cancellationToken)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.ExecuteFetchFirstOrDefaultAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    Task<TResult> IQueryBuilder<T, TResult>.ExecuteFetchSingleAsync(CancellationToken cancellationToken)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.ExecuteFetchSingleAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    Task<TScalar> IQueryBuilder<T, TResult>.ExecuteScalarAsync<TScalar>(CancellationToken cancellationToken)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.ExecuteScalarAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    IAsyncEnumerable<TResult> IQueryBuilder<T, TResult>.ToAsyncEnumerable(CancellationToken cancellationToken)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.ToAsyncEnumerable is not intercepted in this optimized chain. This indicates a code generation bug.");

    QueryDiagnostics IQueryBuilder<T, TResult>.ToDiagnostics()
        => throw new InvalidOperationException("Carrier method IQueryBuilder.ToDiagnostics is not intercepted in this optimized chain. This indicates a code generation bug.");

    PreparedQuery<TResult> IQueryBuilder<T, TResult>.Prepare()
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Prepare is not intercepted in this optimized chain. This indicates a code generation bug.");
}
