namespace Quarry;

/// <summary>
/// Fluent builder for configuring singular (N:1) navigation relationships.
/// Implicitly converts to One&lt;T&gt;.
/// </summary>
/// <typeparam name="T">The target schema type.</typeparam>
public readonly struct OneBuilder<T> where T : Schema { }
