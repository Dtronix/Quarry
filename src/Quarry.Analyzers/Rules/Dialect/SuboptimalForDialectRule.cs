using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;

namespace Quarry.Analyzers.Rules.Dialect;

/// <summary>
/// QRA502: Detects features that are suboptimal or unsupported for the target dialect.
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
    public DiagnosticDescriptor Descriptor => AnalyzerDiagnosticDescriptors.SuboptimalForDialect;

    public IEnumerable<Diagnostic> Analyze(QueryAnalysisContext context)
    {
        var site = context.Site;
        var dialect = context.Context?.Dialect;

        if (dialect == null)
            yield break;

        // SQLite: RIGHT JOIN not supported
        if (dialect == SqlDialect.SQLite && site.Kind == InterceptorKind.RightJoin)
        {
            yield return Diagnostic.Create(
                Descriptor,
                context.InvocationSyntax.GetLocation(),
                "SQLite does not support RIGHT JOIN; consider restructuring as LEFT JOIN");
        }

        // MySQL: RIGHT JOIN has limited optimization
        if (dialect == SqlDialect.MySQL && site.Kind == InterceptorKind.RightJoin)
        {
            yield return Diagnostic.Create(
                Descriptor,
                context.InvocationSyntax.GetLocation(),
                "MySQL has limited RIGHT JOIN optimization; consider restructuring as LEFT JOIN");
        }

        // SQLite: FULL OUTER JOIN not supported
        if (dialect == SqlDialect.SQLite && site.Kind == InterceptorKind.FullOuterJoin)
        {
            yield return Diagnostic.Create(
                Descriptor,
                context.InvocationSyntax.GetLocation(),
                "SQLite does not support FULL OUTER JOIN; consider using UNION of LEFT JOIN and RIGHT JOIN");
        }

        // MySQL: FULL OUTER JOIN not supported
        if (dialect == SqlDialect.MySQL && site.Kind == InterceptorKind.FullOuterJoin)
        {
            yield return Diagnostic.Create(
                Descriptor,
                context.InvocationSyntax.GetLocation(),
                "MySQL does not support FULL OUTER JOIN; consider using UNION of LEFT JOIN and RIGHT JOIN");
        }

        // SQL Server: OFFSET/FETCH requires ORDER BY -- produces invalid SQL without it
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
                    Descriptor,
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
