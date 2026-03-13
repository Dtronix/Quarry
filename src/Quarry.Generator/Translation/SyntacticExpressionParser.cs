using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.Models;

namespace Quarry.Generators.Translation;

/// <summary>
/// Parses lambda expressions into a syntactic expression tree.
/// This parser works purely on syntax without requiring semantic analysis,
/// making it suitable for use when entity types are generated in the same compilation.
/// </summary>
internal static class SyntacticExpressionParser
{
    /// <summary>
    /// Maximum path depth before falling back to non-path tracking.
    /// Deeply nested expressions are unlikely to benefit from path optimization.
    /// </summary>
    private const int MaxPathDepth = 10;

    /// <summary>
    /// Context for tracking the expression path during parsing.
    /// Used to generate direct path navigation code for captured variables.
    /// </summary>
    private sealed class ParseContext
    {
        private readonly List<string> _pathSegments = new();

        /// <summary>
        /// Gets the current path as a dot-separated string.
        /// </summary>
        public string CurrentPath => string.Join(".", _pathSegments);

        /// <summary>
        /// Gets whether path tracking is still valid (not too deep).
        /// </summary>
        public bool IsPathValid => _pathSegments.Count <= MaxPathDepth;

        /// <summary>
        /// Pushes a path segment onto the stack.
        /// </summary>
        public void Push(string segment) => _pathSegments.Add(segment);

        /// <summary>
        /// Pops the last path segment from the stack.
        /// </summary>
        public void Pop()
        {
            if (_pathSegments.Count > 0)
                _pathSegments.RemoveAt(_pathSegments.Count - 1);
        }
    }

    /// <summary>
    /// Parses a lambda expression body into a syntactic expression tree.
    /// </summary>
    /// <param name="expression">The expression syntax to parse.</param>
    /// <param name="lambdaParameterNames">The lambda parameter names to recognize.</param>
    /// <returns>The parsed syntactic expression.</returns>
    public static SyntacticExpression Parse(ExpressionSyntax expression, HashSet<string> lambdaParameterNames)
    {
        return ParseExpression(expression, lambdaParameterNames, context: null);
    }

    /// <summary>
    /// Parses a lambda expression body into a syntactic expression tree with path tracking.
    /// This enables direct path navigation for captured variables.
    /// </summary>
    /// <param name="expression">The expression syntax to parse.</param>
    /// <param name="lambdaParameterNames">The lambda parameter names to recognize.</param>
    /// <returns>The parsed syntactic expression with path information for captured variables.</returns>
    public static SyntacticExpression ParseWithPathTracking(ExpressionSyntax expression, HashSet<string> lambdaParameterNames)
    {
        var context = new ParseContext();
        context.Push("Body");
        return ParseExpression(expression, lambdaParameterNames, context);
    }

    /// <summary>
    /// Parses an expression and returns a syntactic expression tree.
    /// </summary>
    /// <param name="expression">The expression to parse.</param>
    /// <param name="lambdaParameters">The lambda parameter names to recognize.</param>
    /// <param name="context">Optional context for path tracking. If null, path tracking is disabled.</param>
    private static SyntacticExpression ParseExpression(ExpressionSyntax expression, HashSet<string> lambdaParameters, ParseContext? context)
    {
        switch (expression)
        {
            case ParenthesizedExpressionSyntax paren:
                return ParseExpression(paren.Expression, lambdaParameters, context);

            case IdentifierNameSyntax identifier:
                return ParseIdentifier(identifier, lambdaParameters, context);

            case MemberAccessExpressionSyntax memberAccess:
                return ParseMemberAccess(memberAccess, lambdaParameters, context);

            case LiteralExpressionSyntax literal:
                return ParseLiteral(literal);

            case BinaryExpressionSyntax binary:
                return ParseBinary(binary, lambdaParameters, context);

            case PrefixUnaryExpressionSyntax prefixUnary:
                return ParsePrefixUnary(prefixUnary, lambdaParameters, context);

            case InvocationExpressionSyntax invocation:
                return ParseInvocation(invocation, lambdaParameters, context);

            case IsPatternExpressionSyntax isPattern:
                return ParseIsPattern(isPattern, lambdaParameters, context);

            case ConditionalExpressionSyntax conditional:
                return ParseConditional(conditional, lambdaParameters, context);

            case CastExpressionSyntax cast:
                return ParseExpression(cast.Expression, lambdaParameters, context);

            case PostfixUnaryExpressionSyntax postfixUnary:
                // Handle null-forgiving operator (x!)
                if (postfixUnary.OperatorToken.IsKind(SyntaxKind.ExclamationToken))
                    return ParseExpression(postfixUnary.Operand, lambdaParameters, context);
                return new SyntacticUnknown(expression.ToString(), "Unsupported postfix unary operator");

            default:
                return new SyntacticUnknown(expression.ToString(), $"Unsupported expression type: {expression.GetType().Name}");
        }
    }

