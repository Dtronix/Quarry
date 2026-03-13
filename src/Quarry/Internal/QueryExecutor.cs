using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Logsmith;
using Quarry.Logging;

namespace Quarry.Internal;

/// <summary>
/// Internal helper for executing queries.
/// </summary>
internal static class QueryExecutor
{
    /// <summary>
    /// Executes a query and returns all results as a list.
    /// </summary>
    public static Task<List<TResult>> ExecuteFetchAllAsync<TResult>(
        QueryState state,
        Func<DbDataReader, TResult> reader,
        CancellationToken cancellationToken)
    {
        var sql = SqlBuilder.BuildSelectSql(state);
        return ExecuteFetchAllCoreAsync(state, sql, reader, cancellationToken);
    }

    /// <summary>
    /// Promotes Limit/Offset integer values to query parameters for the pre-built SQL path.
    /// The pre-built SQL contains parameterized LIMIT @pN / OFFSET @pM placeholders,
    /// but the runtime Limit()/Offset() methods set integer properties rather than parameters.
    /// </summary>
    /// <remarks>
    /// Integer Limit/Offset properties and parameter-based OffsetParameterIndex/LimitParameterIndex
    /// are mutually exclusive: the prebuilt chain interceptors use integer properties (via Limit()/Offset()),
    /// while the pre-built SQL references parameterized placeholders. This method bridges the two by
    /// promoting integer values to parameters. If both were already set, the parameter would be duplicated,
    /// but the generator guarantees this cannot happen.
    /// </remarks>
    private static QueryState PromotePaginationParameters(QueryState state)
    {
        // Only promote if the integer property is set but no parameter index exists yet
        if (state.Limit.HasValue && !state.LimitParameterIndex.HasValue)
            state = state.WithLimitParameter(state.Limit.Value);
        if (state.Offset.HasValue && !state.OffsetParameterIndex.HasValue)
            state = state.WithOffsetParameter(state.Offset.Value);
        return state;
    }

    /// <summary>
    /// Executes a query with pre-built SQL and returns all results as a list.
    /// </summary>
    public static Task<List<TResult>> ExecuteWithPrebuiltSqlAsync<TResult>(
        QueryState state,
        string sql,
        Func<DbDataReader, TResult> reader,
        CancellationToken cancellationToken)
    {
        state = PromotePaginationParameters(state);
        return ExecuteFetchAllCoreAsync(state, sql, reader, cancellationToken);
    }

    /// <summary>
    /// Executes a query and returns the first result.
    /// </summary>
    public static Task<TResult> ExecuteFetchFirstAsync<TResult>(
        QueryState state,
        Func<DbDataReader, TResult> reader,
        CancellationToken cancellationToken)
    {
        var sql = SqlBuilder.BuildSelectSql(state);
        return ExecuteFetchFirstCoreAsync(state, sql, reader, cancellationToken);
    }

    /// <summary>
    /// Executes a query with pre-built SQL and returns the first result.
    /// </summary>
    public static Task<TResult> ExecuteFirstWithPrebuiltSqlAsync<TResult>(
        QueryState state,
        string sql,
        Func<DbDataReader, TResult> reader,
        CancellationToken cancellationToken)
    {
        state = PromotePaginationParameters(state);
        return ExecuteFetchFirstCoreAsync(state, sql, reader, cancellationToken);
    }

    /// <summary>
    /// Executes a query and returns the first result or default.
    /// </summary>
    public static Task<TResult?> ExecuteFetchFirstOrDefaultAsync<TResult>(
        QueryState state,
        Func<DbDataReader, TResult> reader,
        CancellationToken cancellationToken)
    {
        var sql = SqlBuilder.BuildSelectSql(state);
        return ExecuteFetchFirstOrDefaultCoreAsync(state, sql, reader, cancellationToken);
    }

