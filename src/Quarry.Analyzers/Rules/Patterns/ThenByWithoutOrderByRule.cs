using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.Models;

namespace Quarry.Analyzers.Rules.Patterns;

/// <summary>
/// QRA403: Detects ThenBy/ThenByDescending called without a preceding OrderBy/OrderByDescending
/// in the same fluent chain.
/// </summary>
internal sealed class ThenByWithoutOrderByRule : IQueryAnalysisRule
{
    private static readonly HashSet<string> AnchorMethods = new()
    {
        "OrderBy", "OrderByDescending"
    };

    public string RuleId => "QRA403";
    public DiagnosticDescriptor Descriptor => AnalyzerDiagnosticDescriptors.ThenByWithoutOrderBy;

    public IEnumerable<Diagnostic> Analyze(QueryAnalysisContext context)
    {
        if (context.Site.Kind != InterceptorKind.ThenBy)
            yield break;

        if (context.InvocationSyntax is not InvocationExpressionSyntax outer ||
            outer.Expression is not MemberAccessExpressionSyntax outerMember)
            yield break;

        var methodName = outerMember.Name.Identifier.Text;

        // Walk DOWN the receiver chain looking for an OrderBy/OrderByDescending anchor.
        SyntaxNode? current = outerMember.Expression;
        while (current is InvocationExpressionSyntax invocation &&
               invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            if (AnchorMethods.Contains(memberAccess.Name.Identifier.Text))
                yield break;
            current = memberAccess.Expression;
        }

        yield return Diagnostic.Create(Descriptor, outerMember.Name.GetLocation(), methodName);
    }
}
