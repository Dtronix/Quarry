using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Quarry.Shared.Sql.Parser;

namespace Quarry.Migration;

/// <summary>
/// Public facade for the ADO.NET-to-Quarry conversion pipeline.
/// Reuses the shared SQL parser and ChainEmitter — the same pipeline as Dapper,
/// but with different detection (DbCommand variable tracking instead of Dapper extension methods).
/// </summary>
public sealed class AdoNetConverter
{
    /// <summary>
    /// Scans a compilation for ADO.NET call sites and converts each to a Quarry chain API call.
    /// </summary>
    public IReadOnlyList<AdoNetConversionEntry> ConvertAll(Compilation compilation, string? dialect = null)
    {
        var sqlDialect = ResolveDialect(dialect);

        var schemaResolver = new SchemaResolver();
        var schemaMap = schemaResolver.Resolve(compilation);

        var detector = new AdoNetDetector();
        var results = new List<AdoNetConversionEntry>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            var sites = detector.Detect(model, root);

            foreach (var site in sites)
            {
                var result = Translate(site, schemaMap, sqlDialect);

                var lineSpan = site.Location.GetLineSpan();
                var line = lineSpan.StartLinePosition.Line + 1;

                results.Add(new AdoNetConversionEntry(
                    filePath: tree.FilePath,
                    line: line,
                    adoNetMethod: site.MethodName,
                    originalSql: site.Sql,
                    chainCode: result.ChainCode,
                    diagnostics: result.Diagnostics.Select(d => new AdoNetConversionDiagnostic(
                        d.Severity.ToString(), d.Message)).ToList(),
                    isSuggestionOnly: result.IsSuggestionOnly));
            }
        }

        return results;
    }

    internal static ConversionResult Translate(AdoNetCallSite site, SchemaMap schemaMap, SqlDialect dialect = SqlDialect.SQLite)
    {
        // Map ADO.NET method name to Dapper equivalent for ChainEmitter compatibility
        var dapperMethodName = MapToDapperMethodName(site.MethodName);

        // Create a DapperCallSite adapter so we can reuse ChainEmitter
        var dapperCallSite = new DapperCallSite(
            sql: site.Sql,
            parameterNames: site.ParameterNames,
            methodName: dapperMethodName,
            resultTypeName: null,
            location: site.Location,
            invocationSyntax: site.InvocationSyntax);

        // Parse SQL and translate through shared pipeline
        var parseResult = SqlParser.Parse(site.Sql, dialect);
        var emitter = new ChainEmitter(schemaMap);
        return emitter.Translate(parseResult, dapperCallSite);
    }

    internal static ConversionResult TranslateWithFallback(AdoNetCallSite site, SchemaMap schemaMap)
    {
        var dapperMethodName = MapToDapperMethodName(site.MethodName);

        var dapperCallSite = new DapperCallSite(
            sql: site.Sql,
            parameterNames: site.ParameterNames,
            methodName: dapperMethodName,
            resultTypeName: null,
            location: site.Location,
            invocationSyntax: site.InvocationSyntax);

        var parseResult = DapperMigrationAnalyzer.TryParseWithFallback(site.Sql);
        var emitter = new ChainEmitter(schemaMap);
        return emitter.Translate(parseResult, dapperCallSite);
    }

    private static string MapToDapperMethodName(string adoNetMethod)
    {
        // Strip "Async" suffix
        var baseName = adoNetMethod.Replace("Async", "");

        return baseName switch
        {
            "ExecuteReader" => "QueryAsync",
            "ExecuteNonQuery" => "ExecuteAsync",
            "ExecuteScalar" => "ExecuteScalarAsync",
            _ => "QueryAsync",
        };
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
/// Result of attempting to convert a single ADO.NET call site.
/// </summary>
public sealed class AdoNetConversionEntry
{
    public string FilePath { get; }
    public int Line { get; }
    public string AdoNetMethod { get; }
    public string OriginalSql { get; }
    public string? ChainCode { get; }
    public IReadOnlyList<AdoNetConversionDiagnostic> Diagnostics { get; }
    public bool IsSuggestionOnly { get; }

    public AdoNetConversionEntry(
        string filePath, int line, string adoNetMethod,
        string originalSql, string? chainCode,
        IReadOnlyList<AdoNetConversionDiagnostic> diagnostics,
        bool isSuggestionOnly = false)
    {
        FilePath = filePath;
        Line = line;
        AdoNetMethod = adoNetMethod;
        OriginalSql = originalSql;
        ChainCode = chainCode;
        Diagnostics = diagnostics;
        IsSuggestionOnly = isSuggestionOnly;
    }

    public bool IsConvertible => ChainCode != null && !IsSuggestionOnly;
    public bool HasWarnings => Diagnostics.Any(d => d.Severity == "Warning");
}

/// <summary>
/// A diagnostic message from the ADO.NET conversion process.
/// </summary>
public sealed class AdoNetConversionDiagnostic
{
    public string Severity { get; }
    public string Message { get; }

    public AdoNetConversionDiagnostic(string severity, string message)
    {
        Severity = severity;
        Message = message;
    }
}
