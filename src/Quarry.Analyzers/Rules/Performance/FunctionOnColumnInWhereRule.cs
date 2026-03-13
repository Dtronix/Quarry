using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.Models;

namespace Quarry.Analyzers.Rules.Performance;

/// <summary>
/// QRA302: Detects functions applied to columns in WHERE that prevent index usage.
/// </summary>
internal sealed class FunctionOnColumnInWhereRule : IQueryAnalysisRule
{
    private static readonly HashSet<string> IndexBreakingMethods = new()
    {
        "ToLower", "ToUpper", "Substring", "Trim", "TrimStart", "TrimEnd",
        "Replace", "PadLeft", "PadRight"
    };

    public string RuleId => "QRA302";
    public DiagnosticDescriptor Descriptor => AnalyzerDiagnosticDescriptors.FunctionOnColumnInWhere;

    public IEnumerable<Diagnostic> Analyze(QueryAnalysisContext context)
    {
        var clause = context.Site.ClauseInfo;
        if (clause == null || clause.Kind != ClauseKind.Where || !clause.IsSuccess)
            yield break;

        // Check SQL fragment for common function patterns
        var sql = clause.SqlFragment;
        var functions = new[] { "LOWER(", "UPPER(", "SUBSTRING(", "TRIM(", "LTRIM(", "RTRIM(", "REPLACE(" };
        foreach (var func in functions)
        {
            var idx = sql.IndexOf(func, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                // Try to extract the column name from inside the function
                var afterFunc = sql.Substring(idx + func.Length);
                var closeIdx = afterFunc.IndexOf(')');
                if (closeIdx > 0)
                {
                    var inner = afterFunc.Substring(0, closeIdx).Split(',')[0].Trim();
                    yield return Diagnostic.Create(
                        Descriptor,
                        context.InvocationSyntax.GetLocation(),
                        func.TrimEnd('('),
                        inner);
                    yield break;
                }
            }
        }
    }
}
