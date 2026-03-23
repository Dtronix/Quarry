using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Quarry.Generators.Models;

namespace Quarry.Analyzers.Rules.Simplification;

/// <summary>
/// QRA105: Detects redundant conditions subsumed by stronger ones on the same column.
/// </summary>
internal sealed class RedundantConditionRule : IQueryAnalysisRule
{
    public string RuleId => "QRA105";
    public DiagnosticDescriptor Descriptor => AnalyzerDiagnosticDescriptors.RedundantCondition;

    public IEnumerable<Diagnostic> Analyze(QueryAnalysisContext context)
    {
        if (context.Site.ClauseKind != ClauseKind.Where || context.Site.Expression == null)
            yield break;

        var comparisons = ComparisonExtractor.Extract(context.GetRenderedSql()!);
        if (comparisons.Count < 2)
            yield break;

        for (int i = 0; i < comparisons.Count; i++)
        {
            for (int j = i + 1; j < comparisons.Count; j++)
            {
                var a = comparisons[i];
                var b = comparisons[j];

                if (!string.Equals(a.Column, b.Column, StringComparison.OrdinalIgnoreCase))
                    continue;

                var (redundant, stronger) = FindRedundant(a, b);
                if (redundant != null && stronger != null)
                {
                    yield return Diagnostic.Create(
                        Descriptor,
                        context.InvocationSyntax.GetLocation(),
                        $"{redundant.Value.Column} {redundant.Value.Op} {redundant.Value.Value}",
                        $"{stronger.Value.Column} {stronger.Value.Op} {stronger.Value.Value}");
                    yield break;
                }
            }
        }
    }

    private static (ComparisonTriple? redundant, ComparisonTriple? stronger) FindRedundant(
        ComparisonTriple a, ComparisonTriple b)
    {
        if (!double.TryParse(a.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var va) ||
            !double.TryParse(b.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var vb))
            return (null, null);

        // x > 5 AND x > 3 -> x > 3 is redundant (subsumed by x > 5)
        if (a.Op == ">" && b.Op == ">" && va > vb) return (b, a);
        if (a.Op == ">" && b.Op == ">" && vb > va) return (a, b);

        if (a.Op == ">=" && b.Op == ">=" && va > vb) return (b, a);
        if (a.Op == ">=" && b.Op == ">=" && vb > va) return (a, b);

        if (a.Op == "<" && b.Op == "<" && va < vb) return (b, a);
        if (a.Op == "<" && b.Op == "<" && vb < va) return (a, b);

        if (a.Op == "<=" && b.Op == "<=" && va < vb) return (b, a);
        if (a.Op == "<=" && b.Op == "<=" && vb < va) return (a, b);

        // x > 5 AND x >= 3 -> x >= 3 is redundant
        if (a.Op == ">" && b.Op == ">=" && va >= vb) return (b, a);
        if (b.Op == ">" && a.Op == ">=" && vb >= va) return (a, b);

        if (a.Op == "<" && b.Op == "<=" && va <= vb) return (b, a);
        if (b.Op == "<" && a.Op == "<=" && vb <= va) return (a, b);

        return (null, null);
    }
}
