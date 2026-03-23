using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Quarry.Generators.Models;

namespace Quarry.Analyzers.Rules.Simplification;

/// <summary>
/// QRA104: Detects contradictory WHERE conditions (always false).
/// </summary>
internal sealed class ContradictoryConditionRule : IQueryAnalysisRule
{
    public string RuleId => "QRA104";
    public DiagnosticDescriptor Descriptor => AnalyzerDiagnosticDescriptors.ContradictoryCondition;

    public IEnumerable<Diagnostic> Analyze(QueryAnalysisContext context)
    {
        if (context.Site.ClauseKind != ClauseKind.Where || context.Site.Expression == null)
            yield break;

        var sql = context.GetRenderedSql()!;

        // Extract comparison triples from AND-joined conditions
        var comparisons = ComparisonExtractor.Extract(sql);
        if (comparisons.Count < 2)
            yield break;

        // For same column, check contradictory ranges
        for (int i = 0; i < comparisons.Count; i++)
        {
            for (int j = i + 1; j < comparisons.Count; j++)
            {
                var a = comparisons[i];
                var b = comparisons[j];

                if (!string.Equals(a.Column, b.Column, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (IsContradictory(a, b))
                {
                    yield return Diagnostic.Create(Descriptor, context.InvocationSyntax.GetLocation());
                    yield break;
                }
            }
        }
    }

    private static bool IsContradictory(ComparisonTriple a, ComparisonTriple b)
    {
        if (!double.TryParse(a.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var va) ||
            !double.TryParse(b.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var vb))
            return false;

        // x > 5 AND x < 3 -> contradictory
        if (a.Op == ">" && b.Op == "<" && va >= vb) return true;
        if (a.Op == "<" && b.Op == ">" && vb >= va) return true;
        if (a.Op == ">=" && b.Op == "<" && va >= vb) return true;
        if (a.Op == "<" && b.Op == ">=" && vb >= va) return true;
        if (a.Op == ">" && b.Op == "<=" && va >= vb) return true;
        if (a.Op == "<=" && b.Op == ">" && vb >= va) return true;
        if (a.Op == ">=" && b.Op == "<=" && va > vb) return true;
        if (a.Op == "<=" && b.Op == ">=" && vb > va) return true;

        // x = 5 AND x = 3 -> contradictory
        if (a.Op == "=" && b.Op == "=" && va != vb) return true;

        return false;
    }
}

internal readonly struct ComparisonTriple
{
    public string Column { get; }
    public string Op { get; }
    public string Value { get; }

    public ComparisonTriple(string column, string op, string value)
    {
        Column = column;
        Op = op;
        Value = value;
    }
}

internal static class ComparisonExtractor
{
    private static readonly string[] Operators = { ">=", "<=", "!=", "<>", ">", "<", "=" };

    public static List<ComparisonTriple> Extract(string sql)
    {
        var result = new List<ComparisonTriple>();

        // Split on AND (case-insensitive)
        var parts = sql.Split(new[] { " AND " }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            var trimmed = part.Trim().TrimStart('(').TrimEnd(')').Trim();

            foreach (var op in Operators)
            {
                var opIndex = trimmed.IndexOf($" {op} ");
                if (opIndex < 0) continue;

                var column = trimmed.Substring(0, opIndex).Trim();
                var value = trimmed.Substring(opIndex + op.Length + 2).Trim();

                // Resolve parameter references to literal values if they start with @p
                if (value.StartsWith("@p"))
                    break; // Can't compare parameterized values statically

                result.Add(new ComparisonTriple(column, op, value));
                break;
            }
        }

        return result;
    }
}
