using System.Linq.Expressions;
using Quarry.Internal;

namespace Quarry;

/// <summary>
/// Readonly struct implementation of <see cref="IEntityAccessor{T}"/> for the runtime fallback path.
/// Holds only the 4 values needed to create any builder. Zero heap allocation until
/// a chain-starting method is called, which creates the appropriate concrete builder on demand.
/// </summary>
/// <typeparam name="T">The entity type.</typeparam>
public readonly struct EntityAccessor<T> : IEntityAccessor<T> where T : class
{
    private readonly SqlDialect _dialect;
    private readonly string _tableName;
    private readonly string? _schemaName;
    private readonly IQueryExecutionContext? _ctx;

    public EntityAccessor(SqlDialect dialect, string tableName, string? schemaName, IQueryExecutionContext? ctx)
    {
        _dialect = dialect;
        _tableName = tableName;
        _schemaName = schemaName;
        _ctx = ctx;
    }

    // ── Query chain starters ──

    private IQueryBuilder<T> CreateQueryBuilder()
        => QueryBuilder<T>.Create(_dialect, _tableName, _schemaName, _ctx);

    IQueryBuilder<T> IEntityAccessor<T>.Where(Expression<Func<T, bool>> predicate)
        => CreateQueryBuilder().Where(predicate);

    IQueryBuilder<T, TResult> IEntityAccessor<T>.Select<TResult>(Func<T, TResult> selector)
        => CreateQueryBuilder().Select(selector);

    IJoinedQueryBuilder<T, TJoined> IEntityAccessor<T>.Join<TJoined>(Expression<Func<T, TJoined, bool>> condition)
        => CreateQueryBuilder().Join(condition);

    IJoinedQueryBuilder<T, TJoined> IEntityAccessor<T>.LeftJoin<TJoined>(Expression<Func<T, TJoined, bool>> condition)
        => CreateQueryBuilder().LeftJoin(condition);

    IJoinedQueryBuilder<T, TJoined> IEntityAccessor<T>.RightJoin<TJoined>(Expression<Func<T, TJoined, bool>> condition)
        => CreateQueryBuilder().RightJoin(condition);

    IJoinedQueryBuilder<T, TJoined> IEntityAccessor<T>.Join<TJoined>(Expression<Func<T, NavigationList<TJoined>>> navigation)
        => CreateQueryBuilder().Join(navigation);

    IJoinedQueryBuilder<T, TJoined> IEntityAccessor<T>.LeftJoin<TJoined>(Expression<Func<T, NavigationList<TJoined>>> navigation)
        => CreateQueryBuilder().LeftJoin(navigation);

    IQueryBuilder<T> IEntityAccessor<T>.Distinct()
        => CreateQueryBuilder().Distinct();

    IQueryBuilder<T> IEntityAccessor<T>.WithTimeout(TimeSpan timeout)
        => CreateQueryBuilder().WithTimeout(timeout);

    string IEntityAccessor<T>.ToSql()
        => throw new InvalidOperationException("ToSql requires a Select clause. Use .Select(u => u).ToSql() instead.");

    // ── Modification entry points ──

    IDeleteBuilder<T> IEntityAccessor<T>.Delete()
        => new DeleteBuilder<T>(_dialect, _tableName, _schemaName, _ctx);

    IUpdateBuilder<T> IEntityAccessor<T>.Update()
        => new UpdateBuilder<T>(_dialect, _tableName, _schemaName, _ctx);

    IInsertBuilder<T> IEntityAccessor<T>.Insert(T entity)
        => new InsertBuilder<T>(_dialect, _tableName, _schemaName, _ctx, entity);

    IInsertBuilder<T> IEntityAccessor<T>.InsertMany(IEnumerable<T> entities)
    {
        var builder = new InsertBuilder<T>(_dialect, _tableName, _schemaName, _ctx);
        foreach (var entity in entities)
            builder.Values(entity);
        return builder;
    }
}
