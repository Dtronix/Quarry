using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Quarry.Generators.Models;

namespace Quarry.Analyzers.Rules.Simplification;

/// <summary>
/// QRA106: Detects nullable columns used in equality comparisons without null handling.
/// </summary>
internal sealed class NullableWithoutNullCheckRule : IQueryAnalysisRule
{
    public string RuleId => "QRA106";
    public DiagnosticDescriptor Descriptor => AnalyzerDiagnosticDescriptors.NullableWithoutNullCheck;

    public IEnumerable<Diagnostic> Analyze(QueryAnalysisContext context)
    {
        if (context.Site.ClauseKind != ClauseKind.Where || context.Site.Expression == null)
            yield break;

        if (context.PrimaryEntity == null)
            yield break;

        var sql = context.GetRenderedSql()!;
        var nullableColumns = context.PrimaryEntity.Columns
            .Where(c => c.IsNullable)
            .ToList();

        foreach (var col in nullableColumns)
        {
            // Check if this column appears in an equality comparison
            var colRef = $"t0.\"{col.ColumnName}\"";
            var colRefUnquoted = $"t0.{col.ColumnName}";

            var usesEquality = sql.Contains($"{colRef} = ") || sql.Contains($"{colRefUnquoted} = ");
            if (!usesEquality)
                continue;

            // Check if there's already a null check for this column
            var hasNullCheck = sql.Contains($"{colRef} IS NULL", StringComparison.OrdinalIgnoreCase) ||
                               sql.Contains($"{colRef} IS NOT NULL", StringComparison.OrdinalIgnoreCase) ||
                               sql.Contains($"{colRefUnquoted} IS NULL", StringComparison.OrdinalIgnoreCase) ||
                               sql.Contains($"{colRefUnquoted} IS NOT NULL", StringComparison.OrdinalIgnoreCase);

            if (!hasNullCheck)
            {
                yield return Diagnostic.Create(
                    Descriptor,
                    context.InvocationSyntax.GetLocation(),
                    col.ColumnName);
            }
        }
    }
}
