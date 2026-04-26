using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;

namespace Quarry.Analyzers.Rules.Dialect;

/// <summary>
/// QRA502 (Warning): the chain uses a feature that is suboptimal for the target
/// dialect, but the SQL is still valid and will execute. Capability errors live
/// in the sibling <see cref="UnsupportedForDialectRule"/> (QRA503).
/// </summary>
internal sealed class SuboptimalForDialectRule : IQueryAnalysisRule
{
    public string RuleId => "QRA502";
    public DiagnosticDescriptor Descriptor => AnalyzerDiagnosticDescriptors.SuboptimalForDialect;

    public IEnumerable<Diagnostic> Analyze(QueryAnalysisContext context)
    {
        var site = context.Site;
        var dialect = context.Context?.Dialect;

        if (dialect == null)
            yield break;

        // MySQL: RIGHT JOIN executes but the optimizer's plan is suboptimal.
        // Capability is intact, so this is a perf hint only.
        if (dialect == SqlDialect.MySQL && site.Kind == InterceptorKind.RightJoin)
        {
            yield return Diagnostic.Create(
                Descriptor,
                context.InvocationSyntax.GetLocation(),
                "MySQL has limited RIGHT JOIN optimization; consider restructuring as LEFT JOIN");
        }
    }
}