    /// <summary>
    /// Executes a query with pre-built SQL and returns the first result or default.
    /// </summary>
    public static Task<TResult?> ExecuteFirstOrDefaultWithPrebuiltSqlAsync<TResult>(
        QueryState state,
        string sql,
        Func<DbDataReader, TResult> reader,
        CancellationToken cancellationToken)
    {
        state = PromotePaginationParameters(state);
        return ExecuteFetchFirstOrDefaultCoreAsync(state, sql, reader, cancellationToken);
    }

    /// <summary>
    /// Executes a query and returns exactly one result.
    /// </summary>
    public static Task<TResult> ExecuteFetchSingleAsync<TResult>(
        QueryState state,
        Func<DbDataReader, TResult> reader,
        CancellationToken cancellationToken)
    {
        var sql = SqlBuilder.BuildSelectSql(state);
        return ExecuteFetchSingleCoreAsync(state, sql, reader, cancellationToken);
    }

    /// <summary>
    /// Executes a query with pre-built SQL and returns exactly one result.
    /// </summary>
    public static Task<TResult> ExecuteSingleWithPrebuiltSqlAsync<TResult>(
        QueryState state,
        string sql,
        Func<DbDataReader, TResult> reader,
        CancellationToken cancellationToken)
    {
        state = PromotePaginationParameters(state);
        return ExecuteFetchSingleCoreAsync(state, sql, reader, cancellationToken);
    }

    /// <summary>
    /// Executes a query and returns the scalar result.
    /// </summary>
    public static Task<TScalar> ExecuteScalarAsync<TScalar>(
        QueryState state,
        CancellationToken cancellationToken)
    {
        var sql = SqlBuilder.BuildSelectSql(state);
        return ExecuteScalarCoreAsync<TScalar>(state, sql, cancellationToken);
    }

    /// <summary>
    /// Executes a query with pre-built SQL and returns the scalar result.
    /// </summary>
    public static Task<TScalar> ExecuteScalarWithPrebuiltSqlAsync<TScalar>(
        QueryState state,
        string sql,
        CancellationToken cancellationToken)
    {
        return ExecuteScalarCoreAsync<TScalar>(state, sql, cancellationToken);
    }

    /// <summary>
    /// Executes a query and returns the number of affected rows.
    /// </summary>
    public static async Task<int> ExecuteNonQueryAsync(
        QueryState state,
        CancellationToken cancellationToken)
    {
        var context = state.ExecutionContext
            ?? throw new InvalidOperationException("Cannot execute query: no execution context available. Ensure the query was created from a QuarryContext.");

        var opId = OpId.Next();
        var sql = SqlBuilder.BuildSelectSql(state);

        if (LogManager.IsEnabled(LogLevel.Debug, QueryLog.CategoryName))
            QueryLog.SqlGenerated(opId, sql);

        LogParameters(opId, state);

        await context.EnsureConnectionOpenAsync(cancellationToken).ConfigureAwait(false);

        var startTimestamp = Stopwatch.GetTimestamp();
        await using var command = CreateCommand(context.Connection, sql, state, context.DefaultTimeout);

        try
        {
            var rowCount = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

            if (LogManager.IsEnabled(LogLevel.Debug, QueryLog.CategoryName))
                QueryLog.FetchCompleted(opId, rowCount, elapsedMs);

            CheckSlowQuery(opId, context, elapsedMs, sql);

            return rowCount;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (LogManager.IsEnabled(LogLevel.Error, QueryLog.CategoryName))
                QueryLog.QueryFailed(opId, ex);

            throw new QuarryQueryException($"Error executing query: {ex.Message}", sql, ex);
        }
    }

    /// <summary>
    /// Executes a query and returns results as an async enumerable.
    /// </summary>
    public static IAsyncEnumerable<TResult> ToAsyncEnumerable<TResult>(
        QueryState state,
        Func<DbDataReader, TResult> reader,
        CancellationToken cancellationToken)
    {
        var sql = SqlBuilder.BuildSelectSql(state);
        return ToAsyncEnumerableCore(state, sql, reader, cancellationToken);
    }

