using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Quarry.Generators.Models;

namespace Quarry.Analyzers.Rules.Simplification;

/// <summary>
/// QRA103: Detects tautological WHERE conditions (always true).
/// </summary>
internal sealed class TautologicalConditionRule : IQueryAnalysisRule
{
    public string RuleId => "QRA103";
    public DiagnosticDescriptor Descriptor => AnalyzerDiagnosticDescriptors.TautologicalCondition;

    public IEnumerable<Diagnostic> Analyze(QueryAnalysisContext context)
    {
        var clause = context.Site.ClauseInfo;
        if (clause == null || clause.Kind != ClauseKind.Where || !clause.IsSuccess)
            yield break;

        var sql = clause.SqlFragment.Trim();

        // Check for 1 = 1
        if (sql.Contains("1 = 1") || sql.Contains("1=1"))
        {
            yield return Diagnostic.Create(Descriptor, context.InvocationSyntax.GetLocation());
            yield break;
        }

        // Check for column = same column (x.col = x.col pattern)
        // Parse simple equality: "t0.Col" = "t0.Col"
        var eqIndex = sql.IndexOf(" = ");
        if (eqIndex > 0)
        {
            var left = sql.Substring(0, eqIndex).Trim();
            var right = sql.Substring(eqIndex + 3).Trim();

            // Strip surrounding parens/AND fragments - check for simple case
            if (left.Length > 0 && left == right)
            {
                yield return Diagnostic.Create(Descriptor, context.InvocationSyntax.GetLocation());
            }
        }
    }
}
