using System.Collections.Immutable;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Quarry.Internal;
using Quarry.Logging;

namespace Quarry;

/// <summary>
/// Base class for Quarry database contexts.
/// Inherit from this class and define properties for each schema to enable query building.
/// </summary>
/// <remarks>
/// <para>
/// The source generator will produce a partial class extending your context with
/// typed query builder properties for each registered schema.
/// </para>
/// <para>
/// Example:
/// <code>
/// public partial class AppDbContext : QuarryContext
/// {
///     public AppDbContext(IDbConnection connection) : base(connection) { }
/// }
/// </code>
/// </para>
/// </remarks>
public abstract class QuarryContext : IAsyncDisposable, IDisposable
{
    private readonly DbConnection _connection;
    private readonly bool _connectionWasOpen;
    private readonly bool _ownsConnection;
    private readonly TimeSpan _defaultTimeout;
    private readonly IsolationLevel _defaultIsolation;
    private bool _disposed;

    /// <summary>
    /// Gets the underlying database connection.
    /// </summary>
    public DbConnection Connection => _connection;

    /// <summary>
    /// Gets the default query timeout.
    /// </summary>
    public TimeSpan DefaultTimeout => _defaultTimeout;

    /// <summary>
    /// Gets the default transaction isolation level.
    /// </summary>
    protected IsolationLevel DefaultIsolation => _defaultIsolation;

