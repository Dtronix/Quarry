using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.Models;

namespace Quarry.Analyzers.Rules.WastedWork;

/// <summary>
/// QRA201: Detects joined tables not referenced in SELECT, WHERE, or ORDER BY.
/// </summary>
internal sealed class UnusedJoinRule : IQueryAnalysisRule
{
    public string RuleId => "QRA201";
    public DiagnosticDescriptor Descriptor => AnalyzerDiagnosticDescriptors.UnusedJoin;

    public IEnumerable<Diagnostic> Analyze(QueryAnalysisContext context)
    {
        var site = context.Site;

        // Only check join call sites
        if (site.Kind != InterceptorKind.Join &&
            site.Kind != InterceptorKind.LeftJoin &&
            site.Kind != InterceptorKind.RightJoin &&
            site.Kind != InterceptorKind.CrossJoin &&
            site.Kind != InterceptorKind.FullOuterJoin)
            yield break;

        var joinedEntityName = site.JoinedEntityTypeName;
        if (joinedEntityName == null)
            yield break;

        // Walk the fluent chain from this invocation forward to see if the joined entity is referenced
        var invocation = context.InvocationSyntax;

        // Find the parent fluent chain — walk up to get the full chain
        var current = invocation.Parent;
        bool isReferenced = false;

        while (current != null)
        {
            if (current is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Parent is InvocationExpressionSyntax subsequentCall)
            {
                // Check lambda arguments of subsequent calls for references to the join parameter
                foreach (var arg in subsequentCall.ArgumentList.Arguments)
                {
                    if (arg.Expression is SimpleLambdaExpressionSyntax or ParenthesizedLambdaExpressionSyntax)
                    {
                        // If the lambda has multiple parameters, the joined entity is the last one
                        // Check if it's actually used in the body
                        var lambdaParams = GetLambdaParameters(arg.Expression);
                        if (lambdaParams.Count > 1)
                        {
                            var joinParam = lambdaParams.Last();
                            if (IsParameterUsedInBody(arg.Expression, joinParam))
                            {
                                isReferenced = true;
                                break;
                            }
                        }
                    }
                }

                if (isReferenced)
                    break;

                current = subsequentCall.Parent;
            }
            else
            {
                current = current.Parent;
            }
        }

        if (!isReferenced)
        {
            yield return Diagnostic.Create(
                Descriptor,
                invocation.GetLocation(),
                joinedEntityName);
        }
    }

    private static List<string> GetLambdaParameters(ExpressionSyntax lambda)
    {
        var names = new List<string>();
        if (lambda is SimpleLambdaExpressionSyntax simple)
        {
            names.Add(simple.Parameter.Identifier.Text);
        }
        else if (lambda is ParenthesizedLambdaExpressionSyntax paren)
        {
            foreach (var p in paren.ParameterList.Parameters)
                names.Add(p.Identifier.Text);
        }
        return names;
    }

    private static bool IsParameterUsedInBody(ExpressionSyntax lambda, string paramName)
    {
        SyntaxNode? body = lambda switch
        {
            SimpleLambdaExpressionSyntax s => s.Body,
            ParenthesizedLambdaExpressionSyntax p => p.Body,
            _ => null
        };

        if (body == null)
            return false;

        return body.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Any(id => id.Identifier.Text == paramName);
    }
}
