using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Quarry.Generators.IR;

/// <summary>
/// Parses C# expression syntax into SqlExpr trees.
/// Works without SemanticModel — all column references remain unresolved.
/// Replaces SyntacticExpressionParser.
/// </summary>
internal static class SqlExprParser
{
    private const int MaxPathDepth = 10;

    private sealed class ParseContext
    {
        private readonly List<string> _pathSegments = new();

        public string CurrentPath => string.Join(".", _pathSegments);
        public bool IsPathValid => _pathSegments.Count <= MaxPathDepth;

        public void Push(string segment) => _pathSegments.Add(segment);

        public void Pop()
        {
            if (_pathSegments.Count > 0)
                _pathSegments.RemoveAt(_pathSegments.Count - 1);
        }
    }

    /// <summary>
    /// Parses a lambda expression body into an SqlExpr tree.
    /// </summary>
    public static SqlExpr Parse(ExpressionSyntax expression, HashSet<string> lambdaParameterNames)
    {
        return ParseExpression(expression, lambdaParameterNames, context: null);
    }

    /// <summary>
    /// Parses with expression path tracking for captured variable extraction.
    /// </summary>
    public static SqlExpr ParseWithPathTracking(ExpressionSyntax expression, HashSet<string> lambdaParameterNames)
    {
        var context = new ParseContext();
        context.Push("Body");
        return ParseExpression(expression, lambdaParameterNames, context);
    }

    private static SqlExpr ParseExpression(ExpressionSyntax expression, HashSet<string> lambdaParameters, ParseContext? context)
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
                return new SqlRawExpr(conditional.ToString());

            case CastExpressionSyntax cast:
                return ParseExpression(cast.Expression, lambdaParameters, context);

            case PostfixUnaryExpressionSyntax postfixUnary:
                if (postfixUnary.OperatorToken.IsKind(SyntaxKind.ExclamationToken))
                    return ParseExpression(postfixUnary.Operand, lambdaParameters, context);
                return new SqlRawExpr(expression.ToString());

            case ImplicitArrayCreationExpressionSyntax implicitArray:
                return ParseArrayInitializer(implicitArray.Initializer, lambdaParameters, context);

            case ArrayCreationExpressionSyntax arrayCreation:
                if (arrayCreation.Initializer != null)
                    return ParseArrayInitializer(arrayCreation.Initializer, lambdaParameters, context);
                return new SqlRawExpr(expression.ToString());

            case CollectionExpressionSyntax collection:
                return ParseCollectionExpression(collection, lambdaParameters, context);

