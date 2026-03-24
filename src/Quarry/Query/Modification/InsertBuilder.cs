using System.Data.Common;
using System.Linq.Expressions;
using Quarry.Internal;

namespace Quarry;

/// <summary>
/// Builder for constructing INSERT operations.
/// </summary>
public sealed class InsertBuilder<T> : IInsertBuilder<T> where T : class
{
    private readonly InsertState _state;
    private readonly List<T> _entities = new();

    public InsertBuilder(SqlDialect dialect, string tableName, string? schemaName)
        : this(dialect, tableName, schemaName, null)
    {
    }

    public InsertBuilder(SqlDialect dialect, string tableName, string? schemaName, IQueryExecutionContext? executionContext)
    {
        _state = new InsertState(dialect, tableName, schemaName, executionContext);
    }

    public InsertBuilder(SqlDialect dialect, string tableName, string? schemaName, IQueryExecutionContext? executionContext, T entity)
        : this(dialect, tableName, schemaName, executionContext)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _entities.Add(entity);
    }

    public IInsertBuilder<T> WithTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");
        _state.Timeout = timeout;
        return this;
    }

    public Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default)
    {
        return ModificationExecutor.ExecuteInsertNonQueryAsync(_state, _entities, cancellationToken);
    }

    public Task<TKey> ExecuteScalarAsync<TKey>(CancellationToken cancellationToken = default)
    {
        if (_entities.Count != 1)
        {
            throw new InvalidOperationException(
                "ExecuteScalarAsync can only be used for single entity inserts. " +
                "For batch inserts, use ExecuteNonQueryAsync() instead.");
        }
        return ModificationExecutor.ExecuteInsertScalarAsync<T, TKey>(_state, _entities[0], cancellationToken);
    }

    public QueryDiagnostics ToDiagnostics() => new(
        SqlModificationBuilder.BuildInsertSql(_state, _entities.Count),
        DiagnosticsHelper.ConvertParameters(_state.Parameters),
        DiagnosticQueryKind.Insert,
        _state.Dialect,
        _state.TableName,
        rawState: _state,
        insertRowCount: _entities.Count);

    /// <inheritdoc />
    public PreparedQuery<int> Prepare()
        => throw new NotSupportedException("Prepare() must be intercepted by the Quarry source generator.");

    public InsertState State => _state;

    public IReadOnlyList<T> Entities => _entities;

    #region Generated Code Methods

    public InsertBuilder<T> SetColumns(string[] columns)
    {
        _state.Columns.Clear();
        _state.Columns.AddRange(columns);
        return this;
    }

    public InsertBuilder<T> SetIdentityColumn(string identityColumn)
    {
        _state.IdentityColumn = identityColumn;
        return this;
    }

    public int AddParameter<TValue>(TValue value, bool isSensitive = false)
    {
        return _state.AddParameter(value, isSensitive);
    }

    public int AddParameterBoxed(object? value, bool isSensitive = false)
    {
        return _state.AddParameterBoxed(value, isSensitive);
    }

    public void AddRow(List<int> parameterIndices)
    {
        _state.Rows.Add(parameterIndices);
    }

    #endregion
}
