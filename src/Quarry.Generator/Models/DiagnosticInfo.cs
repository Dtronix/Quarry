using System;

namespace Quarry.Generators.Models;

/// <summary>
/// Carries diagnostic information through the pipeline for deferred reporting.
/// Used when diagnostics are discovered in SelectMany transforms that don't have
/// access to <see cref="Microsoft.CodeAnalysis.SourceProductionContext"/>.
/// </summary>
internal sealed class DiagnosticInfo : IEquatable<DiagnosticInfo>
{
    public DiagnosticInfo(
        string diagnosticId,
        DiagnosticLocation location,
        params string[] messageArgs)
    {
        DiagnosticId = diagnosticId;
        Location = location;
        MessageArgs = messageArgs;
    }

    /// <summary>
    /// Gets the diagnostic descriptor ID (e.g., "QRY001").
    /// </summary>
    public string DiagnosticId { get; }

    /// <summary>
    /// Gets the source location for the diagnostic.
    /// </summary>
    public DiagnosticLocation Location { get; }

    /// <summary>
    /// Gets the message format arguments.
    /// </summary>
    public string[] MessageArgs { get; }

    public bool Equals(DiagnosticInfo? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (DiagnosticId != other.DiagnosticId) return false;
        if (!Location.Equals(other.Location)) return false;
        if (MessageArgs.Length != other.MessageArgs.Length) return false;
        for (int i = 0; i < MessageArgs.Length; i++)
        {
            if (MessageArgs[i] != other.MessageArgs[i]) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as DiagnosticInfo);

    public override int GetHashCode()
        => HashCode.Combine(DiagnosticId, Location);
}
