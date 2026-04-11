using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Quarry.Migration;

/// <summary>
/// Public facade for the EF Core-to-Quarry conversion pipeline.
/// Takes a Roslyn Compilation and returns conversion results for detected EF Core LINQ chains.
/// </summary>
public sealed class EfCoreConverter
{
    /// <summary>
    /// Scans a compilation for EF Core LINQ chains and converts each to a Quarry chain API call.
    /// </summary>
    public IReadOnlyList<EfCoreConversionEntry> ConvertAll(Compilation compilation)
    {
        var schemaResolver = new SchemaResolver();
        var schemaMap = schemaResolver.Resolve(compilation);

        var detector = new EfCoreDetector();
        var results = new List<EfCoreConversionEntry>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            var sites = detector.Detect(model, root);

            foreach (var site in sites)
            {
                var result = Translate(site, schemaMap);

                var lineSpan = site.Location.GetLineSpan();
                var line = lineSpan.StartLinePosition.Line + 1;

                results.Add(new EfCoreConversionEntry(
                    filePath: tree.FilePath,
                    line: line,
                    terminalMethod: site.TerminalMethod,
                    entityType: site.EntityTypeName,
                    originalCode: site.ChainExpression.ToString(),
                    chainCode: result.ChainCode,
                    diagnostics: result.Diagnostics.Select(d => new EfCoreConversionDiagnostic(
                        d.Severity.ToString(), d.Message)).ToList()));
            }
        }

