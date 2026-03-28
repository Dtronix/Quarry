using System.Runtime.CompilerServices;

namespace Quarry.Internal;

/// <summary>
/// Provides JIT-optimized scalar type conversion for database results.
/// Uses typeof pattern matching that the JIT eliminates to dead-code per TScalar instantiation,
/// avoiding the overhead of <see cref="Convert.ChangeType"/> and <see cref="Nullable.GetUnderlyingType"/>.
/// </summary>
internal static class ScalarConverter
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static TScalar Convert<TScalar>(object result)
    {
        if (result is null or DBNull)
            return default!;

        // The JIT recognizes typeof(T) == typeof(X) as a constant per generic instantiation
        // and eliminates all non-matching branches, producing a direct Convert.ToXxx call.

        if (typeof(TScalar) == typeof(int) || typeof(TScalar) == typeof(int?))
            return (TScalar)(object)System.Convert.ToInt32(result);

        if (typeof(TScalar) == typeof(long) || typeof(TScalar) == typeof(long?))
            return (TScalar)(object)System.Convert.ToInt64(result);

        if (typeof(TScalar) == typeof(decimal) || typeof(TScalar) == typeof(decimal?))
            return (TScalar)(object)System.Convert.ToDecimal(result);

        if (typeof(TScalar) == typeof(double) || typeof(TScalar) == typeof(double?))
            return (TScalar)(object)System.Convert.ToDouble(result);

        if (typeof(TScalar) == typeof(float) || typeof(TScalar) == typeof(float?))
            return (TScalar)(object)System.Convert.ToSingle(result);

        if (typeof(TScalar) == typeof(short) || typeof(TScalar) == typeof(short?))
            return (TScalar)(object)System.Convert.ToInt16(result);

        if (typeof(TScalar) == typeof(byte) || typeof(TScalar) == typeof(byte?))
            return (TScalar)(object)System.Convert.ToByte(result);

        if (typeof(TScalar) == typeof(bool) || typeof(TScalar) == typeof(bool?))
            return (TScalar)(object)System.Convert.ToBoolean(result);

        if (typeof(TScalar) == typeof(string))
            return (TScalar)(object)result.ToString()!;

        // Fallback for uncommon types — preserves existing behavior.
        return (TScalar)System.Convert.ChangeType(
            result,
            Nullable.GetUnderlyingType(typeof(TScalar)) ?? typeof(TScalar));
    }
}
