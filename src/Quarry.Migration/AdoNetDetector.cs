using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Quarry.Migration;

/// <summary>
/// Detects ADO.NET DbCommand.Execute* invocations in a Roslyn syntax tree.
/// Tracks the DbCommand variable across the enclosing method/block to collect
/// CommandText assignments and parameter bindings.
/// </summary>
internal sealed class AdoNetDetector
{
    private static readonly HashSet<string> ExecuteMethods = new(StringComparer.Ordinal)
    {
        "ExecuteReader", "ExecuteReaderAsync",
        "ExecuteNonQuery", "ExecuteNonQueryAsync",
        "ExecuteScalar", "ExecuteScalarAsync",
    };

    private static readonly HashSet<string> DbCommandTypeNames = new(StringComparer.Ordinal)
    {
        "DbCommand", "SqlCommand", "NpgsqlCommand", "MySqlCommand", "SqliteCommand",
    };

    public IReadOnlyList<AdoNetCallSite> Detect(SemanticModel model, SyntaxNode root)
    {
        var results = new List<AdoNetCallSite>();

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
    public AdoNetCallSite? TryDetectSingle(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        return TryDetect(model, invocation);
    }

    private static AdoNetCallSite? TryDetect(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        // Get the method name from the invocation
        string? methodName = null;
        ExpressionSyntax? receiver = null;

        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            methodName = memberAccess.Name.Identifier.Text;
            receiver = memberAccess.Expression;
        }

        if (methodName == null || !ExecuteMethods.Contains(methodName))
            return null;

        if (receiver == null)
            return null;

        // Verify the receiver is a DbCommand type via semantic model
        var typeInfo = model.GetTypeInfo(receiver);
        var type = typeInfo.Type;
        if (type == null || !IsDbCommandType(type))
            return null;

        // Get the variable name for the command object
        string? commandVarName = null;
        if (receiver is IdentifierNameSyntax identifier)
            commandVarName = identifier.Identifier.Text;
        else
            return null; // Only support simple variable access, not chained expressions

        // Find the enclosing method/block to search for CommandText and Parameters
        var enclosingBlock = invocation.Ancestors().OfType<BlockSyntax>().FirstOrDefault();
        if (enclosingBlock == null)
            return null;

        // Extract SQL from CommandText assignment
        var commandTextAssignment = FindCommandTextAssignment(enclosingBlock, commandVarName, invocation);
        if (commandTextAssignment == null)
            return null;

        var sql = ExtractStringValue(commandTextAssignment.Right, model);
        if (sql == null)
            return null;

        // Collect parameter names from Parameters.Add/AddWithValue calls
        // between the CommandText assignment and the Execute call
        var parameterNames = CollectParameterNames(enclosingBlock, commandVarName, commandTextAssignment, invocation);

        return new AdoNetCallSite(
            invocationSyntax: invocation,
            commandVariableName: commandVarName,
            sql: sql,
            parameterNames: parameterNames,
            methodName: methodName,
            location: invocation.GetLocation());
    }

    private static bool IsDbCommandType(ITypeSymbol type)
    {
        // Check the type and its base types for known DbCommand types
        var current = type;
        while (current != null)
        {
            if (DbCommandTypeNames.Contains(current.Name))
            {
                // Verify it's in an expected namespace
                var ns = current.ContainingNamespace?.ToDisplayString();
                if (ns == "System.Data.Common" ||
                    ns == "System.Data.SqlClient" ||
                    ns == "Microsoft.Data.SqlClient" ||
                    ns == "Microsoft.Data.Sqlite" ||
                    ns == "Npgsql" ||
                    ns == "MySqlConnector" ||
                    ns == "MySql.Data.MySqlClient")
                    return true;
            }
            current = current.BaseType;
        }

        return false;
    }

    private static AssignmentExpressionSyntax? FindCommandTextAssignment(
        BlockSyntax block,
        string commandVarName,
        InvocationExpressionSyntax executeInvocation)
    {
        // Look for: cmd.CommandText = "SQL string";
        // Uses DescendantNodes to also find assignments inside nested blocks (if, using, try, etc.)
        // When CommandText is reassigned, we want the last assignment before the Execute call.
        AssignmentExpressionSyntax? lastMatch = null;

        foreach (var assignment in block.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
                continue;

            // Check left side is commandVar.CommandText
            if (assignment.Left is MemberAccessExpressionSyntax leftMember &&
                leftMember.Name.Identifier.Text == "CommandText" &&
                leftMember.Expression is IdentifierNameSyntax leftIdentifier &&
                leftIdentifier.Identifier.Text == commandVarName &&
                assignment.Span.End <= executeInvocation.SpanStart)
            {
                lastMatch = assignment;
            }
        }

        return lastMatch;
    }

