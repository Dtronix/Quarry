using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.Models;

namespace Quarry.Analyzers.Rules.Patterns;

/// <summary>
/// QRA404: Detects Having() called without a preceding GroupBy() in the same fluent chain.
/// </summary>
internal sealed class HavingWithoutGroupByRule : IQueryAnalysisRule
{
    private const string AnchorMethod = "GroupBy";

    public string RuleId => "QRA404";
    public DiagnosticDescriptor Descriptor => AnalyzerDiagnosticDescriptors.HavingWithoutGroupBy;

    public IEnumerable<Diagnostic> Analyze(QueryAnalysisContext context)
    {
        if (context.Site.Kind != InterceptorKind.Having)
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
