using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;

namespace Quarry.Analyzers.Rules.Dialect;

/// <summary>
/// QRA502 (Warning) and QRA503 (Error) — dialect feature checks.
///
/// QRA502: feature is suboptimal but valid SQL — the dialect will execute it.
/// QRA503: feature produces SQL the dialect cannot execute (capability gap).
/// </summary>
internal sealed class SuboptimalForDialectRule : IQueryAnalysisRule
{
    private static readonly HashSet<string> OrderByMethods = new()
    {
        "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending"
    };

    private static readonly HashSet<string> PaginationMethods = new()
    {
        "Offset", "Skip"
    };

    public string RuleId => "QRA502";

    // Primary descriptor for SupportedDiagnostics metadata; the rule also emits QRA503.
    public DiagnosticDescriptor Descriptor => AnalyzerDiagnosticDescriptors.SuboptimalForDialect;

    public IEnumerable<Diagnostic> Analyze(QueryAnalysisContext context)
    {
        var site = context.Site;
        var dialect = context.Context?.Dialect;

        if (dialect == null)
            yield break;

        // MySQL: RIGHT JOIN executes but the optimizer's plan is suboptimal.
        // Capability is intact, so this stays QRA502 Warning (perf hint).
        if (dialect == SqlDialect.MySQL && site.Kind == InterceptorKind.RightJoin)
        {
            yield return Diagnostic.Create(
                AnalyzerDiagnosticDescriptors.SuboptimalForDialect,
                context.InvocationSyntax.GetLocation(),
                "MySQL has limited RIGHT JOIN optimization; consider restructuring as LEFT JOIN");
        }

        // MySQL: FULL OUTER JOIN is not supported by any MySQL version.
        // The generator emits "FULL OUTER JOIN" verbatim; MySQL rejects it at parse time.
        if (dialect == SqlDialect.MySQL && site.Kind == InterceptorKind.FullOuterJoin)
        {
            yield return Diagnostic.Create(
                AnalyzerDiagnosticDescriptors.UnsupportedForDialect,
                context.InvocationSyntax.GetLocation(),
                "MySQL does not support FULL OUTER JOIN; consider using UNION of LEFT JOIN and RIGHT JOIN");
        }

        // SQL Server: OFFSET/FETCH without ORDER BY is rejected at parse time.
        // The generator emits invalid SQL on this combination, so this is QRA503 Error.
        if (dialect == SqlDialect.SqlServer && IsExecutionSite(site.Kind))
        {
            bool hasOffset = false;
            bool hasOrderBy = false;

            // Walk DOWN the fluent chain: the invocation syntax is the outermost call
            // (e.g. ExecuteFetchAllAsync()), and inner builder calls (Offset, OrderBy)
            // are nested inside via MemberAccessExpression.Expression.
            SyntaxNode? current = context.InvocationSyntax;
            while (current is InvocationExpressionSyntax invocation &&
                   invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                var methodName = memberAccess.Name.Identifier.Text;
                if (PaginationMethods.Contains(methodName))
                    hasOffset = true;
                if (OrderByMethods.Contains(methodName))
                    hasOrderBy = true;
                current = memberAccess.Expression;
            }

            if (hasOffset && !hasOrderBy)
            {
                yield return Diagnostic.Create(
                    AnalyzerDiagnosticDescriptors.UnsupportedForDialect,
                    context.InvocationSyntax.GetLocation(),
                    "SQL Server OFFSET/FETCH requires ORDER BY; add OrderBy() to avoid invalid SQL");
            }
        }
    }

    private static bool IsExecutionSite(InterceptorKind kind)
    {
        return kind is InterceptorKind.ExecuteFetchAll or InterceptorKind.ExecuteFetchFirst
            or InterceptorKind.ExecuteFetchFirstOrDefault or InterceptorKind.ExecuteFetchSingle
            or InterceptorKind.ExecuteFetchSingleOrDefault
            or InterceptorKind.ExecuteScalar or InterceptorKind.ExecuteNonQuery;
    }
}
