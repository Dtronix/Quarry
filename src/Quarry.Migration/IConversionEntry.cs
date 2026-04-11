using System.Collections.Generic;

namespace Quarry.Migration;

/// <summary>
/// Shared interface for conversion entries produced by any migration converter.
/// Enables uniform processing of results from Dapper, EF Core, ADO.NET, and SqlKata converters.
/// </summary>
public interface IConversionEntry
{
    /// <summary>
    /// Path of the source file containing the original call site.
    /// </summary>
    string FilePath { get; }

    /// <summary>
    /// 1-based line number of the original call site in the source file.
    /// </summary>
    int Line { get; }

    /// <summary>
    /// The generated Quarry chain C# source text, or null if conversion failed.
    /// </summary>
    string? ChainCode { get; }

    /// <summary>
    /// Diagnostics produced during conversion.
    /// </summary>
    IReadOnlyList<IConversionDiagnostic> Diagnostics { get; }

    /// <summary>
    /// True when the call site was translated to a substitutable Quarry chain expression.
    /// </summary>
    bool IsConvertible { get; }

    /// <summary>
    /// True when any diagnostic has Warning severity.
    /// </summary>
    bool HasWarnings { get; }

    /// <summary>
    /// The original source text before conversion (SQL string or LINQ expression).
    /// </summary>
    string OriginalSource { get; }
}
