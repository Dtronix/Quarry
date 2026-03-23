using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Quarry.Generators.Models;

/// <summary>
/// Value type capturing source location for diagnostic reporting.
/// Replaces direct use of <see cref="Location"/> and <see cref="SyntaxNode.GetLocation()"/>
/// in pipeline-visible types, since Roslyn's Location uses reference equality.
/// </summary>
internal readonly struct DiagnosticLocation : IEquatable<DiagnosticLocation>
{
    public DiagnosticLocation(
        string filePath,
        int line,
        int column,
        TextSpan span)
    {
        FilePath = filePath;
        Line = line;
        Column = column;
        Span = span;
    }

    public string FilePath { get; }
    public int Line { get; }
    public int Column { get; }
    public TextSpan Span { get; }

    /// <summary>
    /// Creates a <see cref="DiagnosticLocation"/> from a Roslyn <see cref="Location"/>.
    /// </summary>
    public static DiagnosticLocation FromLocation(Location location)
    {
        var lineSpan = location.GetLineSpan();
        return new DiagnosticLocation(
            lineSpan.Path ?? string.Empty,
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.StartLinePosition.Character + 1,
            location.SourceSpan);
    }

    /// <summary>
    /// Creates a <see cref="DiagnosticLocation"/> from a <see cref="SyntaxNode"/>.
    /// </summary>
    public static DiagnosticLocation FromSyntaxNode(SyntaxNode? node)
        => node != null ? FromLocation(node.GetLocation()) : default;

    /// <summary>
    /// Reconstructs a Roslyn <see cref="Location"/> from this value type.
    /// Requires the original <see cref="SyntaxTree"/> to be provided.
    /// </summary>
    public Location ToLocation(SyntaxTree syntaxTree)
        => Location.Create(syntaxTree, Span);

    public bool Equals(DiagnosticLocation other)
        => FilePath == other.FilePath
        && Line == other.Line
        && Column == other.Column
        && Span.Equals(other.Span);

    public override bool Equals(object? obj)
        => obj is DiagnosticLocation other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(FilePath, Line, Column, Span);

    public static bool operator ==(DiagnosticLocation left, DiagnosticLocation right)
        => left.Equals(right);

    public static bool operator !=(DiagnosticLocation left, DiagnosticLocation right)
        => !left.Equals(right);
}
