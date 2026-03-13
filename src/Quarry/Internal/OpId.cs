namespace Quarry.Internal;

/// <summary>
/// Generates monotonically increasing operation IDs for correlating log entries.
/// </summary>
internal static class OpId
{
    private static long _next;

    /// <summary>
    /// Returns the next operation ID.
    /// </summary>
    internal static long Next() => Interlocked.Increment(ref _next);
}
