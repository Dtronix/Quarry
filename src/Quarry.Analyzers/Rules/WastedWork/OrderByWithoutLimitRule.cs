using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.Models;

namespace Quarry.Analyzers.Rules.WastedWork;

/// <summary>
/// QRA203: Detects ORDER BY without LIMIT/OFFSET pagination.
/// </summary>
internal sealed class OrderByWithoutLimitRule : IQueryAnalysisRule
{
    private static readonly HashSet<string> PaginationMethods = new()
    {
        "Take", "Skip", "Offset", "Limit", "TakeAsync", "SkipAsync"
    };

    private static readonly HashSet<string> OrderByMethods = new()
    {
        "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending"
    };

    public string RuleId => "QRA203";
    public DiagnosticDescriptor Descriptor => AnalyzerDiagnosticDescriptors.OrderByWithoutLimit;

    public IEnumerable<Diagnostic> Analyze(QueryAnalysisContext context)
    {
        if (context.Site.Kind != InterceptorKind.OrderBy &&
            context.Site.Kind != InterceptorKind.ThenBy)
            yield break;

        // Walk the fluent chain to check for pagination methods
        var current = context.InvocationSyntax.Parent;
        bool hasPagination = false;

        while (current != null)
        {
            if (current is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Parent is InvocationExpressionSyntax)
            {
                var methodName = memberAccess.Name.Identifier.Text;
                if (PaginationMethods.Contains(methodName))
                {
                    hasPagination = true;
                    break;
                }
            }
            current = current.Parent;
        }

        // Also check execution methods that imply single-row (FetchFirst, FetchSingle)
        if (!hasPagination)
        {
            current = context.InvocationSyntax.Parent;
            while (current != null)
            {
                if (current is MemberAccessExpressionSyntax memberAccess &&
                    memberAccess.Parent is InvocationExpressionSyntax)
                {
                    var methodName = memberAccess.Name.Identifier.Text;
                    if (methodName.Contains("First") || methodName.Contains("Single"))
                    {
                        hasPagination = true;
                        break;
                    }
                }
                current = current.Parent;
            }
        }

        if (!hasPagination)
        {
            yield return Diagnostic.Create(Descriptor, context.InvocationSyntax.GetLocation());
        }
    }
}
