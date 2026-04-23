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

    #region CTE (Common Table Expressions)

    /// <summary>
    /// Defines a Common Table Expression (CTE) from an inner query built via lambda.
    /// The lambda receives an entity accessor for <typeparamref name="TDto"/> and returns the inner query chain.
    /// </summary>
    /// <remarks>
    /// The generated derived context shadows this method via <c>new</c>. Interceptors only fire
    /// when the call site resolves to the derived overload, so callers must hold the variable
    /// as their generated context type, not as <see cref="QuarryContext"/>.
    /// <para>
    /// For contexts that combine CTEs with entity accessors (e.g.,
    /// <c>db.With&lt;A&gt;(a =&gt; a.Where(...)).Users().Join&lt;Order&gt;(...)</c>), inherit from
    /// <see cref="QuarryContext{TSelf}"/> instead. The generic subclass overrides this method
    /// with a covariant return type so the source generator's SemanticModel can resolve the
    /// full chain without synthetic fallback discovery.
    /// </para>
    /// </remarks>
    /// <seealso cref="QuarryContext{TSelf}"/>
    /// <typeparam name="TDto">The DTO type whose properties define the CTE columns.</typeparam>
    /// <param name="innerBuilder">A lambda that receives an entity accessor and builds the inner query.</param>
    /// <returns>This context for method chaining.</returns>
    public virtual QuarryContext With<TDto>(Func<IEntityAccessor<TDto>, IQueryBuilder<TDto>> innerBuilder) where TDto : class
        => throw new NotSupportedException("CTE methods must be intercepted by the Quarry source generator. If you reach this exception, your context variable is typed as `QuarryContext` (the base class) instead of your generated derived context type — interceptors only fire when the call site resolves to the derived `new` overload. Type the variable as your generated context (e.g. `MyDb`) instead.");

    /// <summary>
    /// Defines a Common Table Expression (CTE) from an inner query with a projection built via lambda.
    /// The lambda receives an entity accessor for <typeparamref name="TEntity"/> and returns the projected inner query chain.
    /// </summary>
    /// <remarks>
    /// <inheritdoc cref="With{TDto}(Func{IEntityAccessor{TDto}, IQueryBuilder{TDto}})" path="/remarks"/>
    /// </remarks>
    /// <typeparam name="TEntity">The entity type of the inner query.</typeparam>
    /// <typeparam name="TDto">The projected DTO type whose properties define the CTE columns.</typeparam>
    /// <param name="innerBuilder">A lambda that receives an entity accessor and builds the projected inner query.</param>
    /// <returns>This context for method chaining.</returns>
    public virtual QuarryContext With<TEntity, TDto>(Func<IEntityAccessor<TEntity>, IQueryBuilder<TEntity, TDto>> innerBuilder)
        where TEntity : class where TDto : class
        => throw new NotSupportedException("CTE methods must be intercepted by the Quarry source generator. If you reach this exception, your context variable is typed as `QuarryContext` (the base class) instead of your generated derived context type — interceptors only fire when the call site resolves to the derived `new` overload. Type the variable as your generated context (e.g. `MyDb`) instead.");

    /// <summary>
    /// Starts a query from a previously defined CTE as the primary table.
    /// Must be preceded by a With&lt;TDto&gt;() call that defines the CTE.
    /// </summary>
    /// <typeparam name="TDto">The CTE DTO type to query from.</typeparam>
    /// <returns>An entity accessor for the CTE.</returns>
    public IEntityAccessor<TDto> FromCte<TDto>() where TDto : class
        => throw new NotSupportedException("CTE methods must be intercepted by the Quarry source generator. If you reach this exception, your context variable is typed as `QuarryContext` (the base class) instead of your generated derived context type — interceptors only fire when the call site resolves to the derived `new` overload. Type the variable as your generated context (e.g. `MyDb`) instead.");

    #endregion

    #region Raw SQL

    /// <summary>
    /// Executes a raw SQL query and streams results as an async enumerable.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="sql">The SQL query to execute. Use <c>@p0</c>, <c>@p1</c>, etc. for parameter placeholders on every dialect, including PostgreSQL — Npgsql rewrites <c>@name</c> markers to native positional form internally, so the same SQL works across providers. Do not mix conventions by using native <c>$N</c> placeholders: the <c>DbParameter.ParameterName</c> assigned here is always <c>@pN</c>, and Npgsql 10 strict binding requires the placeholder and the name to agree.</param>
    /// <param name="parameters">Optional parameter values.</param>
    /// <returns>An async enumerable of results.</returns>
    /// <remarks>
    /// <para>This method requires source generation — the Quarry generator emits a typed
    /// reader delegate at compile time. Calling with an open generic type parameter will
    /// produce compile error QRY031.</para>
    /// <para>Example:</para>
    /// <code>
    /// // Stream rows
    /// await foreach (var user in db.RawSqlAsync&lt;UserDto&gt;("SELECT ..."))
    ///     Process(user);
    ///
    /// // Buffer into list
    /// var results = await db.RawSqlAsync&lt;UserDto&gt;(
    ///     "SELECT user_id, user_name FROM users WHERE created_at > @p0",
    ///     DateTime.UtcNow.AddDays(-7)).ToListAsync();
    /// </code>
    /// </remarks>
    public IAsyncEnumerable<T> RawSqlAsync<T>(
        string sql,
        params object?[] parameters)
        => RawSqlAsync<T>(sql, CancellationToken.None, parameters);

    /// <summary>
    /// Executes a raw SQL query and streams results as an async enumerable.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="sql">The SQL query to execute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="parameters">Optional parameter values.</param>
    /// <returns>An async enumerable of results.</returns>
    public IAsyncEnumerable<T> RawSqlAsync<T>(
        string sql,
        CancellationToken cancellationToken,
        params object?[] parameters)
        => throw new NotSupportedException(
            "RawSqlAsync<T> requires source generation. Ensure Quarry.Generator is referenced as an analyzer.");

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

        // Add parameters. The @pN naming matches the documented RawSql convention
        // (callers write @p0, @p1, ... in their SQL). Portable across dialects:
        // SQLite/SqlServer bind by name; Npgsql translates @name to $N positional;
        // MySqlConnector is positional and ignores the name. Do not switch this to
        // dialect-aware $N — it would break users who follow the @pN contract on PG.
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
    /// Executes a raw SQL query using a generated reader delegate and streams results
    /// as an async enumerable. Called by source-generated interceptors for RawSqlAsync&lt;T&gt;.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public IAsyncEnumerable<T> RawSqlAsyncWithReader<T>(
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

        var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = (int)_defaultTimeout.TotalSeconds;

        for (int i = 0; i < parameters.Length; i++)
        {
            var param = command.CreateParameter();
            param.ParameterName = $"@p{i}";
            param.Value = (parameters[i] is SensitiveParameter sp ? sp.Value : parameters[i]) ?? DBNull.Value;
            command.Parameters.Add(param);
        }

        return QueryExecutor.ToCarrierAsyncEnumerableWithCommandAsync(opId, this, command, reader, CommandBehavior.SingleResult, cancellationToken);
    }

    /// <summary>
    /// Executes a raw SQL query using a struct-based row reader that caches column ordinals.
    /// Called by source-generated interceptors for RawSqlAsync&lt;T&gt; (DTO path).
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public IAsyncEnumerable<T> RawSqlAsyncWithReader<T, TReader>(
        string sql,
        CancellationToken cancellationToken,
        params object?[] parameters)
        where TReader : struct, IRowReader<T>
    {
        ArgumentNullException.ThrowIfNull(sql);

        var opId = OpId.Next();

        if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Debug, RawSqlLog.CategoryName) == true)
            RawSqlLog.SqlGenerated(opId, sql);

        LogRawParameters(opId, parameters);

        var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = (int)_defaultTimeout.TotalSeconds;

        for (int i = 0; i < parameters.Length; i++)
        {
            var param = command.CreateParameter();
            param.ParameterName = $"@p{i}";
            param.Value = (parameters[i] is SensitiveParameter sp ? sp.Value : parameters[i]) ?? DBNull.Value;
            command.Parameters.Add(param);
        }

        return QueryExecutor.ToCarrierAsyncEnumerableWithCommandAsync<T, TReader>(opId, this, command, CommandBehavior.SingleResult, cancellationToken);
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

