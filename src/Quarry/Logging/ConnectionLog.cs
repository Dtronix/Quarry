namespace Quarry.Logging;

[LogCategory("Quarry.Connection")]
internal static partial class ConnectionLog
{
    [LogMessage(LogLevel.Information, "Connection opened")]
    internal static partial void Opened();

    [LogMessage(LogLevel.Information, "Connection closed")]
    internal static partial void Closed();
}
