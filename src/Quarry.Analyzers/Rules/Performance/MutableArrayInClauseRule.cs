using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.Models;

namespace Quarry.Analyzers.Rules.Performance;

/// <summary>
/// QRA305: Detects static readonly arrays used in .Contains() / IN clauses.
/// The generator inlines the initializer values at compile time, but array elements
/// can be mutated at runtime. Suggests ImmutableArray&lt;T&gt; for true immutability.
/// </summary>
internal sealed class MutableArrayInClauseRule : IQueryAnalysisRule
{
    public string RuleId => "QRA305";
    public DiagnosticDescriptor Descriptor => AnalyzerDiagnosticDescriptors.MutableArrayInClause;

    public IEnumerable<Diagnostic> Analyze(QueryAnalysisContext context)
    {
        if (context.Site.ClauseKind != ClauseKind.Where)
            yield break;

        if (context.InvocationSyntax is not InvocationExpressionSyntax invocation)
            yield break;

        // Extract the lambda argument from the .Where() call
        if (invocation.ArgumentList.Arguments.Count == 0)
            yield break;

        var arg = invocation.ArgumentList.Arguments[0].Expression;
        if (arg is not LambdaExpressionSyntax lambda)
            yield break;

        // Walk the lambda body for .Contains() calls on static readonly array fields
        var body = lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Body as SyntaxNode,
            ParenthesizedLambdaExpressionSyntax paren => paren.Body as SyntaxNode,
            _ => null
        };

        if (body == null)
            yield break;

        foreach (var containsInvocation in body.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            if (containsInvocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                continue;

            if (memberAccess.Name.Identifier.ValueText != "Contains")
                continue;

            // Resolve the receiver of .Contains()
            var receiverSymbol = context.SemanticModel.GetSymbolInfo(memberAccess.Expression).Symbol;
            if (receiverSymbol is not IFieldSymbol fieldSymbol)
                continue;

            // Only flag static readonly fields (not const, not mutable static — those aren't inlined)
            if (!fieldSymbol.IsStatic || !fieldSymbol.IsReadOnly || fieldSymbol.IsConst)
                continue;

            // Only flag array types (T[]) — ImmutableArray, ReadOnlyCollection etc. are safe
            if (fieldSymbol.Type is not IArrayTypeSymbol)
                continue;

            yield return Diagnostic.Create(
                Descriptor,
                memberAccess.Expression.GetLocation(),
                fieldSymbol.Name);
        }
    }
}
