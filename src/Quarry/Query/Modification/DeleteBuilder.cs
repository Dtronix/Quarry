using System.Linq.Expressions;
using Quarry.Internal;

namespace Quarry;

/// <summary>
/// Builder for constructing DELETE operations.
/// </summary>
/// <typeparam name="T">The entity type being deleted.</typeparam>
public sealed class DeleteBuilder<T> : IDeleteBuilder<T> where T : class
{
    private readonly DeleteState _state;

    public DeleteBuilder(SqlDialect dialect, string tableName, string? schemaName)
        : this(dialect, tableName, schemaName, null)
    {
    }

    public DeleteBuilder(SqlDialect dialect, string tableName, string? schemaName, IQueryExecutionContext? executionContext)
    {
        _state = new DeleteState(dialect, tableName, schemaName, executionContext);
    }

    public IExecutableDeleteBuilder<T> Where(Expression<Func<T, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new ExecutableDeleteBuilder<T>(_state);
    }

    public IExecutableDeleteBuilder<T> All()
    {
        _state.AllowAll = true;
        return new ExecutableDeleteBuilder<T>(_state);
    }

    public IDeleteBuilder<T> WithTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");
        _state.Timeout = timeout;
        return this;
    }

    public QueryDiagnostics ToDiagnostics() => new(
        SqlModificationBuilder.BuildDeleteSql(_state),
        DiagnosticsHelper.ConvertParameters(_state.Parameters),
        DiagnosticQueryKind.Delete,
        _state.Dialect,
        _state.TableName,
        rawState: _state);

    /// <inheritdoc />
    public PreparedQuery<int> Prepare()
        => throw new NotSupportedException("Prepare() must be intercepted by the Quarry source generator.");

    internal DeleteState State => _state;

    #region Generated Code Methods

    public ExecutableDeleteBuilder<T> AddWhereClause(string whereSql)
    {
        _state.WhereConditions.Add(whereSql);
        return new ExecutableDeleteBuilder<T>(_state);
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

    public DeleteBuilder<T> BindParam(object? value)
    {
        _state.AddParameterBoxed(value);
        return this;
    }

    public DeleteBuilder<T> SetClauseBit(int bit)
    {
        _state.SetClauseBit(bit);
        return this;
    }

    /// <summary>
    /// Transitions to ExecutableDeleteBuilder sharing the same mutable state.
    /// Used by prebuilt chain interceptors to perform the type transition without adding SQL.
    /// </summary>
    public ExecutableDeleteBuilder<T> AsExecutable()
    {
        return new ExecutableDeleteBuilder<T>(_state);
    }

    #endregion
}

/// <summary>
/// A DeleteBuilder that has a WHERE condition or All() marker and can be executed.
/// </summary>
public sealed class ExecutableDeleteBuilder<T> : IExecutableDeleteBuilder<T> where T : class
{
    private readonly DeleteState _state;

    internal ExecutableDeleteBuilder(DeleteState state)
    {
        _state = state;
    }

    public IExecutableDeleteBuilder<T> Where(Expression<Func<T, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return this;
    }

    public IExecutableDeleteBuilder<T> WithTimeout(TimeSpan timeout)
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
                "DELETE requires either a WHERE clause or an explicit All() call. " +
                "This prevents accidental deletion of all rows.");
        }

        return ModificationExecutor.ExecuteDeleteNonQueryAsync(_state, cancellationToken);
    }

    public QueryDiagnostics ToDiagnostics() => new(
        SqlModificationBuilder.BuildDeleteSql(_state),
        DiagnosticsHelper.ConvertParameters(_state.Parameters),
        DiagnosticQueryKind.Delete,
        _state.Dialect,
        _state.TableName,
        rawState: _state);

    /// <inheritdoc />
    public PreparedQuery<int> Prepare()
        => throw new NotSupportedException("Prepare() must be intercepted by the Quarry source generator.");

    public ulong ClauseMask => _state.ClauseMask;

    public Task<int> ExecuteWithPrebuiltSqlAsync(string sql, CancellationToken cancellationToken)
        => ModificationExecutor.ExecuteDeleteWithPrebuiltSqlAsync(_state, sql, cancellationToken);

    public ExecutableDeleteBuilder<T> WithClauseBit(int bit)
    {
        _state.SetClauseBit(bit);
        return this;
    }

    internal DeleteState State => _state;

    #region Generated Code Methods

    public ExecutableDeleteBuilder<T> AddWhereClause(string whereSql)
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

    public ExecutableDeleteBuilder<T> BindParam(object? value)
    {
        _state.AddParameterBoxed(value);
        return this;
    }

    public ExecutableDeleteBuilder<T> SetClauseBit(int bit)
    {
        _state.SetClauseBit(bit);
        return this;
    }

    #endregion
}
