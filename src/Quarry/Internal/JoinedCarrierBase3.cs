using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace Quarry.Internal;

public abstract class JoinedCarrierBase3<T1, T2, T3> : IEntityAccessor<T1>, IQueryBuilder<T1>, IJoinedQueryBuilder<T1, T2>, IJoinedQueryBuilder3<T1, T2, T3>
    where T1 : class
    where T2 : class
    where T3 : class
{
    public IQueryExecutionContext? Ctx;

    // ── IEntityAccessor<T1> stubs ──

    IQueryBuilder<T1> IEntityAccessor<T1>.Where(Expression<Func<T1, bool>> predicate)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Where is not intercepted in this optimized chain. This indicates a code generation bug.");
    IQueryBuilder<T1, TResult> IEntityAccessor<T1>.Select<TResult>(Func<T1, TResult> selector)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Select is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder<T1, TJoined> IEntityAccessor<T1>.Join<TJoined>(Expression<Func<T1, TJoined, bool>> condition)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Join is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder<T1, TJoined> IEntityAccessor<T1>.LeftJoin<TJoined>(Expression<Func<T1, TJoined, bool>> condition)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.LeftJoin is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder<T1, TJoined> IEntityAccessor<T1>.RightJoin<TJoined>(Expression<Func<T1, TJoined, bool>> condition)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.RightJoin is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder<T1, TJoined> IEntityAccessor<T1>.Join<TJoined>(Expression<Func<T1, NavigationList<TJoined>>> navigation)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Join is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder<T1, TJoined> IEntityAccessor<T1>.LeftJoin<TJoined>(Expression<Func<T1, NavigationList<TJoined>>> navigation)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.LeftJoin is not intercepted in this optimized chain. This indicates a code generation bug.");
    IQueryBuilder<T1> IEntityAccessor<T1>.Distinct()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Distinct is not intercepted in this optimized chain. This indicates a code generation bug.");
    IQueryBuilder<T1> IEntityAccessor<T1>.WithTimeout(TimeSpan timeout)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.WithTimeout is not intercepted in this optimized chain. This indicates a code generation bug.");
    string IEntityAccessor<T1>.ToSql()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.ToSql is not intercepted in this optimized chain. This indicates a code generation bug.");
    IDeleteBuilder<T1> IEntityAccessor<T1>.Delete()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Delete is not intercepted in this optimized chain. This indicates a code generation bug.");
    IUpdateBuilder<T1> IEntityAccessor<T1>.Update()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Update is not intercepted in this optimized chain. This indicates a code generation bug.");
    IInsertBuilder<T1> IEntityAccessor<T1>.Insert(T1 entity)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Insert is not intercepted in this optimized chain. This indicates a code generation bug.");
    IInsertBuilder<T1> IEntityAccessor<T1>.InsertMany(IEnumerable<T1> entities)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.InsertMany is not intercepted in this optimized chain. This indicates a code generation bug.");

    QueryPlan IEntityAccessor<T1>.ToQueryPlan()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.ToQueryPlan is not intercepted in this optimized chain. This indicates a code generation bug.");

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

    IJoinedQueryBuilder3<T1, T2, T3x> IJoinedQueryBuilder<T1, T2>.Join<T3x>(Expression<Func<T1, T2, T3x, bool>> condition)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.Join is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder3<T1, T2, T3x> IJoinedQueryBuilder<T1, T2>.LeftJoin<T3x>(Expression<Func<T1, T2, T3x, bool>> condition)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.LeftJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder3<T1, T2, T3x> IJoinedQueryBuilder<T1, T2>.RightJoin<T3x>(Expression<Func<T1, T2, T3x, bool>> condition)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.RightJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    string IJoinedQueryBuilder<T1, T2>.ToSql()
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.ToSql is not intercepted in this optimized chain. This indicates a code generation bug.");

    // IJoinedQueryBuilder3<T1, T2, T3> explicit implementations

    IJoinedQueryBuilder3<T1, T2, T3, TResult> IJoinedQueryBuilder3<T1, T2, T3>.Select<TResult>(Func<T1, T2, T3, TResult> selector)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.Select is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder3<T1, T2, T3> IJoinedQueryBuilder3<T1, T2, T3>.Where(Expression<Func<T1, T2, T3, bool>> predicate)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.Where is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder3<T1, T2, T3> IJoinedQueryBuilder3<T1, T2, T3>.OrderBy<TKey>(Expression<Func<T1, T2, T3, TKey>> keySelector, Direction direction)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.OrderBy is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder3<T1, T2, T3> IJoinedQueryBuilder3<T1, T2, T3>.ThenBy<TKey>(Expression<Func<T1, T2, T3, TKey>> keySelector, Direction direction)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.ThenBy is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder3<T1, T2, T3> IJoinedQueryBuilder3<T1, T2, T3>.Offset(int count)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.Offset is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder3<T1, T2, T3> IJoinedQueryBuilder3<T1, T2, T3>.Limit(int count)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.Limit is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder3<T1, T2, T3> IJoinedQueryBuilder3<T1, T2, T3>.Distinct()
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.Distinct is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder4<T1, T2, T3, T4> IJoinedQueryBuilder3<T1, T2, T3>.Join<T4>(Expression<Func<T1, T2, T3, T4, bool>> condition)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.Join is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder4<T1, T2, T3, T4> IJoinedQueryBuilder3<T1, T2, T3>.LeftJoin<T4>(Expression<Func<T1, T2, T3, T4, bool>> condition)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.LeftJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder4<T1, T2, T3, T4> IJoinedQueryBuilder3<T1, T2, T3>.RightJoin<T4>(Expression<Func<T1, T2, T3, T4, bool>> condition)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.RightJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    string IJoinedQueryBuilder3<T1, T2, T3>.ToSql()
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.ToSql is not intercepted in this optimized chain. This indicates a code generation bug.");
}

