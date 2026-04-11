using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Quarry.Shared.Sql.Parser;

namespace Quarry.Migration;

/// <summary>
/// Public facade for the Dapper-to-Quarry conversion pipeline.
/// Takes a Roslyn Compilation and returns conversion results without
/// exposing internal SQL parser types.
/// </summary>
public sealed class DapperConverter
{
    /// <summary>
    /// Scans a compilation for Dapper call sites and attempts to convert each to a Quarry chain API call.
    /// </summary>
    /// <param name="compilation">The Roslyn compilation containing user code with Dapper calls.</param>
    /// <param name="dialect">SQL dialect string: "sqlite", "postgresql", "mysql", "sqlserver". Defaults to "sqlite".</param>
    /// <returns>A list of conversion entries, one per detected Dapper call site.</returns>
    public IReadOnlyList<DapperConversionEntry> ConvertAll(Compilation compilation, string? dialect = null)
    {
        var sqlDialect = ResolveDialect(dialect);

        var schemaResolver = new SchemaResolver();
        var schemaMap = schemaResolver.Resolve(compilation);

        var detector = new DapperDetector();
        var results = new List<DapperConversionEntry>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            var sites = detector.Detect(model, root);

            foreach (var site in sites)
            {
                var parseResult = SqlParser.Parse(site.Sql, sqlDialect);
                var emitter = new ChainEmitter(schemaMap);
                var result = emitter.Translate(parseResult, site);

                var lineSpan = site.Location.GetLineSpan();
                var line = lineSpan.StartLinePosition.Line + 1;

                results.Add(new DapperConversionEntry(
                    filePath: tree.FilePath,
                    line: line,
                    dapperMethod: site.MethodName,
                    resultType: site.ResultTypeName,
                    originalSql: site.Sql,
                    chainCode: result.ChainCode,
                    diagnostics: result.Diagnostics.Select(d => new DapperConversionDiagnostic(
                        d.Severity.ToString(), d.Message)).ToList(),
                    isSuggestionOnly: result.IsSuggestionOnly));
            }
        }

        return results;
    }

    /// <summary>
    /// Returns the number of Quarry schema entities found in the compilation.
    /// </summary>
    public int CountSchemaEntities(Compilation compilation)
    {
        var resolver = new SchemaResolver();
        var map = resolver.Resolve(compilation);
        return map.Entities.Count();
    }

    private static SqlDialect ResolveDialect(string? dialectStr)
    {
        if (dialectStr == null)
            return SqlDialect.SQLite;

        return dialectStr.ToLowerInvariant() switch
        {
            "sqlite" => SqlDialect.SQLite,
            "postgresql" or "postgres" => SqlDialect.PostgreSQL,
            "mysql" => SqlDialect.MySQL,
            "sqlserver" or "mssql" => SqlDialect.SqlServer,
            _ => SqlDialect.SQLite,
        };
    }
}

/// <summary>
/// Result of attempting to convert a single Dapper call site.
/// </summary>
public sealed class DapperConversionEntry : IConversionEntry
{
    public string FilePath { get; }
    public int Line { get; }
    public string DapperMethod { get; }
    public string? ResultType { get; }
    public string OriginalSql { get; }
    public string? ChainCode { get; }
    public IReadOnlyList<DapperConversionDiagnostic> Diagnostics { get; }

    /// <summary>
    /// True when <see cref="ChainCode"/> is a manual-conversion suggestion (comment text)
    /// rather than a substitutable C# expression — e.g. for INSERT statements which
    /// require constructing an entity object the translator cannot synthesize.
    /// Tools should treat such entries as a separate bucket from auto-convertible calls.
    /// </summary>
    public bool IsSuggestionOnly { get; }

    public DapperConversionEntry(
        string filePath, int line, string dapperMethod, string? resultType,
        string originalSql, string? chainCode,
        IReadOnlyList<DapperConversionDiagnostic> diagnostics,
        bool isSuggestionOnly = false)
    {
        FilePath = filePath;
        Line = line;
        DapperMethod = dapperMethod;
        ResultType = resultType;
        OriginalSql = originalSql;
        ChainCode = chainCode;
        Diagnostics = diagnostics;
        IsSuggestionOnly = isSuggestionOnly;
    }

    /// <summary>
    /// True when the Dapper call was translated to a substitutable Quarry chain expression.
    /// Returns false for both unconvertible calls (no chain code) AND suggestion-only
    /// outputs (e.g. INSERT comment templates) which cannot be auto-applied.
    /// </summary>
    public bool IsConvertible => ChainCode != null && !IsSuggestionOnly;

    public bool HasWarnings => Diagnostics.Any(d => d.Severity == "Warning");

    IReadOnlyList<IConversionDiagnostic> IConversionEntry.Diagnostics => Diagnostics;
    string IConversionEntry.OriginalSource => OriginalSql;
}

/// <summary>
/// A diagnostic message from the conversion process.
/// </summary>
public sealed class DapperConversionDiagnostic : IConversionDiagnostic
{
    public string Severity { get; }
    public string Message { get; }

    public DapperConversionDiagnostic(string severity, string message)
    {
        Severity = severity;
        Message = message;
    }
}
