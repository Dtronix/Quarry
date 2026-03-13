using System.Data.Common;
using System.Diagnostics;
using Logsmith;
using Quarry.Logging;

namespace Quarry.Internal;

/// <summary>
/// Internal helper for executing modification operations (INSERT, UPDATE, DELETE).
/// </summary>
public static class ModificationExecutor
{
    /// <summary>
    /// Executes an INSERT operation and returns the number of affected rows.
    /// </summary>
    public static Task<int> ExecuteInsertNonQueryAsync<T>(
        InsertState state,
        IReadOnlyList<T> entities,
        CancellationToken cancellationToken) where T : class
    {
        var sql = SqlModificationBuilder.BuildInsertSql(state, entities.Count);
        return ExecuteInsertNonQueryCoreAsync(state, sql, entities, cancellationToken);
    }

    /// <summary>
    /// Executes an INSERT operation and returns the generated identity value.
    /// </summary>
    public static Task<TKey> ExecuteInsertScalarAsync<T, TKey>(
        InsertState state,
        T entity,
        CancellationToken cancellationToken) where T : class
    {
        var sql = SqlModificationBuilder.BuildInsertSql(state, 1);
        var lastInsertIdQuery = SqlModificationBuilder.GetLastInsertIdQuery(state.Dialect);
        return ExecuteInsertScalarCoreAsync<T, TKey>(state, sql, lastInsertIdQuery, entity, cancellationToken);
    }

    /// <summary>
    /// Executes an UPDATE operation and returns the number of affected rows.
    /// </summary>
    public static Task<int> ExecuteUpdateNonQueryAsync(
        UpdateState state,
        CancellationToken cancellationToken)
    {
        var sql = SqlModificationBuilder.BuildUpdateSql(state);
        return ExecuteModificationCoreAsync(state.ExecutionContext, sql, state.Parameters,
            state.Timeout, state.Dialect, "UPDATE", cancellationToken);
    }

    /// <summary>
    /// Executes a DELETE operation and returns the number of affected rows.
    /// </summary>
    public static Task<int> ExecuteDeleteNonQueryAsync(
        DeleteState state,
        CancellationToken cancellationToken)
    {
        var sql = SqlModificationBuilder.BuildDeleteSql(state);
        return ExecuteModificationCoreAsync(state.ExecutionContext, sql, state.Parameters,
            state.Timeout, state.Dialect, "DELETE", cancellationToken);
    }

    /// <summary>
    /// Executes an INSERT operation with pre-built SQL and returns the number of affected rows.
    /// </summary>
    public static Task<int> ExecuteInsertNonQueryWithPrebuiltSqlAsync<T>(
        InsertState state,
        string sql,
        IReadOnlyList<T> entities,
        CancellationToken cancellationToken) where T : class
    {
        return ExecuteInsertNonQueryCoreAsync(state, sql, entities, cancellationToken);
    }

    /// <summary>
    /// Executes an INSERT operation with pre-built SQL and returns the generated identity value.
    /// </summary>
    public static Task<TKey> ExecuteInsertScalarWithPrebuiltSqlAsync<T, TKey>(
        InsertState state,
        string sql,
        string? lastInsertIdQuery,
        T entity,
        CancellationToken cancellationToken) where T : class
    {
        return ExecuteInsertScalarCoreAsync<T, TKey>(state, sql, lastInsertIdQuery, entity, cancellationToken);
    }

    /// <summary>
    /// Executes an UPDATE operation with pre-built SQL and returns the number of affected rows.
    /// </summary>
    public static Task<int> ExecuteUpdateWithPrebuiltSqlAsync(
        UpdateState state,
        string sql,
        CancellationToken cancellationToken)
    {
        return ExecuteModificationCoreAsync(state.ExecutionContext, sql, state.Parameters,
            state.Timeout, state.Dialect, "UPDATE", cancellationToken);
    }

