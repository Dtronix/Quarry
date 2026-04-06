using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Quarry.Migration;

/// <summary>
/// Detects Dapper method invocations in a Roslyn syntax tree.
/// </summary>
internal sealed class DapperDetector
{
    private static readonly HashSet<string> DapperMethods = new(StringComparer.Ordinal)
    {
        "QueryAsync",
        "QueryFirstAsync",
        "QueryFirstOrDefaultAsync",
        "QuerySingleAsync",
        "QuerySingleOrDefaultAsync",
        "ExecuteAsync",
        "ExecuteScalarAsync",
        // Sync variants
        "Query",
        "QueryFirst",
        "QueryFirstOrDefault",
        "QuerySingle",
        "QuerySingleOrDefault",
        "Execute",
        "ExecuteScalar",
    };

    public IReadOnlyList<DapperCallSite> Detect(SemanticModel model, SyntaxNode root)
    {
        var results = new List<DapperCallSite>();

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var callSite = TryDetect(model, invocation);
            if (callSite != null)
                results.Add(callSite);
        }

        return results;
    }

    /// <summary>
    /// Checks a single invocation expression. Used by the analyzer which receives one node at a time.
    /// </summary>
    public DapperCallSite? TryDetectSingle(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        return TryDetect(model, invocation);
    }

    private static DapperCallSite? TryDetect(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        // Get the method name from the invocation
        string? methodName;
        string? resultTypeName = null;

        switch (invocation.Expression)
        {
            case MemberAccessExpressionSyntax memberAccess:
                methodName = memberAccess.Name switch
                {
                    GenericNameSyntax generic => generic.Identifier.Text,
                    IdentifierNameSyntax identifier => identifier.Identifier.Text,
                    _ => null,
                };
                // Extract generic type argument
                if (memberAccess.Name is GenericNameSyntax genericName &&
                    genericName.TypeArgumentList.Arguments.Count > 0)
                {
                    resultTypeName = genericName.TypeArgumentList.Arguments[0].ToString();
                }
                break;

            default:
                return null;
        }

        if (methodName == null || !DapperMethods.Contains(methodName))
            return null;

        // Verify this is actually a Dapper method via semantic model
        var symbolInfo = model.GetSymbolInfo(invocation);
        var methodSymbol = symbolInfo.Symbol as IMethodSymbol ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        if (methodSymbol == null)
            return null;

        if (!IsDapperMethod(methodSymbol))
            return null;

        // Extract the SQL string argument (first string argument)
        var sql = ExtractSqlString(invocation, model);
        if (sql == null)
            return null;

        // Extract parameter names from anonymous object
        var parameterNames = ExtractParameterNames(invocation, model);

        return new DapperCallSite(
            sql: sql,
            parameterNames: parameterNames,
            methodName: methodName,
            resultTypeName: resultTypeName,
            location: invocation.GetLocation(),
            invocationSyntax: invocation);
    }

    private static bool IsDapperMethod(IMethodSymbol method)
    {
        // Dapper extension methods are in the Dapper namespace, class SqlMapper
        var containingType = method.ContainingType;
        if (containingType == null)
            return false;

        // Check for Dapper.SqlMapper or Dapper.CommandDefinition
        var ns = containingType.ContainingNamespace?.ToDisplayString();
        if (ns == "Dapper" && containingType.Name == "SqlMapper")
            return true;

        // Also check if it's a reduced extension method whose original is from Dapper
        if (method.IsExtensionMethod && method.ReducedFrom != null)
        {
            var originalType = method.ReducedFrom.ContainingType;
            var originalNs = originalType?.ContainingNamespace?.ToDisplayString();
            if (originalNs == "Dapper" && originalType?.Name == "SqlMapper")
                return true;
        }

        return false;
    }

    private static string? ExtractSqlString(InvocationExpressionSyntax invocation, SemanticModel model)
    {
        if (invocation.ArgumentList.Arguments.Count == 0)
            return null;

        // The SQL is typically the first argument
        var sqlArg = invocation.ArgumentList.Arguments[0].Expression;

        // String literal
        if (sqlArg is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
            return literal.Token.ValueText;

        // Verbatim/raw string
        if (sqlArg is LiteralExpressionSyntax verbatim &&
            (verbatim.IsKind(SyntaxKind.StringLiteralExpression) || verbatim.IsKind(SyntaxKind.Utf8StringLiteralExpression)))
            return verbatim.Token.ValueText;

        // Constant field or variable reference
        if (sqlArg is IdentifierNameSyntax || sqlArg is MemberAccessExpressionSyntax)
        {
            var constValue = model.GetConstantValue(sqlArg);
            if (constValue.HasValue && constValue.Value is string str)
                return str;
        }

        // String concatenation of literals
        if (sqlArg is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.AddExpression))
        {
            var constValue = model.GetConstantValue(binary);
            if (constValue.HasValue && constValue.Value is string str)
                return str;
        }

        return null;
    }

    private static IReadOnlyList<string> ExtractParameterNames(InvocationExpressionSyntax invocation, SemanticModel model)
    {
        // Find the anonymous object argument (param object)
        // Dapper: connection.QueryAsync<T>(sql, new { userId, name })
        // The param object is typically the second argument
        foreach (var arg in invocation.ArgumentList.Arguments)
        {
            if (arg.Expression is AnonymousObjectCreationExpressionSyntax anonObj)
            {
                return anonObj.Initializers
                    .Select(init => GetAnonymousMemberName(init))
                    .Where(n => n != null)
                    .Select(n => n!)
                    .ToList();
            }

            // Also check for 'param:' named argument
            if (arg.NameColon?.Name.Identifier.Text == "param" &&
                arg.Expression is AnonymousObjectCreationExpressionSyntax namedAnonObj)
            {
                return namedAnonObj.Initializers
                    .Select(init => GetAnonymousMemberName(init))
                    .Where(n => n != null)
                    .Select(n => n!)
                    .ToList();
            }
        }

        return Array.Empty<string>();
    }

    private static string? GetAnonymousMemberName(AnonymousObjectMemberDeclaratorSyntax member)
    {
        // Named: new { UserId = userId } → "UserId"
        if (member.NameEquals != null)
            return member.NameEquals.Name.Identifier.Text;

        // Shorthand: new { userId } → "userId"
        if (member.Expression is IdentifierNameSyntax identifier)
            return identifier.Identifier.Text;

        // Member access: new { user.Id } → "Id"
        if (member.Expression is MemberAccessExpressionSyntax memberAccess)
            return memberAccess.Name.Identifier.Text;

        return null;
    }
}
