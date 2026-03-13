using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Quarry.Generators.Models;

namespace Quarry.Analyzers.Rules.WastedWork;

/// <summary>
/// QRA205: Detects JOIN without meaningful ON condition (Cartesian product).
/// </summary>
internal sealed class CartesianProductRule : IQueryAnalysisRule
{
    public string RuleId => "QRA205";
    public DiagnosticDescriptor Descriptor => AnalyzerDiagnosticDescriptors.CartesianProduct;

    public IEnumerable<Diagnostic> Analyze(QueryAnalysisContext context)
    {
        var clause = context.Site.ClauseInfo;
        if (clause is not JoinClauseInfo joinClause)
            yield break;

        var onSql = joinClause.OnConditionSql?.Trim();
        if (string.IsNullOrEmpty(onSql) || onSql == "1 = 1" || onSql == "1=1")
        {
            yield return Diagnostic.Create(Descriptor, context.InvocationSyntax.GetLocation());
        }
    }
}
