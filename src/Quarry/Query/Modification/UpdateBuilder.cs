using System.Linq.Expressions;
using Quarry.Internal;

namespace Quarry;

/// <summary>
/// Builder for constructing UPDATE operations.
/// </summary>
public sealed class UpdateBuilder<T> : IUpdateBuilder<T> where T : class
{
    private readonly UpdateState _state;

    public UpdateBuilder(SqlDialect dialect, string tableName, string? schemaName)
        : this(dialect, tableName, schemaName, null)
    {
    }

    public UpdateBuilder(SqlDialect dialect, string tableName, string? schemaName, IQueryExecutionContext? executionContext)
    {
        _state = new UpdateState(dialect, tableName, schemaName, executionContext);
    }

    public IUpdateBuilder<T> Set(T entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return this;
    }

    public IUpdateBuilder<T> Set(Action<T> assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        return this;
    }

    public IExecutableUpdateBuilder<T> Where(Expression<Func<T, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new ExecutableUpdateBuilder<T>(_state);
    }

    public IExecutableUpdateBuilder<T> All()
    {
        _state.AllowAll = true;
        return new ExecutableUpdateBuilder<T>(_state);
    }

    public IUpdateBuilder<T> WithTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");
        _state.Timeout = timeout;
        return this;
    }

    public string ToSql()
    {
        return SqlModificationBuilder.BuildUpdateSql(_state);
    }

    public QueryDiagnostics ToDiagnostics() => new(
        ToSql(),
        DiagnosticsHelper.ConvertParameters(_state.Parameters),
        DiagnosticQueryKind.Update,
        _state.Dialect,
        _state.TableName,
        rawState: _state);

    internal UpdateState State => _state;

    #region Generated Code Methods

    public UpdateBuilder<T> AddSetClause<TValue>(string columnSql, TValue value, bool isSensitive = false)
    {
        var paramIndex = _state.AddParameter(value, isSensitive);
        _state.SetClauses.Add(new SetClause(columnSql, paramIndex));
        return this;
    }

    public UpdateBuilder<T> AddSetClauseBoxed(string columnSql, object? value, bool isSensitive = false)
    {
        var paramIndex = _state.AddParameterBoxed(value, isSensitive);
        _state.SetClauses.Add(new SetClause(columnSql, paramIndex));
        return this;
    }

    public ExecutableUpdateBuilder<T> AddWhereClause(string whereSql)
    {
        _state.WhereConditions.Add(whereSql);
        return new ExecutableUpdateBuilder<T>(_state);
    }

    public int AddParameter<TValue>(TValue value)
    {
        return _state.AddParameter(value);
    }

    public int AddParameterBoxed(object? value)
    {
        return _state.AddParameterBoxed(value);
    }

    public void AllocatePrebuiltParams(int count)
    {
        // No-op for mutable modification builders — params added directly to state via BindParam
    }

    public UpdateBuilder<T> BindParam(object? value)
    {
        _state.AddParameterBoxed(value);
        return this;
    }

    public UpdateBuilder<T> SetClauseBit(int bit)
    {
        _state.SetClauseBit(bit);
        return this;
    }

    /// <summary>
    /// Transitions to ExecutableUpdateBuilder sharing the same mutable state.
    /// Used by prebuilt chain interceptors to perform the type transition without adding SQL.
    /// </summary>
    public ExecutableUpdateBuilder<T> AsExecutable()
    {
        return new ExecutableUpdateBuilder<T>(_state);
    }

    #endregion
}

/// <summary>
/// An UpdateBuilder that has a WHERE condition or All() marker and can be executed.
/// </summary>
public sealed class ExecutableUpdateBuilder<T> : IExecutableUpdateBuilder<T> where T : class
{
    private readonly UpdateState _state;

    internal ExecutableUpdateBuilder(UpdateState state)
    {
        _state = state;
    }

    public IExecutableUpdateBuilder<T> Set(T entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return this;
    }

    public IExecutableUpdateBuilder<T> Set(Action<T> assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        return this;
    }

    public IExecutableUpdateBuilder<T> Where(Expression<Func<T, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return this;
    }

    public IExecutableUpdateBuilder<T> WithTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");
        _state.Timeout = timeout;
        return this;
    }

    public Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default)
    {
        if (!_state.IsSafeToExecute)
        {
            throw new InvalidOperationException(
                "UPDATE requires either a WHERE clause or an explicit All() call. " +
                "This prevents accidental updates of all rows.");
        }

        if (!_state.HasSetClauses)
        {
            throw new InvalidOperationException(
                "UPDATE requires at least one SET clause.");
        }

        return ModificationExecutor.ExecuteUpdateNonQueryAsync(_state, cancellationToken);
    }

    public string ToSql()
    {
        return SqlModificationBuilder.BuildUpdateSql(_state);
    }

    public QueryDiagnostics ToDiagnostics() => new(
        ToSql(),
        DiagnosticsHelper.ConvertParameters(_state.Parameters),
        DiagnosticQueryKind.Update,
        _state.Dialect,
        _state.TableName,
        rawState: _state);

    public ulong ClauseMask => _state.ClauseMask;

    public Task<int> ExecuteWithPrebuiltSqlAsync(string sql, CancellationToken cancellationToken)
        => ModificationExecutor.ExecuteUpdateWithPrebuiltSqlAsync(_state, sql, cancellationToken);

    public ExecutableUpdateBuilder<T> WithClauseBit(int bit)
    {
        _state.SetClauseBit(bit);
        return this;
    }

    internal UpdateState State => _state;

    #region Generated Code Methods

    public ExecutableUpdateBuilder<T> AddSetClause<TValue>(string columnSql, TValue value, bool isSensitive = false)
    {
        var paramIndex = _state.AddParameter(value, isSensitive);
        _state.SetClauses.Add(new SetClause(columnSql, paramIndex));
        return this;
    }

    public ExecutableUpdateBuilder<T> AddSetClauseBoxed(string columnSql, object? value, bool isSensitive = false)
    {
        var paramIndex = _state.AddParameterBoxed(value, isSensitive);
        _state.SetClauses.Add(new SetClause(columnSql, paramIndex));
        return this;
    }

    public ExecutableUpdateBuilder<T> AddWhereClause(string whereSql)
    {
        _state.WhereConditions.Add(whereSql);
        return this;
    }

    public int AddParameter<TValue>(TValue value)
    {
        return _state.AddParameter(value);
    }

    public int AddParameterBoxed(object? value)
    {
        return _state.AddParameterBoxed(value);
    }

    public void AllocatePrebuiltParams(int count)
    {
        // No-op for mutable modification builders — params added directly to state via BindParam
    }

    public ExecutableUpdateBuilder<T> BindParam(object? value)
    {
        _state.AddParameterBoxed(value);
        return this;
    }

    public ExecutableUpdateBuilder<T> SetClauseBit(int bit)
    {
        _state.SetClauseBit(bit);
        return this;
    }

    #endregion
}
