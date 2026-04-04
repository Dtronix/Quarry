namespace Quarry;

/// <summary>
/// Represents a singular (N:1) navigation in a schema definition.
/// When accessed through property chains in query lambdas, the source generator
/// emits an implicit JOIN in the generated SQL.
/// </summary>
/// <typeparam name="T">The target schema type.</typeparam>
public readonly struct One<T> where T : Schema
{
    /// <summary>
    /// Implicitly converts an OneBuilder to a One.
    /// </summary>
    public static implicit operator One<T>(OneBuilder<T> builder) => default;
}
