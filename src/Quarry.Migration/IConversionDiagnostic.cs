namespace Quarry.Migration;

/// <summary>
/// Shared interface for diagnostic messages produced by any migration converter.
/// </summary>
public interface IConversionDiagnostic
{
    /// <summary>
    /// Severity level of the diagnostic (e.g. "Info", "Warning", "Error").
    /// </summary>
    string Severity { get; }

    /// <summary>
    /// Human-readable description of the diagnostic.
    /// </summary>
    string Message { get; }
}
