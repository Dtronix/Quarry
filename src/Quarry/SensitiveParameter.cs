namespace Quarry;

/// <summary>
/// Wraps a parameter value to prevent it from being logged.
/// When passed to <c>RawSqlAsync</c>, the value is bound normally but logged as <c>[SENSITIVE]</c>.
/// </summary>
/// <param name="Value">The actual parameter value to bind.</param>
public readonly record struct SensitiveParameter(object? Value);