    /// <summary>
    /// Executes a DELETE operation with pre-built SQL and returns the number of affected rows.
    /// </summary>
    public static Task<int> ExecuteDeleteWithPrebuiltSqlAsync(
        DeleteState state,
        string sql,
        CancellationToken cancellationToken)
    {
        return ExecuteModificationCoreAsync(state.ExecutionContext, sql, state.Parameters,
            state.Timeout, state.Dialect, "DELETE", cancellationToken);
    }

    private static async Task<int> ExecuteInsertNonQueryCoreAsync<T>(
        InsertState state,
        string sql,
        IReadOnlyList<T> entities,
        CancellationToken cancellationToken) where T : class
    {
        var context = state.ExecutionContext
            ?? throw new InvalidOperationException("Cannot execute insert: no execution context available. Ensure the operation was created from a QuarryContext.");

        if (entities.Count == 0)
        {
            return 0;
        }

        var opId = OpId.Next();

        if (LogManager.IsEnabled(LogLevel.Debug, ModifyLog.CategoryName))
            ModifyLog.SqlGenerated(opId, sql);

        LogParameters(opId, state.Parameters);

        await context.EnsureConnectionOpenAsync(cancellationToken).ConfigureAwait(false);

        var startTimestamp = Stopwatch.GetTimestamp();
        await using var command = CreateCommand(context.Connection, sql, state.Parameters, state.Timeout ?? context.DefaultTimeout, state.Dialect);

        try
        {
            var rowCount = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

            if (LogManager.IsEnabled(LogLevel.Debug, ModifyLog.CategoryName))
                ModifyLog.Completed(opId, "INSERT", rowCount, elapsedMs);

            CheckSlowQuery(opId, context, elapsedMs, sql);

            return rowCount;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (LogManager.IsEnabled(LogLevel.Error, ModifyLog.CategoryName))
                ModifyLog.Failed(opId, ex);

            throw new QuarryQueryException($"Error executing INSERT: {ex.Message}", sql, ex);
        }
    }