            default:
                return new SqlRawExpr(expression.ToString());
        }
    }

    private static SqlExpr ParseIdentifier(IdentifierNameSyntax identifier, HashSet<string> lambdaParameters, ParseContext? context)
    {
        var name = identifier.Identifier.ValueText;

        if (lambdaParameters.Contains(name))
        {
            // Bare lambda parameter — in boolean context this is the entity itself
            return new ColumnRefExpr(name, name);
        }

        // Captured variable from the enclosing scope
        var path = context?.IsPathValid == true ? context.CurrentPath : null;
        return new CapturedValueExpr(name, name, expressionPath: path);
    }

    private static SqlExpr ParseMemberAccess(MemberAccessExpressionSyntax memberAccess, HashSet<string> lambdaParameters, ParseContext? context)
    {
        var memberName = memberAccess.Name.Identifier.ValueText;

        // Direct property access on lambda parameter: u.PropertyName
        if (memberAccess.Expression is IdentifierNameSyntax identifier)
        {
            var targetName = identifier.Identifier.ValueText;
            if (lambdaParameters.Contains(targetName))
            {
                return new ColumnRefExpr(targetName, memberName);
            }

            // Member access on a captured variable (e.g., someObj.Property)
            var path = context?.IsPathValid == true ? context.CurrentPath : null;
            return new CapturedValueExpr(targetName, memberAccess.ToString(), expressionPath: path);
        }

        // Chained access: u.Ref.Id
        if (memberAccess.Expression is MemberAccessExpressionSyntax innerMemberAccess)
        {
            var innerExpr = ParseMemberAccess(innerMemberAccess, lambdaParameters, context);

            if (innerExpr is ColumnRefExpr propAccess)
            {
                // Ref<T, K>.Id access (foreign key)
                if (memberName == "Id")
                {
                    return new ColumnRefExpr(propAccess.ParameterName, propAccess.PropertyName, nestedProperty: "Id");
                }

                // Nullable<T>.Value — unwrap to just the column (SQL uses the column directly)
                if (memberName == "Value")
                {
                    return propAccess;
                }

                // Nullable<T>.HasValue — translate to IS NOT NULL check
                if (memberName == "HasValue")
                {
                    return new IsNullCheckExpr(propAccess, isNegated: true);
                }

                // e.g., u.Name.Length — member access on a column
                return new SqlRawExpr(memberAccess.ToString());
            }

            if (innerExpr is CapturedValueExpr capturedVar)
            {
                return new CapturedValueExpr(capturedVar.VariableName, memberAccess.ToString(), capturedVar.ClrType, capturedVar.ExpressionPath);
            }

            return new SqlRawExpr(memberAccess.ToString());
        }

        // Generic member access
        var target = ParseExpression(memberAccess.Expression, lambdaParameters, context);

        if (target is CapturedValueExpr captured)
        {
            return new CapturedValueExpr(captured.VariableName, memberAccess.ToString(), captured.ClrType, captured.ExpressionPath);
        }

        return new SqlRawExpr(memberAccess.ToString());
    }

    private static SqlExpr ParseLiteral(LiteralExpressionSyntax literal)
    {
        switch (literal.Kind())
        {
            case SyntaxKind.TrueLiteralExpression:
                return new LiteralExpr("TRUE", "bool");

            case SyntaxKind.FalseLiteralExpression:
                return new LiteralExpr("FALSE", "bool");

            case SyntaxKind.NullLiteralExpression:
                return new LiteralExpr("NULL", "object", isNull: true);

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
                // Use Value.ToString() to strip C# type suffixes (m, f, d, L, etc.)
                // For decimal, preserve trailing zeros by using the raw text minus the suffix
                var numText = literal.Token.Text;
                if (numericValue is decimal && numText.Length > 0)
                {
                    var last = numText[numText.Length - 1];
                    if (last == 'm' || last == 'M')
                        numText = numText.Substring(0, numText.Length - 1);
                }
                else if (numericValue is float && numText.Length > 0)
                {
                    var last = numText[numText.Length - 1];
                    if (last == 'f' || last == 'F')
                        numText = numText.Substring(0, numText.Length - 1);
                }
                else if (numericValue is double && numText.Length > 0)
                {
                    var last = numText[numText.Length - 1];
                    if (last == 'd' || last == 'D')
                        numText = numText.Substring(0, numText.Length - 1);
                }
                else if (numericValue is long && numText.Length > 0)
                {
                    var last = numText[numText.Length - 1];
                    if (last == 'l' || last == 'L')
                        numText = numText.Substring(0, numText.Length - 1);
                }
                return new LiteralExpr(numText, clrType);

            case SyntaxKind.StringLiteralExpression:
                return new LiteralExpr(literal.Token.ValueText, "string");

            case SyntaxKind.CharacterLiteralExpression:
                return new LiteralExpr(literal.Token.ValueText, "char");

            case SyntaxKind.DefaultLiteralExpression:
                return new LiteralExpr("NULL", "object", isNull: true);

            default:
                return new SqlRawExpr(literal.ToString());
        }
    }

    private static SqlExpr ParseBinary(BinaryExpressionSyntax binary, HashSet<string> lambdaParameters, ParseContext? context)
    {
        SqlExpr left;
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

        SqlExpr right;
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

        // Handle null comparisons → IsNullCheckExpr
        if (right is LiteralExpr { IsNull: true })
        {
            if (binary.Kind() == SyntaxKind.EqualsExpression)
                return new IsNullCheckExpr(left, isNegated: false);
            if (binary.Kind() == SyntaxKind.NotEqualsExpression)
                return new IsNullCheckExpr(left, isNegated: true);
        }

        if (left is LiteralExpr { IsNull: true })
        {
            if (binary.Kind() == SyntaxKind.EqualsExpression)
                return new IsNullCheckExpr(right, isNegated: false);
            if (binary.Kind() == SyntaxKind.NotEqualsExpression)
                return new IsNullCheckExpr(right, isNegated: true);
        }

        var op = MapBinaryOperator(binary.Kind());
        if (op == null)
            return new SqlRawExpr(binary.ToString());

        return new BinaryOpExpr(left, op.Value, right);
    }

    private static SqlBinaryOperator? MapBinaryOperator(SyntaxKind kind)
    {
        return kind switch
        {
            SyntaxKind.EqualsExpression => SqlBinaryOperator.Equal,
            SyntaxKind.NotEqualsExpression => SqlBinaryOperator.NotEqual,
            SyntaxKind.LessThanExpression => SqlBinaryOperator.LessThan,
            SyntaxKind.LessThanOrEqualExpression => SqlBinaryOperator.LessThanOrEqual,
            SyntaxKind.GreaterThanExpression => SqlBinaryOperator.GreaterThan,
            SyntaxKind.GreaterThanOrEqualExpression => SqlBinaryOperator.GreaterThanOrEqual,
            SyntaxKind.LogicalAndExpression => SqlBinaryOperator.And,
            SyntaxKind.LogicalOrExpression => SqlBinaryOperator.Or,
            SyntaxKind.AddExpression => SqlBinaryOperator.Add,
            SyntaxKind.SubtractExpression => SqlBinaryOperator.Subtract,
            SyntaxKind.MultiplyExpression => SqlBinaryOperator.Multiply,
            SyntaxKind.DivideExpression => SqlBinaryOperator.Divide,
            SyntaxKind.ModuloExpression => SqlBinaryOperator.Modulo,
            SyntaxKind.BitwiseAndExpression => SqlBinaryOperator.BitwiseAnd,
            SyntaxKind.BitwiseOrExpression => SqlBinaryOperator.BitwiseOr,
            SyntaxKind.ExclusiveOrExpression => SqlBinaryOperator.BitwiseXor,
            _ => null
        };
    }

    private static SqlExpr ParsePrefixUnary(PrefixUnaryExpressionSyntax prefixUnary, HashSet<string> lambdaParameters, ParseContext? context)
    {
        SqlExpr operand;
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

        return prefixUnary.Kind() switch
        {
            SyntaxKind.LogicalNotExpression => new UnaryOpExpr(SqlUnaryOperator.Not, operand),
            SyntaxKind.UnaryMinusExpression => new UnaryOpExpr(SqlUnaryOperator.Negate, operand),
            SyntaxKind.UnaryPlusExpression => operand,
            _ => new SqlRawExpr(prefixUnary.ToString())
        };
    }

    private static SqlExpr ParseInvocation(InvocationExpressionSyntax invocation, HashSet<string> lambdaParameters, ParseContext? context)
    {
        // Parse arguments
        var arguments = new List<SqlExpr>();
        for (int i = 0; i < invocation.ArgumentList.Arguments.Count; i++)
        {
            var arg = invocation.ArgumentList.Arguments[i];
            SqlExpr argExpr;
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

            // Check for subquery methods on navigation properties (e.g., u.Orders.Any())
            if (IsSubqueryMethod(methodName))
            {
                var target = ParseExpression(memberAccess.Expression, lambdaParameters, context);
                if (target is ColumnRefExpr navRef)
                {
                    return ParseSubqueryCall(navRef, methodName, invocation.ArgumentList, lambdaParameters, context);
                }
            }

            SqlExpr target2;
            if (context != null)
            {
                context.Push("Object");
                target2 = ParseExpression(memberAccess.Expression, lambdaParameters, context);
                context.Pop();
            }
            else
            {
                target2 = ParseExpression(memberAccess.Expression, lambdaParameters, context);
            }

            return MapMethodCall(target2, methodName, arguments, lambdaParameters);
        }

        // Handle simple invocation: Method(args) or Sql.Method(args) handled above
        if (invocation.Expression is IdentifierNameSyntax identifier)
        {
            var methodName = identifier.Identifier.ValueText;
            return MapStaticMethodCall(methodName, arguments);
        }

        if (invocation.Expression is GenericNameSyntax genericName)
        {
            var methodName = genericName.Identifier.ValueText;
            return MapStaticMethodCall(methodName, arguments);
        }

        return new SqlRawExpr(invocation.ToString());
    }

    /// <summary>
    /// Maps an instance method call to the appropriate SqlExpr node.
    /// </summary>
    private static SqlExpr MapMethodCall(SqlExpr target, string methodName, List<SqlExpr> arguments, HashSet<string> lambdaParameters)
    {
        // String methods on column references
        if (target is ColumnRefExpr columnRef)
        {
            switch (methodName)
            {
                case "Contains" when arguments.Count == 1:
                    return CreateLikeExpr(new ColumnRefExpr(columnRef.ParameterName, columnRef.PropertyName), arguments[0], "%", "%");

                case "StartsWith" when arguments.Count == 1:
                    return CreateLikeExpr(new ColumnRefExpr(columnRef.ParameterName, columnRef.PropertyName), arguments[0], "", "%");

                case "EndsWith" when arguments.Count == 1:
                    return CreateLikeExpr(new ColumnRefExpr(columnRef.ParameterName, columnRef.PropertyName), arguments[0], "%", "");

                case "ToLower":
                case "ToLowerInvariant":
                    return new FunctionCallExpr("LOWER", new SqlExpr[] { columnRef });

                case "ToUpper":
                case "ToUpperInvariant":
                    return new FunctionCallExpr("UPPER", new SqlExpr[] { columnRef });

                case "Trim":
                    return new FunctionCallExpr("TRIM", new SqlExpr[] { columnRef });

                case "TrimStart":
                    return new FunctionCallExpr("LTRIM", new SqlExpr[] { columnRef });

                case "TrimEnd":
                    return new FunctionCallExpr("RTRIM", new SqlExpr[] { columnRef });
            }
        }

        // Collection Contains (IN clause) on captured variables
        // collection.Contains(item) → item IN (collection_param)
        if (methodName == "Contains" && arguments.Count == 1 && target is CapturedValueExpr capturedCollection)
        {
            return new InExpr(arguments[0], new SqlExpr[] { capturedCollection });
        }

        // Sql.* method access: target is an identifier named "Sql"
        if (target is CapturedValueExpr cv && cv.VariableName == "Sql")
        {
            return MapSqlFunction(methodName, arguments);
        }

        // Generic method calls we can't resolve syntactically
        return new SqlRawExpr($"{target}.{methodName}(...)");
    }

    /// <summary>
    /// Maps Sql.Function() calls to FunctionCallExpr nodes.
    /// </summary>
    private static SqlExpr MapSqlFunction(string methodName, List<SqlExpr> arguments)
    {
        switch (methodName)
        {
            case "Count" when arguments.Count == 0:
                return new FunctionCallExpr("COUNT", new SqlExpr[] { new SqlRawExpr("*") }, isAggregate: true);

            case "Count" when arguments.Count >= 1:
                return new FunctionCallExpr("COUNT", new SqlExpr[] { arguments[0] }, isAggregate: true);

            case "Sum" when arguments.Count >= 1:
                return new FunctionCallExpr("SUM", new SqlExpr[] { arguments[0] }, isAggregate: true);

            case "Avg" when arguments.Count >= 1:
                return new FunctionCallExpr("AVG", new SqlExpr[] { arguments[0] }, isAggregate: true);

            case "Min" when arguments.Count >= 1:
                return new FunctionCallExpr("MIN", new SqlExpr[] { arguments[0] }, isAggregate: true);

            case "Max" when arguments.Count >= 1:
                return new FunctionCallExpr("MAX", new SqlExpr[] { arguments[0] }, isAggregate: true);

            case "Raw" when arguments.Count >= 1 && arguments[0] is LiteralExpr templateLiteral && templateLiteral.ClrType == "string":
                // Sql.Raw<T>("template {0} {1}", arg0, arg1) -> RawCallExpr
                // Fix expression paths: params object[] packs args into Arguments[1].Expressions[i]
                var rawArgs = new List<SqlExpr>(arguments.Count - 1);
                for (int i = 1; i < arguments.Count; i++)
                {
                    var arg = arguments[i];
                    if (arg is CapturedValueExpr captured && captured.ExpressionPath != null)
                    {
                        // Remap: Body.Arguments[N] -> Body.Arguments[1].Expressions[N-1]
                        var fixedPath = captured.ExpressionPath.Replace(
                            $"Arguments[{i}]", $"Arguments[1].Expressions[{i - 1}]");
                        arg = new CapturedValueExpr(captured.VariableName, captured.SyntaxText, captured.ClrType, fixedPath);
                    }
                    rawArgs.Add(arg);
                }
                return new RawCallExpr(templateLiteral.SqlText, rawArgs);

            default:
                return new SqlRawExpr($"Sql.{methodName}(...)");
        }
    }

    private static SqlExpr MapStaticMethodCall(string methodName, List<SqlExpr> arguments)
    {
        // Support Sql.Count(), etc. when called without receiver qualification
        return MapSqlFunction(methodName, arguments);
    }

    /// <summary>
    /// Creates a LIKE expression, handling literal pattern escaping.
    /// </summary>
    private static SqlExpr CreateLikeExpr(SqlExpr operand, SqlExpr pattern, string prefix, string suffix)
    {
        bool needsEscape = false;

        // If the pattern is a string literal, escape LIKE metacharacters
        if (pattern is LiteralExpr literal && literal.ClrType == "string")
        {
            var escaped = Translation.SqlLikeHelpers.EscapeLikeMetaChars(literal.SqlText);
            needsEscape = escaped != literal.SqlText;
            if (needsEscape)
            {
                pattern = new LiteralExpr(escaped, "string");
            }
        }

        return new LikeExpr(
            operand, pattern,
            likePrefix: string.IsNullOrEmpty(prefix) ? null : prefix,
            likeSuffix: string.IsNullOrEmpty(suffix) ? null : suffix,
            needsEscape: needsEscape);
    }

    private static SqlExpr ParseIsPattern(IsPatternExpressionSyntax isPattern, HashSet<string> lambdaParameters, ParseContext? context)
    {
        SqlExpr expr;
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
                    return new IsNullCheckExpr(expr, isNegated: false);
                }
                break;

            case UnaryPatternSyntax unaryPattern when unaryPattern.OperatorToken.IsKind(SyntaxKind.NotKeyword):
                if (unaryPattern.Pattern is ConstantPatternSyntax innerConstant &&
                    innerConstant.Expression is LiteralExpressionSyntax innerLiteral &&
                    innerLiteral.IsKind(SyntaxKind.NullLiteralExpression))
                {
                    return new IsNullCheckExpr(expr, isNegated: true);
                }
                break;
        }

        return new SqlRawExpr(isPattern.ToString());
    }

    private static SqlExpr ParseArrayInitializer(InitializerExpressionSyntax initializer, HashSet<string> lambdaParameters, ParseContext? context)
    {
        var elements = new List<SqlExpr>();
        foreach (var expr in initializer.Expressions)
        {
            elements.Add(ParseExpression(expr, lambdaParameters, context));
        }
        return new ExprListExpr(elements);
    }

    private static SqlExpr ParseCollectionExpression(CollectionExpressionSyntax collection, HashSet<string> lambdaParameters, ParseContext? context)
    {
        var elements = new List<SqlExpr>();
        foreach (var element in collection.Elements)
        {
            if (element is ExpressionElementSyntax exprElement)
            {
                elements.Add(ParseExpression(exprElement.Expression, lambdaParameters, context));
            }
        }
        return new ExprListExpr(elements);
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
    /// Gets the ordered list of lambda parameter names.
    /// </summary>
    public static List<string> GetLambdaParameterNamesOrdered(LambdaExpressionSyntax lambda)
    {
        var result = new List<string>();
        switch (lambda)
        {
            case SimpleLambdaExpressionSyntax simpleLambda:
                result.Add(simpleLambda.Parameter.Identifier.ValueText);
                break;
            case ParenthesizedLambdaExpressionSyntax parenLambda:
                foreach (var param in parenLambda.ParameterList.Parameters)
                    result.Add(param.Identifier.ValueText);
                break;
        }
        return result;
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

    private static bool IsSubqueryMethod(string methodName)
    {
        return methodName == "Any" || methodName == "All" || methodName == "Count";
    }

    private static SqlExpr ParseSubqueryCall(
        ColumnRefExpr navRef,
        string methodName,
        ArgumentListSyntax argumentList,
        HashSet<string> lambdaParameters,
        ParseContext? context)
    {
        var kind = methodName switch
        {
            "Any" => SubqueryKind.Exists,
            "All" => SubqueryKind.All,
            "Count" => SubqueryKind.Count,
            _ => SubqueryKind.Exists
        };

        SqlExpr? predicate = null;
        string? innerParamName = null;

        if (argumentList.Arguments.Count == 1)
        {
            var argExpr = argumentList.Arguments[0].Expression;
            if (argExpr is SimpleLambdaExpressionSyntax simpleLambda)
            {
                innerParamName = simpleLambda.Parameter.Identifier.ValueText;
                var innerParams = new HashSet<string>(lambdaParameters, StringComparer.Ordinal) { innerParamName };
                var body = simpleLambda.Body as ExpressionSyntax;
                if (body != null)
                {
                    if (context != null)
                    {
                        context.Push("Arguments[1]");
                        context.Push("LambdaBody");
                        predicate = ParseExpression(body, innerParams, context);
                        context.Pop();
                        context.Pop();
                    }
                    else
                    {
                        predicate = ParseExpression(body, innerParams, null);
                    }
                }
            }
            else if (argExpr is ParenthesizedLambdaExpressionSyntax parenLambda
                     && parenLambda.ParameterList.Parameters.Count == 1)
            {
                innerParamName = parenLambda.ParameterList.Parameters[0].Identifier.ValueText;
                var innerParams = new HashSet<string>(lambdaParameters, StringComparer.Ordinal) { innerParamName };
                var body = parenLambda.Body as ExpressionSyntax;
                if (body != null)
                {
                    if (context != null)
                    {
                        context.Push("Arguments[1]");
                        context.Push("LambdaBody");
                        predicate = ParseExpression(body, innerParams, context);
                        context.Pop();
                        context.Pop();
                    }
                    else
                    {
                        predicate = ParseExpression(body, innerParams, null);
                    }
                }
            }
        }

        return new SubqueryExpr(
            navRef.ParameterName,
            navRef.PropertyName,
            kind,
            predicate,
            innerParamName);
    }
}