    /// <summary>
    /// Parses an identifier (variable reference).
    /// </summary>
    private static SyntacticExpression ParseIdentifier(IdentifierNameSyntax identifier, HashSet<string> lambdaParameters, ParseContext? context)
    {
        var name = identifier.Identifier.ValueText;

        if (lambdaParameters.Contains(name))
        {
            return new SyntacticParameter(name);
        }

        // This is a captured variable from the enclosing scope
        // It needs to be evaluated at runtime and passed as a SQL parameter
        var path = context?.IsPathValid == true ? context.CurrentPath : null;
        return new SyntacticCapturedVariable(name, name, path);
    }

    /// <summary>
    /// Parses a member access expression (e.g., u.Name or u.Name.Length).
    /// </summary>
    private static SyntacticExpression ParseMemberAccess(MemberAccessExpressionSyntax memberAccess, HashSet<string> lambdaParameters, ParseContext? context)
    {
        var memberName = memberAccess.Name.Identifier.ValueText;

        // Check for direct property access on lambda parameter: u.PropertyName
        if (memberAccess.Expression is IdentifierNameSyntax identifier)
        {
            var targetName = identifier.Identifier.ValueText;
            if (lambdaParameters.Contains(targetName))
            {
                return new SyntacticPropertyAccess(targetName, memberName);
            }

            // This is a member access on a captured variable (e.g., someObj.Property)
            // Treat the entire expression as a captured variable
            var path = context?.IsPathValid == true ? context.CurrentPath : null;
            return new SyntacticCapturedVariable(targetName, memberAccess.ToString(), path);
        }

        // Check for chained access: u.Ref.Id
        if (memberAccess.Expression is MemberAccessExpressionSyntax innerMemberAccess)
        {
            // Parse the inner part first (path tracking continues through the chain)
            var innerExpr = ParseMemberAccess(innerMemberAccess, lambdaParameters, context);

            // If the inner is a property access, this is a chained member access
            if (innerExpr is SyntacticPropertyAccess propAccess)
            {
                // Special case: Ref<T, K>.Id access (foreign key)
                if (memberName == "Id")
                {
                    // Return as property access with ".Id" suffix for FK handling
                    return new SyntacticPropertyAccess(propAccess.ParameterName, propAccess.PropertyName + ".Id");
                }

                return new SyntacticMemberAccess(innerExpr, memberName);
            }

            // If inner is a captured variable, extend it with this member
            // The path stays the same since we're extending the same captured variable chain
            if (innerExpr is SyntacticCapturedVariable capturedVar)
            {
                return new SyntacticCapturedVariable(capturedVar.VariableName, memberAccess.ToString(), capturedVar.ExpressionPath);
            }

            return new SyntacticMemberAccess(innerExpr, memberName);
        }

        // Generic member access
        var target = ParseExpression(memberAccess.Expression, lambdaParameters, context);

        // If target is a captured variable, extend it
        if (target is SyntacticCapturedVariable captured)
        {
            return new SyntacticCapturedVariable(captured.VariableName, memberAccess.ToString(), captured.ExpressionPath);
        }

        return new SyntacticMemberAccess(target, memberName);
    }

    /// <summary>
    /// Parses a literal expression.
    /// </summary>
    private static SyntacticExpression ParseLiteral(LiteralExpressionSyntax literal)
    {
        var value = literal.Token.ValueText;

        switch (literal.Kind())
        {
            case SyntaxKind.TrueLiteralExpression:
                return new SyntacticLiteral("true", "bool");

            case SyntaxKind.FalseLiteralExpression:
                return new SyntacticLiteral("false", "bool");

            case SyntaxKind.NullLiteralExpression:
                return new SyntacticLiteral("null", "object", isNull: true);

            case SyntaxKind.NumericLiteralExpression:
                var numericValue = literal.Token.Value;
                var clrType = numericValue switch
                {
                    int => "int",
                    long => "long",
                    float => "float",
                    double => "double",
                    decimal => "decimal",
                    _ => "int"
                };
                return new SyntacticLiteral(literal.Token.Text, clrType);

            case SyntaxKind.StringLiteralExpression:
                return new SyntacticLiteral(value, "string");

            case SyntaxKind.CharacterLiteralExpression:
                return new SyntacticLiteral(value, "char");

            case SyntaxKind.DefaultLiteralExpression:
                return new SyntacticLiteral("default", "object", isNull: true);

            default:
                return new SyntacticUnknown(literal.ToString(), $"Unknown literal kind: {literal.Kind()}");
        }
    }

