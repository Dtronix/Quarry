using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Quarry.Generators.Models;

namespace Quarry.Analyzers.Rules.WastedWork;

/// <summary>
/// QRA204: Detects duplicate columns in SELECT projection.
/// </summary>
internal sealed class DuplicateProjectionColumnRule : IQueryAnalysisRule
{
    public string RuleId => "QRA204";
    public DiagnosticDescriptor Descriptor => AnalyzerDiagnosticDescriptors.DuplicateProjectionColumn;

    public IEnumerable<Diagnostic> Analyze(QueryAnalysisContext context)
    {
        if (context.Site.Kind != InterceptorKind.Select)
            yield break;

        var projection = context.Site.ProjectionInfo;
        if (projection == null || projection.Columns == null || projection.Columns.Count < 2)
            yield break;

        var seen = new HashSet<string>();
        foreach (var col in projection.Columns)
        {
            if (!seen.Add(col.ColumnName))
            {
                yield return Diagnostic.Create(
                    Descriptor,
                    context.InvocationSyntax.GetLocation(),
                    col.ColumnName);
            }
        }
    }
}
