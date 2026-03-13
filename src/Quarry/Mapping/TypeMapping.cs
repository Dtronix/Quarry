namespace Quarry;

/// <summary>
/// Base class for custom type mappings between C# types and database types.
/// </summary>
/// <typeparam name="TCustom">The custom C# type.</typeparam>
/// <typeparam name="TDb">The database-compatible type.</typeparam>
/// <remarks>
/// <para>
/// Implement this class to define how custom types are converted to and from database values.
/// </para>
/// <para>
/// Example:
/// <code>
/// public class MoneyMapping : TypeMapping&lt;Money, decimal&gt;
/// {
///     public override decimal ToDb(Money value) =&gt; value.Amount;
///     public override Money FromDb(decimal value) =&gt; new(value);
/// }
/// </code>
/// </para>
/// </remarks>
public abstract class TypeMapping<TCustom, TDb> : ITypeMappingConverter
{
    /// <summary>
    /// Creates a new TypeMapping instance and registers it for runtime fallback conversion.
    /// </summary>
    protected TypeMapping()
    {
        TypeMappingRegistry.Register(typeof(TCustom), this);
    }

    /// <summary>
    /// Converts a custom type value to its database representation.
    /// </summary>
    /// <param name="value">The custom type value to convert.</param>
    /// <returns>The database-compatible value.</returns>
    public abstract TDb ToDb(TCustom value);

    /// <summary>
    /// Converts a database value to the custom type.
    /// </summary>
    /// <param name="value">The database value to convert.</param>
    /// <returns>The custom type value.</returns>
    public abstract TCustom FromDb(TDb value);

    /// <inheritdoc />
    object ITypeMappingConverter.ConvertToDb(object value) => ToDb((TCustom)value)!;
}
