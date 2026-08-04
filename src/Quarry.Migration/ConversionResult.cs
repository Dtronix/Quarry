using System.Collections.Generic;

namespace Quarry.Migration;

/// <summary>
/// Result of converting a Dapper call site to a Quarry chain API call.
/// </summary>
internal sealed class ConversionResult
{
    /// <summary>
    /// The generated Quarry chain C# source text, or null if conversion failed entirely.
    /// </summary>
    public string? ChainCode { get; }

    /// <summary>
    /// Diagnostics produced during conversion (warnings for Sql.Raw fallbacks,
    /// no-WHERE DML, INSERT manual-conversion suggestions, etc.).
    /// </summary>
    public IReadOnlyList<ConversionDiagnostic> Diagnostics { get; }

    /// <summary>
    /// The original SQL string from the Dapper call.
    /// </summary>
    public string OriginalSql { get; }

    /// <summary>
    /// True when <see cref="ChainCode"/> is a manual-conversion suggestion (comment text)
    /// rather than a substitutable C# expression. The IDE code fix must NOT replace the
    /// invocation in this case — the user has to construct the entity by hand.
    /// </summary>
    public bool IsSuggestionOnly { get; }

    /// <summary>
    /// C# type declarations the chain code depends on but the user's source does not contain —
    /// currently the DTOs synthesized for projected CTEs (#331). The IDE code fix must insert
    /// these alongside the expression replacement or the result will not compile. Empty when
    /// the conversion needs no new types.
    /// </summary>
    public IReadOnlyList<string> GeneratedTypeDeclarations { get; }

    public ConversionResult(
        string originalSql,
        string? chainCode,
        IReadOnlyList<ConversionDiagnostic> diagnostics,
        bool isSuggestionOnly = false,
        IReadOnlyList<string>? generatedTypeDeclarations = null)
    {
        OriginalSql = originalSql;
        ChainCode = chainCode;
        Diagnostics = diagnostics;
        IsSuggestionOnly = isSuggestionOnly;
        GeneratedTypeDeclarations = generatedTypeDeclarations ?? System.Array.Empty<string>();
    }
}

internal sealed class ConversionDiagnostic
{
    public ConversionDiagnosticSeverity Severity { get; }
    public string Message { get; }

    public ConversionDiagnostic(ConversionDiagnosticSeverity severity, string message)
    {
        Severity = severity;
        Message = message;
    }
}

internal enum ConversionDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}
