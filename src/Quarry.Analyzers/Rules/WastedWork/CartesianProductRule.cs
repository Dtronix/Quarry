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
        if (context.Site.ClauseKind != ClauseKind.Join)
            yield break;

        // If there's no ON expression at all, it's a cartesian product
        if (context.Site.Expression == null)
        {
            yield return Diagnostic.Create(Descriptor, context.InvocationSyntax.GetLocation());
            yield break;
        }

        // Render and check for trivial ON conditions
        var sql = context.GetRenderedSql();
        if (sql != null)
        {
            var trimmed = sql.Trim();
            if (trimmed == "1 = 1" || trimmed == "1=1")
            {
                yield return Diagnostic.Create(Descriptor, context.InvocationSyntax.GetLocation());
            }
        }
    }
}
