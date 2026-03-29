using System;
using System.Collections.Generic;
using Quarry.Generators.Translation;

namespace Quarry.Generators.IR;

/// <summary>
/// Provides static helpers for SqlExpr parameter extraction.
/// </summary>
internal static class SqlExprClauseTranslator
{
    /// <summary>
    /// Public entry point for parameter extraction, used by CallSiteTranslator.
    /// </summary>
    internal static SqlExpr ExtractParametersPublic(SqlExpr expr, List<ParameterInfo> parameters, ref int paramIndex)
        => ExtractParameters(expr, parameters, ref paramIndex);

    private static SqlExpr ExtractParameters(SqlExpr expr, List<ParameterInfo> parameters, ref int paramIndex)
    {
        switch (expr)
        {
            case CapturedValueExpr captured:
            {
                var idx = paramIndex++;
                var name = $"@p{idx}";
                var paramInfo = new ParameterInfo(
                    idx, name, captured.ClrType, captured.SyntaxText,
                    isCaptured: true, expressionPath: captured.ExpressionPath);
                paramInfo.CapturedFieldName = captured.VariableName;
                paramInfo.IsStaticCapture = captured.IsStaticField;
                parameters.Add(paramInfo);
                return new ParamSlotExpr(idx, captured.ClrType, captured.SyntaxText,
                    isCaptured: true, expressionPath: captured.ExpressionPath);
            }

            case LiteralExpr literal when literal.ClrType == "string" && !literal.IsNull:
            {
                var idx = paramIndex++;
                var name = $"@p{idx}";
                var escaped = EscapeString(literal.SqlText);
                var valueExpression = $"\"{escaped}\"";
                parameters.Add(new ParameterInfo(idx, name, "string", valueExpression));
                return new ParamSlotExpr(idx, "string", valueExpression);
            }

            case LiteralExpr literal when literal.ClrType == "char" && !literal.IsNull:
            {
                var idx = paramIndex++;
                var name = $"@p{idx}";
                var valueExpression = $"'{literal.SqlText}'";
                parameters.Add(new ParameterInfo(idx, name, "char", valueExpression));
                return new ParamSlotExpr(idx, "char", valueExpression);
            }

            case BinaryOpExpr bin:
            {
                var left = ExtractParameters(bin.Left, parameters, ref paramIndex);
                var right = ExtractParameters(bin.Right, parameters, ref paramIndex);
                if (ReferenceEquals(left, bin.Left) && ReferenceEquals(right, bin.Right))
                    return bin;
                return new BinaryOpExpr(left, bin.Operator, right);
            }

            case UnaryOpExpr unary:
            {
                var operand = ExtractParameters(unary.Operand, parameters, ref paramIndex);
                if (ReferenceEquals(operand, unary.Operand))
                    return unary;
                return new UnaryOpExpr(unary.Operator, operand);
            }

            case FunctionCallExpr func:
            {
                var changed = false;
                var newArgs = new SqlExpr[func.Arguments.Count];
                for (int i = 0; i < func.Arguments.Count; i++)
                {
                    newArgs[i] = ExtractParameters(func.Arguments[i], parameters, ref paramIndex);
                    if (!ReferenceEquals(newArgs[i], func.Arguments[i])) changed = true;
                }
                return changed ? new FunctionCallExpr(func.FunctionName, newArgs, func.IsAggregate) : func;
            }

            case InExpr inExpr:
            {
                var operand = ExtractParameters(inExpr.Operand, parameters, ref paramIndex);
                var changed = !ReferenceEquals(operand, inExpr.Operand);
                var newValues = new SqlExpr[inExpr.Values.Count];
                for (int i = 0; i < inExpr.Values.Count; i++)
                {
                    // CapturedValueExpr inside IN → collection parameter
                    if (inExpr.Values[i] is CapturedValueExpr captured)
                    {
                        var idx = paramIndex++;
                        var name = $"@p{idx}";
                        var elementType = ExtractElementType(captured.ClrType);
                        var paramInfo = new ParameterInfo(
                            idx, name, captured.ClrType, captured.SyntaxText,
                            isCollection: true, isCaptured: true,
                            expressionPath: "__CONTAINS_COLLECTION__");
                        paramInfo.CollectionElementType = elementType;
                        paramInfo.CapturedFieldName = captured.VariableName;
                        paramInfo.IsStaticCapture = captured.IsStaticField;
                        parameters.Add(paramInfo);
                        newValues[i] = new ParamSlotExpr(idx, captured.ClrType, captured.SyntaxText,
                            isCaptured: true, expressionPath: "__CONTAINS_COLLECTION__",
                            isCollection: true, elementTypeName: elementType);
                        changed = true;
                    }
                    else if (inExpr.Values[i] is LiteralExpr)
                    {
                        // Inlined constant literals (from constant array resolution) stay as literals
                        newValues[i] = inExpr.Values[i];
                    }
                    else
                    {
                        newValues[i] = ExtractParameters(inExpr.Values[i], parameters, ref paramIndex);
                        if (!ReferenceEquals(newValues[i], inExpr.Values[i])) changed = true;
                    }
                }
                return changed ? new InExpr(operand, newValues, inExpr.IsNegated) : inExpr;
            }

            case IsNullCheckExpr isNull:
            {
                var operand = ExtractParameters(isNull.Operand, parameters, ref paramIndex);
                if (ReferenceEquals(operand, isNull.Operand)) return isNull;
                return new IsNullCheckExpr(operand, isNull.IsNegated);
            }

            case LikeExpr like:
            {
                var operand = ExtractParameters(like.Operand, parameters, ref paramIndex);
                // String literals in LIKE patterns are inlined directly — no parameterization needed
                var pattern = like.Pattern is LiteralExpr { ClrType: "string", IsNull: false }
                    ? like.Pattern
                    : ExtractParameters(like.Pattern, parameters, ref paramIndex);
                if (ReferenceEquals(operand, like.Operand) && ReferenceEquals(pattern, like.Pattern))
                    return like;
                return new LikeExpr(operand, pattern, like.IsNegated, like.LikePrefix, like.LikeSuffix, like.NeedsEscape);
            }

            case SubqueryExpr sub:
                return ExtractSubqueryParameters(sub, parameters, ref paramIndex);

            case RawCallExpr rawCall:
            {
                var changed = false;
                var newArgs = new SqlExpr[rawCall.Arguments.Count];
                for (int i = 0; i < rawCall.Arguments.Count; i++)
                {
                    newArgs[i] = ExtractParameters(rawCall.Arguments[i], parameters, ref paramIndex);
                    if (!ReferenceEquals(newArgs[i], rawCall.Arguments[i])) changed = true;
                }
                return changed ? new RawCallExpr(rawCall.Template, newArgs) : rawCall;
            }

            // Terminal nodes that don't need parameter extraction
            default:
                return expr;
        }
    }

