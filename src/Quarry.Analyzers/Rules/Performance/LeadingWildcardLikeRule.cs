using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Quarry.Generators.Models;

namespace Quarry.Analyzers.Rules.Performance;

/// <summary>
/// QRA301: Detects Contains() translating to LIKE '%...%' which prevents index usage.
/// </summary>
internal sealed class LeadingWildcardLikeRule : IQueryAnalysisRule
{
    public string RuleId => "QRA301";
    public DiagnosticDescriptor Descriptor => AnalyzerDiagnosticDescriptors.LeadingWildcardLike;

    public IEnumerable<Diagnostic> Analyze(QueryAnalysisContext context)
    {
        if (context.Site.ClauseKind != ClauseKind.Where || context.Site.Expression == null)
            yield break;

        var sql = context.GetRenderedSql()!;
        if (sql.Contains("LIKE '%") || sql.Contains("LIKE N'%"))
        {
            yield return Diagnostic.Create(Descriptor, context.InvocationSyntax.GetLocation());
        }
    }
}