    /// <summary>
    /// Gets or sets the threshold for slow query warnings.
    /// Queries exceeding this duration emit a warning log. Null disables slow query detection.
    /// </summary>
    public TimeSpan? SlowQueryThreshold { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Creates a new context with the specified connection.
    /// </summary>
    /// <param name="connection">The database connection to use.</param>
    protected QuarryContext(IDbConnection connection)
        : this(connection, ownsConnection: false, null, null)
    {
    }

    /// <summary>
    /// Creates a new context with the specified connection and ownership flag.
    /// </summary>
    /// <param name="connection">The database connection to use.</param>
    /// <param name="ownsConnection">If true, the context will dispose the connection when the context is disposed.</param>
    protected QuarryContext(IDbConnection connection, bool ownsConnection)
        : this(connection, ownsConnection, null, null)
    {
    }

    /// <summary>
    /// Creates a new context with the specified connection and options.
    /// </summary>
    /// <param name="connection">The database connection to use.</param>
    /// <param name="ownsConnection">If true, the context will dispose the connection when the context is disposed.</param>
    /// <param name="defaultTimeout">Optional default timeout for queries. Defaults to 30 seconds.</param>
    /// <param name="defaultIsolation">Optional default isolation level for transactions.</param>
    protected QuarryContext(
        IDbConnection connection,
        bool ownsConnection,
        TimeSpan? defaultTimeout,
        IsolationLevel? defaultIsolation)
    {
        if (connection is not DbConnection dbConnection)
            throw new ArgumentException("Connection must be a DbConnection for async support.", nameof(connection));

        _connection = dbConnection;
        _connectionWasOpen = connection.State == ConnectionState.Open;
        _ownsConnection = ownsConnection;
        _defaultTimeout = defaultTimeout ?? TimeSpan.FromSeconds(30);
        _defaultIsolation = defaultIsolation ?? IsolationLevel.ReadCommitted;
    }

    /// <summary>
    /// Ensures the connection is open.
    /// Returns <see cref="Task.CompletedTask"/> synchronously when the connection is already open (the common case),
    /// avoiding async state machine overhead on the hot path.
    /// </summary>
    public Task EnsureConnectionOpenAsync(CancellationToken cancellationToken = default)
    {
        return _connection.State == ConnectionState.Open
            ? Task.CompletedTask
            : EnsureConnectionOpenCoreAsync(cancellationToken);
    }

    /// <summary>
    /// Async slow-path: actually opens the connection. Called only when the connection is not yet open.
    /// </summary>
    private async Task EnsureConnectionOpenCoreAsync(CancellationToken cancellationToken)
    {
        await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Information, ConnectionLog.CategoryName) == true)
            ConnectionLog.Opened();
    }

    // Update/Delete/Insert operations are now accessed via EntityAccessor:
    // db.Users().Update(), db.Users().Delete(), db.Users().Insert(entity)

    #region Raw SQL

    /// <summary>
    /// Executes a raw SQL query and returns the results.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="sql">The SQL query to execute. Use @p0, @p1, etc. for parameter placeholders.</param>
    /// <param name="parameters">Optional parameter values.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A list of results.</returns>
    /// <remarks>
    /// <para>Example:</para>
    /// <code>
    /// var results = await db.RawSqlAsync&lt;UserDto&gt;(
    ///     "SELECT user_id, user_name FROM users WHERE created_at > @p0",
    ///     DateTime.UtcNow.AddDays(-7));
    /// </code>
    /// </remarks>
    public async Task<List<T>> RawSqlAsync<T>(
        string sql,
        params object?[] parameters)
    {
        return await RawSqlAsync<T>(sql, CancellationToken.None, parameters);
    }

    /// <summary>
    /// Executes a raw SQL query and returns the results.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="sql">The SQL query to execute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="parameters">Optional parameter values.</param>
    /// <returns>A list of results.</returns>
    public async Task<List<T>> RawSqlAsync<T>(
        string sql,
        CancellationToken cancellationToken,
        params object?[] parameters)
    {
        ArgumentNullException.ThrowIfNull(sql);

        var opId = OpId.Next();

        if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Debug, RawSqlLog.CategoryName) == true)
            RawSqlLog.SqlGenerated(opId, sql);

        LogRawParameters(opId, parameters);

        await EnsureConnectionOpenAsync(cancellationToken);

        var startTimestamp = Stopwatch.GetTimestamp();
        await using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = (int)_defaultTimeout.TotalSeconds;

        // Add parameters
        for (int i = 0; i < parameters.Length; i++)
        {
            var param = command.CreateParameter();
            param.ParameterName = $"@p{i}";
            param.Value = (parameters[i] is SensitiveParameter sp ? sp.Value : parameters[i]) ?? DBNull.Value;
            command.Parameters.Add(param);
        }

        var results = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken);

        // Create a runtime reader using reflection (fallback path)
        var type = typeof(T);
        var properties = type.GetProperties();

        while (await reader.ReadAsync(cancellationToken))
        {
            var item = Activator.CreateInstance<T>();

            for (int i = 0; i < reader.FieldCount; i++)
            {
                var columnName = reader.GetName(i);

                // Find matching property
                foreach (var prop in properties)
                {
                    if (string.Equals(prop.Name, columnName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!reader.IsDBNull(i))
                        {
                            var value = reader.GetValue(i);
                            var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                            prop.SetValue(item, Convert.ChangeType(value, targetType));
                        }
                        break;
                    }
                }
            }

            results.Add(item);
        }

        var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

        if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Debug, RawSqlLog.CategoryName) == true)
            RawSqlLog.FetchCompleted(opId, results.Count, elapsedMs);

        CheckSlowQuery(opId, elapsedMs, sql);

        return results;
    }

    /// <summary>
    /// Executes a raw SQL command and returns the number of affected rows.
    /// </summary>
    /// <param name="sql">The SQL command to execute.</param>
    /// <param name="parameters">Optional parameter values.</param>
    /// <returns>The number of affected rows.</returns>
    public async Task<int> RawSqlNonQueryAsync(
        string sql,
        params object?[] parameters)
    {
        return await RawSqlNonQueryAsync(sql, CancellationToken.None, parameters);
    }

    /// <summary>
    /// Executes a raw SQL command and returns the number of affected rows.
    /// </summary>
    /// <param name="sql">The SQL command to execute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="parameters">Optional parameter values.</param>
    /// <returns>The number of affected rows.</returns>
    public async Task<int> RawSqlNonQueryAsync(
        string sql,
        CancellationToken cancellationToken,
        params object?[] parameters)
    {
        ArgumentNullException.ThrowIfNull(sql);

        var opId = OpId.Next();

        if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Debug, RawSqlLog.CategoryName) == true)
            RawSqlLog.SqlGenerated(opId, sql);

        LogRawParameters(opId, parameters);

        await EnsureConnectionOpenAsync(cancellationToken);

        var startTimestamp = Stopwatch.GetTimestamp();
        await using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = (int)_defaultTimeout.TotalSeconds;

        // Add parameters
        for (int i = 0; i < parameters.Length; i++)
        {
            var param = command.CreateParameter();
            param.ParameterName = $"@p{i}";
            param.Value = (parameters[i] is SensitiveParameter sp ? sp.Value : parameters[i]) ?? DBNull.Value;
            command.Parameters.Add(param);
        }

        var rowCount = await command.ExecuteNonQueryAsync(cancellationToken);
        var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

        if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Debug, RawSqlLog.CategoryName) == true)
            RawSqlLog.NonQueryCompleted(opId, rowCount, elapsedMs);

        CheckSlowQuery(opId, elapsedMs, sql);

        return rowCount;
    }

    /// <summary>
    /// Executes a raw SQL query and returns a scalar value.
    /// </summary>
    /// <typeparam name="T">The scalar type.</typeparam>
    /// <param name="sql">The SQL query to execute.</param>
    /// <param name="parameters">Optional parameter values.</param>
    /// <returns>The scalar result.</returns>
    public async Task<T> RawSqlScalarAsync<T>(
        string sql,
        params object?[] parameters)
    {
        return await RawSqlScalarAsync<T>(sql, CancellationToken.None, parameters);
    }

    /// <summary>
    /// Executes a raw SQL query and returns a scalar value.
    /// </summary>
    /// <typeparam name="T">The scalar type.</typeparam>
    /// <param name="sql">The SQL query to execute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="parameters">Optional parameter values.</param>
    /// <returns>The scalar result.</returns>
    public async Task<T> RawSqlScalarAsync<T>(
        string sql,
        CancellationToken cancellationToken,
        params object?[] parameters)
    {
        ArgumentNullException.ThrowIfNull(sql);

        var opId = OpId.Next();

        if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Debug, RawSqlLog.CategoryName) == true)
            RawSqlLog.SqlGenerated(opId, sql);

        LogRawParameters(opId, parameters);

        await EnsureConnectionOpenAsync(cancellationToken);

        var startTimestamp = Stopwatch.GetTimestamp();
        await using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = (int)_defaultTimeout.TotalSeconds;

        // Add parameters
        for (int i = 0; i < parameters.Length; i++)
        {
            var param = command.CreateParameter();
            param.ParameterName = $"@p{i}";
            param.Value = (parameters[i] is SensitiveParameter sp ? sp.Value : parameters[i]) ?? DBNull.Value;
            command.Parameters.Add(param);
        }

        var result = await command.ExecuteScalarAsync(cancellationToken);
        var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

        if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Debug, RawSqlLog.CategoryName) == true)
            RawSqlLog.ScalarResult(opId, result?.ToString() ?? "null");

        CheckSlowQuery(opId, elapsedMs, sql);

        if (result == null || result == DBNull.Value)
        {
            return default!;
        }

        return (T)Convert.ChangeType(result, typeof(T));
    }

    #endregion

    #region Helpers for generated interceptors

    /// <summary>
    /// Executes a raw SQL query using a generated reader delegate instead of reflection.
    /// Called by source-generated interceptors for RawSqlAsync&lt;T&gt;.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public async Task<List<T>> RawSqlAsyncWithReader<T>(
        string sql,
        Func<DbDataReader, T> reader,
        CancellationToken cancellationToken,
        params object?[] parameters)
    {
        ArgumentNullException.ThrowIfNull(sql);

        var opId = OpId.Next();

        if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Debug, RawSqlLog.CategoryName) == true)
            RawSqlLog.SqlGenerated(opId, sql);

        LogRawParameters(opId, parameters);

        await EnsureConnectionOpenAsync(cancellationToken);

        var startTimestamp = Stopwatch.GetTimestamp();
        await using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = (int)_defaultTimeout.TotalSeconds;

        for (int i = 0; i < parameters.Length; i++)
        {
            var param = command.CreateParameter();
            param.ParameterName = $"@p{i}";
            param.Value = (parameters[i] is SensitiveParameter sp ? sp.Value : parameters[i]) ?? DBNull.Value;
            command.Parameters.Add(param);
        }

        var results = new List<T>();
        await using var dataReader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess, cancellationToken);

        while (await dataReader.ReadAsync(cancellationToken))
        {
            results.Add(reader(dataReader));
        }

        var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

        if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Debug, RawSqlLog.CategoryName) == true)
            RawSqlLog.FetchCompleted(opId, results.Count, elapsedMs);

        CheckSlowQuery(opId, elapsedMs, sql);

        return results;
    }

    /// <summary>
    /// Executes a raw SQL scalar query with typed conversion instead of Convert.ChangeType.
    /// Called by source-generated interceptors for RawSqlScalarAsync&lt;T&gt;.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public async Task<T> RawSqlScalarAsyncWithConverter<T>(
        string sql,
        Func<object, T> converter,
        CancellationToken cancellationToken,
        params object?[] parameters)
    {
        ArgumentNullException.ThrowIfNull(sql);

        var opId = OpId.Next();

        if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Debug, RawSqlLog.CategoryName) == true)
            RawSqlLog.SqlGenerated(opId, sql);

        LogRawParameters(opId, parameters);

        await EnsureConnectionOpenAsync(cancellationToken);

        var startTimestamp = Stopwatch.GetTimestamp();
        await using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = (int)_defaultTimeout.TotalSeconds;

        for (int i = 0; i < parameters.Length; i++)
        {
            var param = command.CreateParameter();
            param.ParameterName = $"@p{i}";
            param.Value = (parameters[i] is SensitiveParameter sp ? sp.Value : parameters[i]) ?? DBNull.Value;
            command.Parameters.Add(param);
        }

        var result = await command.ExecuteScalarAsync(cancellationToken);
        var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

        if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Debug, RawSqlLog.CategoryName) == true)
            RawSqlLog.ScalarResult(opId, result?.ToString() ?? "null");

        CheckSlowQuery(opId, elapsedMs, sql);

        if (result == null || result == DBNull.Value)
        {
            return default!;
        }

        return converter(result);
    }

    #endregion

    /// <summary>
    /// Logs raw SQL parameters at Trace level.
    /// </summary>
    private static void LogRawParameters(long opId, object?[] parameters)
    {
        if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Trace, ParameterLog.CategoryName) != true)
            return;

        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i] is SensitiveParameter)
                ParameterLog.BoundSensitive(opId, i);
            else
                ParameterLog.Bound(opId, i, parameters[i]?.ToString() ?? "null");
        }
    }

    /// <summary>
    /// Checks if a query exceeded the slow query threshold and emits a warning.
    /// </summary>
    private void CheckSlowQuery(long opId, double elapsedMs, string sql)
    {
        var threshold = SlowQueryThreshold;
        if (threshold.HasValue && elapsedMs > threshold.Value.TotalMilliseconds)
        {
            if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Warning, ExecutionLog.CategoryName) == true)
                ExecutionLog.SlowQuery(opId, elapsedMs, sql);
        }
    }

    /// <summary>
    /// Disposes the context and restores the connection to its original state.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the context and restores the connection to its original state.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            if (_ownsConnection)
            {
                _connection.Dispose();

                if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Information, ConnectionLog.CategoryName) == true)
                    ConnectionLog.Closed();
            }
            else if (!_connectionWasOpen && _connection.State == ConnectionState.Open)
            {
                _connection.Close();

                if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Information, ConnectionLog.CategoryName) == true)
                    ConnectionLog.Closed();
            }
        }

        _disposed = true;
    }

    /// <summary>
    /// Asynchronously disposes the context and restores the connection to its original state.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        if (_ownsConnection)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);

            if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Information, ConnectionLog.CategoryName) == true)
                ConnectionLog.Closed();
        }
        else if (!_connectionWasOpen && _connection.State == ConnectionState.Open)
        {
            await _connection.CloseAsync().ConfigureAwait(false);

            if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Information, ConnectionLog.CategoryName) == true)
                ConnectionLog.Closed();
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