/// <summary>
/// Generic base class for Quarry database contexts that provides typed CTE chain support.
/// Inherit from this class instead of <see cref="QuarryContext"/> when your queries combine
/// <c>With&lt;TDto&gt;(...)</c> with entity accessors and further builder methods
/// (e.g., <c>db.With&lt;A&gt;(inner).Users().Join&lt;Order&gt;(...).Select(...)</c>).
/// </summary>
/// <remarks>
/// <para>
/// The source generator's discovery pass runs against a SemanticModel that contains only the
/// user's source — the generator's own output is invisible to itself. By placing the typed
/// <c>With</c> overloads in a hand-written base class (this class), the correct return type
/// (<typeparamref name="TSelf"/>) is visible to discovery and the rest of the chain resolves
/// normally without syntactic fallback workarounds.
/// </para>
/// <para>
/// Usage:
/// <code>
/// [QuarryContext(Dialect = SqlDialect.SQLite)]
/// public partial class AppDbContext : QuarryContext&lt;AppDbContext&gt;
/// {
///     public AppDbContext(IDbConnection connection) : base(connection) { }
/// }
/// </code>
/// </para>
/// </remarks>
/// <typeparam name="TSelf">
/// The concrete derived context type (CRTP). The self-referencing constraint ensures the
/// <c>With</c> overloads return the correct derived type for fluent chaining.
/// </typeparam>
public abstract class QuarryContext<TSelf> : QuarryContext
    where TSelf : QuarryContext<TSelf>
{
    /// <inheritdoc cref="QuarryContext(IDbConnection)"/>
    protected QuarryContext(IDbConnection connection)
        : base(connection)
    {
    }

    /// <inheritdoc cref="QuarryContext(IDbConnection, bool)"/>
    protected QuarryContext(IDbConnection connection, bool ownsConnection)
        : base(connection, ownsConnection)
    {
    }

    /// <inheritdoc cref="QuarryContext(IDbConnection, bool, TimeSpan?, IsolationLevel?)"/>
    protected QuarryContext(
        IDbConnection connection,
        bool ownsConnection,
        TimeSpan? defaultTimeout,
        IsolationLevel? defaultIsolation)
        : base(connection, ownsConnection, defaultTimeout, defaultIsolation)
    {
    }

    /// <inheritdoc cref="QuarryContext.With{TDto}(Func{IEntityAccessor{TDto}, IQueryBuilder{TDto}})"/>
    public override TSelf With<TDto>(Func<IEntityAccessor<TDto>, IQueryBuilder<TDto>> innerBuilder)
        => throw new NotSupportedException(
            "CTE methods must be intercepted by the Quarry source generator. " +
            "If you reach this exception your context variable is typed as the abstract " +
            "base class instead of your generated derived type — interceptors only fire " +
            "when the call site resolves to the derived overload.");

    /// <inheritdoc cref="QuarryContext.With{TEntity, TDto}(Func{IEntityAccessor{TEntity}, IQueryBuilder{TEntity, TDto}})"/>
    public override TSelf With<TEntity, TDto>(Func<IEntityAccessor<TEntity>, IQueryBuilder<TEntity, TDto>> innerBuilder)
        => throw new NotSupportedException(
            "CTE methods must be intercepted by the Quarry source generator. " +
            "If you reach this exception your context variable is typed as the abstract " +
            "base class instead of your generated derived type — interceptors only fire " +
            "when the call site resolves to the derived overload.");
}
