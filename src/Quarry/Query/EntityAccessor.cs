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

    public IQueryBuilder<T> CreateQueryBuilder()
        => QueryBuilder<T>.Create(_dialect, _tableName, _schemaName, _ctx);

    public IQueryBuilder<T> Where(Expression<Func<T, bool>> predicate)
        => CreateQueryBuilder().Where(predicate);

    public IQueryBuilder<T, TResult> Select<TResult>(Func<T, TResult> selector)
        => CreateQueryBuilder().Select(selector);

    public IJoinedQueryBuilder<T, TJoined> Join<TJoined>(Expression<Func<T, TJoined, bool>> condition) where TJoined : class
        => CreateQueryBuilder().Join(condition);

    public IJoinedQueryBuilder<T, TJoined> LeftJoin<TJoined>(Expression<Func<T, TJoined, bool>> condition) where TJoined : class
        => CreateQueryBuilder().LeftJoin(condition);

    public IJoinedQueryBuilder<T, TJoined> RightJoin<TJoined>(Expression<Func<T, TJoined, bool>> condition) where TJoined : class
        => CreateQueryBuilder().RightJoin(condition);

    public IJoinedQueryBuilder<T, TJoined> Join<TJoined>(Expression<Func<T, NavigationList<TJoined>>> navigation) where TJoined : class
        => CreateQueryBuilder().Join(navigation);

    public IJoinedQueryBuilder<T, TJoined> LeftJoin<TJoined>(Expression<Func<T, NavigationList<TJoined>>> navigation) where TJoined : class
        => CreateQueryBuilder().LeftJoin(navigation);

    public IQueryBuilder<T> Distinct()
        => CreateQueryBuilder().Distinct();

    public IQueryBuilder<T> WithTimeout(TimeSpan timeout)
        => CreateQueryBuilder().WithTimeout(timeout);

    public string ToSql()
        => CreateQueryBuilder().ToSql();

    public QueryDiagnostics ToDiagnostics()
        => CreateQueryBuilder().ToDiagnostics();

    // ── Modification entry points ──

    public IDeleteBuilder<T> Delete()
        => new DeleteBuilder<T>(_dialect, _tableName, _schemaName, _ctx);

    public IUpdateBuilder<T> Update()
        => new UpdateBuilder<T>(_dialect, _tableName, _schemaName, _ctx);

    public IInsertBuilder<T> Insert(T entity)
        => new InsertBuilder<T>(_dialect, _tableName, _schemaName, _ctx, entity);

    public IBatchInsertBuilder<T> InsertBatch<TColumns>(Func<T, TColumns> columnSelector)
        => throw new InvalidOperationException(
            "Batch insert with column selector requires source generation. " +
            "Ensure the Quarry source generator is referenced in your project.");

    public QueryPlan ToQueryPlan()
        => new QueryPlan(CreateQueryBuilder().ToSql(), QueryPlanTier.RuntimeBuild, _dialect);
}
