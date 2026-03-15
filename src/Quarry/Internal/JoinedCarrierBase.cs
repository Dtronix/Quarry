using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace Quarry.Internal;

public abstract class JoinedCarrierBase<T1, T2> : IQueryBuilder<T1>, IJoinedQueryBuilder<T1, T2>
    where T1 : class
    where T2 : class
{
    public IQueryExecutionContext? Ctx;

    // IQueryBuilder<T1> explicit implementations

    IQueryBuilder<T1> IQueryBuilder<T1>.Where(Expression<Func<T1, bool>> predicate)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Where is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T1, TResult> IQueryBuilder<T1>.Select<TResult>(Func<T1, TResult> selector)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Select is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T1> IQueryBuilder<T1>.OrderBy<TKey>(Expression<Func<T1, TKey>> keySelector, Direction direction)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.OrderBy is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T1> IQueryBuilder<T1>.ThenBy<TKey>(Expression<Func<T1, TKey>> keySelector, Direction direction)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.ThenBy is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T1> IQueryBuilder<T1>.Offset(int count)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Offset is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T1> IQueryBuilder<T1>.Limit(int count)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Limit is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T1> IQueryBuilder<T1>.Distinct()
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Distinct is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T1> IQueryBuilder<T1>.WithTimeout(TimeSpan timeout)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.WithTimeout is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T1> IQueryBuilder<T1>.GroupBy<TKey>(Expression<Func<T1, TKey>> keySelector)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.GroupBy is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T1> IQueryBuilder<T1>.Having(Expression<Func<T1, bool>> predicate)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Having is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder<T1, TJoined> IQueryBuilder<T1>.Join<TJoined>(Expression<Func<T1, TJoined, bool>> condition)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Join is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder<T1, TJoined> IQueryBuilder<T1>.LeftJoin<TJoined>(Expression<Func<T1, TJoined, bool>> condition)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.LeftJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder<T1, TJoined> IQueryBuilder<T1>.RightJoin<TJoined>(Expression<Func<T1, TJoined, bool>> condition)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.RightJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder<T1, TJoined> IQueryBuilder<T1>.Join<TJoined>(Expression<Func<T1, NavigationList<TJoined>>> navigation)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Join is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder<T1, TJoined> IQueryBuilder<T1>.LeftJoin<TJoined>(Expression<Func<T1, NavigationList<TJoined>>> navigation)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.LeftJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    string IQueryBuilder<T1>.ToSql()
        => throw new InvalidOperationException("Carrier method IQueryBuilder.ToSql is not intercepted in this optimized chain. This indicates a code generation bug.");

    // IJoinedQueryBuilder<T1, T2> explicit implementations

    IJoinedQueryBuilder<T1, T2, TResult> IJoinedQueryBuilder<T1, T2>.Select<TResult>(Func<T1, T2, TResult> selector)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.Select is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder<T1, T2> IJoinedQueryBuilder<T1, T2>.Where(Expression<Func<T1, T2, bool>> predicate)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.Where is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder<T1, T2> IJoinedQueryBuilder<T1, T2>.OrderBy<TKey>(Expression<Func<T1, T2, TKey>> keySelector, Direction direction)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.OrderBy is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder<T1, T2> IJoinedQueryBuilder<T1, T2>.ThenBy<TKey>(Expression<Func<T1, T2, TKey>> keySelector, Direction direction)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.ThenBy is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder<T1, T2> IJoinedQueryBuilder<T1, T2>.Offset(int count)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.Offset is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder<T1, T2> IJoinedQueryBuilder<T1, T2>.Limit(int count)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.Limit is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder<T1, T2> IJoinedQueryBuilder<T1, T2>.Distinct()
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.Distinct is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder3<T1, T2, T3> IJoinedQueryBuilder<T1, T2>.Join<T3>(Expression<Func<T1, T2, T3, bool>> condition)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.Join is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder3<T1, T2, T3> IJoinedQueryBuilder<T1, T2>.LeftJoin<T3>(Expression<Func<T1, T2, T3, bool>> condition)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.LeftJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder3<T1, T2, T3> IJoinedQueryBuilder<T1, T2>.RightJoin<T3>(Expression<Func<T1, T2, T3, bool>> condition)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.RightJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    string IJoinedQueryBuilder<T1, T2>.ToSql()
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.ToSql is not intercepted in this optimized chain. This indicates a code generation bug.");
}

