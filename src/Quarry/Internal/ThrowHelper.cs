using System;
using System.Diagnostics.CodeAnalysis;

namespace Quarry.Internal;

/// <summary>
/// Throw helpers for generated interceptor code.
/// </summary>
public static class ThrowHelper
{
    /// <summary>
    /// Throws for a conditional-clause mask value that has no pre-built SQL variant.
    /// Reached only if the generator's mask enumeration missed a runtime-reachable
    /// if/else branch combination — the alternative is dispatching a null CommandText
    /// into the provider, which surfaces as an unactionable provider error.
    /// Declared to return <see cref="string"/> so call sites can use it as the
    /// fallback arm of a SQL-selection expression.
    /// </summary>
    [DoesNotReturn]
    public static string UnenumeratedMask(int mask)
    {
        throw new InvalidOperationException(
            $"Quarry: no SQL variant was generated for conditional clause combination (mask {mask}). " +
            "The executed if/else branch combination was not enumerated at compile time. " +
            "This is a Quarry generator defect — please report the query chain shape at " +
            "https://github.com/Dtronix/Quarry/issues.");
    }
}
