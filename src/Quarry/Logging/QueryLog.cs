using System.ComponentModel;

namespace Quarry.Logging;

/// <summary>
/// Query logging infrastructure used by generated interceptor code. Not intended for direct use.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[LogCategory("Quarry.Query")]
public static partial class QueryLog
{
    [LogMessage(LogLevel.Debug, "[{opId}] SQL: {sql}", AlwaysEmit = true)]
    public static partial void SqlGenerated(long opId, string sql);

    [LogMessage(LogLevel.Debug, "[{opId}] Fetched {rowCount} rows in {elapsedMs:F1}ms", AlwaysEmit = true)]
    public static partial void FetchCompleted(long opId, int rowCount, double elapsedMs);

    [LogMessage(LogLevel.Debug, "[{opId}] Scalar result: {result}", AlwaysEmit = true)]
    public static partial void ScalarResult(long opId, string result);

    [LogMessage(LogLevel.Error, "[{opId}] Query failed")]
    public static partial void QueryFailed(long opId, Exception ex);
}
