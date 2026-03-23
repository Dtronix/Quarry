using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Quarry.Generators.Models;

namespace Quarry.Analyzers.Rules.Performance;

/// <summary>
/// QRA303: Detects OR conditions across different columns that may prevent index usage.
/// </summary>
internal sealed class OrOnDifferentColumnsRule : IQueryAnalysisRule
{
    public string RuleId => "QRA303";
    public DiagnosticDescriptor Descriptor => AnalyzerDiagnosticDescriptors.OrOnDifferentColumns;

    public IEnumerable<Diagnostic> Analyze(QueryAnalysisContext context)
    {
        if (context.Site.ClauseKind != ClauseKind.Where || context.Site.Expression == null)
            yield break;

        var sql = context.GetRenderedSql()!;
        if (!sql.Contains(" OR ", StringComparison.OrdinalIgnoreCase))
            yield break;

        // Split on OR and extract column names from each side
        var parts = sql.Split(new[] { " OR " }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            yield break;

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool hasDifferentColumns = false;

        foreach (var part in parts)
        {
            var col = ExtractColumnName(part.Trim());
            if (col != null)
            {
                if (columns.Count > 0 && !columns.Contains(col))
                {
                    hasDifferentColumns = true;
                    break;
                }
                columns.Add(col);
            }
        }

        if (hasDifferentColumns)
        {
            yield return Diagnostic.Create(Descriptor, context.InvocationSyntax.GetLocation());
        }
    }

    private static string? ExtractColumnName(string condition)
    {
        // Extract column reference like "t0.ColumnName" before the operator
        condition = condition.TrimStart('(').TrimEnd(')').Trim();
        var spaceIdx = condition.IndexOf(' ');
        if (spaceIdx > 0)
            return condition.Substring(0, spaceIdx);
        return null;
    }
}
