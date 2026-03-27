using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Quarry.Logging;

namespace Quarry.Internal;

/// <summary>
/// Execution mode for carrier-optimized command-based execution.
/// </summary>
internal enum ExecutionMode
{
    FetchAll,
    First,
    FirstOrDefault,
    Single,
    Scalar,
    NonQuery
}

/// <summary>
/// Internal helper for executing carrier-optimized queries.
/// All methods accept a pre-built DbCommand from the generated carrier interceptor.
/// </summary>
internal static class QueryExecutor
{
    /// <summary>
    /// Executes a carrier-optimized query with a pre-built command and returns all results as a list.
    /// </summary>
    public static async Task<List<TResult>> ExecuteCarrierWithCommandAsync<TResult>(
        long opId, IQueryExecutionContext ctx,
        DbCommand command, Func<DbDataReader, TResult> reader, CancellationToken ct)
    {
        await ctx.EnsureConnectionOpenAsync(ct).ConfigureAwait(false);

        var startTimestamp = Stopwatch.GetTimestamp();
        await using var dbReader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var results = new List<TResult>();
        try
        {
            while (await dbReader.ReadAsync(ct).ConfigureAwait(false))
            {
                results.Add(reader(dbReader));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Error, QueryLog.CategoryName) == true)
                QueryLog.QueryFailed(opId, ex);

            throw new QuarryQueryException($"Error reading query results: {ex.Message}", command.CommandText, ex);
        }

        var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

