using System;

namespace Quarry.Generators.IR;

/// <summary>
/// Output of the Stage 3 bind transform: either a successfully bound call site or a
/// bind failure. Bind exceptions produce no <see cref="BoundCallSite"/> to attach an
/// error to, so failures travel as first-class pipeline values — a dedicated output
/// node collects them and reports QRY900. This replaced the [ThreadStatic]
/// PipelineErrorBag side-channel (#311), which was thread-affine and whose entries
/// were drained-and-discarded before reporting.
/// </summary>
internal sealed class BindStageResult : IEquatable<BindStageResult>
{
    public BindStageResult(BoundCallSite site)
    {
        Site = site;
    }

    public BindStageResult(BindFailure failure)
    {
        Failure = failure;
    }

    public BoundCallSite? Site { get; }
    public BindFailure? Failure { get; }

    public bool Equals(BindStageResult? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Equals(Site, other.Site) && Equals(Failure, other.Failure);
    }

    public override bool Equals(object? obj) => Equals(obj as BindStageResult);

    public override int GetHashCode()
        => Site?.GetHashCode() ?? Failure?.GetHashCode() ?? 0;
}

/// <summary>
/// A Stage 3 bind exception, carrying enough location detail to report QRY900 at the
/// failing call site. Equality includes the message so an error-state change
/// invalidates the incremental cache (mirrors TranslatedCallSite.PipelineError).
/// </summary>
internal sealed class BindFailure : IEquatable<BindFailure>
{
    public BindFailure(string filePath, int line, int column, string message)
    {
        FilePath = filePath;
        Line = line;
        Column = column;
        Message = message;
    }

    public string FilePath { get; }
    public int Line { get; }
    public int Column { get; }
    public string Message { get; }

    public bool Equals(BindFailure? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return FilePath == other.FilePath
            && Line == other.Line
            && Column == other.Column
            && Message == other.Message;
    }

    public override bool Equals(object? obj) => Equals(obj as BindFailure);

    public override int GetHashCode()
        => HashCode.Combine(FilePath, Line, Column, Message);
}
