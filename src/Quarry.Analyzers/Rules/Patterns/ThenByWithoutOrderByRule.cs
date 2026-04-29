using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.Models;

namespace Quarry.Analyzers.Rules.Patterns;

/// <summary>
/// QRA403: Detects ThenBy called without a preceding OrderBy in the same fluent chain.
/// Quarry uses an ascending/descending Direction parameter on a single OrderBy/ThenBy
/// method, so the anchor and target method names are each single-valued.
/// </summary>
internal sealed class ThenByWithoutOrderByRule : IQueryAnalysisRule
{
    private const string AnchorMethod = "OrderBy";

    public string RuleId => "QRA403";
    public DiagnosticDescriptor Descriptor => AnalyzerDiagnosticDescriptors.ThenByWithoutOrderBy;

    public IEnumerable<Diagnostic> Analyze(QueryAnalysisContext context)
    {
        if (context.Site.Kind != InterceptorKind.ThenBy)
            yield break;

        if (context.InvocationSyntax is not InvocationExpressionSyntax outer ||
            outer.Expression is not MemberAccessExpressionSyntax outerMember)
            yield break;

        SyntaxNode? current = outerMember.Expression;
        while (current is InvocationExpressionSyntax invocation &&
               invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            if (memberAccess.Name.Identifier.Text == AnchorMethod)
                yield break;
            current = memberAccess.Expression;
        }

        yield return Diagnostic.Create(Descriptor, outerMember.Name.GetLocation());
    }
}
