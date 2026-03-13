using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Quarry.Generators.Models;

namespace Quarry.Analyzers.Rules.WastedWork;

/// <summary>
/// QRA202: Detects SELECT * on tables with many columns.
/// </summary>
internal sealed class WideTableSelectRule : IQueryAnalysisRule
{
    private const int DefaultThreshold = 10;
    private const string ThresholdKey = "quarry_analyzers.wide_table_column_count";

    public string RuleId => "QRA202";
    public DiagnosticDescriptor Descriptor => AnalyzerDiagnosticDescriptors.WideTableSelect;

    public IEnumerable<Diagnostic> Analyze(QueryAnalysisContext context)
    {
        var site = context.Site;
        if (site.Kind != InterceptorKind.Select)
            yield break;

        var projection = site.ProjectionInfo;
        if (projection == null || projection.Kind != ProjectionKind.Entity)
            yield break;

        var entity = context.PrimaryEntity;
        if (entity == null)
            yield break;

        var threshold = DefaultThreshold;
        if (context.Options.TryGetValue(ThresholdKey, out var thresholdStr) &&
            int.TryParse(thresholdStr, out var parsed))
        {
            threshold = parsed;
        }

        if (entity.Columns.Count > threshold)
        {
            yield return Diagnostic.Create(
                Descriptor,
                context.InvocationSyntax.GetLocation(),
                entity.EntityName,
                entity.Columns.Count);
        }
    }
}
