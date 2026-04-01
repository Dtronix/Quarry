using System.Collections.Generic;

namespace Quarry.Generators.IR;

/// <summary>
/// Side-channel accumulator for pipeline errors from stages that cannot
/// propagate errors through their return types (e.g., Stage 3 Bind).
/// Uses [ThreadStatic] storage, same pattern as <see cref="TraceCapture"/>.
/// </summary>
internal static class PipelineErrorBag
{
    [System.ThreadStatic]
    private static List<PipelineErrorEntry>? _errors;

    internal static void Report(string sourceFilePath, int line, int column, string error)
    {
        _errors ??= new List<PipelineErrorEntry>();
        _errors.Add(new PipelineErrorEntry(sourceFilePath, line, column, error));
    }

    internal static List<PipelineErrorEntry> DrainErrors()
    {
        var errors = _errors;
        _errors = null;
        return errors ?? new List<PipelineErrorEntry>();
    }
}

internal readonly struct PipelineErrorEntry
{
    public PipelineErrorEntry(string sourceFilePath, int line, int column, string error)
    {
        SourceFilePath = sourceFilePath;
        Line = line;
        Column = column;
        Error = error;
    }

    public string SourceFilePath { get; }
    public int Line { get; }
    public int Column { get; }
    public string Error { get; }
}