    /// <summary>
    /// Executes a query with pre-built SQL and returns results as an async enumerable.
    /// </summary>
    public static IAsyncEnumerable<TResult> ToAsyncEnumerableWithPrebuiltSql<TResult>(
        QueryState state,
        string sql,
        Func<DbDataReader, TResult> reader,
        CancellationToken cancellationToken)
    {
        return ToAsyncEnumerableCore(state, sql, reader, cancellationToken);
    }

    #region Core implementations

    private static async Task<List<TResult>> ExecuteFetchAllCoreAsync<TResult>(
        QueryState state,
        string sql,
        Func<DbDataReader, TResult> reader,
        CancellationToken cancellationToken)
    {
        var context = state.ExecutionContext
            ?? throw new InvalidOperationException("Cannot execute query: no execution context available. Ensure the query was created from a QuarryContext.");

        var opId = OpId.Next();

        if (LogManager.IsEnabled(LogLevel.Debug, QueryLog.CategoryName))
            QueryLog.SqlGenerated(opId, sql);

        LogParameters(opId, state);

        await context.EnsureConnectionOpenAsync(cancellationToken).ConfigureAwait(false);

        var startTimestamp = Stopwatch.GetTimestamp();
        await using var command = CreateCommand(context.Connection, sql, state, context.DefaultTimeout);
        await using var dbReader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var results = new List<TResult>();
        try
        {
            while (await dbReader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(reader(dbReader));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (LogManager.IsEnabled(LogLevel.Error, QueryLog.CategoryName))
                QueryLog.QueryFailed(opId, ex);

            throw new QuarryQueryException($"Error reading query results: {ex.Message}", sql, ex);
        }

        var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

        if (LogManager.IsEnabled(LogLevel.Debug, QueryLog.CategoryName))
            QueryLog.FetchCompleted(opId, results.Count, elapsedMs);

        CheckSlowQuery(opId, context, elapsedMs, sql);

        return results;
    }

    private static async Task<TResult> ExecuteFetchFirstCoreAsync<TResult>(
        QueryState state,
        string sql,
        Func<DbDataReader, TResult> reader,
        CancellationToken cancellationToken)
    {
        var context = state.ExecutionContext
            ?? throw new InvalidOperationException("Cannot execute query: no execution context available. Ensure the query was created from a QuarryContext.");

        var opId = OpId.Next();

        if (LogManager.IsEnabled(LogLevel.Debug, QueryLog.CategoryName))
            QueryLog.SqlGenerated(opId, sql);

        LogParameters(opId, state);

        await context.EnsureConnectionOpenAsync(cancellationToken).ConfigureAwait(false);

        var startTimestamp = Stopwatch.GetTimestamp();
        await using var command = CreateCommand(context.Connection, sql, state, context.DefaultTimeout);
        await using var dbReader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (await dbReader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

                if (LogManager.IsEnabled(LogLevel.Debug, QueryLog.CategoryName))
                    QueryLog.FetchCompleted(opId, 1, elapsedMs);

                CheckSlowQuery(opId, context, elapsedMs, sql);

                return reader(dbReader);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (LogManager.IsEnabled(LogLevel.Error, QueryLog.CategoryName))
                QueryLog.QueryFailed(opId, ex);

            throw new QuarryQueryException($"Error reading query results: {ex.Message}", sql, ex);
        }

        throw new InvalidOperationException("Sequence contains no elements.");
    }

    private static async Task<TResult?> ExecuteFetchFirstOrDefaultCoreAsync<TResult>(
        QueryState state,
        string sql,
        Func<DbDataReader, TResult> reader,
        CancellationToken cancellationToken)
    {
        var context = state.ExecutionContext
            ?? throw new InvalidOperationException("Cannot execute query: no execution context available. Ensure the query was created from a QuarryContext.");

        var opId = OpId.Next();

        if (LogManager.IsEnabled(LogLevel.Debug, QueryLog.CategoryName))
            QueryLog.SqlGenerated(opId, sql);

        LogParameters(opId, state);

        await context.EnsureConnectionOpenAsync(cancellationToken).ConfigureAwait(false);

        var startTimestamp = Stopwatch.GetTimestamp();
        await using var command = CreateCommand(context.Connection, sql, state, context.DefaultTimeout);
        await using var dbReader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (await dbReader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

                if (LogManager.IsEnabled(LogLevel.Debug, QueryLog.CategoryName))
                    QueryLog.FetchCompleted(opId, 1, elapsedMs);

                CheckSlowQuery(opId, context, elapsedMs, sql);

                return reader(dbReader);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (LogManager.IsEnabled(LogLevel.Error, QueryLog.CategoryName))
                QueryLog.QueryFailed(opId, ex);

            throw new QuarryQueryException($"Error reading query results: {ex.Message}", sql, ex);
        }

        var elapsed = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

        if (LogManager.IsEnabled(LogLevel.Debug, QueryLog.CategoryName))
            QueryLog.FetchCompleted(opId, 0, elapsed);

        CheckSlowQuery(opId, context, elapsed, sql);

        return default;
    }

    private static async Task<TResult> ExecuteFetchSingleCoreAsync<TResult>(
        QueryState state,
        string sql,
        Func<DbDataReader, TResult> reader,
        CancellationToken cancellationToken)
    {
        var context = state.ExecutionContext
            ?? throw new InvalidOperationException("Cannot execute query: no execution context available. Ensure the query was created from a QuarryContext.");

        var opId = OpId.Next();

        if (LogManager.IsEnabled(LogLevel.Debug, QueryLog.CategoryName))
            QueryLog.SqlGenerated(opId, sql);

        LogParameters(opId, state);

        await context.EnsureConnectionOpenAsync(cancellationToken).ConfigureAwait(false);

        var startTimestamp = Stopwatch.GetTimestamp();
        await using var command = CreateCommand(context.Connection, sql, state, context.DefaultTimeout);
        await using var dbReader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (!await dbReader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Sequence contains no elements.");
            }

            var result = reader(dbReader);

            if (await dbReader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Sequence contains more than one element.");
            }

            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

            if (LogManager.IsEnabled(LogLevel.Debug, QueryLog.CategoryName))
                QueryLog.FetchCompleted(opId, 1, elapsedMs);

            CheckSlowQuery(opId, context, elapsedMs, sql);

            return result;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (LogManager.IsEnabled(LogLevel.Error, QueryLog.CategoryName))
                QueryLog.QueryFailed(opId, ex);

            throw new QuarryQueryException($"Error reading query results: {ex.Message}", sql, ex);
        }
    }

