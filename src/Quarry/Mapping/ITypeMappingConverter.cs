namespace Quarry;

/// <summary>
/// Non-generic interface for runtime type mapping conversion.
/// Used by the fallback execution path to apply TypeMapping conversions
/// when compile-time interceptors are not available.
/// </summary>
internal interface ITypeMappingConverter
{
    /// <summary>
    /// Converts a custom type value to its database representation.
    /// </summary>
    object ConvertToDb(object value);
}
