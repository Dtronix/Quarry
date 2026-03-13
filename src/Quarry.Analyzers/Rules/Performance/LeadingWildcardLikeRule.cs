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
        var clause = context.Site.ClauseInfo;
        if (clause == null || clause.Kind != ClauseKind.Where || !clause.IsSuccess)
            yield break;

        var sql = clause.SqlFragment;
        if (sql.Contains("LIKE '%") || sql.Contains("LIKE N'%"))
        {
            yield return Diagnostic.Create(Descriptor, context.InvocationSyntax.GetLocation());
        }
    }
}
