using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace Quarry;

/// <summary>
/// Static registry for TypeMapping instances, enabling runtime parameter conversion
/// on the fallback execution path (QRY001 queries).
/// </summary>
/// <remarks>
/// Compile-time interceptors call ToDb() directly with full type information.
/// This registry is only consulted by <c>NormalizeParameterValue</c> when a query
/// falls back to runtime execution. Registration happens automatically in the
/// <see cref="TypeMapping{TCustom,TDb}"/> constructor.
/// </remarks>
internal static class TypeMappingRegistry
{
    private static readonly ConcurrentDictionary<Type, ITypeMappingConverter> Converters = new();

    /// <summary>
    /// Registers a type mapping converter for the given custom type.
    /// First registration wins; subsequent registrations for the same type are ignored.
    /// </summary>
    internal static void Register(Type customType, ITypeMappingConverter converter)
        => Converters.TryAdd(customType, converter);

    /// <summary>
    /// Attempts to convert a value using a registered type mapping.
    /// </summary>
    /// <returns><c>true</c> if a mapping was found and the value was converted.</returns>
    internal static bool TryConvert(Type type, object value, [NotNullWhen(true)] out object? converted)
    {
        if (Converters.TryGetValue(type, out var converter))
        {
            converted = converter.ConvertToDb(value);
            return true;
        }

        converted = null;
        return false;
    }

    /// <summary>
    /// Attempts to configure a DbParameter using dialect-aware type mapping for the given type.
    /// Called on the runtime fallback path after parameter value has been set.
    /// </summary>
    /// <returns><c>true</c> if a dialect-aware mapping was found and applied.</returns>
    internal static bool TryConfigureParameter(Type type, SqlDialect dialect, DbParameter parameter)
    {
        if (Converters.TryGetValue(type, out var converter) && converter is IDialectAwareTypeMapping dialectAware)
        {
            dialectAware.ConfigureParameter(dialect, parameter);
            return true;
        }

        return false;
    }
}
