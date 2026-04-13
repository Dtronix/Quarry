using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Quarry.Logging;

namespace Quarry.Internal;

/// <summary>
/// Executes carrier-optimized queries with pre-built commands.
/// This type is used by generated interceptor code and is not intended for direct use.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class QueryExecutor
{
    /// <summary>
    /// Executes a carrier-optimized query with a pre-built command and materializes all results into a list.
    /// </summary>
    public static async Task<List<TResult>> ExecuteCarrierWithCommandAsync<TResult>(
        long opId, QuarryContext ctx,
        DbCommand command, Func<DbDataReader, TResult> reader,
        CommandBehavior behavior, CancellationToken ct)
    {
        await using var _cmd = command;

        if (ctx.Connection.State != ConnectionState.Open)
            await ctx.EnsureConnectionOpenAsync(ct).ConfigureAwait(false);

        var startTimestamp = Stopwatch.GetTimestamp();
        await using var dbReader = await command.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);

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
            ReportReaderFailure(opId, command, ex);
        }

        FinalizeQuery(opId, ctx, startTimestamp, results.Count, command.CommandText);
        return results;
    }

    /// <summary>
    /// Executes a carrier-optimized query with a pre-built command and returns the first result.
    /// </summary>
    public static async Task<TResult> ExecuteCarrierFirstWithCommandAsync<TResult>(
        long opId, QuarryContext ctx,
        DbCommand command, Func<DbDataReader, TResult> reader,
        CommandBehavior behavior, CancellationToken ct)
    {
        await using var _cmd = command;

        if (ctx.Connection.State != ConnectionState.Open)
            await ctx.EnsureConnectionOpenAsync(ct).ConfigureAwait(false);

        var startTimestamp = Stopwatch.GetTimestamp();
        await using var dbReader = await command.ExecuteReaderAsync(behavior | CommandBehavior.SingleRow, ct).ConfigureAwait(false);

        try
        {
            if (await dbReader.ReadAsync(ct).ConfigureAwait(false))
            {
                FinalizeQuery(opId, ctx, startTimestamp, 1, command.CommandText);
                return reader(dbReader);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ReportReaderFailure(opId, command, ex);
        }

        throw new InvalidOperationException("Sequence contains no elements.");
    }

    /// <summary>
    /// Executes a carrier-optimized query with a pre-built command and returns the first result or default.
    /// </summary>
    public static async Task<TResult?> ExecuteCarrierFirstOrDefaultWithCommandAsync<TResult>(
        long opId, QuarryContext ctx,
        DbCommand command, Func<DbDataReader, TResult> reader,
        CommandBehavior behavior, CancellationToken ct)
    {
        await using var _cmd = command;

        if (ctx.Connection.State != ConnectionState.Open)
            await ctx.EnsureConnectionOpenAsync(ct).ConfigureAwait(false);

        var startTimestamp = Stopwatch.GetTimestamp();
        await using var dbReader = await command.ExecuteReaderAsync(behavior | CommandBehavior.SingleRow, ct).ConfigureAwait(false);

        try
        {
            if (await dbReader.ReadAsync(ct).ConfigureAwait(false))
            {
                var result = reader(dbReader);
                FinalizeQuery(opId, ctx, startTimestamp, 1, command.CommandText);
                return result;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ReportReaderFailure(opId, command, ex);
        }

        FinalizeQuery(opId, ctx, startTimestamp, 0, command.CommandText);
        return default;
    }

    /// <summary>
    /// Executes a carrier-optimized query with a pre-built command and returns exactly one result.
    /// </summary>
    public static async Task<TResult> ExecuteCarrierSingleWithCommandAsync<TResult>(
        long opId, QuarryContext ctx,
        DbCommand command, Func<DbDataReader, TResult> reader,
        CommandBehavior behavior, CancellationToken ct)
    {
        await using var _cmd = command;

        if (ctx.Connection.State != ConnectionState.Open)
            await ctx.EnsureConnectionOpenAsync(ct).ConfigureAwait(false);

        var startTimestamp = Stopwatch.GetTimestamp();
        await using var dbReader = await command.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);

        try
        {
            if (!await dbReader.ReadAsync(ct).ConfigureAwait(false))
                throw new InvalidOperationException("Sequence contains no elements.");

            var result = reader(dbReader);

            if (await dbReader.ReadAsync(ct).ConfigureAwait(false))
                throw new InvalidOperationException("Sequence contains more than one element.");

            FinalizeQuery(opId, ctx, startTimestamp, 1, command.CommandText);
            return result;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ReportReaderFailure(opId, command, ex);
            throw; // unreachable — ReportReaderFailure always throws
        }
    }

    /// <summary>
    /// Executes a carrier-optimized single-row query with a pre-built command.
    /// Returns default if no results; throws if more than one result.
    /// </summary>
    public static async Task<TResult?> ExecuteCarrierSingleOrDefaultWithCommandAsync<TResult>(
        long opId, QuarryContext ctx,
        DbCommand command, Func<DbDataReader, TResult> reader,
        CommandBehavior behavior, CancellationToken ct)
    {
        await using var _cmd = command;

        if (ctx.Connection.State != ConnectionState.Open)
            await ctx.EnsureConnectionOpenAsync(ct).ConfigureAwait(false);

        var startTimestamp = Stopwatch.GetTimestamp();
        await using var dbReader = await command.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);

        try
        {
            if (!await dbReader.ReadAsync(ct).ConfigureAwait(false))
            {
                FinalizeQuery(opId, ctx, startTimestamp, 0, command.CommandText);
                return default;
            }

            var result = reader(dbReader);

            if (await dbReader.ReadAsync(ct).ConfigureAwait(false))
                throw new InvalidOperationException("Sequence contains more than one element.");

            FinalizeQuery(opId, ctx, startTimestamp, 1, command.CommandText);
            return result;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ReportReaderFailure(opId, command, ex);
            throw; // unreachable
        }
    }

    /// <summary>
    /// Executes a carrier-optimized scalar query with a pre-built command.
    /// </summary>
    public static async Task<TScalar> ExecuteCarrierScalarWithCommandAsync<TScalar>(
        long opId, QuarryContext ctx,
        DbCommand command, CancellationToken ct)
    {
        await using var _cmd = command;

        if (ctx.Connection.State != ConnectionState.Open)
            await ctx.EnsureConnectionOpenAsync(ct).ConfigureAwait(false);

        var logger = LogsmithOutput.Logger;
        var instrumented = logger != null || ctx.SlowQueryThreshold.HasValue;
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
                if (logger?.IsEnabled(LogLevel.Debug, QueryLog.CategoryName) == true)
                    QueryLog.ScalarResult(opId, "null");

                if (default(TScalar) is null)
                    return default!;

                throw new InvalidOperationException("Query returned null but expected a non-nullable value.");
            }

            if (logger?.IsEnabled(LogLevel.Debug, QueryLog.CategoryName) == true)
                QueryLog.ScalarResult(opId, result.ToString() ?? "null");

            return ScalarConverter.Convert<TScalar>(result);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (logger?.IsEnabled(LogLevel.Error, QueryLog.CategoryName) == true)
                QueryLog.QueryFailed(opId, ex);

            throw new QuarryQueryException($"Error executing scalar query: {ex.Message}", command.CommandText, ex);
        }
    }

    /// <summary>
    /// Executes a carrier-optimized non-query (DELETE/UPDATE) with a pre-built command.
    /// </summary>
    public static async Task<int> ExecuteCarrierNonQueryWithCommandAsync(
        long opId, QuarryContext ctx,
        DbCommand command, CancellationToken ct)
    {
        await using var _cmd = command;

        if (ctx.Connection.State != ConnectionState.Open)
            await ctx.EnsureConnectionOpenAsync(ct).ConfigureAwait(false);

        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            var rowCount = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            FinalizeQuery(opId, ctx, startTimestamp, rowCount, command.CommandText);
            return rowCount;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var logger = LogsmithOutput.Logger;
            if (logger?.IsEnabled(LogLevel.Error, QueryLog.CategoryName) == true)
                QueryLog.QueryFailed(opId, ex);

            throw new QuarryQueryException($"Error executing query: {ex.Message}", command.CommandText, ex);
        }
    }

    /// <summary>
    /// Executes a carrier-optimized query with a pre-built command and returns results as an async enumerable.
    /// </summary>
    public static async IAsyncEnumerable<TResult> ToCarrierAsyncEnumerableWithCommandAsync<TResult>(
        long opId, QuarryContext ctx,
        DbCommand command, Func<DbDataReader, TResult> reader,
        CommandBehavior behavior,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await using var _cmd = command;

        if (ctx.Connection.State != ConnectionState.Open)
            await ctx.EnsureConnectionOpenAsync(ct).ConfigureAwait(false);

        var startTimestamp = Stopwatch.GetTimestamp();
        await using var dbReader = await command.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);

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
                ReportReaderFailure(opId, command, ex);
                throw; // unreachable
            }

            rowCount++;
            yield return result;
        }

        FinalizeQuery(opId, ctx, startTimestamp, rowCount, command.CommandText);
    }

    /// <summary>
    /// Executes a carrier-optimized query using a struct-based row reader that caches column ordinals.
    /// The reader's Resolve method is called once after ExecuteReaderAsync, then Read is called per row.
    /// </summary>
    public static async IAsyncEnumerable<TResult> ToCarrierAsyncEnumerableWithCommandAsync<TResult, TReader>(
        long opId, QuarryContext ctx,
        DbCommand command,
        CommandBehavior behavior,
        [EnumeratorCancellation] CancellationToken ct)
        where TReader : struct, IRowReader<TResult>
    {
        await using var _cmd = command;

        if (ctx.Connection.State != ConnectionState.Open)
            await ctx.EnsureConnectionOpenAsync(ct).ConfigureAwait(false);

        var startTimestamp = Stopwatch.GetTimestamp();
        await using var dbReader = await command.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);

        var reader = new TReader();
        reader.Resolve(dbReader);

        int rowCount = 0;
        while (await dbReader.ReadAsync(ct).ConfigureAwait(false))
        {
            TResult result;
            try
            {
                result = reader.Read(dbReader);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ReportReaderFailure(opId, command, ex);
                throw; // unreachable
            }

            rowCount++;
            yield return result;
        }

        FinalizeQuery(opId, ctx, startTimestamp, rowCount, command.CommandText);
    }

    /// <summary>
    /// Logs fetch completion at Debug and runs the slow-query check, in one place.
    /// </summary>
    private static void FinalizeQuery(long opId, QuarryContext ctx, long startTimestamp, int rowCount, string sql)
    {
        var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

        var logger = LogsmithOutput.Logger;
        if (logger?.IsEnabled(LogLevel.Debug, QueryLog.CategoryName) == true)
            QueryLog.FetchCompleted(opId, rowCount, elapsedMs);

        CheckSlowQuery(opId, ctx, elapsedMs, sql);
    }

    /// <summary>
    /// Centralizes the wrap-as-QuarryQueryException pattern used by every reader path.
    /// </summary>
    [DoesNotReturn]
    private static void ReportReaderFailure(long opId, DbCommand command, Exception ex)
    {
        var logger = LogsmithOutput.Logger;
        if (logger?.IsEnabled(LogLevel.Error, QueryLog.CategoryName) == true)
            QueryLog.QueryFailed(opId, ex);

        throw new QuarryQueryException($"Error reading query results: {ex.Message}", command.CommandText, ex);
    }

    /// <summary>
    /// Checks if a query exceeded the slow query threshold and emits a warning.
    /// </summary>
    private static void CheckSlowQuery(long opId, QuarryContext context, double elapsedMs, string sql)
    {
        var threshold = context.SlowQueryThreshold;
        if (threshold.HasValue && elapsedMs > threshold.Value.TotalMilliseconds)
        {
            if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Warning, ExecutionLog.CategoryName) == true)
                ExecutionLog.SlowQuery(opId, elapsedMs, sql);
        }
    }
}
