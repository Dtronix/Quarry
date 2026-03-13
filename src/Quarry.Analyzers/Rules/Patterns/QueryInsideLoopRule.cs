using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.Models;

namespace Quarry.Analyzers.Rules.Patterns;

/// <summary>
/// QRA401: Detects Quarry query execution inside loops (potential N+1).
/// </summary>
internal sealed class QueryInsideLoopRule : IQueryAnalysisRule
{
    private static readonly HashSet<string> ExecutionMethods = new()
    {
        "FetchAllAsync", "FetchFirstAsync", "FetchFirstOrDefaultAsync",
        "FetchSingleAsync", "FetchSingleOrDefaultAsync",
        "ExecuteScalarAsync", "ExecuteNonQueryAsync",
        "CountAsync", "AnyAsync", "ToAsyncEnumerable"
    };

    public string RuleId => "QRA401";
    public DiagnosticDescriptor Descriptor => AnalyzerDiagnosticDescriptors.QueryInsideLoop;

    public IEnumerable<Diagnostic> Analyze(QueryAnalysisContext context)
    {
        // Only flag execution methods (not builder methods like Where/Select)
        if (!ExecutionMethods.Contains(context.Site.MethodName))
            yield break;

        var node = context.InvocationSyntax;
        var parent = node.Parent;

        while (parent != null)
        {
            if (parent is ForStatementSyntax or
                ForEachStatementSyntax or
                WhileStatementSyntax or
                DoStatementSyntax)
            {
                yield return Diagnostic.Create(Descriptor, node.GetLocation());
                yield break;
            }

            // Check for LINQ-based loops: .Select(x => query)
            if (parent is SimpleLambdaExpressionSyntax or ParenthesizedLambdaExpressionSyntax)
            {
                var lambdaParent = parent.Parent;
                if (lambdaParent is ArgumentSyntax arg &&
                    arg.Parent is ArgumentListSyntax argList &&
                    argList.Parent is InvocationExpressionSyntax outerInvocation)
                {
                    if (outerInvocation.Expression is MemberAccessExpressionSyntax memberAccess)
                    {
                        var methodName = memberAccess.Name.Identifier.Text;
                        if (methodName is "Select" or "SelectMany" or "ForEach")
                        {
                            yield return Diagnostic.Create(Descriptor, node.GetLocation());
                            yield break;
                        }
                    }
                }
            }

            parent = parent.Parent;
        }
    }
}
