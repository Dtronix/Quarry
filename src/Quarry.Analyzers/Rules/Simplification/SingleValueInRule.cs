using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Quarry.Generators.Models;

namespace Quarry.Analyzers.Rules.Simplification;

/// <summary>
/// QRA102: Detects IN clause with a single value, suggesting == instead.
/// </summary>
internal sealed class SingleValueInRule : IQueryAnalysisRule
{
    public string RuleId => "QRA102";
    public DiagnosticDescriptor Descriptor => AnalyzerDiagnosticDescriptors.SingleValueIn;

    public IEnumerable<Diagnostic> Analyze(QueryAnalysisContext context)
    {
        if (context.Site.ClauseKind != ClauseKind.Where || context.Site.Expression == null)
            yield break;

        // Check SQL fragment for single-parameter IN clause
        var sql = context.GetRenderedSql()!;
        if (!sql.Contains("IN ("))
            yield break;

        // Verify it's a single-value IN by checking the SQL pattern
        var inIndex = sql.IndexOf("IN (");
        if (inIndex < 0)
            yield break;

        var afterIn = sql.Substring(inIndex + 4);
        var closeIndex = afterIn.IndexOf(')');
        if (closeIndex < 0)
            yield break;

        var inContent = afterIn.Substring(0, closeIndex).Trim();
        // Single parameter like @p0 — no commas means single value
        if (inContent.StartsWith("@p") && !inContent.Contains(","))
        {
            yield return Diagnostic.Create(
                Descriptor,
                context.InvocationSyntax.GetLocation(),
                inContent);
        }
    }
}
