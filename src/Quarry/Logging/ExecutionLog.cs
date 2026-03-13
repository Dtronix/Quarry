using Logsmith;

namespace Quarry.Logging;

[LogCategory("Quarry.Execution")]
internal static partial class ExecutionLog
{
    [LogMessage(LogLevel.Warning, "[{opId}] Slow query ({elapsedMs:F0}ms): {sql}")]
    internal static partial void SlowQuery(long opId, double elapsedMs, string sql);
}