public abstract class JoinedCarrierBase3<T1, T2, T3, TResult> : IEntityAccessor<T1>, IQueryBuilder<T1>, IJoinedQueryBuilder<T1, T2>, IJoinedQueryBuilder3<T1, T2, T3>, IJoinedQueryBuilder3<T1, T2, T3, TResult>
    where T1 : class
    where T2 : class
    where T3 : class
{
    public IQueryExecutionContext? Ctx;

    // ── IEntityAccessor<T1> stubs ──

    IQueryBuilder<T1> IEntityAccessor<T1>.Where(Expression<Func<T1, bool>> predicate)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Where is not intercepted in this optimized chain. This indicates a code generation bug.");
    IQueryBuilder<T1, TResult1> IEntityAccessor<T1>.Select<TResult1>(Func<T1, TResult1> selector)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Select is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder<T1, TJoined> IEntityAccessor<T1>.Join<TJoined>(Expression<Func<T1, TJoined, bool>> condition)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Join is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder<T1, TJoined> IEntityAccessor<T1>.LeftJoin<TJoined>(Expression<Func<T1, TJoined, bool>> condition)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.LeftJoin is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder<T1, TJoined> IEntityAccessor<T1>.RightJoin<TJoined>(Expression<Func<T1, TJoined, bool>> condition)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.RightJoin is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder<T1, TJoined> IEntityAccessor<T1>.Join<TJoined>(Expression<Func<T1, NavigationList<TJoined>>> navigation)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Join is not intercepted in this optimized chain. This indicates a code generation bug.");
    IJoinedQueryBuilder<T1, TJoined> IEntityAccessor<T1>.LeftJoin<TJoined>(Expression<Func<T1, NavigationList<TJoined>>> navigation)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.LeftJoin is not intercepted in this optimized chain. This indicates a code generation bug.");
    IQueryBuilder<T1> IEntityAccessor<T1>.Distinct()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Distinct is not intercepted in this optimized chain. This indicates a code generation bug.");
    IQueryBuilder<T1> IEntityAccessor<T1>.WithTimeout(TimeSpan timeout)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.WithTimeout is not intercepted in this optimized chain. This indicates a code generation bug.");
    string IEntityAccessor<T1>.ToSql()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.ToSql is not intercepted in this optimized chain. This indicates a code generation bug.");
    IDeleteBuilder<T1> IEntityAccessor<T1>.Delete()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Delete is not intercepted in this optimized chain. This indicates a code generation bug.");
    IUpdateBuilder<T1> IEntityAccessor<T1>.Update()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Update is not intercepted in this optimized chain. This indicates a code generation bug.");
    IInsertBuilder<T1> IEntityAccessor<T1>.Insert(T1 entity)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.Insert is not intercepted in this optimized chain. This indicates a code generation bug.");
    IInsertBuilder<T1> IEntityAccessor<T1>.InsertMany(IEnumerable<T1> entities)
        => throw new InvalidOperationException("Carrier method IEntityAccessor.InsertMany is not intercepted in this optimized chain. This indicates a code generation bug.");

    QueryPlan IEntityAccessor<T1>.ToQueryPlan()
        => throw new InvalidOperationException("Carrier method IEntityAccessor.ToQueryPlan is not intercepted in this optimized chain. This indicates a code generation bug.");

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

    IJoinedQueryBuilder3<T1, T2, T3x> IJoinedQueryBuilder<T1, T2>.Join<T3x>(Expression<Func<T1, T2, T3x, bool>> condition)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.Join is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder3<T1, T2, T3x> IJoinedQueryBuilder<T1, T2>.LeftJoin<T3x>(Expression<Func<T1, T2, T3x, bool>> condition)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.LeftJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder3<T1, T2, T3x> IJoinedQueryBuilder<T1, T2>.RightJoin<T3x>(Expression<Func<T1, T2, T3x, bool>> condition)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.RightJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    string IJoinedQueryBuilder<T1, T2>.ToSql()
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder.ToSql is not intercepted in this optimized chain. This indicates a code generation bug.");

    // IJoinedQueryBuilder3<T1, T2, T3> explicit implementations

    IJoinedQueryBuilder3<T1, T2, T3, TResult1> IJoinedQueryBuilder3<T1, T2, T3>.Select<TResult1>(Func<T1, T2, T3, TResult1> selector)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.Select is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder3<T1, T2, T3> IJoinedQueryBuilder3<T1, T2, T3>.Where(Expression<Func<T1, T2, T3, bool>> predicate)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.Where is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder3<T1, T2, T3> IJoinedQueryBuilder3<T1, T2, T3>.OrderBy<TKey>(Expression<Func<T1, T2, T3, TKey>> keySelector, Direction direction)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.OrderBy is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder3<T1, T2, T3> IJoinedQueryBuilder3<T1, T2, T3>.ThenBy<TKey>(Expression<Func<T1, T2, T3, TKey>> keySelector, Direction direction)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.ThenBy is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder3<T1, T2, T3> IJoinedQueryBuilder3<T1, T2, T3>.Offset(int count)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.Offset is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder3<T1, T2, T3> IJoinedQueryBuilder3<T1, T2, T3>.Limit(int count)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.Limit is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder3<T1, T2, T3> IJoinedQueryBuilder3<T1, T2, T3>.Distinct()
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.Distinct is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder4<T1, T2, T3, T4> IJoinedQueryBuilder3<T1, T2, T3>.Join<T4>(Expression<Func<T1, T2, T3, T4, bool>> condition)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.Join is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder4<T1, T2, T3, T4> IJoinedQueryBuilder3<T1, T2, T3>.LeftJoin<T4>(Expression<Func<T1, T2, T3, T4, bool>> condition)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.LeftJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder4<T1, T2, T3, T4> IJoinedQueryBuilder3<T1, T2, T3>.RightJoin<T4>(Expression<Func<T1, T2, T3, T4, bool>> condition)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.RightJoin is not intercepted in this optimized chain. This indicates a code generation bug.");

    string IJoinedQueryBuilder3<T1, T2, T3>.ToSql()
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.ToSql is not intercepted in this optimized chain. This indicates a code generation bug.");

    // IJoinedQueryBuilder3<T1, T2, T3, TResult> explicit implementations

    IJoinedQueryBuilder3<T1, T2, T3, TResult> IJoinedQueryBuilder3<T1, T2, T3, TResult>.Where(Expression<Func<T1, T2, T3, bool>> predicate)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.Where is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder3<T1, T2, T3, TResult> IJoinedQueryBuilder3<T1, T2, T3, TResult>.OrderBy<TKey>(Expression<Func<T1, T2, T3, TKey>> keySelector, Direction direction)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.OrderBy is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder3<T1, T2, T3, TResult> IJoinedQueryBuilder3<T1, T2, T3, TResult>.ThenBy<TKey>(Expression<Func<T1, T2, T3, TKey>> keySelector, Direction direction)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.ThenBy is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder3<T1, T2, T3, TResult> IJoinedQueryBuilder3<T1, T2, T3, TResult>.Offset(int count)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.Offset is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder3<T1, T2, T3, TResult> IJoinedQueryBuilder3<T1, T2, T3, TResult>.Limit(int count)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.Limit is not intercepted in this optimized chain. This indicates a code generation bug.");

    IJoinedQueryBuilder3<T1, T2, T3, TResult> IJoinedQueryBuilder3<T1, T2, T3, TResult>.Distinct()
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.Distinct is not intercepted in this optimized chain. This indicates a code generation bug.");

    Task<List<TResult>> IJoinedQueryBuilder3<T1, T2, T3, TResult>.ExecuteFetchAllAsync(CancellationToken cancellationToken)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.ExecuteFetchAllAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    Task<TResult> IJoinedQueryBuilder3<T1, T2, T3, TResult>.ExecuteFetchFirstAsync(CancellationToken cancellationToken)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.ExecuteFetchFirstAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    Task<TResult?> IJoinedQueryBuilder3<T1, T2, T3, TResult>.ExecuteFetchFirstOrDefaultAsync(CancellationToken cancellationToken)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.ExecuteFetchFirstOrDefaultAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    Task<TResult> IJoinedQueryBuilder3<T1, T2, T3, TResult>.ExecuteFetchSingleAsync(CancellationToken cancellationToken)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.ExecuteFetchSingleAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    IAsyncEnumerable<TResult> IJoinedQueryBuilder3<T1, T2, T3, TResult>.ToAsyncEnumerable(CancellationToken cancellationToken)
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.ToAsyncEnumerable is not intercepted in this optimized chain. This indicates a code generation bug.");

    string IJoinedQueryBuilder3<T1, T2, T3, TResult>.ToSql()
        => throw new InvalidOperationException("Carrier method IJoinedQueryBuilder3.ToSql is not intercepted in this optimized chain. This indicates a code generation bug.");
}
