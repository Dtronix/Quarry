using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Quarry.Migration;

/// <summary>
/// Public facade for the SqlKata-to-Quarry conversion pipeline.
/// Maps SqlKata fluent calls mechanically to Quarry chain API equivalents.
/// </summary>
public sealed class SqlKataConverter
{
    /// <summary>
    /// Scans a compilation for SqlKata Query chains and converts each to a Quarry chain API call.
    /// </summary>
    public IReadOnlyList<SqlKataConversionEntry> ConvertAll(Compilation compilation)
    {
        var schemaResolver = new SchemaResolver();
        var schemaMap = schemaResolver.Resolve(compilation);

        var detector = new SqlKataDetector();
        var results = new List<SqlKataConversionEntry>();

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

                results.Add(new SqlKataConversionEntry(
                    filePath: tree.FilePath,
                    line: line,
                    tableName: site.TableName,
                    originalCode: site.ChainExpression.ToString(),
                    chainCode: result.ChainCode,
                    diagnostics: result.Diagnostics.Select(d => new SqlKataConversionDiagnostic(
                        d.Severity.ToString(), d.Message)).ToList()));
            }
        }

        return results;
    }

    internal static ConversionResult Translate(SqlKataCallSite site, SchemaMap schemaMap)
    {
        var diagnostics = new List<ConversionDiagnostic>();

        // Resolve table name to schema mapping
        if (!schemaMap.TryGetEntity(site.TableName, out var entity))
        {
            diagnostics.Add(new ConversionDiagnostic(
                ConversionDiagnosticSeverity.Error,
                $"No Quarry schema found for table '{site.TableName}'"));
            return new ConversionResult(site.ChainExpression.ToString(), null, diagnostics);
        }

        // Add warnings for unsupported methods
        foreach (var method in site.UnsupportedMethods)
        {
            diagnostics.Add(new ConversionDiagnostic(
                ConversionDiagnosticSeverity.Warning,
                $"Unsupported SqlKata method '{method}' was skipped"));
        }

        var sb = new StringBuilder();
        sb.Append($"db.{entity.AccessorName}()");

        var lambdaVar = DeriveLambdaVariable(entity.AccessorName);
        bool hasAggregate = false;
        string? aggregateCode = null;

        foreach (var step in site.Steps)
        {
            switch (step.MethodName)
            {
                case "Where":
                    EmitWhereClause(sb, step, lambdaVar, entity, diagnostics);
                    break;

                case "OrWhere":
                    diagnostics.Add(new ConversionDiagnostic(
                        ConversionDiagnosticSeverity.Warning,
                        "OrWhere requires manual combination with preceding Where using || operator"));
                    EmitWhereClause(sb, step, lambdaVar, entity, diagnostics);
                    break;

                case "WhereNull":
                    EmitNullCheck(sb, step, lambdaVar, entity, "==");
                    break;

                case "WhereNotNull":
                    EmitNullCheck(sb, step, lambdaVar, entity, "!=");
                    break;

                case "WhereIn":
                    EmitWhereIn(sb, step, lambdaVar, entity, diagnostics);
                    break;

                case "WhereBetween":
                case "OrWhereBetween":
                    EmitWhereBetween(sb, step, lambdaVar, entity, diagnostics);
                    break;

                case "WhereNot":
                    diagnostics.Add(new ConversionDiagnostic(
                        ConversionDiagnosticSeverity.Warning,
                        "WhereNot requires manual negation in Quarry Where lambda"));
                    EmitWhereClause(sb, step, lambdaVar, entity, diagnostics);
                    break;

                case "OrWhereNull":
                    diagnostics.Add(new ConversionDiagnostic(
                        ConversionDiagnosticSeverity.Warning,
                        "OrWhereNull requires manual combination with preceding Where using || operator"));
                    EmitNullCheck(sb, step, lambdaVar, entity, "==");
                    break;

                case "OrWhereNotNull":
                    diagnostics.Add(new ConversionDiagnostic(
                        ConversionDiagnosticSeverity.Warning,
                        "OrWhereNotNull requires manual combination with preceding Where using || operator"));
                    EmitNullCheck(sb, step, lambdaVar, entity, "!=");
                    break;

                case "WhereNotIn":
                case "OrWhereIn":
                    diagnostics.Add(new ConversionDiagnostic(
                        ConversionDiagnosticSeverity.Warning,
                        $"'{step.MethodName}' requires manual rewrite — use Contains() with negation or || as needed"));
                    EmitWhereIn(sb, step, lambdaVar, entity, diagnostics);
                    break;

                case "WhereTrue":
                case "WhereFalse":
                    diagnostics.Add(new ConversionDiagnostic(
                        ConversionDiagnosticSeverity.Warning,
                        $"'{step.MethodName}' requires manual conversion to boolean comparison"));
                    sb.Append($"/* TODO: {step.MethodName}({FormatArguments(step)}) */");
                    break;

                case "OrderBy":
                    EmitOrderBy(sb, step, lambdaVar, entity, ascending: true);
                    break;

                case "OrderByDesc":
                    EmitOrderBy(sb, step, lambdaVar, entity, ascending: false);
                    break;

                case "Select":
                    EmitSelect(sb, step, lambdaVar, entity);
                    break;

                case "Join":
                    EmitJoin(sb, step, "Join", diagnostics);
                    break;

                case "LeftJoin":
                    EmitJoin(sb, step, "LeftJoin", diagnostics);
                    break;

                case "RightJoin":
                    EmitJoin(sb, step, "RightJoin", diagnostics);
                    break;

                case "CrossJoin":
                    EmitCrossJoin(sb, step, diagnostics);
                    break;

                case "Limit":
                case "Take":
                    sb.Append($".Limit({FormatArguments(step)})");
                    break;

                case "Offset":
                case "Skip":
                    sb.Append($".Offset({FormatArguments(step)})");
                    break;

                case "GroupBy":
                    EmitGroupBy(sb, step, lambdaVar, entity);
                    break;

                case "Having":
                    diagnostics.Add(new ConversionDiagnostic(
                        ConversionDiagnosticSeverity.Warning,
                        "SqlKata Having requires manual rewrite to Quarry .Having(lambda) syntax"));
                    sb.Append($"/* TODO: .Having(...) */");
                    break;

                case "Distinct":
                    sb.Append(".Distinct()");
                    break;

                case "AsCount":
                    hasAggregate = true;
                    aggregateCode = $".Select({lambdaVar} => Sql.Count()).ExecuteScalarAsync<int>()";
                    break;

                case "AsSum":
                    hasAggregate = true;
                    var sumCol = ExtractFirstStringArg(step);
                    var sumProp = sumCol != null ? ResolveProperty(sumCol, entity) : "/* column */";
                    aggregateCode = $".Select({lambdaVar} => Sql.Sum({lambdaVar}.{sumProp})).ExecuteScalarAsync<int>()";
                    break;

                case "AsAvg":
                    hasAggregate = true;
                    var avgCol = ExtractFirstStringArg(step);
                    var avgProp = avgCol != null ? ResolveProperty(avgCol, entity) : "/* column */";
                    aggregateCode = $".Select({lambdaVar} => Sql.Avg({lambdaVar}.{avgProp})).ExecuteScalarAsync<double>()";
                    break;

                case "AsMin":
                case "AsMax":
                    hasAggregate = true;
                    var minMaxFunc = step.MethodName == "AsMin" ? "Min" : "Max";
                    var minMaxCol = ExtractFirstStringArg(step);
                    var minMaxProp = minMaxCol != null ? ResolveProperty(minMaxCol, entity) : "/* column */";
                    aggregateCode = $".Select({lambdaVar} => Sql.{minMaxFunc}({lambdaVar}.{minMaxProp})).ExecuteScalarAsync<int>()";
                    break;

                case "ForPage":
                    EmitForPage(sb, step, diagnostics);
                    break;

                default:
                    diagnostics.Add(new ConversionDiagnostic(
                        ConversionDiagnosticSeverity.Warning,
                        $"Unrecognized chain method '{step.MethodName}' was skipped"));
                    break;
            }
        }

        // Append terminal or aggregate
        if (hasAggregate && aggregateCode != null)
        {
            sb.Append(aggregateCode);
        }
        else if (site.TerminalMethod != null)
        {
            sb.Append(MapTerminal(site.TerminalMethod));
        }
        else
        {
            // No terminal — append default
            sb.Append(".ExecuteFetchAllAsync()");
        }

        return new ConversionResult(
            site.ChainExpression.ToString(),
            sb.ToString(),
            diagnostics);
    }

    private static void EmitWhereClause(StringBuilder sb, SqlKataChainStep step,
        string lambdaVar, EntityMapping entity, List<ConversionDiagnostic> diagnostics)
    {
        // Where("column", "op", value) or Where("column", value)
        if (step.Arguments.Count >= 2)
        {
            var columnArg = ExtractStringLiteral(step.Arguments[0]);
            if (columnArg == null)
            {
                sb.Append($".Where({FormatArguments(step)})");
                return;
            }

            var prop = ResolveProperty(columnArg, entity);

            if (step.Arguments.Count >= 3)
            {
                // Where("column", "op", value)
                var op = ExtractStringLiteral(step.Arguments[1]) ?? "==";
                var csharpOp = MapOperator(op);
                var value = step.Arguments[2].ToString();
                sb.Append($".Where({lambdaVar} => {lambdaVar}.{prop} {csharpOp} {value})");
            }
            else
            {
                // Where("column", value) — default equals
                var value = step.Arguments[1].ToString();
                sb.Append($".Where({lambdaVar} => {lambdaVar}.{prop} == {value})");
            }
        }
        else
        {
            sb.Append($".Where({FormatArguments(step)})");
        }
    }

    private static void EmitNullCheck(StringBuilder sb, SqlKataChainStep step,
        string lambdaVar, EntityMapping entity, string op)
    {
        var columnArg = step.Arguments.Count > 0 ? ExtractStringLiteral(step.Arguments[0]) : null;
        if (columnArg != null)
        {
            var prop = ResolveProperty(columnArg, entity);
            sb.Append($".Where({lambdaVar} => {lambdaVar}.{prop} {op} null)");
        }
        else
        {
            sb.Append($"/* TODO: {step.MethodName}({FormatArguments(step)}) — non-literal column */");
        }
    }

    private static void EmitWhereIn(StringBuilder sb, SqlKataChainStep step,
        string lambdaVar, EntityMapping entity, List<ConversionDiagnostic> diagnostics)
    {
        if (step.Arguments.Count >= 2)
        {
            var columnArg = ExtractStringLiteral(step.Arguments[0]);
            if (columnArg != null)
            {
                var prop = ResolveProperty(columnArg, entity);
                var values = step.Arguments[1].ToString();
                sb.Append($".Where({lambdaVar} => {values}.Contains({lambdaVar}.{prop}))");
                return;
            }
        }
        sb.Append($".Where(/* WhereIn: {FormatArguments(step)} */)");
    }

    private static void EmitWhereBetween(StringBuilder sb, SqlKataChainStep step,
        string lambdaVar, EntityMapping entity, List<ConversionDiagnostic> diagnostics)
    {
        if (step.Arguments.Count >= 3)
        {
            var columnArg = ExtractStringLiteral(step.Arguments[0]);
            if (columnArg != null)
            {
                var prop = ResolveProperty(columnArg, entity);
                var low = step.Arguments[1].ToString();
                var high = step.Arguments[2].ToString();
                sb.Append($".Where({lambdaVar} => {lambdaVar}.{prop} >= {low} && {lambdaVar}.{prop} <= {high})");
                return;
            }
        }
        sb.Append($".Where(/* WhereBetween: {FormatArguments(step)} */)");
    }

    private static void EmitOrderBy(StringBuilder sb, SqlKataChainStep step,
        string lambdaVar, EntityMapping entity, bool ascending)
    {
        var columnArg = step.Arguments.Count > 0 ? ExtractStringLiteral(step.Arguments[0]) : null;
        if (columnArg != null)
        {
            var prop = ResolveProperty(columnArg, entity);
            if (ascending)
                sb.Append($".OrderBy({lambdaVar} => {lambdaVar}.{prop})");
            else
                sb.Append($".OrderBy({lambdaVar} => {lambdaVar}.{prop}, Direction.Descending)");
        }
        else
        {
            sb.Append($".OrderBy({FormatArguments(step)})");
        }
    }

    private static void EmitSelect(StringBuilder sb, SqlKataChainStep step,
        string lambdaVar, EntityMapping entity)
    {
        var columns = new List<string>();
        foreach (var arg in step.Arguments)
        {
            var col = ExtractStringLiteral(arg);
            if (col != null)
                columns.Add(ResolveProperty(col, entity));
        }

        if (columns.Count == 1)
        {
            sb.Append($".Select({lambdaVar} => {lambdaVar}.{columns[0]})");
        }
        else if (columns.Count > 1)
        {
            var props = string.Join(", ", columns.Select(c => $"{lambdaVar}.{c}"));
            sb.Append($".Select({lambdaVar} => new {{ {props} }})");
        }
    }

    private static void EmitGroupBy(StringBuilder sb, SqlKataChainStep step,
        string lambdaVar, EntityMapping entity)
    {
        var columnArg = step.Arguments.Count > 0 ? ExtractStringLiteral(step.Arguments[0]) : null;
        if (columnArg != null)
        {
            var prop = ResolveProperty(columnArg, entity);
            sb.Append($".GroupBy({lambdaVar} => {lambdaVar}.{prop})");
        }
        else
        {
            sb.Append($".GroupBy({FormatArguments(step)})");
        }
    }

    private static void EmitJoin(StringBuilder sb, SqlKataChainStep step,
        string joinMethod, List<ConversionDiagnostic> diagnostics)
    {
        // Join("table", "t.col1", "o.col2")
        diagnostics.Add(new ConversionDiagnostic(
            ConversionDiagnosticSeverity.Warning,
            $"SqlKata {joinMethod} requires manual rewrite to Quarry .{joinMethod}<T>((a, b) => condition) syntax"));
        var args = FormatArguments(step);
        sb.Append($"/* TODO: .{joinMethod}<T>(condition) — was: {joinMethod}({args}) */");
    }

    private static void EmitCrossJoin(StringBuilder sb, SqlKataChainStep step, List<ConversionDiagnostic> diagnostics)
    {
        diagnostics.Add(new ConversionDiagnostic(
            ConversionDiagnosticSeverity.Warning,
            "SqlKata CrossJoin requires manual rewrite to Quarry .CrossJoin<T>() syntax"));
        sb.Append($"/* TODO: .CrossJoin<T>() */");
    }

    private static void EmitForPage(StringBuilder sb, SqlKataChainStep step, List<ConversionDiagnostic> diagnostics)
    {
        if (step.Arguments.Count >= 2)
        {
            var page = step.Arguments[0].ToString();
            var perPage = step.Arguments[1].ToString();
            sb.Append($".Offset(({page} - 1) * {perPage}).Limit({perPage})");
        }
        else if (step.Arguments.Count == 1)
        {
            var page = step.Arguments[0].ToString();
            sb.Append($".Offset(({page} - 1) * 15).Limit(15)");
        }
    }

    private static string MapTerminal(string terminalMethod)
    {
        return terminalMethod switch
        {
            "Get" or "GetAsync" => ".ExecuteFetchAllAsync()",
            "First" or "FirstAsync" => ".ExecuteFetchFirstAsync()",
            "FirstOrDefault" or "FirstOrDefaultAsync" => ".ExecuteFetchFirstOrDefaultAsync()",
            "Count" or "CountAsync" => ".ExecuteScalarAsync<int>()",
            "Paginate" or "PaginateAsync" => ".ExecuteFetchAllAsync()",
            _ => $".ExecuteFetchAllAsync() /* unmapped terminal: {terminalMethod} */",
        };
    }

    private static string MapOperator(string sqlOp)
    {
        return sqlOp switch
        {
            "=" => "==",
            "!=" or "<>" => "!=",
            "<" => "<",
            ">" => ">",
            "<=" => "<=",
            ">=" => ">=",
            _ => sqlOp,
        };
    }

    private static string ResolveProperty(string columnName, EntityMapping entity)
    {
        if (entity.TryGetProperty(columnName, out var prop))
            return prop;

        return ToPascalCase(columnName);
    }

    private static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var parts = name.Split('_');
        return string.Concat(parts.Select(p =>
            p.Length > 0 ? char.ToUpperInvariant(p[0]) + p.Substring(1).ToLowerInvariant() : ""));
    }

    private static string? ExtractStringLiteral(ArgumentSyntax arg)
    {
        if (arg.Expression is LiteralExpressionSyntax literal &&
            literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return literal.Token.ValueText;
        }
        return null;
    }

    private static string? ExtractFirstStringArg(SqlKataChainStep step)
    {
        if (step.Arguments.Count > 0)
            return ExtractStringLiteral(step.Arguments[0]);
        return null;
    }

    private static string FormatArguments(SqlKataChainStep step)
    {
        return string.Join(", ", step.Arguments.Select(a => a.ToString()));
    }

    private static string DeriveLambdaVariable(string accessorName)
    {
        if (string.IsNullOrEmpty(accessorName)) return "x";
        return char.ToLowerInvariant(accessorName[0]).ToString();
    }
}

/// <summary>
/// Result of attempting to convert a single SqlKata Query chain.
/// </summary>
public sealed class SqlKataConversionEntry
{
    public string FilePath { get; }
    public int Line { get; }
    public string TableName { get; }
    public string OriginalCode { get; }
    public string? ChainCode { get; }
    public IReadOnlyList<SqlKataConversionDiagnostic> Diagnostics { get; }

    public SqlKataConversionEntry(
        string filePath, int line, string tableName,
        string originalCode, string? chainCode,
        IReadOnlyList<SqlKataConversionDiagnostic> diagnostics)
    {
        FilePath = filePath;
        Line = line;
        TableName = tableName;
        OriginalCode = originalCode;
        ChainCode = chainCode;
        Diagnostics = diagnostics;
    }

    public bool IsConvertible => ChainCode != null;
    public bool HasWarnings => Diagnostics.Any(d => d.Severity == "Warning");
}

/// <summary>
/// A diagnostic message from the SqlKata conversion process.
/// </summary>
public sealed class SqlKataConversionDiagnostic
{
    public string Severity { get; }
    public string Message { get; }

    public SqlKataConversionDiagnostic(string severity, string message)
    {
        Severity = severity;
        Message = message;
    }
}
