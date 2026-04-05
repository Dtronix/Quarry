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
    /// Compile-time marker for SUM aggregate subquery.
    /// Translated to: (SELECT SUM(column) FROM ... WHERE correlation).
    /// </summary>
    public int Sum(Func<T, int> selector) => throw MarkerException();

    /// <inheritdoc cref="Sum(Func{T, int})"/>
    public long Sum(Func<T, long> selector) => throw MarkerException();

    /// <inheritdoc cref="Sum(Func{T, int})"/>
    public decimal Sum(Func<T, decimal> selector) => throw MarkerException();

    /// <inheritdoc cref="Sum(Func{T, int})"/>
    public double Sum(Func<T, double> selector) => throw MarkerException();

    /// <summary>
    /// Compile-time marker for AVG aggregate subquery.
    /// Translated to: (SELECT AVG(column) FROM ... WHERE correlation).
    /// </summary>
    public double Avg(Func<T, int> selector) => throw MarkerException();

    /// <inheritdoc cref="Avg(Func{T, int})"/>
    public double Avg(Func<T, long> selector) => throw MarkerException();

    /// <inheritdoc cref="Avg(Func{T, int})"/>
    public decimal Avg(Func<T, decimal> selector) => throw MarkerException();

    /// <inheritdoc cref="Avg(Func{T, int})"/>
    public double Avg(Func<T, double> selector) => throw MarkerException();

    /// <summary>
    /// Compile-time marker for MIN aggregate subquery.
    /// Translated to: (SELECT MIN(column) FROM ... WHERE correlation).
    /// </summary>
    public TResult Min<TResult>(Func<T, TResult> selector) => throw MarkerException();

    /// <summary>
    /// Compile-time marker for MAX aggregate subquery.
    /// Translated to: (SELECT MAX(column) FROM ... WHERE correlation).
    /// </summary>
    public TResult Max<TResult>(Func<T, TResult> selector) => throw MarkerException();

    /// <summary>
    /// Implicitly converts a RelationshipBuilder to a Many.
    /// </summary>
    public static implicit operator Many<T>(RelationshipBuilder<T> builder) => default;

    private static InvalidOperationException MarkerException() =>
        new("Many<T> methods are compile-time markers for the Quarry source generator and cannot be called at runtime.");
}