public abstract class JoinedCarrierBase<T1, T2, TResult> : IQueryBuilder<T1>, IJoinedQueryBuilder<T1, T2>, IJoinedQueryBuilder<T1, T2, TResult>
    where T1 : class
    where T2 : class
{
    public IQueryExecutionContext? Ctx;

    // IQueryBuilder<T1> explicit implementations

    IQueryBuilder<T1> IQueryBuilder<T1>.Where(Expression<Func<T1, bool>> predicate)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Where is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T1, TResult1> IQueryBuilder<T1>.Select<TResult1>(Func<T1, TResult1> selector)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Select is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T1> IQueryBuilder<T1>.OrderBy<TKey>(Expression<Func<T1, TKey>> keySelector, Direction direction)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.OrderBy is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T1> IQueryBuilder<T1>.ThenBy<TKey>(Expression<Func<T1, TKey>> keySelector, Direction direction)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.ThenBy is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T1> IQueryBuilder<T1>.Offset(int count)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Offset is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T1> IQueryBuilder<T1>.Limit(int count)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Limit is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T1> IQueryBuilder<T1>.Distinct()
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Distinct is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T1> IQueryBuilder<T1>.WithTimeout(TimeSpan timeout)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.WithTimeout is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T1> IQueryBuilder<T1>.GroupBy<TKey>(Expression<Func<T1, TKey>> keySelector)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.GroupBy is not intercepted in this optimized chain. This indicates a code generation bug.");

    IQueryBuilder<T1> IQueryBuilder<T1>.Having(Expression<Func<T1, bool>> predicate)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Having is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder<T1, TJoined> IQueryBuilder<T1>.Join<TJoined>(Expression<Func<T1, TJoined, bool>> condition)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Join is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder<T1, TJoined> IQueryBuilder<T1>.LeftJoin<TJoined>(Expression<Func<T1, TJoined, bool>> condition)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.LeftJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder<T1, TJoined> IQueryBuilder<T1>.RightJoin<TJoined>(Expression<Func<T1, TJoined, bool>> condition)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.RightJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder<T1, TJoined> IQueryBuilder<T1>.Join<TJoined>(Expression<Func<T1, NavigationList<TJoined>>> navigation)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.Join is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder<T1, TJoined> IQueryBuilder<T1>.LeftJoin<TJoined>(Expression<Func<T1, NavigationList<TJoined>>> navigation)
        => throw new InvalidOperationException("Carrier method IQueryBuilder.LeftJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    string IQueryBuilder<T1>.ToSql()
        => throw new InvalidOperationException("Carrier method IQueryBuilder.ToSql is not intercepted in this optimized chain. This indicates a code generation bug.");

    // IJoinedQueryBuilder<T1, T2> explicit implementations

    IJoinedQueryBuilder<T1, T2, TResult1> IJoinedQueryBuilder<T1, T2>.Select<TResult1>(Func<T1, T2, TResult1> selector)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.Select is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder<T1, T2> IJoinedQueryBuilder<T1, T2>.Where(Expression<Func<T1, T2, bool>> predicate)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.Where is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder<T1, T2> IJoinedQueryBuilder<T1, T2>.OrderBy<TKey>(Expression<Func<T1, T2, TKey>> keySelector, Direction direction)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.OrderBy is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder<T1, T2> IJoinedQueryBuilder<T1, T2>.ThenBy<TKey>(Expression<Func<T1, T2, TKey>> keySelector, Direction direction)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.ThenBy is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder<T1, T2> IJoinedQueryBuilder<T1, T2>.Offset(int count)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.Offset is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder<T1, T2> IJoinedQueryBuilder<T1, T2>.Limit(int count)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.Limit is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder<T1, T2> IJoinedQueryBuilder<T1, T2>.Distinct()
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.Distinct is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder3<T1, T2, T3> IJoinedQueryBuilder<T1, T2>.Join<T3>(Expression<Func<T1, T2, T3, bool>> condition)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.Join is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder3<T1, T2, T3> IJoinedQueryBuilder<T1, T2>.LeftJoin<T3>(Expression<Func<T1, T2, T3, bool>> condition)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.LeftJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder3<T1, T2, T3> IJoinedQueryBuilder<T1, T2>.RightJoin<T3>(Expression<Func<T1, T2, T3, bool>> condition)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.RightJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    string IJoinedQueryBuilder<T1, T2>.ToSql()
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.ToSql is not intercepted in this optimized chain. This indicates a code generation bug.");

    // IJoinedQueryBuilder<T1, T2, TResult> explicit implementations

    IJoinedQueryBuilder<T1, T2, TResult> IJoinedQueryBuilder<T1, T2, TResult>.Where(Expression<Func<T1, T2, bool>> predicate)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.Where is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder<T1, T2, TResult> IJoinedQueryBuilder<T1, T2, TResult>.OrderBy<TKey>(Expression<Func<T1, T2, TKey>> keySelector, Direction direction)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.OrderBy is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder<T1, T2, TResult> IJoinedQueryBuilder<T1, T2, TResult>.ThenBy<TKey>(Expression<Func<T1, T2, TKey>> keySelector, Direction direction)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.ThenBy is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder<T1, T2, TResult> IJoinedQueryBuilder<T1, T2, TResult>.Offset(int count)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.Offset is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder<T1, T2, TResult> IJoinedQueryBuilder<T1, T2, TResult>.Limit(int count)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.Limit is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder<T1, T2, TResult> IJoinedQueryBuilder<T1, T2, TResult>.Distinct()
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.Distinct is not intercepted in this optimized chain. This indicates a code generation bug.");

    Task<List<TResult>> IJoinedQueryBuilder<T1, T2, TResult>.ExecuteFetchAllAsync(CancellationToken cancellationToken)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.ExecuteFetchAllAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    Task<TResult> IJoinedQueryBuilder<T1, T2, TResult>.ExecuteFetchFirstAsync(CancellationToken cancellationToken)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.ExecuteFetchFirstAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    Task<TResult?> IJoinedQueryBuilder<T1, T2, TResult>.ExecuteFetchFirstOrDefaultAsync(CancellationToken cancellationToken)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.ExecuteFetchFirstOrDefaultAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    Task<TResult> IJoinedQueryBuilder<T1, T2, TResult>.ExecuteFetchSingleAsync(CancellationToken cancellationToken)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.ExecuteFetchSingleAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    IAsyncEnumerable<TResult> IJoinedQueryBuilder<T1, T2, TResult>.ToAsyncEnumerable(CancellationToken cancellationToken)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.ToAsyncEnumerable is not intercepted in this optimized chain. This indicates a code generation bug.");

    string IJoinedQueryBuilder<T1, T2, TResult>.ToSql()
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.ToSql is not intercepted in this optimized chain. This indicates a code generation bug.");
}
