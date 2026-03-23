using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Quarry.Generators.Models;

namespace Quarry.Analyzers.Rules.Performance;

/// <summary>
/// QRA304: Detects WHERE filters on columns not covered by any declared index.
/// </summary>
internal sealed class WhereOnNonIndexedColumnRule : IQueryAnalysisRule
{
    public string RuleId => "QRA304";
    public DiagnosticDescriptor Descriptor => AnalyzerDiagnosticDescriptors.WhereOnNonIndexedColumn;

    public IEnumerable<Diagnostic> Analyze(QueryAnalysisContext context)
    {
        if (context.Site.ClauseKind != ClauseKind.Where || context.Site.Expression == null)
            yield break;

        var entity = context.PrimaryEntity;
        if (entity == null || entity.Indexes == null || entity.Indexes.Count == 0)
            yield break;

        // Collect leading index columns
        var indexedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var index in entity.Indexes)
        {
            if (index.Columns.Count > 0)
                indexedColumns.Add(index.Columns[0].PropertyName);
        }

        // Extract column names referenced in WHERE
        var sql = context.GetRenderedSql()!;
        foreach (var col in entity.Columns)
        {
            var colRef = $"t0.\"{col.ColumnName}\"";
            var colRefUnquoted = $"t0.{col.ColumnName}";

            if (sql.Contains(colRef) || sql.Contains(colRefUnquoted))
            {
                // Check if this column's property name is in any index as leading column
                if (!indexedColumns.Contains(col.PropertyName))
                {
                    yield return Diagnostic.Create(
                        Descriptor,
                        context.InvocationSyntax.GetLocation(),
                        col.ColumnName);
                }
            }
        }
    }
}