    private static async Task<TScalar> ExecuteScalarCoreAsync<TScalar>(
        QueryState state,
        string sql,
        CancellationToken cancellationToken)
    {
        var context = state.ExecutionContext
            ?? throw new InvalidOperationException("Cannot execute query: no execution context available. Ensure the query was created from a QuarryContext.");

        var opId = OpId.Next();

        if (LogManager.IsEnabled(LogLevel.Debug, QueryLog.CategoryName))
            QueryLog.SqlGenerated(opId, sql);

        LogParameters(opId, state);

        await context.EnsureConnectionOpenAsync(cancellationToken).ConfigureAwait(false);

        var startTimestamp = Stopwatch.GetTimestamp();
        await using var command = CreateCommand(context.Connection, sql, state, context.DefaultTimeout);

        try
        {
            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            CheckSlowQuery(opId, context, elapsedMs, sql);

            if (result is null or DBNull)
            {
                if (LogManager.IsEnabled(LogLevel.Debug, QueryLog.CategoryName))
                    QueryLog.ScalarResult(opId, "null");

                if (default(TScalar) is null)
                {
                    return default!;
                }
                throw new InvalidOperationException("Query returned null but expected a non-nullable value.");
            }

            if (LogManager.IsEnabled(LogLevel.Debug, QueryLog.CategoryName))
                QueryLog.ScalarResult(opId, result.ToString() ?? "null");

            return (TScalar)Convert.ChangeType(result, Nullable.GetUnderlyingType(typeof(TScalar)) ?? typeof(TScalar));
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (LogManager.IsEnabled(LogLevel.Error, QueryLog.CategoryName))
                QueryLog.QueryFailed(opId, ex);

            throw new QuarryQueryException($"Error executing scalar query: {ex.Message}", sql, ex);
        }
    }

