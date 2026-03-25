using System.Text;
using Quarry.Logging;

namespace Quarry.Tests.Integration;

/// <summary>
/// A test logger implementing <see cref="ILogsmithLogger"/> that records log entries for assertions.
/// </summary>
internal sealed class RecordingLogsmithLogger : ILogsmithLogger
{
    private static long _globalSequence;
    private readonly object _lock = new();
    private readonly List<LogRecord> _entries = new();

    public LogLevel MinimumLevel { get; set; } = LogLevel.Trace;

    public Dictionary<string, LogLevel> CategoryOverrides { get; } = new();

    public IReadOnlyList<LogRecord> Entries
    {
        get
        {
            lock (_lock)
                return _entries.ToList();
        }
    }

    public bool IsEnabled(LogLevel level, string category)
    {
        if (CategoryOverrides.TryGetValue(category, out var categoryLevel))
            return level >= categoryLevel;

        return level >= MinimumLevel;
    }

    public void Write(in LogEntry entry, ReadOnlySpan<byte> utf8Message)
    {
        var record = new LogRecord(
            Interlocked.Increment(ref _globalSequence),
            entry.Level,
            entry.Category,
            Encoding.UTF8.GetString(utf8Message),
            entry.Exception);

        lock (_lock)
            _entries.Add(record);
    }

    public void Clear()
    {
        lock (_lock)
            _entries.Clear();
    }

    internal sealed record LogRecord(
        long Sequence,
        LogLevel Level,
        string Category,
        string Message,
        Exception? Exception);
}