    /// <summary>
    /// Parses a binary expression.
    /// </summary>
    private static SyntacticExpression ParseBinary(BinaryExpressionSyntax binary, HashSet<string> lambdaParameters, ParseContext? context)
    {
        // Parse left operand with path tracking
        SyntacticExpression left;
        if (context != null)
        {
            context.Push("Left");
            left = ParseExpression(binary.Left, lambdaParameters, context);
            context.Pop();
        }
        else
        {
            left = ParseExpression(binary.Left, lambdaParameters, context);
        }

        // Parse right operand with path tracking
        SyntacticExpression right;
        if (context != null)
        {
            context.Push("Right");
            right = ParseExpression(binary.Right, lambdaParameters, context);
            context.Pop();
        }
        else
        {
            right = ParseExpression(binary.Right, lambdaParameters, context);
        }

        var op = binary.Kind() switch
        {
            SyntaxKind.EqualsExpression => "==",
            SyntaxKind.NotEqualsExpression => "!=",
            SyntaxKind.LessThanExpression => "<",
            SyntaxKind.LessThanOrEqualExpression => "<=",
            SyntaxKind.GreaterThanExpression => ">",
            SyntaxKind.GreaterThanOrEqualExpression => ">=",
            SyntaxKind.LogicalAndExpression => "&&",
            SyntaxKind.LogicalOrExpression => "||",
            SyntaxKind.AddExpression => "+",
            SyntaxKind.SubtractExpression => "-",
            SyntaxKind.MultiplyExpression => "*",
            SyntaxKind.DivideExpression => "/",
            SyntaxKind.ModuloExpression => "%",
            SyntaxKind.BitwiseAndExpression => "&",
            SyntaxKind.BitwiseOrExpression => "|",
            SyntaxKind.ExclusiveOrExpression => "^",
            SyntaxKind.CoalesceExpression => "??",
            _ => binary.OperatorToken.Text
        };

        return new SyntacticBinary(left, op, right);
    }

    /// <summary>
    /// Parses a prefix unary expression.
    /// </summary>
    private static SyntacticExpression ParsePrefixUnary(PrefixUnaryExpressionSyntax prefixUnary, HashSet<string> lambdaParameters, ParseContext? context)
    {
        // Parse operand with path tracking
        SyntacticExpression operand;
        if (context != null)
        {
            context.Push("Operand");
            operand = ParseExpression(prefixUnary.Operand, lambdaParameters, context);
            context.Pop();
        }
        else
        {
            operand = ParseExpression(prefixUnary.Operand, lambdaParameters, context);
        }

        var op = prefixUnary.Kind() switch
        {
            SyntaxKind.LogicalNotExpression => "!",
            SyntaxKind.UnaryMinusExpression => "-",
            SyntaxKind.UnaryPlusExpression => "+",
            SyntaxKind.BitwiseNotExpression => "~",
            _ => prefixUnary.OperatorToken.Text
        };

        return new SyntacticUnary(op, operand);
    }

    /// <summary>
    /// Parses an invocation expression (method call).
    /// </summary>
    private static SyntacticExpression ParseInvocation(InvocationExpressionSyntax invocation, HashSet<string> lambdaParameters, ParseContext? context)
    {
        // Parse arguments with path tracking
        var arguments = new List<SyntacticExpression>();
        for (int i = 0; i < invocation.ArgumentList.Arguments.Count; i++)
        {
            var arg = invocation.ArgumentList.Arguments[i];
            SyntacticExpression argExpr;
            if (context != null)
            {
                context.Push($"Arguments[{i}]");
                argExpr = ParseExpression(arg.Expression, lambdaParameters, context);
                context.Pop();
            }
            else
            {
                argExpr = ParseExpression(arg.Expression, lambdaParameters, context);
            }
            arguments.Add(argExpr);
        }

        // Handle member invocation: target.Method(args)
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var methodName = memberAccess.Name.Identifier.ValueText;
            SyntacticExpression target;
            if (context != null)
            {
                context.Push("Object");
                target = ParseExpression(memberAccess.Expression, lambdaParameters, context);
                context.Pop();
            }
            else
            {
                target = ParseExpression(memberAccess.Expression, lambdaParameters, context);
            }
            return new SyntacticMethodCall(target, methodName, arguments);
        }

