using System.ComponentModel;

namespace Quarry.Internal;

/// <summary>
/// Generates monotonically increasing operation IDs for correlating log entries.
/// This type is used by generated interceptor code and is not intended for direct use.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class OpId
{
    private static long _next;

    /// <summary>
    /// Returns the next operation ID.
    /// </summary>
    public static long Next() => Interlocked.Increment(ref _next);
}
