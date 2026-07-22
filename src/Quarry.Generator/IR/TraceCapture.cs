using System;
using System.Collections.Generic;

namespace Quarry.Generators.IR;

/// <summary>
/// Side-channel accumulator for per-site trace messages.
/// Data is keyed by site UniqueId and collected at emission time for traced chains.
/// </summary>
internal static class TraceCapture
{
    [ThreadStatic]
    private static Dictionary<string, List<string>>? _data;

    internal static void Log(string uniqueId, string message)
    {
        _data ??= new Dictionary<string, List<string>>();
        if (!_data.TryGetValue(uniqueId, out var list))
        {
            list = new List<string>();
            _data[uniqueId] = list;
        }
        list.Add(message);
    }

    internal static void LogFormat(string uniqueId, string category, string key, string value)
    {
        Log(uniqueId, $"  {key}={value}");
    }

    internal static IReadOnlyList<string>? Get(string uniqueId)
    {
        if (_data == null) return null;
        return _data.TryGetValue(uniqueId, out var list) ? list : null;
    }

    internal static void Clear()
    {
        _data?.Clear();
    }

    /// <summary>
    /// True when no trace lines are buffered on this thread. Test hook: pins the
    /// produce-and-consume-within-one-orchestrator-call lifecycle (#311) — after any
    /// generator run the buffer must be empty, because AnalyzeAndGroupTranslated
    /// captures lines onto AssembledPlan.TraceLines and clears in a finally.
    /// </summary>
    internal static bool IsEmpty => _data == null || _data.Count == 0;
}