        return results;
    }

    internal static ConversionResult Translate(EfCoreCallSite site, SchemaMap schemaMap)
    {
        var diagnostics = new List<ConversionDiagnostic>();

        // Resolve entity type to schema mapping
        if (!schemaMap.TryGetEntityByTypeName(site.EntityTypeName, out var entity))
        {
            diagnostics.Add(new ConversionDiagnostic(
                ConversionDiagnosticSeverity.Error,
                $"No Quarry schema found for entity '{site.EntityTypeName}'"));
            return new ConversionResult(site.ChainExpression.ToString(), null, diagnostics);
        }

        // Add warnings for unsupported methods
        foreach (var method in site.UnsupportedMethods)
        {
            diagnostics.Add(new ConversionDiagnostic(
                ConversionDiagnosticSeverity.Warning,
                $"Unsupported EF Core method '{method}' was skipped"));
        }

        var sb = new StringBuilder();
        sb.Append($"db.{entity.AccessorName}()");

        var lambdaVar = DeriveLambdaVariable(entity.AccessorName);

        foreach (var step in site.Steps)
        {
            switch (step.MethodName)
            {
                case "Where":
                    var whereLambda = RewriteLambda(step, lambdaVar, entity);
                    if (whereLambda != null)
                        sb.Append($".Where({whereLambda})");
                    else
                        sb.Append($".Where({FormatArguments(step)})");
                    break;

                case "OrderBy":
                    var orderByLambda = RewriteLambda(step, lambdaVar, entity);
                    if (orderByLambda != null)
                        sb.Append($".OrderBy({orderByLambda})");
                    else
                        sb.Append($".OrderBy({FormatArguments(step)})");
                    break;

                case "OrderByDescending":
                    var orderByDescLambda = RewriteLambda(step, lambdaVar, entity);
                    if (orderByDescLambda != null)
                        sb.Append($".OrderBy({orderByDescLambda}, Direction.Descending)");
                    else
                        sb.Append($".OrderBy({FormatArguments(step)}, Direction.Descending)");
                    break;

                case "ThenBy":
                    var thenByLambda = RewriteLambda(step, lambdaVar, entity);
                    if (thenByLambda != null)
                        sb.Append($".ThenBy({thenByLambda})");
                    else
                        sb.Append($".ThenBy({FormatArguments(step)})");
                    break;

                case "ThenByDescending":
                    var thenByDescLambda = RewriteLambda(step, lambdaVar, entity);
                    if (thenByDescLambda != null)
                        sb.Append($".ThenBy({thenByDescLambda}, Direction.Descending)");
                    else
                        sb.Append($".ThenBy({FormatArguments(step)}, Direction.Descending)");
                    break;

                case "Select":
                    var selectLambda = RewriteLambda(step, lambdaVar, entity);
                    if (selectLambda != null)
                        sb.Append($".Select({selectLambda})");
                    else
                        sb.Append($".Select({FormatArguments(step)})");
                    break;

                case "GroupBy":
                    var groupByLambda = RewriteLambda(step, lambdaVar, entity);
                    if (groupByLambda != null)
                        sb.Append($".GroupBy({groupByLambda})");
                    else
                        sb.Append($".GroupBy({FormatArguments(step)})");
                    break;

                case "Take":
                    sb.Append($".Limit({FormatArguments(step)})");
                    break;

                case "Skip":
                    sb.Append($".Offset({FormatArguments(step)})");
                    break;

                case "Distinct":
                    sb.Append(".Distinct()");
                    break;

                case "Join":
                    diagnostics.Add(new ConversionDiagnostic(
                        ConversionDiagnosticSeverity.Warning,
                        "LINQ Join requires manual rewrite to Quarry Join<T> syntax"));
                    sb.Append($"/* TODO: .Join<T>(...) */");
                    break;

                default:
                    diagnostics.Add(new ConversionDiagnostic(
                        ConversionDiagnosticSeverity.Warning,
                        $"Unrecognized chain method '{step.MethodName}' was skipped"));
                    break;
            }
        }

        // Map terminal method
        sb.Append(MapTerminal(site.TerminalMethod, diagnostics));

        return new ConversionResult(
            site.ChainExpression.ToString(),
            sb.ToString(),
            diagnostics);
    }

    private static string? RewriteLambda(EfCoreChainStep step, string lambdaVar, EntityMapping entity)
    {
        if (step.Arguments.Count == 0) return null;

        var arg = step.Arguments[0];
        var expr = arg.Expression;

        // Match lambda: x => x.Property or (x) => expression
        if (expr is SimpleLambdaExpressionSyntax simpleLambda)
        {
            var originalParam = simpleLambda.Parameter.Identifier.Text;
            var body = simpleLambda.Body.ToString();
            if (originalParam != lambdaVar)
                body = ReplaceParameter(body, originalParam, lambdaVar);
            return $"{lambdaVar} => {body}";
        }

        if (expr is ParenthesizedLambdaExpressionSyntax parenLambda &&
            parenLambda.ParameterList.Parameters.Count == 1)
        {
            var originalParam = parenLambda.ParameterList.Parameters[0].Identifier.Text;
            var body = parenLambda.Body.ToString();
            if (originalParam != lambdaVar)
                body = ReplaceParameter(body, originalParam, lambdaVar);
            return $"{lambdaVar} => {body}";
        }

        return null;
    }

    private static string ReplaceParameter(string body, string oldParam, string newParam)
    {
        // Simple token replacement — replace parameter name at word boundaries
        var result = new StringBuilder();
        for (int i = 0; i < body.Length; i++)
        {
            if (i + oldParam.Length <= body.Length &&
                body.Substring(i, oldParam.Length) == oldParam)
            {
                bool startBound = i == 0 || !char.IsLetterOrDigit(body[i - 1]) && body[i - 1] != '_';
                bool endBound = i + oldParam.Length >= body.Length ||
                    !char.IsLetterOrDigit(body[i + oldParam.Length]) && body[i + oldParam.Length] != '_';

                if (startBound && endBound)
                {
                    result.Append(newParam);
                    i += oldParam.Length - 1;
                    continue;
                }
            }
            result.Append(body[i]);
        }
        return result.ToString();
    }

    private static string FormatArguments(EfCoreChainStep step)
    {
        return string.Join(", ", step.Arguments.Select(a => a.ToString()));
    }

    private static string MapTerminal(string terminalMethod, List<ConversionDiagnostic> diagnostics)
    {
        switch (terminalMethod)
        {
            case "ToListAsync":
            case "ToList":
            case "ToArrayAsync":
            case "ToArray":
                return ".ExecuteFetchAllAsync()";
            case "FirstAsync":
            case "First":
                return ".ExecuteFetchFirstAsync()";
            case "FirstOrDefaultAsync":
            case "FirstOrDefault":
                return ".ExecuteFetchFirstOrDefaultAsync()";
            case "SingleAsync":
            case "Single":
                return ".ExecuteFetchSingleAsync()";
            case "SingleOrDefaultAsync":
            case "SingleOrDefault":
                return ".ExecuteFetchSingleOrDefaultAsync()";
            case "CountAsync":
            case "Count":
                return ".ExecuteScalarAsync<int>()";
            case "LongCountAsync":
            case "LongCount":
                return ".ExecuteScalarAsync<long>()";
            case "AnyAsync":
            case "Any":
                diagnostics.Add(new ConversionDiagnostic(
                    ConversionDiagnosticSeverity.Warning,
                    "Any() has no direct Quarry equivalent — using Limit(1).ExecuteFetchFirstOrDefaultAsync() != null pattern"));
                return ".Limit(1).ExecuteFetchFirstOrDefaultAsync()";
            case "SumAsync":
            case "Sum":
            case "AverageAsync":
            case "Average":
            case "MinAsync":
            case "Min":
            case "MaxAsync":
            case "Max":
                diagnostics.Add(new ConversionDiagnostic(
                    ConversionDiagnosticSeverity.Warning,
                    $"Aggregate terminal '{terminalMethod}' requires manual conversion to Sql.{MapAggregateFunction(terminalMethod)}() + Select + ExecuteScalarAsync"));
                return $".ExecuteScalarAsync<int>() /* TODO: use Sql.{MapAggregateFunction(terminalMethod)}() in Select */";
            default:
                diagnostics.Add(new ConversionDiagnostic(
                    ConversionDiagnosticSeverity.Warning,
                    $"Unknown terminal method '{terminalMethod}'"));
                return $".ExecuteFetchAllAsync() /* TODO: map '{terminalMethod}' */";
        }
    }

    private static string MapAggregateFunction(string terminal)
    {
        if (terminal.StartsWith("Sum", StringComparison.Ordinal)) return "Sum";
        if (terminal.StartsWith("Average", StringComparison.Ordinal)) return "Avg";
        if (terminal.StartsWith("Min", StringComparison.Ordinal)) return "Min";
        if (terminal.StartsWith("Max", StringComparison.Ordinal)) return "Max";
        return terminal;
    }

    private static string DeriveLambdaVariable(string accessorName)
    {
        if (string.IsNullOrEmpty(accessorName)) return "x";
        var first = char.ToLowerInvariant(accessorName[0]);
        return first.ToString();
    }
}

