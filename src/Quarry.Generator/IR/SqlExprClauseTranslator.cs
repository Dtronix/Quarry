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
        => ExtractParametersCore(expr, parameters, ref paramIndex, subqueryPredicate: false);

    /// <summary>
    /// Extracts parameters from a SubqueryExpr predicate.
    /// </summary>
    private static SqlExpr ExtractSubqueryParameters(SubqueryExpr sub, List<ParameterInfo> parameters, ref int paramIndex)
    {
        if (sub.Predicate == null) return sub;

        var newPredicate = ExtractParametersCore(sub.Predicate, parameters, ref paramIndex, subqueryPredicate: true);
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
    /// Unified parameter extraction that walks an SqlExpr tree and replaces captured values
    /// and (optionally) literals with ParamSlotExpr nodes.
    /// <para>
    /// When <paramref name="subqueryPredicate"/> is false (standard mode):
    ///   - All CapturedValueExpr nodes are parameterized.
    ///   - String/char LiteralExpr nodes are parameterized.
    /// </para>
    /// <para>
    /// When <paramref name="subqueryPredicate"/> is true (subquery predicate mode):
    ///   - All CapturedValueExpr nodes are still parameterized (enum constants are already
    ///     folded to LiteralExpr by SqlExprAnnotator before reaching this method).
    ///   - String/char LiteralExpr nodes are left inline (rendered as 'value' by SqlExprRenderer).
    /// </para>
    /// </summary>
    private static SqlExpr ExtractParametersCore(
        SqlExpr expr, List<ParameterInfo> parameters, ref int paramIndex, bool subqueryPredicate)
    {
        switch (expr)
        {
            case CapturedValueExpr captured:
            {
                // Note: enum/constant member accesses (e.g., OrderPriority.Urgent) are already
                // folded to LiteralExpr by SqlExprAnnotator before reaching parameter extraction.
                // Any CapturedValueExpr here is a genuine runtime capture that must be parameterized.
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

            case LiteralExpr literal when !subqueryPredicate && literal.ClrType == "string" && !literal.IsNull:
            {
                var idx = paramIndex++;
                var name = $"@p{idx}";
                var escaped = EscapeString(literal.SqlText);
                var valueExpression = $"\"{escaped}\"";
                parameters.Add(new ParameterInfo(idx, name, "string", valueExpression));
                return new ParamSlotExpr(idx, "string", valueExpression);
            }

            case LiteralExpr literal when !subqueryPredicate && literal.ClrType == "char" && !literal.IsNull:
            {
                var idx = paramIndex++;
                var name = $"@p{idx}";
                var escaped = EscapeString(literal.SqlText);
                var valueExpression = $"'{escaped}'";
                parameters.Add(new ParameterInfo(idx, name, "char", valueExpression));
                return new ParamSlotExpr(idx, "char", valueExpression);
            }

            case BinaryOpExpr bin:
            {
                var left = ExtractParametersCore(bin.Left, parameters, ref paramIndex, subqueryPredicate);
                var right = ExtractParametersCore(bin.Right, parameters, ref paramIndex, subqueryPredicate);
                if (ReferenceEquals(left, bin.Left) && ReferenceEquals(right, bin.Right))
                    return bin;
                return new BinaryOpExpr(left, bin.Operator, right);
            }

            case UnaryOpExpr unary:
            {
                var operand = ExtractParametersCore(unary.Operand, parameters, ref paramIndex, subqueryPredicate);
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
                    newArgs[i] = ExtractParametersCore(func.Arguments[i], parameters, ref paramIndex, subqueryPredicate);
                    if (!ReferenceEquals(newArgs[i], func.Arguments[i])) changed = true;
                }
                return changed ? new FunctionCallExpr(func.FunctionName, newArgs, func.IsAggregate) : func;
            }

            case InExpr inExpr:
            {
                var operand = ExtractParametersCore(inExpr.Operand, parameters, ref paramIndex, subqueryPredicate);
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
                        paramInfo.CollectionReceiverSymbol = captured.TypeSymbol;
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
                        newValues[i] = ExtractParametersCore(inExpr.Values[i], parameters, ref paramIndex, subqueryPredicate);
                        if (!ReferenceEquals(newValues[i], inExpr.Values[i])) changed = true;
                    }
                }
                return changed ? new InExpr(operand, newValues, inExpr.IsNegated) : inExpr;
            }

            case IsNullCheckExpr isNull:
            {
                var operand = ExtractParametersCore(isNull.Operand, parameters, ref paramIndex, subqueryPredicate);
                if (ReferenceEquals(operand, isNull.Operand)) return isNull;
                return new IsNullCheckExpr(operand, isNull.IsNegated);
            }

            case LikeExpr like:
            {
                var operand = ExtractParametersCore(like.Operand, parameters, ref paramIndex, subqueryPredicate);
                // String literals in LIKE patterns are inlined directly — no parameterization needed
                var pattern = like.Pattern is LiteralExpr { ClrType: "string", IsNull: false }
                    ? like.Pattern
                    : ExtractParametersCore(like.Pattern, parameters, ref paramIndex, subqueryPredicate: false);
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
                    newArgs[i] = ExtractParametersCore(rawCall.Arguments[i], parameters, ref paramIndex, subqueryPredicate);
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
    /// Escapes a string for use as a C# string literal value expression.
    /// Input is Roslyn's Token.ValueText (evaluated bytes, not raw source text),
    /// so control characters appear as actual bytes that must be escaped.
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
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\0': sb.Append("\\0"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Extracts the element type from a collection CLR type string.
    /// </summary>
    internal static string? ExtractElementType(string clrType)
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