    private static async Task<TKey> ExecuteInsertScalarCoreAsync<T, TKey>(
        InsertState state,
        string sql,
        string? lastInsertIdQuery,
        T entity,
        CancellationToken cancellationToken) where T : class
    {
        var context = state.ExecutionContext
            ?? throw new InvalidOperationException("Cannot execute insert: no execution context available. Ensure the operation was created from a QuarryContext.");

        var opId = OpId.Next();

        if (LogManager.IsEnabled(LogLevel.Debug, ModifyLog.CategoryName))
            ModifyLog.SqlGenerated(opId, sql);

        LogParameters(opId, state.Parameters);

        await context.EnsureConnectionOpenAsync(cancellationToken).ConfigureAwait(false);

        var startTimestamp = Stopwatch.GetTimestamp();
        await using var command = CreateCommand(context.Connection, sql, state.Parameters, state.Timeout ?? context.DefaultTimeout, state.Dialect);

        try
        {
            if (lastInsertIdQuery != null)
            {
                // MySQL path: Execute INSERT, then SELECT LAST_INSERT_ID()
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                command.CommandText = lastInsertIdQuery;
                command.Parameters.Clear();

                if (LogManager.IsEnabled(LogLevel.Debug, ModifyLog.CategoryName))
                    ModifyLog.SqlGenerated(opId, lastInsertIdQuery);

                var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

                if (LogManager.IsEnabled(LogLevel.Debug, ModifyLog.CategoryName))
                    ModifyLog.Completed(opId, "INSERT", 1, elapsedMs);

                CheckSlowQuery(opId, context, elapsedMs, sql);

                return ConvertScalar<TKey>(result);
            }
            else
            {
                // SQLite/PostgreSQL/SQL Server path: Use RETURNING/OUTPUT
                var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

                if (LogManager.IsEnabled(LogLevel.Debug, ModifyLog.CategoryName))
                    ModifyLog.Completed(opId, "INSERT", 1, elapsedMs);

                CheckSlowQuery(opId, context, elapsedMs, sql);

                return ConvertScalar<TKey>(result);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (LogManager.IsEnabled(LogLevel.Error, ModifyLog.CategoryName))
                ModifyLog.Failed(opId, ex);

            throw new QuarryQueryException($"Error executing INSERT: {ex.Message}", sql, ex);
        }
    }

    private static async Task<int> ExecuteModificationCoreAsync(
        IQueryExecutionContext? executionContext,
        string sql,
        List<ModificationParameter> parameters,
        TimeSpan? timeout,
        SqlDialect dialect,
        string operationName,
        CancellationToken cancellationToken)
    {
        var context = executionContext
            ?? throw new InvalidOperationException($"Cannot execute {operationName.ToLowerInvariant()}: no execution context available. Ensure the operation was created from a QuarryContext.");

        var opId = OpId.Next();

        if (LogManager.IsEnabled(LogLevel.Debug, ModifyLog.CategoryName))
            ModifyLog.SqlGenerated(opId, sql);

        LogParameters(opId, parameters);

        await context.EnsureConnectionOpenAsync(cancellationToken).ConfigureAwait(false);

        var startTimestamp = Stopwatch.GetTimestamp();
        await using var command = CreateCommand(context.Connection, sql, parameters, timeout ?? context.DefaultTimeout, dialect);

        try
        {
            var rowCount = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

            if (LogManager.IsEnabled(LogLevel.Debug, ModifyLog.CategoryName))
                ModifyLog.Completed(opId, operationName, rowCount, elapsedMs);

            CheckSlowQuery(opId, context, elapsedMs, sql);

            return rowCount;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (LogManager.IsEnabled(LogLevel.Error, ModifyLog.CategoryName))
                ModifyLog.Failed(opId, ex);

            throw new QuarryQueryException($"Error executing {operationName}: {ex.Message}", sql, ex);
        }
    }

    /// <summary>
    /// Logs modification parameters at Trace level.
    /// </summary>
    private static void LogParameters(long opId, List<ModificationParameter> parameters)
    {
        if (!LogManager.IsEnabled(LogLevel.Trace, ParameterLog.CategoryName))
            return;

        foreach (var param in parameters)
        {
            var displayValue = param.IsSensitive ? "***" : (param.Value?.ToString() ?? "null");
            ParameterLog.Bound(opId, param.Index, displayValue);
        }
    }

    /// <summary>
    /// Checks if an operation exceeded the slow query threshold and emits a warning.
    /// </summary>
    private static void CheckSlowQuery(long opId, IQueryExecutionContext context, double elapsedMs, string sql)
    {
        var threshold = context.SlowQueryThreshold;
        if (threshold.HasValue && elapsedMs > threshold.Value.TotalMilliseconds)
        {
            if (LogManager.IsEnabled(LogLevel.Warning, ExecutionLog.CategoryName))
                ExecutionLog.SlowQuery(opId, elapsedMs, sql);
        }
    }

    /// <summary>
    /// Creates a command with parameters from modification state.
    /// </summary>
    private static DbCommand CreateCommand(
        DbConnection connection,
        string sql,
        List<ModificationParameter> parameters,
        TimeSpan timeout,
        SqlDialect dialect = SqlDialect.SQLite)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = (int)timeout.TotalSeconds;

        // Add parameters
        foreach (var param in parameters)
        {
            var dbParam = command.CreateParameter();
            dbParam.ParameterName = $"@p{param.Index}";
            dbParam.Value = param.Value ?? DBNull.Value;
            param.DialectConfigurator?.ConfigureParameter(dialect, dbParam);
            command.Parameters.Add(dbParam);
        }

        return command;
    }

    /// <summary>
    /// Converts a scalar database result to the target type.
    /// </summary>
    private static TKey ConvertScalar<TKey>(object? result)
    {
        if (result is null or DBNull)
        {
            if (default(TKey) is null)
            {
                return default!;
            }
            throw new InvalidOperationException("INSERT returned null but expected a non-nullable identity value.");
        }

        var targetType = Nullable.GetUnderlyingType(typeof(TKey)) ?? typeof(TKey);
        return (TKey)Convert.ChangeType(result, targetType);
    }
}
