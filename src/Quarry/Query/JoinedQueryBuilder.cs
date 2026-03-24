using System.Data.Common;
using System.Linq.Expressions;
using Quarry.Internal;

namespace Quarry;

/// <summary>
/// Builder for constructing SELECT queries that involve a JOIN between two tables.
/// </summary>
public sealed class JoinedQueryBuilder<T1, T2> : IJoinedQueryBuilder<T1, T2>
    where T1 : class
    where T2 : class
{
    private readonly QueryState _state;

    internal JoinedQueryBuilder(QueryState state)
    {
        _state = state;
    }

    public IJoinedQueryBuilder<T1, T2, TResult> Select<TResult>(Func<T1, T2, TResult> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new JoinedQueryBuilder<T1, T2, TResult>(_state, selector);
    }

    public IJoinedQueryBuilder<T1, T2> Where(Expression<Func<T1, T2, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new JoinedQueryBuilder<T1, T2>(_state);
    }

    public IJoinedQueryBuilder<T1, T2> OrderBy<TKey>(Expression<Func<T1, T2, TKey>> keySelector, Direction direction = Direction.Ascending)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        return new JoinedQueryBuilder<T1, T2>(_state);
    }

    public IJoinedQueryBuilder<T1, T2> ThenBy<TKey>(Expression<Func<T1, T2, TKey>> keySelector, Direction direction = Direction.Ascending)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        return new JoinedQueryBuilder<T1, T2>(_state);
    }

    public IJoinedQueryBuilder<T1, T2> Offset(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "Offset cannot be negative.");
        return new JoinedQueryBuilder<T1, T2>(_state.WithOffset(count));
    }

    public IJoinedQueryBuilder<T1, T2> Limit(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "Limit cannot be negative.");
        return new JoinedQueryBuilder<T1, T2>(_state.WithLimit(count));
    }

    public IJoinedQueryBuilder<T1, T2> Distinct()
        => new JoinedQueryBuilder<T1, T2>(_state.WithDistinct());

    public QueryDiagnostics ToDiagnostics() => new(
        SqlBuilder.BuildSelectSql(_state),
        DiagnosticsHelper.ConvertParameters(_state.Parameters),
        DiagnosticQueryKind.Select,
        _state.Dialect,
        _state.TableName,
        rawState: _state);

    /// <inheritdoc />
    public PreparedQuery<(T1, T2)> Prepare()
        => throw new NotSupportedException("Prepare() must be intercepted by the Quarry source generator.");

    internal QueryState State => _state;

    #region Join Chaining (2-table → 3-table)

    public IJoinedQueryBuilder3<T1, T2, T3> Join<T3>(Expression<Func<T1, T2, T3, bool>> condition) where T3 : class
    {
        ArgumentNullException.ThrowIfNull(condition);
        return new JoinedQueryBuilder3<T1, T2, T3>(_state);
    }

    public IJoinedQueryBuilder3<T1, T2, T3> LeftJoin<T3>(Expression<Func<T1, T2, T3, bool>> condition) where T3 : class
    {
        ArgumentNullException.ThrowIfNull(condition);
        return new JoinedQueryBuilder3<T1, T2, T3>(_state);
    }

    public IJoinedQueryBuilder3<T1, T2, T3> RightJoin<T3>(Expression<Func<T1, T2, T3, bool>> condition) where T3 : class
    {
        ArgumentNullException.ThrowIfNull(condition);
        return new JoinedQueryBuilder3<T1, T2, T3>(_state);
    }

    #endregion

    #region Internal Methods for Interceptors

    public JoinedQueryBuilder<T1, T2> AddWhereClause(string sql)
        => new(_state.WithWhere(sql));

    public JoinedQueryBuilder<T1, T2> AddWhereClause(string sql, params object?[] paramValues)
        => new(_state.WithWhereAndParameters(sql, paramValues));

    public JoinedQueryBuilder<T1, T2> AddOrderByClause(string sql, Direction direction)
        => new(_state.WithOrderBy(sql, direction));

    public JoinedQueryBuilder<T1, T2> AddThenByClause(string sql, Direction direction)
        => new(_state.WithOrderBy(sql, direction));

    public JoinedQueryBuilder<T1, T2, TResult> AddSelectClause<TResult>(string[] columnNames, Func<DbDataReader, TResult> reader)
    {
        var columns = System.Collections.Immutable.ImmutableArray.Create(columnNames);
        var newState = _state.WithSelect(columns);
        return new JoinedQueryBuilder<T1, T2, TResult>(newState, null!, reader);
    }

    public JoinedQueryBuilder3<T1, T2, T3> AddJoinClause<T3>(JoinKind kind, string tableName, string onConditionSql) where T3 : class
    {
        var joinClause = new JoinClause(kind, tableName, null, null, onConditionSql);
        var newState = _state.WithJoin(joinClause);
        return new JoinedQueryBuilder3<T1, T2, T3>(newState);
    }

    public ulong ClauseMask => _state.ClauseMask;

    public JoinedQueryBuilder<T1, T2> WithClauseBit(int bit)
        => new(_state.WithClauseBit(bit));

    #region Prebuilt Parameter Binding (Generated Code API)

    internal object?[]? PrebuiltParams;
    internal int PrebuiltParamIndex;

    public void AllocatePrebuiltParams(int count)
    {
        PrebuiltParams = new object?[count];
        PrebuiltParamIndex = 0;
    }

    public JoinedQueryBuilder<T1, T2> BindParam(object? value)
    {
        PrebuiltParams![PrebuiltParamIndex++] = value;
        return this;
    }

    public JoinedQueryBuilder<T1, T2> SetClauseBit(int bit)
    {
        _state.SetClauseBitMutable(bit);
        return this;
    }

    public JoinedQueryBuilder<T1, T2, TResult> AsProjected<TResult>()
    {
        var projected = new JoinedQueryBuilder<T1, T2, TResult>(_state, null!, null);
        projected.PrebuiltParams = PrebuiltParams;
        projected.PrebuiltParamIndex = PrebuiltParamIndex;
        return projected;
    }

    /// <summary>
    /// Creates a 3-table joined builder for the prebuilt path, transferring the PrebuiltParams array.
    /// Performs only a type conversion without modifying state (no JoinClause/alias mutation).
    /// </summary>
    public JoinedQueryBuilder3<T1, T2, T3> AsJoined<T3>() where T3 : class
    {
        var joined = new JoinedQueryBuilder3<T1, T2, T3>(_state);
        joined.PrebuiltParams = PrebuiltParams;
        joined.PrebuiltParamIndex = PrebuiltParamIndex;
        return joined;
    }

    #endregion

    #endregion
}

