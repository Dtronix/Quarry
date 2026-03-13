using System;

namespace Quarry;

/// <summary>
/// Represents a one-to-many navigation collection in a schema definition.
/// </summary>
/// <typeparam name="T">The type of the related entities.</typeparam>
public readonly struct Many<T> where T : Schema
{
    /// <summary>
    /// Compile-time marker for EXISTS subquery (parameterless).
    /// Translated to: EXISTS (SELECT 1 FROM ... WHERE correlation).
    /// </summary>
    public bool Any() => throw MarkerException();

    /// <summary>
    /// Compile-time marker for filtered EXISTS subquery.
    /// Translated to: EXISTS (SELECT 1 FROM ... WHERE correlation AND predicate).
    /// </summary>
    public bool Any(Func<T, bool> predicate) => throw MarkerException();

    /// <summary>
    /// Compile-time marker for universal quantification subquery.
    /// Translated to: NOT EXISTS (SELECT 1 FROM ... WHERE correlation AND NOT predicate).
    /// </summary>
    public bool All(Func<T, bool> predicate) => throw MarkerException();

    /// <summary>
    /// Compile-time marker for scalar count subquery.
    /// Translated to: (SELECT COUNT(*) FROM ... WHERE correlation).
    /// </summary>
    public int Count() => throw MarkerException();

    /// <summary>
    /// Implicitly converts a RelationshipBuilder to a Many.
    /// </summary>
    public static implicit operator Many<T>(RelationshipBuilder<T> builder) => default;

    private static InvalidOperationException MarkerException() =>
        new("Many<T> methods are compile-time markers for the Quarry source generator and cannot be called at runtime.");
}