    /// <summary>
    /// Extracts parameters from a SubqueryExpr predicate.
    /// </summary>
    private static SqlExpr ExtractSubqueryParameters(SubqueryExpr sub, List<ParameterInfo> parameters, ref int paramIndex)
    {
        if (sub.Predicate == null) return sub;

        var newPredicate = ExtractSubqueryPredicateParams(sub.Predicate, parameters, ref paramIndex);
        if (ReferenceEquals(newPredicate, sub.Predicate)) return sub;

        if (sub.IsResolved)
        {
            return new SubqueryExpr(
                sub.OuterParameterName,
                sub.NavigationPropertyName,
                sub.SubqueryKind,
                newPredicate,
                sub.InnerParameterName,
                sub.InnerTableQuoted!,
                sub.InnerAliasQuoted!,
                sub.CorrelationSql!);
        }
        return new SubqueryExpr(
            sub.OuterParameterName,
            sub.NavigationPropertyName,
            sub.SubqueryKind,
            newPredicate,
            sub.InnerParameterName);
    }

    /// <summary>
    /// Walks a subquery predicate tree and only extracts CapturedValueExpr nodes as parameters.
    /// String/char LiteralExpr are left inline (rendered as 'value' by SqlExprRenderer).
    /// LIKE patterns are routed through the full ExtractParameters to parameterize the pattern.
    /// </summary>
    private static SqlExpr ExtractSubqueryPredicateParams(SqlExpr expr, List<ParameterInfo> parameters, ref int paramIndex)
    {
        switch (expr)
        {
            case CapturedValueExpr captured:
            {
                // Skip enum/constant member accesses (e.g., OrderPriority.Urgent) which are
                // compile-time constants, not closure captures. These render inline as literals.
                // Real closure captures are simple variable names without dots.
                if (captured.SyntaxText.Contains('.'))
                    return captured;

                var idx = paramIndex++;
                var name = $"@p{idx}";
                var paramInfo = new ParameterInfo(
                    idx, name, captured.ClrType, captured.SyntaxText,
                    isCaptured: true, expressionPath: captured.ExpressionPath);
                paramInfo.CapturedFieldName = captured.VariableName;
                parameters.Add(paramInfo);
                return new ParamSlotExpr(idx, captured.ClrType, captured.SyntaxText,
                    isCaptured: true, expressionPath: captured.ExpressionPath);
            }

            case BinaryOpExpr bin:
            {
                var left = ExtractSubqueryPredicateParams(bin.Left, parameters, ref paramIndex);
                var right = ExtractSubqueryPredicateParams(bin.Right, parameters, ref paramIndex);
                if (ReferenceEquals(left, bin.Left) && ReferenceEquals(right, bin.Right))
                    return bin;
                return new BinaryOpExpr(left, bin.Operator, right);
            }

            case UnaryOpExpr unary:
            {
                var operand = ExtractSubqueryPredicateParams(unary.Operand, parameters, ref paramIndex);
                if (ReferenceEquals(operand, unary.Operand))
                    return unary;
                return new UnaryOpExpr(unary.Operator, operand);
            }

            case FunctionCallExpr func:
            {
                var changed = false;
                var newArgs = new SqlExpr[func.Arguments.Count];
                for (int i = 0; i < func.Arguments.Count; i++)
                {
                    newArgs[i] = ExtractSubqueryPredicateParams(func.Arguments[i], parameters, ref paramIndex);
                    if (!ReferenceEquals(newArgs[i], func.Arguments[i])) changed = true;
                }
                return changed ? new FunctionCallExpr(func.FunctionName, newArgs, func.IsAggregate) : func;
            }

            case LikeExpr like:
            {
                var operand = ExtractSubqueryPredicateParams(like.Operand, parameters, ref paramIndex);
                // String literals in LIKE patterns are inlined directly — no parameterization needed
                var pattern = like.Pattern is LiteralExpr { ClrType: "string", IsNull: false }
                    ? like.Pattern
                    : ExtractParameters(like.Pattern, parameters, ref paramIndex);
                if (ReferenceEquals(operand, like.Operand) && ReferenceEquals(pattern, like.Pattern))
                    return like;
                return new LikeExpr(operand, pattern, like.IsNegated, like.LikePrefix, like.LikeSuffix, like.NeedsEscape);
            }

            case SubqueryExpr nestedSub:
                return ExtractSubqueryParameters(nestedSub, parameters, ref paramIndex);

            // LiteralExpr (including strings), ParamSlotExpr, etc. pass through unchanged
            default:
                return expr;
        }
    }

    /// <summary>
    /// Escapes a string for use as a C# string literal value expression.
    /// </summary>
    private static string EscapeString(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length + 4);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\'': sb.Append("''"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Extracts the element type from a collection CLR type string.
    /// </summary>
    private static string? ExtractElementType(string clrType)
    {
        // Array types: "string[]", "int[]"
        if (clrType.EndsWith("[]"))
            return clrType.Substring(0, clrType.Length - 2);

        // Generic types: "System.Collections.Generic.List<string>", "IEnumerable<int>"
        var openAngle = clrType.IndexOf('<');
        if (openAngle >= 0)
        {
            var closeAngle = clrType.LastIndexOf('>');
            if (closeAngle > openAngle)
                return clrType.Substring(openAngle + 1, closeAngle - openAngle - 1);
        }

        return null;
    }
}