/// <summary>
/// Builder for constructing SELECT queries with a two-table join and a specified projection.
/// </summary>
public sealed class JoinedQueryBuilder<T1, T2, TResult> : IJoinedQueryBuilder<T1, T2, TResult>
    where T1 : class
    where T2 : class
{
    private readonly QueryState _state;
    private readonly Func<T1, T2, TResult> _selector;
    private readonly Func<DbDataReader, TResult>? _reader;

    internal JoinedQueryBuilder(QueryState state, Func<T1, T2, TResult> selector)
    {
        _state = state;
        _selector = selector;
        _reader = null;
    }

    internal JoinedQueryBuilder(QueryState state, Func<T1, T2, TResult> selector, Func<DbDataReader, TResult>? reader)
    {
        _state = state;
        _selector = selector;
        _reader = reader;
    }

    public IJoinedQueryBuilder<T1, T2, TResult> Where(Expression<Func<T1, T2, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new JoinedQueryBuilder<T1, T2, TResult>(_state, _selector, _reader);
    }

    public IJoinedQueryBuilder<T1, T2, TResult> OrderBy<TKey>(Expression<Func<T1, T2, TKey>> keySelector, Direction direction = Direction.Ascending)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        return new JoinedQueryBuilder<T1, T2, TResult>(_state, _selector, _reader);
    }

    public IJoinedQueryBuilder<T1, T2, TResult> ThenBy<TKey>(Expression<Func<T1, T2, TKey>> keySelector, Direction direction = Direction.Ascending)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        return new JoinedQueryBuilder<T1, T2, TResult>(_state, _selector, _reader);
    }

    public IJoinedQueryBuilder<T1, T2, TResult> Offset(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "Offset cannot be negative.");
        return new JoinedQueryBuilder<T1, T2, TResult>(_state.WithOffset(count), _selector, _reader);
    }

    public IJoinedQueryBuilder<T1, T2, TResult> Limit(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "Limit cannot be negative.");
        return new JoinedQueryBuilder<T1, T2, TResult>(_state.WithLimit(count), _selector, _reader);
    }

    public IJoinedQueryBuilder<T1, T2, TResult> Distinct()
        => new JoinedQueryBuilder<T1, T2, TResult>(_state.WithDistinct(), _selector, _reader);

    public QueryDiagnostics ToDiagnostics() => new(
        SqlBuilder.BuildSelectSql(_state),
        DiagnosticsHelper.ConvertParameters(_state.Parameters),
        DiagnosticQueryKind.Select,
        _state.Dialect,
        _state.TableName,
        rawState: _state);

    /// <inheritdoc />
    public PreparedQuery<TResult> Prepare()
        => throw new NotSupportedException("Prepare() must be intercepted by the Quarry source generator.");

    #region Execution Methods

    public Task<List<TResult>> ExecuteFetchAllAsync(CancellationToken cancellationToken = default)
        => QueryExecutor.ExecuteFetchAllAsync(_state, GetReader(), cancellationToken);

    public Task<TResult> ExecuteFetchFirstAsync(CancellationToken cancellationToken = default)
        => QueryExecutor.ExecuteFetchFirstAsync(_state, GetReader(), cancellationToken);

    public Task<TResult?> ExecuteFetchFirstOrDefaultAsync(CancellationToken cancellationToken = default)
        => QueryExecutor.ExecuteFetchFirstOrDefaultAsync(_state, GetReader(), cancellationToken);

    public Task<TResult> ExecuteFetchSingleAsync(CancellationToken cancellationToken = default)
        => QueryExecutor.ExecuteFetchSingleAsync(_state, GetReader(), cancellationToken);

    public IAsyncEnumerable<TResult> ToAsyncEnumerable(CancellationToken cancellationToken = default)
        => QueryExecutor.ToAsyncEnumerable(_state, GetReader(), cancellationToken);

    private Func<DbDataReader, TResult> GetReader()
        => _reader ?? throw new InvalidOperationException(
            "No reader delegate available. Ensure the query is built in a single fluent chain.");

    #endregion

    internal QueryState State => _state;
    internal Func<T1, T2, TResult> Selector => _selector;

    #region Internal Methods for Interceptors

    public JoinedQueryBuilder<T1, T2, TResult> AddWhereClause(string sql)
        => new(_state.WithWhere(sql), _selector, _reader);

    public JoinedQueryBuilder<T1, T2, TResult> AddWhereClause(string sql, params object?[] paramValues)
        => new(_state.WithWhereAndParameters(sql, paramValues), _selector, _reader);

    public JoinedQueryBuilder<T1, T2, TResult> AddOrderByClause(string sql, Direction direction)
        => new(_state.WithOrderBy(sql, direction), _selector, _reader);

    public JoinedQueryBuilder<T1, T2, TResult> AddThenByClause(string sql, Direction direction)
        => new(_state.WithOrderBy(sql, direction), _selector, _reader);

    public ulong ClauseMask => _state.ClauseMask;

    public JoinedQueryBuilder<T1, T2, TResult> WithClauseBit(int bit)
        => new(_state.WithClauseBit(bit), _selector, _reader);

    public Task<List<TResult>> ExecuteWithPrebuiltSqlAsync(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
        => QueryExecutor.ExecuteWithPrebuiltSqlAsync(_state, sql, reader, cancellationToken);

    public Task<TResult> ExecuteFirstWithPrebuiltSqlAsync(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
        => QueryExecutor.ExecuteFirstWithPrebuiltSqlAsync(_state, sql, reader, cancellationToken);

    public Task<TResult?> ExecuteFirstOrDefaultWithPrebuiltSqlAsync(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
        => QueryExecutor.ExecuteFirstOrDefaultWithPrebuiltSqlAsync(_state, sql, reader, cancellationToken);

    public Task<TResult> ExecuteSingleWithPrebuiltSqlAsync(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
        => QueryExecutor.ExecuteSingleWithPrebuiltSqlAsync(_state, sql, reader, cancellationToken);

    public Task<TScalar> ExecuteScalarWithPrebuiltSqlAsync<TScalar>(
        string sql, CancellationToken cancellationToken)
        => QueryExecutor.ExecuteScalarWithPrebuiltSqlAsync<TScalar>(_state, sql, cancellationToken);

    public IAsyncEnumerable<TResult> ToAsyncEnumerableWithPrebuiltSql(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
        => QueryExecutor.ToAsyncEnumerableWithPrebuiltSql(_state, sql, reader, cancellationToken);

    #region Prebuilt Parameter Binding (Generated Code API)

    internal object?[]? PrebuiltParams;
    internal int PrebuiltParamIndex;

    public void AllocatePrebuiltParams(int count)
    {
        PrebuiltParams = new object?[count];
        PrebuiltParamIndex = 0;
    }

    public JoinedQueryBuilder<T1, T2, TResult> BindParam(object? value)
    {
        PrebuiltParams![PrebuiltParamIndex++] = value;
        return this;
    }

    public JoinedQueryBuilder<T1, T2, TResult> SetClauseBit(int bit)
    {
        _state.SetClauseBitMutable(bit);
        return this;
    }

    public Task<List<TResult>> ExecuteWithPrebuiltParamsAsync(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
    {
        var state = _state.WithPrebuiltParams(PrebuiltParams!, PrebuiltParamIndex);
        return QueryExecutor.ExecuteWithPrebuiltSqlAsync(state, sql, reader, cancellationToken);
    }

    public Task<TResult> ExecuteFirstWithPrebuiltParamsAsync(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
    {
        var state = _state.WithPrebuiltParams(PrebuiltParams!, PrebuiltParamIndex);
        return QueryExecutor.ExecuteFirstWithPrebuiltSqlAsync(state, sql, reader, cancellationToken);
    }

    public Task<TResult?> ExecuteFirstOrDefaultWithPrebuiltParamsAsync(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
    {
        var state = _state.WithPrebuiltParams(PrebuiltParams!, PrebuiltParamIndex);
        return QueryExecutor.ExecuteFirstOrDefaultWithPrebuiltSqlAsync(state, sql, reader, cancellationToken);
    }

    public Task<TResult> ExecuteSingleWithPrebuiltParamsAsync(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
    {
        var state = _state.WithPrebuiltParams(PrebuiltParams!, PrebuiltParamIndex);
        return QueryExecutor.ExecuteSingleWithPrebuiltSqlAsync(state, sql, reader, cancellationToken);
    }

    public Task<TScalar> ExecuteScalarWithPrebuiltParamsAsync<TScalar>(
        string sql, CancellationToken cancellationToken)
    {
        var state = _state.WithPrebuiltParams(PrebuiltParams!, PrebuiltParamIndex);
        return QueryExecutor.ExecuteScalarWithPrebuiltSqlAsync<TScalar>(state, sql, cancellationToken);
    }

    public IAsyncEnumerable<TResult> ToAsyncEnumerableWithPrebuiltParams(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
    {
        var state = _state.WithPrebuiltParams(PrebuiltParams!, PrebuiltParamIndex);
        return QueryExecutor.ToAsyncEnumerableWithPrebuiltSql(state, sql, reader, cancellationToken);
    }

    #endregion

    #endregion
}

// ============================================================================
// 3-Table Join Builders
// ============================================================================

/// <summary>
/// Builder for constructing SELECT queries with a three-table join (no projection).
/// Uses distinct name to avoid generic arity conflict with JoinedQueryBuilder&lt;T1,T2,TResult&gt;.
/// </summary>
public sealed class JoinedQueryBuilder3<T1, T2, T3> : IJoinedQueryBuilder3<T1, T2, T3>
    where T1 : class
    where T2 : class
    where T3 : class
{
    private readonly QueryState _state;

    internal JoinedQueryBuilder3(QueryState state)
    {
        _state = state;
    }

    public IJoinedQueryBuilder3<T1, T2, T3, TResult> Select<TResult>(Func<T1, T2, T3, TResult> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new JoinedQueryBuilder3<T1, T2, T3, TResult>(_state, selector);
    }

    public IJoinedQueryBuilder3<T1, T2, T3> Where(Expression<Func<T1, T2, T3, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new JoinedQueryBuilder3<T1, T2, T3>(_state);
    }

    public IJoinedQueryBuilder3<T1, T2, T3> OrderBy<TKey>(Expression<Func<T1, T2, T3, TKey>> keySelector, Direction direction = Direction.Ascending)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        return new JoinedQueryBuilder3<T1, T2, T3>(_state);
    }

    public IJoinedQueryBuilder3<T1, T2, T3> ThenBy<TKey>(Expression<Func<T1, T2, T3, TKey>> keySelector, Direction direction = Direction.Ascending)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        return new JoinedQueryBuilder3<T1, T2, T3>(_state);
    }

    public IJoinedQueryBuilder3<T1, T2, T3> Offset(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "Offset cannot be negative.");
        return new JoinedQueryBuilder3<T1, T2, T3>(_state.WithOffset(count));
    }

    public IJoinedQueryBuilder3<T1, T2, T3> Limit(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "Limit cannot be negative.");
        return new JoinedQueryBuilder3<T1, T2, T3>(_state.WithLimit(count));
    }

    public IJoinedQueryBuilder3<T1, T2, T3> Distinct()
        => new JoinedQueryBuilder3<T1, T2, T3>(_state.WithDistinct());

    public QueryDiagnostics ToDiagnostics() => new(
        SqlBuilder.BuildSelectSql(_state),
        DiagnosticsHelper.ConvertParameters(_state.Parameters),
        DiagnosticQueryKind.Select,
        _state.Dialect,
        _state.TableName,
        rawState: _state);

    /// <inheritdoc />
    public PreparedQuery<(T1, T2, T3)> Prepare()
        => throw new NotSupportedException("Prepare() must be intercepted by the Quarry source generator.");

    internal QueryState State => _state;

    #region Join Chaining (3-table → 4-table)

    public IJoinedQueryBuilder4<T1, T2, T3, T4> Join<T4>(Expression<Func<T1, T2, T3, T4, bool>> condition) where T4 : class
    {
        ArgumentNullException.ThrowIfNull(condition);
        return new JoinedQueryBuilder4<T1, T2, T3, T4>(_state);
    }

    public IJoinedQueryBuilder4<T1, T2, T3, T4> LeftJoin<T4>(Expression<Func<T1, T2, T3, T4, bool>> condition) where T4 : class
    {
        ArgumentNullException.ThrowIfNull(condition);
        return new JoinedQueryBuilder4<T1, T2, T3, T4>(_state);
    }

    public IJoinedQueryBuilder4<T1, T2, T3, T4> RightJoin<T4>(Expression<Func<T1, T2, T3, T4, bool>> condition) where T4 : class
    {
        ArgumentNullException.ThrowIfNull(condition);
        return new JoinedQueryBuilder4<T1, T2, T3, T4>(_state);
    }

    #endregion

    #region Internal Methods for Interceptors

    public JoinedQueryBuilder3<T1, T2, T3> AddWhereClause(string sql)
        => new(_state.WithWhere(sql));

    public JoinedQueryBuilder3<T1, T2, T3> AddWhereClause(string sql, params object?[] paramValues)
        => new(_state.WithWhereAndParameters(sql, paramValues));

    public JoinedQueryBuilder3<T1, T2, T3> AddOrderByClause(string sql, Direction direction)
        => new(_state.WithOrderBy(sql, direction));

    public JoinedQueryBuilder3<T1, T2, T3> AddThenByClause(string sql, Direction direction)
        => new(_state.WithOrderBy(sql, direction));

    public JoinedQueryBuilder3<T1, T2, T3, TResult> AddSelectClause<TResult>(string[] columnNames, Func<DbDataReader, TResult> reader)
    {
        var columns = System.Collections.Immutable.ImmutableArray.Create(columnNames);
        var newState = _state.WithSelect(columns);
        return new JoinedQueryBuilder3<T1, T2, T3, TResult>(newState, null!, reader);
    }

    public JoinedQueryBuilder4<T1, T2, T3, T4> AddJoinClause<T4>(JoinKind kind, string tableName, string onConditionSql) where T4 : class
    {
        var joinClause = new JoinClause(kind, tableName, null, null, onConditionSql);
        var newState = _state.WithJoin(joinClause);
        return new JoinedQueryBuilder4<T1, T2, T3, T4>(newState);
    }

    public ulong ClauseMask => _state.ClauseMask;

    public JoinedQueryBuilder3<T1, T2, T3> WithClauseBit(int bit)
        => new(_state.WithClauseBit(bit));

    #region Prebuilt Parameter Binding (Generated Code API)

    internal object?[]? PrebuiltParams;
    internal int PrebuiltParamIndex;

    public void AllocatePrebuiltParams(int count)
    {
        PrebuiltParams = new object?[count];
        PrebuiltParamIndex = 0;
    }

    public JoinedQueryBuilder3<T1, T2, T3> BindParam(object? value)
    {
        PrebuiltParams![PrebuiltParamIndex++] = value;
        return this;
    }

    public JoinedQueryBuilder3<T1, T2, T3> SetClauseBit(int bit)
    {
        _state.SetClauseBitMutable(bit);
        return this;
    }

    public JoinedQueryBuilder3<T1, T2, T3, TResult> AsProjected<TResult>()
    {
        var projected = new JoinedQueryBuilder3<T1, T2, T3, TResult>(_state, null!, null);
        projected.PrebuiltParams = PrebuiltParams;
        projected.PrebuiltParamIndex = PrebuiltParamIndex;
        return projected;
    }

    /// <summary>
    /// Creates a 4-table joined builder for the prebuilt path, transferring the PrebuiltParams array.
    /// Performs only a type conversion without modifying state (no JoinClause/alias mutation).
    /// </summary>
    public JoinedQueryBuilder4<T1, T2, T3, T4> AsJoined<T4>() where T4 : class
    {
        var joined = new JoinedQueryBuilder4<T1, T2, T3, T4>(_state);
        joined.PrebuiltParams = PrebuiltParams;
        joined.PrebuiltParamIndex = PrebuiltParamIndex;
        return joined;
    }

    #endregion

    #endregion
}

/// <summary>
/// Builder for constructing SELECT queries with a three-table join and a specified projection.
/// </summary>
public sealed class JoinedQueryBuilder3<T1, T2, T3, TResult> : IJoinedQueryBuilder3<T1, T2, T3, TResult>
    where T1 : class
    where T2 : class
    where T3 : class
{
    private readonly QueryState _state;
    private readonly Func<T1, T2, T3, TResult> _selector;
    private readonly Func<DbDataReader, TResult>? _reader;

    internal JoinedQueryBuilder3(QueryState state, Func<T1, T2, T3, TResult> selector)
    {
        _state = state; _selector = selector; _reader = null;
    }

    internal JoinedQueryBuilder3(QueryState state, Func<T1, T2, T3, TResult> selector, Func<DbDataReader, TResult>? reader)
    {
        _state = state; _selector = selector; _reader = reader;
    }

    public IJoinedQueryBuilder3<T1, T2, T3, TResult> Where(Expression<Func<T1, T2, T3, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new JoinedQueryBuilder3<T1, T2, T3, TResult>(_state, _selector, _reader);
    }

    public IJoinedQueryBuilder3<T1, T2, T3, TResult> OrderBy<TKey>(Expression<Func<T1, T2, T3, TKey>> keySelector, Direction direction = Direction.Ascending)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        return new JoinedQueryBuilder3<T1, T2, T3, TResult>(_state, _selector, _reader);
    }

    public IJoinedQueryBuilder3<T1, T2, T3, TResult> ThenBy<TKey>(Expression<Func<T1, T2, T3, TKey>> keySelector, Direction direction = Direction.Ascending)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        return new JoinedQueryBuilder3<T1, T2, T3, TResult>(_state, _selector, _reader);
    }

    public IJoinedQueryBuilder3<T1, T2, T3, TResult> Offset(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "Offset cannot be negative.");
        return new JoinedQueryBuilder3<T1, T2, T3, TResult>(_state.WithOffset(count), _selector, _reader);
    }

    public IJoinedQueryBuilder3<T1, T2, T3, TResult> Limit(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "Limit cannot be negative.");
        return new JoinedQueryBuilder3<T1, T2, T3, TResult>(_state.WithLimit(count), _selector, _reader);
    }

    public IJoinedQueryBuilder3<T1, T2, T3, TResult> Distinct()
        => new JoinedQueryBuilder3<T1, T2, T3, TResult>(_state.WithDistinct(), _selector, _reader);

    public QueryDiagnostics ToDiagnostics() => new(
        SqlBuilder.BuildSelectSql(_state),
        DiagnosticsHelper.ConvertParameters(_state.Parameters),
        DiagnosticQueryKind.Select,
        _state.Dialect,
        _state.TableName,
        rawState: _state);

    /// <inheritdoc />
    public PreparedQuery<TResult> Prepare()
        => throw new NotSupportedException("Prepare() must be intercepted by the Quarry source generator.");

    #region Execution Methods

    public Task<List<TResult>> ExecuteFetchAllAsync(CancellationToken cancellationToken = default)
        => QueryExecutor.ExecuteFetchAllAsync(_state, GetReader(), cancellationToken);

    public Task<TResult> ExecuteFetchFirstAsync(CancellationToken cancellationToken = default)
        => QueryExecutor.ExecuteFetchFirstAsync(_state, GetReader(), cancellationToken);

    public Task<TResult?> ExecuteFetchFirstOrDefaultAsync(CancellationToken cancellationToken = default)
        => QueryExecutor.ExecuteFetchFirstOrDefaultAsync(_state, GetReader(), cancellationToken);

    public Task<TResult> ExecuteFetchSingleAsync(CancellationToken cancellationToken = default)
        => QueryExecutor.ExecuteFetchSingleAsync(_state, GetReader(), cancellationToken);

    public IAsyncEnumerable<TResult> ToAsyncEnumerable(CancellationToken cancellationToken = default)
        => QueryExecutor.ToAsyncEnumerable(_state, GetReader(), cancellationToken);

    private Func<DbDataReader, TResult> GetReader()
        => _reader ?? throw new InvalidOperationException(
            "No reader delegate available. Ensure the query is built in a single fluent chain.");

    #endregion

    internal QueryState State => _state;

    #region Internal Methods for Interceptors

    public JoinedQueryBuilder3<T1, T2, T3, TResult> AddWhereClause(string sql)
        => new(_state.WithWhere(sql), _selector, _reader);

    public JoinedQueryBuilder3<T1, T2, T3, TResult> AddWhereClause(string sql, params object?[] paramValues)
        => new(_state.WithWhereAndParameters(sql, paramValues), _selector, _reader);

    public JoinedQueryBuilder3<T1, T2, T3, TResult> AddOrderByClause(string sql, Direction direction)
        => new(_state.WithOrderBy(sql, direction), _selector, _reader);

    public JoinedQueryBuilder3<T1, T2, T3, TResult> AddThenByClause(string sql, Direction direction)
        => new(_state.WithOrderBy(sql, direction), _selector, _reader);

    public ulong ClauseMask => _state.ClauseMask;

    public JoinedQueryBuilder3<T1, T2, T3, TResult> WithClauseBit(int bit)
        => new(_state.WithClauseBit(bit), _selector, _reader);

    public Task<List<TResult>> ExecuteWithPrebuiltSqlAsync(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
        => QueryExecutor.ExecuteWithPrebuiltSqlAsync(_state, sql, reader, cancellationToken);

    public Task<TResult> ExecuteFirstWithPrebuiltSqlAsync(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
        => QueryExecutor.ExecuteFirstWithPrebuiltSqlAsync(_state, sql, reader, cancellationToken);

    public Task<TResult?> ExecuteFirstOrDefaultWithPrebuiltSqlAsync(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
        => QueryExecutor.ExecuteFirstOrDefaultWithPrebuiltSqlAsync(_state, sql, reader, cancellationToken);

    public Task<TResult> ExecuteSingleWithPrebuiltSqlAsync(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
        => QueryExecutor.ExecuteSingleWithPrebuiltSqlAsync(_state, sql, reader, cancellationToken);

    public Task<TScalar> ExecuteScalarWithPrebuiltSqlAsync<TScalar>(
        string sql, CancellationToken cancellationToken)
        => QueryExecutor.ExecuteScalarWithPrebuiltSqlAsync<TScalar>(_state, sql, cancellationToken);

    public IAsyncEnumerable<TResult> ToAsyncEnumerableWithPrebuiltSql(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
        => QueryExecutor.ToAsyncEnumerableWithPrebuiltSql(_state, sql, reader, cancellationToken);

    #region Prebuilt Parameter Binding (Generated Code API)

    internal object?[]? PrebuiltParams;
    internal int PrebuiltParamIndex;

    public void AllocatePrebuiltParams(int count)
    {
        PrebuiltParams = new object?[count];
        PrebuiltParamIndex = 0;
    }

    public JoinedQueryBuilder3<T1, T2, T3, TResult> BindParam(object? value)
    {
        PrebuiltParams![PrebuiltParamIndex++] = value;
        return this;
    }

    public JoinedQueryBuilder3<T1, T2, T3, TResult> SetClauseBit(int bit)
    {
        _state.SetClauseBitMutable(bit);
        return this;
    }

    public Task<List<TResult>> ExecuteWithPrebuiltParamsAsync(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
    {
        var state = _state.WithPrebuiltParams(PrebuiltParams!, PrebuiltParamIndex);
        return QueryExecutor.ExecuteWithPrebuiltSqlAsync(state, sql, reader, cancellationToken);
    }

    public Task<TResult> ExecuteFirstWithPrebuiltParamsAsync(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
    {
        var state = _state.WithPrebuiltParams(PrebuiltParams!, PrebuiltParamIndex);
        return QueryExecutor.ExecuteFirstWithPrebuiltSqlAsync(state, sql, reader, cancellationToken);
    }

    public Task<TResult?> ExecuteFirstOrDefaultWithPrebuiltParamsAsync(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
    {
        var state = _state.WithPrebuiltParams(PrebuiltParams!, PrebuiltParamIndex);
        return QueryExecutor.ExecuteFirstOrDefaultWithPrebuiltSqlAsync(state, sql, reader, cancellationToken);
    }

    public Task<TResult> ExecuteSingleWithPrebuiltParamsAsync(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
    {
        var state = _state.WithPrebuiltParams(PrebuiltParams!, PrebuiltParamIndex);
        return QueryExecutor.ExecuteSingleWithPrebuiltSqlAsync(state, sql, reader, cancellationToken);
    }

    public Task<TScalar> ExecuteScalarWithPrebuiltParamsAsync<TScalar>(
        string sql, CancellationToken cancellationToken)
    {
        var state = _state.WithPrebuiltParams(PrebuiltParams!, PrebuiltParamIndex);
        return QueryExecutor.ExecuteScalarWithPrebuiltSqlAsync<TScalar>(state, sql, cancellationToken);
    }

    public IAsyncEnumerable<TResult> ToAsyncEnumerableWithPrebuiltParams(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
    {
        var state = _state.WithPrebuiltParams(PrebuiltParams!, PrebuiltParamIndex);
        return QueryExecutor.ToAsyncEnumerableWithPrebuiltSql(state, sql, reader, cancellationToken);
    }

    #endregion

    #endregion
}

// ============================================================================
// 4-Table Join Builders (cap — no further Join<T5>)
// ============================================================================

/// <summary>
/// Builder for constructing SELECT queries with a four-table join (no projection).
/// This is the maximum join depth — no further joins can be chained.
/// </summary>
public sealed class JoinedQueryBuilder4<T1, T2, T3, T4> : IJoinedQueryBuilder4<T1, T2, T3, T4>
    where T1 : class
    where T2 : class
    where T3 : class
    where T4 : class
{
    private readonly QueryState _state;

    internal JoinedQueryBuilder4(QueryState state)
    {
        _state = state;
    }

    public IJoinedQueryBuilder4<T1, T2, T3, T4, TResult> Select<TResult>(Func<T1, T2, T3, T4, TResult> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new JoinedQueryBuilder4<T1, T2, T3, T4, TResult>(_state, selector);
    }

    public IJoinedQueryBuilder4<T1, T2, T3, T4> Where(Expression<Func<T1, T2, T3, T4, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new JoinedQueryBuilder4<T1, T2, T3, T4>(_state);
    }

    public IJoinedQueryBuilder4<T1, T2, T3, T4> OrderBy<TKey>(Expression<Func<T1, T2, T3, T4, TKey>> keySelector, Direction direction = Direction.Ascending)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        return new JoinedQueryBuilder4<T1, T2, T3, T4>(_state);
    }

    public IJoinedQueryBuilder4<T1, T2, T3, T4> ThenBy<TKey>(Expression<Func<T1, T2, T3, T4, TKey>> keySelector, Direction direction = Direction.Ascending)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        return new JoinedQueryBuilder4<T1, T2, T3, T4>(_state);
    }

    public IJoinedQueryBuilder4<T1, T2, T3, T4> Offset(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "Offset cannot be negative.");
        return new JoinedQueryBuilder4<T1, T2, T3, T4>(_state.WithOffset(count));
    }

    public IJoinedQueryBuilder4<T1, T2, T3, T4> Limit(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "Limit cannot be negative.");
        return new JoinedQueryBuilder4<T1, T2, T3, T4>(_state.WithLimit(count));
    }

    public IJoinedQueryBuilder4<T1, T2, T3, T4> Distinct()
        => new JoinedQueryBuilder4<T1, T2, T3, T4>(_state.WithDistinct());

    public QueryDiagnostics ToDiagnostics() => new(
        SqlBuilder.BuildSelectSql(_state),
        DiagnosticsHelper.ConvertParameters(_state.Parameters),
        DiagnosticQueryKind.Select,
        _state.Dialect,
        _state.TableName,
        rawState: _state);

    /// <inheritdoc />
    public PreparedQuery<(T1, T2, T3, T4)> Prepare()
        => throw new NotSupportedException("Prepare() must be intercepted by the Quarry source generator.");

    internal QueryState State => _state;

    #region Internal Methods for Interceptors

    public JoinedQueryBuilder4<T1, T2, T3, T4> AddWhereClause(string sql)
        => new(_state.WithWhere(sql));

    public JoinedQueryBuilder4<T1, T2, T3, T4> AddWhereClause(string sql, params object?[] paramValues)
        => new(_state.WithWhereAndParameters(sql, paramValues));

    public JoinedQueryBuilder4<T1, T2, T3, T4> AddOrderByClause(string sql, Direction direction)
        => new(_state.WithOrderBy(sql, direction));

    public JoinedQueryBuilder4<T1, T2, T3, T4> AddThenByClause(string sql, Direction direction)
        => new(_state.WithOrderBy(sql, direction));

    public JoinedQueryBuilder4<T1, T2, T3, T4, TResult> AddSelectClause<TResult>(string[] columnNames, Func<DbDataReader, TResult> reader)
    {
        var columns = System.Collections.Immutable.ImmutableArray.Create(columnNames);
        var newState = _state.WithSelect(columns);
        return new JoinedQueryBuilder4<T1, T2, T3, T4, TResult>(newState, null!, reader);
    }

    public ulong ClauseMask => _state.ClauseMask;

    public JoinedQueryBuilder4<T1, T2, T3, T4> WithClauseBit(int bit)
        => new(_state.WithClauseBit(bit));

    #region Prebuilt Parameter Binding (Generated Code API)

    internal object?[]? PrebuiltParams;
    internal int PrebuiltParamIndex;

    public void AllocatePrebuiltParams(int count)
    {
        PrebuiltParams = new object?[count];
        PrebuiltParamIndex = 0;
    }

    public JoinedQueryBuilder4<T1, T2, T3, T4> BindParam(object? value)
    {
        PrebuiltParams![PrebuiltParamIndex++] = value;
        return this;
    }

    public JoinedQueryBuilder4<T1, T2, T3, T4> SetClauseBit(int bit)
    {
        _state.SetClauseBitMutable(bit);
        return this;
    }

    public JoinedQueryBuilder4<T1, T2, T3, T4, TResult> AsProjected<TResult>()
    {
        var projected = new JoinedQueryBuilder4<T1, T2, T3, T4, TResult>(_state, null!, null);
        projected.PrebuiltParams = PrebuiltParams;
        projected.PrebuiltParamIndex = PrebuiltParamIndex;
        return projected;
    }

    #endregion

    #endregion
}

/// <summary>
/// Builder for constructing SELECT queries with a four-table join and a specified projection.
/// </summary>
public sealed class JoinedQueryBuilder4<T1, T2, T3, T4, TResult> : IJoinedQueryBuilder4<T1, T2, T3, T4, TResult>
    where T1 : class
    where T2 : class
    where T3 : class
    where T4 : class
{
    private readonly QueryState _state;
    private readonly Func<T1, T2, T3, T4, TResult> _selector;
    private readonly Func<DbDataReader, TResult>? _reader;

    internal JoinedQueryBuilder4(QueryState state, Func<T1, T2, T3, T4, TResult> selector)
    {
        _state = state; _selector = selector; _reader = null;
    }

    internal JoinedQueryBuilder4(QueryState state, Func<T1, T2, T3, T4, TResult> selector, Func<DbDataReader, TResult>? reader)
    {
        _state = state; _selector = selector; _reader = reader;
    }

    public IJoinedQueryBuilder4<T1, T2, T3, T4, TResult> Where(Expression<Func<T1, T2, T3, T4, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new JoinedQueryBuilder4<T1, T2, T3, T4, TResult>(_state, _selector, _reader);
    }

    public IJoinedQueryBuilder4<T1, T2, T3, T4, TResult> OrderBy<TKey>(Expression<Func<T1, T2, T3, T4, TKey>> keySelector, Direction direction = Direction.Ascending)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        return new JoinedQueryBuilder4<T1, T2, T3, T4, TResult>(_state, _selector, _reader);
    }

    public IJoinedQueryBuilder4<T1, T2, T3, T4, TResult> ThenBy<TKey>(Expression<Func<T1, T2, T3, T4, TKey>> keySelector, Direction direction = Direction.Ascending)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        return new JoinedQueryBuilder4<T1, T2, T3, T4, TResult>(_state, _selector, _reader);
    }

    public IJoinedQueryBuilder4<T1, T2, T3, T4, TResult> Offset(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "Offset cannot be negative.");
        return new JoinedQueryBuilder4<T1, T2, T3, T4, TResult>(_state.WithOffset(count), _selector, _reader);
    }

    public IJoinedQueryBuilder4<T1, T2, T3, T4, TResult> Limit(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "Limit cannot be negative.");
        return new JoinedQueryBuilder4<T1, T2, T3, T4, TResult>(_state.WithLimit(count), _selector, _reader);
    }

    public IJoinedQueryBuilder4<T1, T2, T3, T4, TResult> Distinct()
        => new JoinedQueryBuilder4<T1, T2, T3, T4, TResult>(_state.WithDistinct(), _selector, _reader);

    public QueryDiagnostics ToDiagnostics() => new(
        SqlBuilder.BuildSelectSql(_state),
        DiagnosticsHelper.ConvertParameters(_state.Parameters),
        DiagnosticQueryKind.Select,
        _state.Dialect,
        _state.TableName,
        rawState: _state);

    /// <inheritdoc />
    public PreparedQuery<TResult> Prepare()
        => throw new NotSupportedException("Prepare() must be intercepted by the Quarry source generator.");

    #region Execution Methods

    public Task<List<TResult>> ExecuteFetchAllAsync(CancellationToken cancellationToken = default)
        => QueryExecutor.ExecuteFetchAllAsync(_state, GetReader(), cancellationToken);

    public Task<TResult> ExecuteFetchFirstAsync(CancellationToken cancellationToken = default)
        => QueryExecutor.ExecuteFetchFirstAsync(_state, GetReader(), cancellationToken);

    public Task<TResult?> ExecuteFetchFirstOrDefaultAsync(CancellationToken cancellationToken = default)
        => QueryExecutor.ExecuteFetchFirstOrDefaultAsync(_state, GetReader(), cancellationToken);

    public Task<TResult> ExecuteFetchSingleAsync(CancellationToken cancellationToken = default)
        => QueryExecutor.ExecuteFetchSingleAsync(_state, GetReader(), cancellationToken);

    public IAsyncEnumerable<TResult> ToAsyncEnumerable(CancellationToken cancellationToken = default)
        => QueryExecutor.ToAsyncEnumerable(_state, GetReader(), cancellationToken);

    private Func<DbDataReader, TResult> GetReader()
        => _reader ?? throw new InvalidOperationException(
            "No reader delegate available. Ensure the query is built in a single fluent chain.");

    #endregion

    internal QueryState State => _state;

    #region Internal Methods for Interceptors

    public JoinedQueryBuilder4<T1, T2, T3, T4, TResult> AddWhereClause(string sql)
        => new(_state.WithWhere(sql), _selector, _reader);

    public JoinedQueryBuilder4<T1, T2, T3, T4, TResult> AddWhereClause(string sql, params object?[] paramValues)
        => new(_state.WithWhereAndParameters(sql, paramValues), _selector, _reader);

    public JoinedQueryBuilder4<T1, T2, T3, T4, TResult> AddOrderByClause(string sql, Direction direction)
        => new(_state.WithOrderBy(sql, direction), _selector, _reader);

    public JoinedQueryBuilder4<T1, T2, T3, T4, TResult> AddThenByClause(string sql, Direction direction)
        => new(_state.WithOrderBy(sql, direction), _selector, _reader);

    public ulong ClauseMask => _state.ClauseMask;

    public JoinedQueryBuilder4<T1, T2, T3, T4, TResult> WithClauseBit(int bit)
        => new(_state.WithClauseBit(bit), _selector, _reader);

    public Task<List<TResult>> ExecuteWithPrebuiltSqlAsync(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
        => QueryExecutor.ExecuteWithPrebuiltSqlAsync(_state, sql, reader, cancellationToken);

    public Task<TResult> ExecuteFirstWithPrebuiltSqlAsync(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
        => QueryExecutor.ExecuteFirstWithPrebuiltSqlAsync(_state, sql, reader, cancellationToken);

    public Task<TResult?> ExecuteFirstOrDefaultWithPrebuiltSqlAsync(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
        => QueryExecutor.ExecuteFirstOrDefaultWithPrebuiltSqlAsync(_state, sql, reader, cancellationToken);

    public Task<TResult> ExecuteSingleWithPrebuiltSqlAsync(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
        => QueryExecutor.ExecuteSingleWithPrebuiltSqlAsync(_state, sql, reader, cancellationToken);

    public Task<TScalar> ExecuteScalarWithPrebuiltSqlAsync<TScalar>(
        string sql, CancellationToken cancellationToken)
        => QueryExecutor.ExecuteScalarWithPrebuiltSqlAsync<TScalar>(_state, sql, cancellationToken);

    public IAsyncEnumerable<TResult> ToAsyncEnumerableWithPrebuiltSql(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
        => QueryExecutor.ToAsyncEnumerableWithPrebuiltSql(_state, sql, reader, cancellationToken);

    #region Prebuilt Parameter Binding (Generated Code API)

    internal object?[]? PrebuiltParams;
    internal int PrebuiltParamIndex;

    public void AllocatePrebuiltParams(int count)
    {
        PrebuiltParams = new object?[count];
        PrebuiltParamIndex = 0;
    }

    public JoinedQueryBuilder4<T1, T2, T3, T4, TResult> BindParam(object? value)
    {
        PrebuiltParams![PrebuiltParamIndex++] = value;
        return this;
    }

    public JoinedQueryBuilder4<T1, T2, T3, T4, TResult> SetClauseBit(int bit)
    {
        _state.SetClauseBitMutable(bit);
        return this;
    }

    public Task<List<TResult>> ExecuteWithPrebuiltParamsAsync(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
    {
        var state = _state.WithPrebuiltParams(PrebuiltParams!, PrebuiltParamIndex);
        return QueryExecutor.ExecuteWithPrebuiltSqlAsync(state, sql, reader, cancellationToken);
    }

    public Task<TResult> ExecuteFirstWithPrebuiltParamsAsync(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
    {
        var state = _state.WithPrebuiltParams(PrebuiltParams!, PrebuiltParamIndex);
        return QueryExecutor.ExecuteFirstWithPrebuiltSqlAsync(state, sql, reader, cancellationToken);
    }

    public Task<TResult?> ExecuteFirstOrDefaultWithPrebuiltParamsAsync(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
    {
        var state = _state.WithPrebuiltParams(PrebuiltParams!, PrebuiltParamIndex);
        return QueryExecutor.ExecuteFirstOrDefaultWithPrebuiltSqlAsync(state, sql, reader, cancellationToken);
    }

    public Task<TResult> ExecuteSingleWithPrebuiltParamsAsync(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
    {
        var state = _state.WithPrebuiltParams(PrebuiltParams!, PrebuiltParamIndex);
        return QueryExecutor.ExecuteSingleWithPrebuiltSqlAsync(state, sql, reader, cancellationToken);
    }

    public Task<TScalar> ExecuteScalarWithPrebuiltParamsAsync<TScalar>(
        string sql, CancellationToken cancellationToken)
    {
        var state = _state.WithPrebuiltParams(PrebuiltParams!, PrebuiltParamIndex);
        return QueryExecutor.ExecuteScalarWithPrebuiltSqlAsync<TScalar>(state, sql, cancellationToken);
    }

    public IAsyncEnumerable<TResult> ToAsyncEnumerableWithPrebuiltParams(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
    {
        var state = _state.WithPrebuiltParams(PrebuiltParams!, PrebuiltParamIndex);
        return QueryExecutor.ToAsyncEnumerableWithPrebuiltSql(state, sql, reader, cancellationToken);
    }

    #endregion

    #endregion
}
