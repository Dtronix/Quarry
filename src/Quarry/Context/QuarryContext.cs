using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Logsmith;
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
public abstract class QuarryContext : IAsyncDisposable, IDisposable, IQueryExecutionContext
{
    private readonly DbConnection _connection;
    private readonly bool _connectionWasOpen;
    private readonly TimeSpan _defaultTimeout;
    private readonly IsolationLevel _defaultIsolation;
    private bool _disposed;

    /// <summary>
    /// Gets the underlying database connection.
    /// </summary>
    protected DbConnection Connection => _connection;

    /// <summary>
    /// Gets the default query timeout.
    /// </summary>
    protected TimeSpan DefaultTimeout => _defaultTimeout;

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
        : this(connection, null, null)
    {
    }

    /// <summary>
    /// Creates a new context with the specified connection and options.
    /// </summary>
    /// <param name="connection">The database connection to use.</param>
    /// <param name="defaultTimeout">Optional default timeout for queries. Defaults to 30 seconds.</param>
    /// <param name="defaultIsolation">Optional default isolation level for transactions.</param>
    protected QuarryContext(
        IDbConnection connection,
        TimeSpan? defaultTimeout,
        IsolationLevel? defaultIsolation)
    {
        if (connection is not DbConnection dbConnection)
            throw new ArgumentException("Connection must be a DbConnection for async support.", nameof(connection));

        _connection = dbConnection;
        _connectionWasOpen = connection.State == ConnectionState.Open;
        _defaultTimeout = defaultTimeout ?? TimeSpan.FromSeconds(30);
        _defaultIsolation = defaultIsolation ?? IsolationLevel.ReadCommitted;
    }

    // IQueryExecutionContext implementation
    DbConnection IQueryExecutionContext.Connection => _connection;
    TimeSpan IQueryExecutionContext.DefaultTimeout => _defaultTimeout;
    TimeSpan? IQueryExecutionContext.SlowQueryThreshold => SlowQueryThreshold;
    Task IQueryExecutionContext.EnsureConnectionOpenAsync(CancellationToken cancellationToken) =>
        EnsureConnectionOpenAsync(cancellationToken);

    /// <summary>
    /// Ensures the connection is open.
    /// </summary>
    protected async Task EnsureConnectionOpenAsync(CancellationToken cancellationToken = default)
    {
        if (_connection.State != ConnectionState.Open)
        {
            await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            if (LogManager.IsEnabled(LogLevel.Information, ConnectionLog.CategoryName))
                ConnectionLog.Opened();
        }
    }

    #region Update Operations

    /// <summary>
    /// Creates an UPDATE operation for an entity type.
    /// This method is overridden by generated code with entity-specific implementations.
    /// </summary>
    /// <typeparam name="T">The entity type to update.</typeparam>
    /// <returns>An UpdateBuilder for building the update operation.</returns>
    public IUpdateBuilder<T> Update<T>() where T : class
    {
        throw new NotImplementedException(
            "Update<T>() requires a generated context with entity mappings. " +
            "Ensure the source generator has run and your context declares this entity type.");
    }

    #endregion

    #region Delete Operations

    /// <summary>
    /// Creates a DELETE operation for an entity type.
    /// This method is overridden by generated code with entity-specific implementations.
    /// </summary>
    /// <typeparam name="T">The entity type to delete.</typeparam>
    /// <returns>A DeleteBuilder for building the delete operation.</returns>
    public IDeleteBuilder<T> Delete<T>() where T : class
    {
        throw new NotImplementedException(
            "Delete<T>() requires a generated context with entity mappings. " +
            "Ensure the source generator has run and your context declares this entity type.");
    }

    #endregion

    #region Insert Operations

    /// <summary>
    /// Creates an INSERT operation for a single entity.
    /// This method is overridden by generated code with entity-specific implementations.
    /// </summary>
    /// <typeparam name="T">The entity type to insert.</typeparam>
    /// <param name="entity">The entity to insert.</param>
    /// <returns>An InsertBuilder for execution.</returns>
    public IInsertBuilder<T> Insert<T>(T entity) where T : class
    {
        throw new NotImplementedException(
            "Insert() requires a generated context with entity mappings. " +
            "Ensure the source generator has run and your context declares this entity type.");
    }

