using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.Models;

namespace Quarry.Analyzers.Rules.Simplification;

/// <summary>
/// QRA101: Detects Count() compared to zero, suggesting Any() instead.
/// </summary>
internal sealed class CountComparedToZeroRule : IQueryAnalysisRule
{
    public string RuleId => "QRA101";
    public DiagnosticDescriptor Descriptor => AnalyzerDiagnosticDescriptors.CountComparedToZero;

    public IEnumerable<Diagnostic> Analyze(QueryAnalysisContext context)
    {
        // Look for patterns like: .Count() > 0, .Count() == 0, .Count() != 0, .Count() >= 1
        if (context.Site.Kind != InterceptorKind.ExecuteScalar)
            yield break;

        if (context.Site.MethodName != "CountAsync" && context.Site.MethodName != "Count")
            yield break;

        // Walk up to find a binary comparison with 0 or 1
        var invocation = context.InvocationSyntax;
        var parent = invocation.Parent;

        // Skip through parenthesized expressions
        while (parent is ParenthesizedExpressionSyntax)
            parent = parent.Parent;

        if (parent is not BinaryExpressionSyntax binary)
            yield break;

        // Check if comparing with 0 or 1
        var otherSide = binary.Left == invocation || binary.Left is ParenthesizedExpressionSyntax
            ? binary.Right
            : binary.Left;

        if (otherSide is not LiteralExpressionSyntax literal)
            yield break;

        if (literal.Token.Value is not (int and (0 or 1)))
            yield break;

        var op = binary.Kind();
        var isComparisonToZero = (int)literal.Token.Value! == 0 &&
            (op == SyntaxKind.GreaterThanExpression ||
             op == SyntaxKind.EqualsExpression ||
             op == SyntaxKind.NotEqualsExpression);

        var isGreaterEqualOne = (int)literal.Token.Value! == 1 &&
            op == SyntaxKind.GreaterThanOrEqualExpression;

        if (!isComparisonToZero && !isGreaterEqualOne)
            yield break;

        var operatorText = binary.OperatorToken.Text;
        yield return Diagnostic.Create(
            Descriptor,
            invocation.GetLocation(),
            operatorText);
    }
}