/// <summary>
/// Result of attempting to convert a single EF Core LINQ chain.
/// </summary>
public sealed class EfCoreConversionEntry
{
    public string FilePath { get; }
    public int Line { get; }
    public string TerminalMethod { get; }
    public string? EntityType { get; }
    public string OriginalCode { get; }
    public string? ChainCode { get; }
    public IReadOnlyList<EfCoreConversionDiagnostic> Diagnostics { get; }

    public EfCoreConversionEntry(
        string filePath, int line, string terminalMethod, string? entityType,
        string originalCode, string? chainCode,
        IReadOnlyList<EfCoreConversionDiagnostic> diagnostics)
    {
        FilePath = filePath;
        Line = line;
        TerminalMethod = terminalMethod;
        EntityType = entityType;
        OriginalCode = originalCode;
        ChainCode = chainCode;
        Diagnostics = diagnostics;
    }

    public bool IsConvertible => ChainCode != null;
    public bool HasWarnings => Diagnostics.Any(d => d.Severity == "Warning");
}

/// <summary>
/// A diagnostic message from the EF Core conversion process.
/// </summary>
public sealed class EfCoreConversionDiagnostic
{
    public string Severity { get; }
    public string Message { get; }

    public EfCoreConversionDiagnostic(string severity, string message)
    {
        Severity = severity;
        Message = message;
    }
}