        // Handle simple invocation: Method(args)
        if (invocation.Expression is IdentifierNameSyntax identifier)
        {
            var methodName = identifier.Identifier.ValueText;
            return new SyntacticMethodCall(null, methodName, arguments);
        }

        // Handle generic method: Method<T>(args)
        if (invocation.Expression is GenericNameSyntax genericName)
        {
            var methodName = genericName.Identifier.ValueText;
            return new SyntacticMethodCall(null, methodName, arguments);
        }

        // Handle member binding in generic: target.Method<T>(args)
        if (invocation.Expression is MemberBindingExpressionSyntax)
        {
            return new SyntacticUnknown(invocation.ToString(), "Member binding expressions not supported");
        }

        return new SyntacticUnknown(invocation.ToString(), "Unknown invocation pattern");
    }

    /// <summary>
    /// Parses an 'is' pattern expression (e.g., x is null, x is not null).
    /// </summary>
    private static SyntacticExpression ParseIsPattern(IsPatternExpressionSyntax isPattern, HashSet<string> lambdaParameters, ParseContext? context)
    {
        // Parse the expression with path tracking
        // Note: For "is" patterns, the expression is at "Left" in the binary result
        SyntacticExpression expr;
        if (context != null)
        {
            context.Push("Left");
            expr = ParseExpression(isPattern.Expression, lambdaParameters, context);
            context.Pop();
        }
        else
        {
            expr = ParseExpression(isPattern.Expression, lambdaParameters, context);
        }

        switch (isPattern.Pattern)
        {
            case ConstantPatternSyntax constantPattern:
                if (constantPattern.Expression is LiteralExpressionSyntax literal &&
                    literal.IsKind(SyntaxKind.NullLiteralExpression))
                {
                    // x is null -> x == null
                    return new SyntacticBinary(expr, "==", new SyntacticLiteral("null", "object", isNull: true));
                }
                break;

            case UnaryPatternSyntax unaryPattern when unaryPattern.OperatorToken.IsKind(SyntaxKind.NotKeyword):
                if (unaryPattern.Pattern is ConstantPatternSyntax innerConstant &&
                    innerConstant.Expression is LiteralExpressionSyntax innerLiteral &&
                    innerLiteral.IsKind(SyntaxKind.NullLiteralExpression))
                {
                    // x is not null -> x != null
                    return new SyntacticBinary(expr, "!=", new SyntacticLiteral("null", "object", isNull: true));
                }
                break;
        }

        return new SyntacticUnknown(isPattern.ToString(), "Unsupported pattern expression");
    }

    /// <summary>
    /// Parses a conditional expression (ternary operator).
    /// </summary>
    private static SyntacticExpression ParseConditional(ConditionalExpressionSyntax conditional, HashSet<string> lambdaParameters, ParseContext? context)
    {
        // For now, treat conditional as unknown - it's complex to translate to SQL
        // If we later support conditionals, we would track paths as:
        // - "Test" for the condition
        // - "IfTrue" for the true branch
        // - "IfFalse" for the false branch
        return new SyntacticUnknown(conditional.ToString(), "Conditional expressions not directly supported in SQL");
    }

    /// <summary>
    /// Extracts lambda parameter names from a lambda expression.
    /// </summary>
    public static HashSet<string> GetLambdaParameterNames(LambdaExpressionSyntax lambda)
    {
        var parameters = new HashSet<string>(StringComparer.Ordinal);

        switch (lambda)
        {
            case SimpleLambdaExpressionSyntax simpleLambda:
                parameters.Add(simpleLambda.Parameter.Identifier.ValueText);
                break;

            case ParenthesizedLambdaExpressionSyntax parenLambda:
                foreach (var param in parenLambda.ParameterList.Parameters)
                {
                    parameters.Add(param.Identifier.ValueText);
                }
                break;
        }

        return parameters;
    }

    /// <summary>
    /// Gets the body expression from a lambda.
    /// </summary>
    public static ExpressionSyntax? GetLambdaBody(LambdaExpressionSyntax lambda)
    {
        return lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Body as ExpressionSyntax,
            ParenthesizedLambdaExpressionSyntax paren => paren.Body as ExpressionSyntax,
            _ => null
        };
    }
}
