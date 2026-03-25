namespace Quarry.Logging;

[LogCategory("Quarry.Parameters")]
internal static partial class ParameterLog
{
    [LogMessage(LogLevel.Trace, "[{opId}] @p{index} = {value}", AlwaysEmit = true)]
    internal static partial void Bound(long opId, int index, string value);

    [LogMessage(LogLevel.Trace, "[{opId}] @p{index} = [SENSITIVE]", AlwaysEmit = true)]
    internal static partial void BoundSensitive(long opId, int index);
}
