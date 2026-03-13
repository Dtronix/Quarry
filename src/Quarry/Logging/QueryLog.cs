using Logsmith;

namespace Quarry.Logging;

[LogCategory("Quarry.Query")]
internal static partial class QueryLog
{
    [LogMessage(LogLevel.Debug, "[{opId}] SQL: {sql}", AlwaysEmit = true)]
    internal static partial void SqlGenerated(long opId, string sql);

    [LogMessage(LogLevel.Debug, "[{opId}] Fetched {rowCount} rows in {elapsedMs:F1}ms", AlwaysEmit = true)]
    internal static partial void FetchCompleted(long opId, int rowCount, double elapsedMs);

    [LogMessage(LogLevel.Debug, "[{opId}] Scalar result: {result}", AlwaysEmit = true)]
    internal static partial void ScalarResult(long opId, string result);

    [LogMessage(LogLevel.Error, "[{opId}] Query failed")]
    internal static partial void QueryFailed(long opId, Exception ex);
}