        if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Debug, QueryLog.CategoryName) == true)
            QueryLog.FetchCompleted(opId, results.Count, elapsedMs);

        CheckSlowQuery(opId, ctx, elapsedMs, command.CommandText);

        return results;
    }

    /// <summary>
    /// Executes a carrier-optimized query with a pre-built command and returns the first result.
    /// </summary>
    public static async Task<TResult> ExecuteCarrierFirstWithCommandAsync<TResult>(
        long opId, IQueryExecutionContext ctx,
        DbCommand command, Func<DbDataReader, TResult> reader, CancellationToken ct)
    {
        await ctx.EnsureConnectionOpenAsync(ct).ConfigureAwait(false);

        var startTimestamp = Stopwatch.GetTimestamp();
        await using var dbReader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        try
        {
            if (await dbReader.ReadAsync(ct).ConfigureAwait(false))
            {
                var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

                if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Debug, QueryLog.CategoryName) == true)
                    QueryLog.FetchCompleted(opId, 1, elapsedMs);

                CheckSlowQuery(opId, ctx, elapsedMs, command.CommandText);

                return reader(dbReader);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Error, QueryLog.CategoryName) == true)
                QueryLog.QueryFailed(opId, ex);

            throw new QuarryQueryException($"Error reading query results: {ex.Message}", command.CommandText, ex);
        }

        throw new InvalidOperationException("Sequence contains no elements.");
    }

    /// <summary>
    /// Executes a carrier-optimized query with a pre-built command and returns the first result or default.
    /// </summary>
    public static async Task<TResult?> ExecuteCarrierFirstOrDefaultWithCommandAsync<TResult>(
        long opId, IQueryExecutionContext ctx,
        DbCommand command, Func<DbDataReader, TResult> reader, CancellationToken ct)
    {
        await ctx.EnsureConnectionOpenAsync(ct).ConfigureAwait(false);

        var startTimestamp = Stopwatch.GetTimestamp();
        await using var dbReader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        try
        {
            if (await dbReader.ReadAsync(ct).ConfigureAwait(false))
            {
                var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

                if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Debug, QueryLog.CategoryName) == true)
                    QueryLog.FetchCompleted(opId, 1, elapsedMs);

                CheckSlowQuery(opId, ctx, elapsedMs, command.CommandText);

                return reader(dbReader);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Error, QueryLog.CategoryName) == true)
                QueryLog.QueryFailed(opId, ex);

            throw new QuarryQueryException($"Error reading query results: {ex.Message}", command.CommandText, ex);
        }

        var elapsed = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

        if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Debug, QueryLog.CategoryName) == true)
            QueryLog.FetchCompleted(opId, 0, elapsed);

        CheckSlowQuery(opId, ctx, elapsed, command.CommandText);

        return default;
    }

    /// <summary>
    /// Executes a carrier-optimized query with a pre-built command and returns exactly one result.
    /// </summary>
    public static async Task<TResult> ExecuteCarrierSingleWithCommandAsync<TResult>(
        long opId, IQueryExecutionContext ctx,
        DbCommand command, Func<DbDataReader, TResult> reader, CancellationToken ct)
    {
        await ctx.EnsureConnectionOpenAsync(ct).ConfigureAwait(false);

        var startTimestamp = Stopwatch.GetTimestamp();
        await using var dbReader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        try
        {
            if (!await dbReader.ReadAsync(ct).ConfigureAwait(false))
                throw new InvalidOperationException("Sequence contains no elements.");

            var result = reader(dbReader);

            if (await dbReader.ReadAsync(ct).ConfigureAwait(false))
                throw new InvalidOperationException("Sequence contains more than one element.");

            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

            if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Debug, QueryLog.CategoryName) == true)
                QueryLog.FetchCompleted(opId, 1, elapsedMs);

            CheckSlowQuery(opId, ctx, elapsedMs, command.CommandText);

            return result;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Error, QueryLog.CategoryName) == true)
                QueryLog.QueryFailed(opId, ex);

            throw new QuarryQueryException($"Error reading query results: {ex.Message}", command.CommandText, ex);
        }
    }

    /// <summary>
    /// Executes a carrier-optimized scalar query with a pre-built command.
    /// </summary>
    public static async Task<TScalar> ExecuteCarrierScalarWithCommandAsync<TScalar>(
        long opId, IQueryExecutionContext ctx,
        DbCommand command, CancellationToken ct)
    {
        await ctx.EnsureConnectionOpenAsync(ct).ConfigureAwait(false);

        var instrumented = LogsmithOutput.Logger != null || ctx.SlowQueryThreshold.HasValue;
        var startTimestamp = instrumented ? Stopwatch.GetTimestamp() : 0;

        try
        {
            var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);

            if (instrumented)
            {
                var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
                CheckSlowQuery(opId, ctx, elapsedMs, command.CommandText);
            }

            if (result is null or DBNull)
            {
                if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Debug, QueryLog.CategoryName) == true)
                    QueryLog.ScalarResult(opId, "null");

                if (default(TScalar) is null)
                    return default!;

                throw new InvalidOperationException("Query returned null but expected a non-nullable value.");
            }

            if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Debug, QueryLog.CategoryName) == true)
                QueryLog.ScalarResult(opId, result.ToString() ?? "null");

            return ScalarConverter.Convert<TScalar>(result);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Error, QueryLog.CategoryName) == true)
                QueryLog.QueryFailed(opId, ex);

            throw new QuarryQueryException($"Error executing scalar query: {ex.Message}", command.CommandText, ex);
        }
    }

    /// <summary>
    /// Executes a carrier-optimized non-query (DELETE/UPDATE) with a pre-built command.
    /// </summary>
    public static async Task<int> ExecuteCarrierNonQueryWithCommandAsync(
        long opId, IQueryExecutionContext ctx,
        DbCommand command, CancellationToken ct)
    {
        await ctx.EnsureConnectionOpenAsync(ct).ConfigureAwait(false);

        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            var rowCount = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

            if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Debug, QueryLog.CategoryName) == true)
                QueryLog.FetchCompleted(opId, rowCount, elapsedMs);

            CheckSlowQuery(opId, ctx, elapsedMs, command.CommandText);

            return rowCount;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Error, QueryLog.CategoryName) == true)
                QueryLog.QueryFailed(opId, ex);

            throw new QuarryQueryException($"Error executing query: {ex.Message}", command.CommandText, ex);
        }
    }

    /// <summary>
    /// Executes a carrier-optimized query with a pre-built command and returns results as an async enumerable.
    /// </summary>
    public static async IAsyncEnumerable<TResult> ToCarrierAsyncEnumerableWithCommandAsync<TResult>(
        long opId, IQueryExecutionContext ctx,
        DbCommand command, Func<DbDataReader, TResult> reader,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await ctx.EnsureConnectionOpenAsync(ct).ConfigureAwait(false);

        var startTimestamp = Stopwatch.GetTimestamp();
        await using var dbReader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        int rowCount = 0;
        while (await dbReader.ReadAsync(ct).ConfigureAwait(false))
        {
            TResult result;
            try
            {
                result = reader(dbReader);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Error, QueryLog.CategoryName) == true)
                    QueryLog.QueryFailed(opId, ex);

                throw new QuarryQueryException($"Error reading query results: {ex.Message}", command.CommandText, ex);
            }

            rowCount++;
            yield return result;
        }

        var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

        if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Debug, QueryLog.CategoryName) == true)
            QueryLog.FetchCompleted(opId, rowCount, elapsedMs);

        CheckSlowQuery(opId, ctx, elapsedMs, command.CommandText);
    }

    /// <summary>
    /// Checks if a query exceeded the slow query threshold and emits a warning.
    /// </summary>
    private static void CheckSlowQuery(long opId, IQueryExecutionContext context, double elapsedMs, string sql)
    {
        var threshold = context.SlowQueryThreshold;
        if (threshold.HasValue && elapsedMs > threshold.Value.TotalMilliseconds)
        {
            if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Warning, ExecutionLog.CategoryName) == true)
                ExecutionLog.SlowQuery(opId, elapsedMs, sql);
        }
    }
}
