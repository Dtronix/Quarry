namespace Quarry;

/// <summary>
/// Represents an index definition in a schema.
/// Target type for index properties in schema classes.
/// </summary>
public readonly struct Index
{
    /// <summary>
    /// Implicitly converts an IndexBuilder to an Index.
    /// </summary>
    public static implicit operator Index(IndexBuilder builder) => default;
}
