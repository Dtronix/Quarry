using Logsmith;

namespace Quarry.Logging;

[LogCategory("Quarry.Modify")]
internal static partial class ModifyLog
{
    [LogMessage(LogLevel.Debug, "[{opId}] SQL: {sql}", AlwaysEmit = true)]
    internal static partial void SqlGenerated(long opId, string sql);

    [LogMessage(LogLevel.Debug, "[{opId}] {operation} affected {rowCount} rows in {elapsedMs:F1}ms", AlwaysEmit = true)]
    internal static partial void Completed(long opId, string operation, int rowCount, double elapsedMs);

    [LogMessage(LogLevel.Error, "[{opId}] Modification failed")]
    internal static partial void Failed(long opId, Exception ex);
}