    /// <summary>
    /// Creates an INSERT operation for multiple entities.
    /// This method is overridden by generated code with entity-specific implementations.
    /// </summary>
    /// <typeparam name="T">The entity type to insert.</typeparam>
    /// <param name="entities">The entities to insert.</param>
    /// <returns>An InsertBuilder for execution.</returns>
    public IInsertBuilder<T> InsertMany<T>(IEnumerable<T> entities) where T : class
    {
        throw new NotImplementedException(
            "InsertMany() requires a generated context with entity mappings. " +
            "Ensure the source generator has run and your context declares this entity type.");
    }

    #endregion

    #region Set Operations

    /// <summary>
    /// Combines multiple queries using UNION (removes duplicates).
    /// </summary>
    /// <typeparam name="T">The result type of the queries.</typeparam>
    /// <param name="queries">The queries to combine.</param>
    /// <returns>A set operation builder for the combined query.</returns>
    /// <remarks>
    /// <para>All queries must have the same projection structure.</para>
    /// <para>Example:</para>
    /// <code>
    /// var results = await db.Union(
    ///     db.Users.Select(u => new { u.UserId, u.UserName }).Where(u => u.IsActive),
    ///     db.Users.Select(u => new { u.UserId, u.UserName }).Where(u => u.CreatedAt > recentDate)
    /// ).ExecuteFetchAllAsync();
    /// </code>
    /// </remarks>
    public SetOperationBuilder<T> Union<T>(params QueryBuilder<object, T>[] queries)
    {
        throw new NotImplementedException(
            "Union() requires compile-time analysis. Ensure queries are built in a fluent chain.");
    }

    /// <summary>
    /// Combines multiple queries using UNION ALL (keeps duplicates).
    /// </summary>
    /// <typeparam name="T">The result type of the queries.</typeparam>
    /// <param name="queries">The queries to combine.</param>
    /// <returns>A set operation builder for the combined query.</returns>
    public SetOperationBuilder<T> UnionAll<T>(params QueryBuilder<object, T>[] queries)
    {
        throw new NotImplementedException(
            "UnionAll() requires compile-time analysis. Ensure queries are built in a fluent chain.");
    }

    /// <summary>
    /// Returns rows from the first query that are not in any subsequent queries.
    /// </summary>
    /// <typeparam name="T">The result type of the queries.</typeparam>
    /// <param name="queries">The queries to process.</param>
    /// <returns>A set operation builder for the difference query.</returns>
    public SetOperationBuilder<T> Except<T>(params QueryBuilder<object, T>[] queries)
    {
        throw new NotImplementedException(
            "Except() requires compile-time analysis. Ensure queries are built in a fluent chain.");
    }

    /// <summary>
    /// Returns only rows that appear in all queries.
    /// </summary>
    /// <typeparam name="T">The result type of the queries.</typeparam>
    /// <param name="queries">The queries to intersect.</param>
    /// <returns>A set operation builder for the intersection query.</returns>
    public SetOperationBuilder<T> Intersect<T>(params QueryBuilder<object, T>[] queries)
    {
        throw new NotImplementedException(
            "Intersect() requires compile-time analysis. Ensure queries are built in a fluent chain.");
    }

    #endregion

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

