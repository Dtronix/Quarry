using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Quarry.Migration;

/// <summary>
/// Detects SqlKata Query fluent chains in a Roslyn syntax tree.
/// </summary>
internal sealed class SqlKataDetector
{
    private static readonly HashSet<string> ChainMethods = new(StringComparer.Ordinal)
    {
        "Where", "OrWhere", "WhereNot",
        "WhereNull", "WhereNotNull", "OrWhereNull", "OrWhereNotNull",
        "WhereIn", "WhereNotIn", "OrWhereIn",
        "WhereBetween", "OrWhereBetween",
        "WhereTrue", "WhereFalse",
        "OrderBy", "OrderByDesc",
        "Select",
        "Join", "LeftJoin", "RightJoin", "CrossJoin",
        "Limit", "Offset", "Take", "Skip",
        "GroupBy", "Having",
        "Distinct",
        "AsCount", "AsSum", "AsAvg", "AsMin", "AsMax",
        "ForPage",
    };

    private static readonly HashSet<string> UnsupportedSqlKataMethods = new(StringComparer.Ordinal)
    {
        "WhereRaw", "OrWhereRaw", "SelectRaw", "HavingRaw", "OrderByRaw",
        "WhereSubQuery", "With", "WithRaw",
    };

    private static readonly HashSet<string> TerminalMethods = new(StringComparer.Ordinal)
    {
        "Get", "GetAsync", "First", "FirstAsync",
        "FirstOrDefault", "FirstOrDefaultAsync",
        "Paginate", "PaginateAsync",
        "Count", "CountAsync",
    };

    public IReadOnlyList<SqlKataCallSite> Detect(SemanticModel model, SyntaxNode root)
    {
        var results = new List<SqlKataCallSite>();

        // Find all object creation expressions: new Query("table")
        foreach (var creation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            var callSite = TryDetectFromCreation(model, creation);
            if (callSite != null)
                results.Add(callSite);
        }

        return results;
    }

    /// <summary>
    /// Checks a single invocation expression (for terminal methods on existing chains).
    /// Used by the analyzer which receives one node at a time.
    /// </summary>
    public SqlKataCallSite? TryDetectSingle(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        // Walk backwards from this invocation to find the new Query(...) root
        var current = invocation as ExpressionSyntax;
        ObjectCreationExpressionSyntax? creation = null;

        while (current != null)
        {
            if (current is InvocationExpressionSyntax inv &&
                inv.Expression is MemberAccessExpressionSyntax ma)
            {
                current = ma.Expression;
            }
            else if (current is ObjectCreationExpressionSyntax oc)
            {
                creation = oc;
                break;
            }
            else
            {
                break;
            }
        }

        if (creation == null) return null;

        return TryDetectFromCreation(model, creation);
    }

    private static SqlKataCallSite? TryDetectFromCreation(SemanticModel model, ObjectCreationExpressionSyntax creation)
    {
        // Verify this is SqlKata.Query via semantic model
        var typeInfo = model.GetTypeInfo(creation);
        var type = typeInfo.Type;
        if (type == null || !IsSqlKataQueryType(type))
            return null;

        // Extract table name from constructor argument: new Query("table_name")
        var tableName = ExtractTableName(creation);
        if (tableName == null)
            return null;

        // Walk the chain forwards from the creation
        var steps = new List<SqlKataChainStep>();
        var unsupported = new List<string>();
        string? terminalMethod = null;
        ExpressionSyntax chainExpression = creation;

        // The creation might be part of a chain: new Query("t").Where(...).Get()
        // Walk upwards through member access / invocations
        var currentNode = creation.Parent;
        while (currentNode != null)
        {
            if (currentNode is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Parent is InvocationExpressionSyntax parentInvocation)
            {
                var methodName = memberAccess.Name.Identifier.Text;

                if (ChainMethods.Contains(methodName))
                {
                    steps.Add(new SqlKataChainStep(
                        methodName,
                        parentInvocation.ArgumentList.Arguments.ToList(),
                        parentInvocation.GetLocation()));
                    chainExpression = parentInvocation;
                    currentNode = parentInvocation.Parent;
                    continue;
                }
                else if (UnsupportedSqlKataMethods.Contains(methodName))
                {
                    unsupported.Add(methodName);
                    chainExpression = parentInvocation;
                    currentNode = parentInvocation.Parent;
                    continue;
                }
                else if (TerminalMethods.Contains(methodName))
                {
                    terminalMethod = methodName;
                    chainExpression = parentInvocation;
                    break;
                }
            }

            break;
        }

        return new SqlKataCallSite(
            chainExpression: chainExpression,
            tableName: tableName,
            steps: steps,
            terminalMethod: terminalMethod,
            location: chainExpression.GetLocation(),
            unsupportedMethods: unsupported);
    }

    private static bool IsSqlKataQueryType(ITypeSymbol type)
    {
        if (type.Name == "Query")
        {
            var ns = type.ContainingNamespace?.ToDisplayString();
            if (ns == "SqlKata")
                return true;
        }

        return false;
    }

    private static string? ExtractTableName(ObjectCreationExpressionSyntax creation)
    {
        if (creation.ArgumentList?.Arguments.Count >= 1)
        {
            var firstArg = creation.ArgumentList.Arguments[0].Expression;
            if (firstArg is LiteralExpressionSyntax literal &&
                literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return literal.Token.ValueText;
            }
        }

        return null;
    }
}
