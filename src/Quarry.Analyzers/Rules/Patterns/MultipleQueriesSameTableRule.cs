using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.Models;
using Quarry.Generators.Parsing;

namespace Quarry.Analyzers.Rules.Patterns;

/// <summary>
/// QRA402: Detects multiple independent queries on the same table within a method.
/// </summary>
internal sealed class MultipleQueriesSameTableRule : IQueryAnalysisRule
{
    public string RuleId => "QRA402";
    public DiagnosticDescriptor Descriptor => AnalyzerDiagnosticDescriptors.MultipleQueriesSameTable;

    public IEnumerable<Diagnostic> Analyze(QueryAnalysisContext context)
    {
        // Only analyze at execution sites to avoid duplicate reports
        if (context.Site.EntityTypeName == null)
            yield break;

        // Find the containing method body
        var method = context.InvocationSyntax.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (method?.Body == null && method?.ExpressionBody == null)
            yield break;

        var body = (SyntaxNode?)method.Body ?? method.ExpressionBody;
        if (body == null)
            yield break;

        // Find all Quarry invocations in this method for the same entity
        var invocations = body.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(inv => UsageSiteDiscovery.IsQuarryMethodCandidate(inv))
            .ToList();

        // Count distinct invocation chains for the same entity
        var entityOccurrences = new List<InvocationExpressionSyntax>();
        foreach (var inv in invocations)
        {
            var site = UsageSiteDiscovery.DiscoverUsageSite(inv, context.SemanticModel, default);
            if (site?.EntityTypeName == context.Site.EntityTypeName)
                entityOccurrences.Add(inv);
        }

        // Only report on the second occurrence, and only if this is that occurrence
        if (entityOccurrences.Count > 1 &&
            entityOccurrences[1].SpanStart == context.InvocationSyntax.SpanStart)
        {
            yield return Diagnostic.Create(
                Descriptor,
                context.InvocationSyntax.GetLocation(),
                context.Site.EntityTypeName);
        }
    }
}