    private static async IAsyncEnumerable<TResult> ToAsyncEnumerableCore<TResult>(
        QueryState state,
        string sql,
        Func<DbDataReader, TResult> reader,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var context = state.ExecutionContext
            ?? throw new InvalidOperationException("Cannot execute query: no execution context available. Ensure the query was created from a QuarryContext.");

        var opId = OpId.Next();

        if (LogManager.IsEnabled(LogLevel.Debug, QueryLog.CategoryName))
            QueryLog.SqlGenerated(opId, sql);

        LogParameters(opId, state);

        await context.EnsureConnectionOpenAsync(cancellationToken).ConfigureAwait(false);

        var startTimestamp = Stopwatch.GetTimestamp();
        await using var command = CreateCommand(context.Connection, sql, state, context.DefaultTimeout);
        await using var dbReader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        int rowCount = 0;
        while (await dbReader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            TResult result;
            try
            {
                result = reader(dbReader);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (LogManager.IsEnabled(LogLevel.Error, QueryLog.CategoryName))
                    QueryLog.QueryFailed(opId, ex);

                throw new QuarryQueryException($"Error reading query results: {ex.Message}", sql, ex);
            }

            rowCount++;
            yield return result;
        }

        var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

        if (LogManager.IsEnabled(LogLevel.Debug, QueryLog.CategoryName))
            QueryLog.FetchCompleted(opId, rowCount, elapsedMs);

        CheckSlowQuery(opId, context, elapsedMs, sql);
    }

    #endregion

    /// <summary>
    /// Logs query parameters at Trace level.
    /// </summary>
    private static void LogParameters(long opId, QueryState state)
    {
        if (!LogManager.IsEnabled(LogLevel.Trace, ParameterLog.CategoryName))
            return;

        foreach (var param in state.Parameters)
        {
            ParameterLog.Bound(opId, param.Index, param.Value?.ToString() ?? "null");
        }
    }

    /// <summary>
    /// Checks if a query exceeded the slow query threshold and emits a warning.
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
    /// Creates a command with parameters.
    /// </summary>
    private static DbCommand CreateCommand(DbConnection connection, string sql, QueryState state, TimeSpan defaultTimeout)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = (int)(state.Timeout ?? defaultTimeout).TotalSeconds;

        // Add parameters
        foreach (var param in state.Parameters)
        {
            var dbParam = command.CreateParameter();
            dbParam.ParameterName = $"@p{param.Index}";

            var originalType = param.Value?.GetType();
            dbParam.Value = NormalizeParameterValue(param.Value);

            // Apply dialect-aware parameter configuration if the original value
            // had a registered IDialectAwareTypeMapping
            if (originalType != null)
                TypeMappingRegistry.TryConfigureParameter(originalType, state.Dialect, dbParam);

            command.Parameters.Add(dbParam);
        }

        return command;
    }

    /// <summary>
    /// Normalizes a parameter value for ADO.NET binding.
    /// Converts enum values to their underlying integral type and applies
    /// registered TypeMapping conversions for custom types.
    /// </summary>
    private static object NormalizeParameterValue(object? value)
    {
        if (value is null)
            return DBNull.Value;

        var type = value.GetType();
        if (type.IsEnum)
            return Convert.ChangeType(value, Enum.GetUnderlyingType(type));

        if (TypeMappingRegistry.TryConvert(type, value, out var converted))
            return converted;

        return value;
    }
}