        if (LogManager.IsEnabled(LogLevel.Debug, RawSqlLog.CategoryName))
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
            param.Value = parameters[i] ?? DBNull.Value;
            command.Parameters.Add(param);
        }

        var results = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

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
                            prop.SetValue(item, Convert.ChangeType(value, prop.PropertyType));
                        }
                        break;
                    }
                }
            }

            results.Add(item);
        }

        var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

        if (LogManager.IsEnabled(LogLevel.Debug, RawSqlLog.CategoryName))
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

        if (LogManager.IsEnabled(LogLevel.Debug, RawSqlLog.CategoryName))
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
            param.Value = parameters[i] ?? DBNull.Value;
            command.Parameters.Add(param);
        }

        var rowCount = await command.ExecuteNonQueryAsync(cancellationToken);
        var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

        if (LogManager.IsEnabled(LogLevel.Debug, RawSqlLog.CategoryName))
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

        if (LogManager.IsEnabled(LogLevel.Debug, RawSqlLog.CategoryName))
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
            param.Value = parameters[i] ?? DBNull.Value;
            command.Parameters.Add(param);
        }

        var result = await command.ExecuteScalarAsync(cancellationToken);
        var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

        if (LogManager.IsEnabled(LogLevel.Debug, RawSqlLog.CategoryName))
            RawSqlLog.ScalarResult(opId, result?.ToString() ?? "null");

        CheckSlowQuery(opId, elapsedMs, sql);

        if (result == null || result == DBNull.Value)
        {
            return default!;
        }

        return (T)Convert.ChangeType(result, typeof(T));
    }

    #endregion

    #region Internal helpers for generated interceptors

    /// <summary>
    /// Executes a raw SQL query using a generated reader delegate instead of reflection.
    /// Called by source-generated interceptors for RawSqlAsync&lt;T&gt;.
    /// </summary>
    internal async Task<List<T>> RawSqlAsyncWithReader<T>(
        string sql,
        Func<DbDataReader, T> reader,
        CancellationToken cancellationToken,
        params object?[] parameters)
    {
        ArgumentNullException.ThrowIfNull(sql);

        var opId = OpId.Next();

        if (LogManager.IsEnabled(LogLevel.Debug, RawSqlLog.CategoryName))
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
            param.Value = parameters[i] ?? DBNull.Value;
            command.Parameters.Add(param);
        }

        var results = new List<T>();
        await using var dataReader = await command.ExecuteReaderAsync(cancellationToken);

        while (await dataReader.ReadAsync(cancellationToken))
        {
            results.Add(reader(dataReader));
        }

        var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

        if (LogManager.IsEnabled(LogLevel.Debug, RawSqlLog.CategoryName))
            RawSqlLog.FetchCompleted(opId, results.Count, elapsedMs);

        CheckSlowQuery(opId, elapsedMs, sql);

        return results;
    }

    /// <summary>
    /// Executes a raw SQL scalar query with typed conversion instead of Convert.ChangeType.
    /// Called by source-generated interceptors for RawSqlScalarAsync&lt;T&gt;.
    /// </summary>
    internal async Task<T> RawSqlScalarAsyncWithConverter<T>(
        string sql,
        Func<object, T> converter,
        CancellationToken cancellationToken,
        params object?[] parameters)
    {
        ArgumentNullException.ThrowIfNull(sql);

        var opId = OpId.Next();

        if (LogManager.IsEnabled(LogLevel.Debug, RawSqlLog.CategoryName))
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
            param.Value = parameters[i] ?? DBNull.Value;
            command.Parameters.Add(param);
        }

        var result = await command.ExecuteScalarAsync(cancellationToken);
        var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

        if (LogManager.IsEnabled(LogLevel.Debug, RawSqlLog.CategoryName))
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
        if (!LogManager.IsEnabled(LogLevel.Trace, ParameterLog.CategoryName))
            return;

        for (int i = 0; i < parameters.Length; i++)
        {
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
            if (LogManager.IsEnabled(LogLevel.Warning, ExecutionLog.CategoryName))
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
            // Restore connection to original state
            if (!_connectionWasOpen && _connection.State == ConnectionState.Open)
            {
                _connection.Close();

                if (LogManager.IsEnabled(LogLevel.Information, ConnectionLog.CategoryName))
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

        // Restore connection to original state
        if (!_connectionWasOpen && _connection.State == ConnectionState.Open)
        {
            await _connection.CloseAsync().ConfigureAwait(false);

            if (LogManager.IsEnabled(LogLevel.Information, ConnectionLog.CategoryName))
                ConnectionLog.Closed();
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
