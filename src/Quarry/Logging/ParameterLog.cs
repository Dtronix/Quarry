using System.ComponentModel;

namespace Quarry.Logging;

/// <summary>
/// Parameter logging infrastructure used by generated interceptor code. Not intended for direct use.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[LogCategory("Quarry.Parameters")]
public static partial class ParameterLog
{
    [LogMessage(LogLevel.Trace, "[{opId}] @p{index} = {value}", AlwaysEmit = true)]
    public static partial void Bound(long opId, int index, string value);

    [LogMessage(LogLevel.Trace, "[{opId}] @p{index} = [SENSITIVE]", AlwaysEmit = true)]
    public static partial void BoundSensitive(long opId, int index);
}