    private static string? ExtractStringValue(ExpressionSyntax expression, SemanticModel model)
    {
        // String literal
        if (expression is LiteralExpressionSyntax literal &&
            (literal.IsKind(SyntaxKind.StringLiteralExpression) || literal.IsKind(SyntaxKind.Utf8StringLiteralExpression)))
            return literal.Token.ValueText;

        // Constant field or variable
        if (expression is IdentifierNameSyntax || expression is MemberAccessExpressionSyntax)
        {
            var constValue = model.GetConstantValue(expression);
            if (constValue.HasValue && constValue.Value is string str)
                return str;
        }

        // String concatenation
        if (expression is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.AddExpression))
        {
            var constValue = model.GetConstantValue(binary);
            if (constValue.HasValue && constValue.Value is string str)
                return str;
        }

        // Verbatim interpolated string / raw string — not supported for now
        return null;
    }

    private static IReadOnlyList<string> CollectParameterNames(
        BlockSyntax block,
        string commandVarName,
        AssignmentExpressionSyntax commandTextAssignment,
        InvocationExpressionSyntax executeInvocation)
    {
        var names = new List<string>();

        foreach (var invocation in block.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            // Only collect parameters added between the CommandText assignment and the Execute call
            if (invocation.Span.End <= commandTextAssignment.Span.End ||
                invocation.Span.End > executeInvocation.SpanStart)
                continue;

            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                var methodName = memberAccess.Name.Identifier.Text;

                // cmd.Parameters.AddWithValue("@name", value)
                if (methodName == "AddWithValue" &&
                    memberAccess.Expression is MemberAccessExpressionSyntax parametersAccess &&
                    parametersAccess.Name.Identifier.Text == "Parameters" &&
                    parametersAccess.Expression is IdentifierNameSyntax cmdIdentifier &&
                    cmdIdentifier.Identifier.Text == commandVarName)
                {
                    var paramName = ExtractParameterName(invocation);
                    if (paramName != null)
                        names.Add(paramName);
                }

                // cmd.Parameters.Add(new SqlParameter("@name", value))
                if (methodName == "Add" &&
                    memberAccess.Expression is MemberAccessExpressionSyntax parametersAccess2 &&
                    parametersAccess2.Name.Identifier.Text == "Parameters" &&
                    parametersAccess2.Expression is IdentifierNameSyntax cmdIdentifier2 &&
                    cmdIdentifier2.Identifier.Text == commandVarName)
                {
                    var paramName = ExtractParameterNameFromAdd(invocation);
                    if (paramName != null)
                        names.Add(paramName);
                }
            }
        }

        return names;
    }

    private static string? ExtractParameterName(InvocationExpressionSyntax invocation)
    {
        // AddWithValue("@name", value) — first arg is the parameter name string
        if (invocation.ArgumentList.Arguments.Count >= 2)
        {
            var firstArg = invocation.ArgumentList.Arguments[0].Expression;
            if (firstArg is LiteralExpressionSyntax literal &&
                literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return StripParameterPrefix(literal.Token.ValueText);
            }
        }

        return null;
    }

    private static string? ExtractParameterNameFromAdd(InvocationExpressionSyntax invocation)
    {
        if (invocation.ArgumentList.Arguments.Count == 0)
            return null;

        var firstArg = invocation.ArgumentList.Arguments[0].Expression;

        // new SqlParameter("@name", value)
        if (firstArg is ObjectCreationExpressionSyntax objectCreation &&
            objectCreation.ArgumentList?.Arguments.Count >= 1)
        {
            var nameArg = objectCreation.ArgumentList.Arguments[0].Expression;
            if (nameArg is LiteralExpressionSyntax literal &&
                literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return StripParameterPrefix(literal.Token.ValueText);
            }
        }

        // Just a string: cmd.Parameters.Add("@name")
        if (firstArg is LiteralExpressionSyntax stringLiteral &&
            stringLiteral.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return StripParameterPrefix(stringLiteral.Token.ValueText);
        }

        return null;
    }

    private static string StripParameterPrefix(string name)
    {
        if (name.StartsWith("@", StringComparison.Ordinal) ||
            name.StartsWith("$", StringComparison.Ordinal) ||
            name.StartsWith(":", StringComparison.Ordinal))
            return name.Substring(1);
        return name;
    }
}
