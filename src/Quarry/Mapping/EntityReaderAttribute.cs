namespace Quarry;

/// <summary>
/// Specifies a custom <see cref="EntityReader{T}"/> to use for materializing entities
/// from DbDataReader instead of the auto-generated ordinal-based reader.
/// Apply to a Schema class to override entity materialization.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class EntityReaderAttribute : Attribute
{
    /// <summary>
    /// Gets the type of the custom entity reader.
    /// Must inherit from <see cref="EntityReader{T}"/> where T matches the entity type.
    /// </summary>
    public Type ReaderType { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityReaderAttribute"/> class.
    /// </summary>
    /// <param name="readerType">The type that inherits from <see cref="EntityReader{T}"/>.</param>
    public EntityReaderAttribute(Type readerType) => ReaderType = readerType;
}
