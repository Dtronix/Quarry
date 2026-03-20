using System;
using System.Collections.Generic;
using Quarry.Generators.Models;

namespace Quarry.Generators.IR;

/// <summary>
/// Temporary adapter that converts SyntacticExpression trees to SqlExpr trees.
/// This enables incremental migration from the old syntactic path to the new SqlExpr IR.
/// Will be removed once all callers produce SqlExpr directly via SqlExprParser.
/// </summary>
internal static class SyntacticExpressionAdapter
{
    /// <summary>
    /// Converts a SyntacticExpression tree to an SqlExpr tree.
    /// </summary>
    public static SqlExpr Convert(SyntacticExpression expression)
    {
        switch (expression)
        {
            case SyntacticPropertyAccess propAccess:
            {
                var propertyName = propAccess.PropertyName;
                string? nestedProperty = null;
                // Handle Ref<T,K>.Id access (stored as "PropertyName.Id")
                if (propertyName.EndsWith(".Id"))
                {
                    nestedProperty = "Id";
                    propertyName = propertyName.Substring(0, propertyName.Length - 3);
                }
                return new ColumnRefExpr(propAccess.ParameterName, propertyName, nestedProperty);
            }

            case SyntacticLiteral literal:
            {
                if (literal.IsNull)
                    return new LiteralExpr("NULL", "object", isNull: true);

                switch (literal.ClrType)
                {
                    case "bool":
                        return new LiteralExpr(literal.Value == "true" ? "TRUE" : "FALSE", "bool");
                    default:
                        return new LiteralExpr(literal.Value, literal.ClrType);
                }
            }

            case SyntacticParameter param:
                // Bare lambda parameter — used in boolean context
                return new ColumnRefExpr(param.Name, param.Name);

            case SyntacticBinary binary:
            {
                var left = Convert(binary.Left);
                var right = Convert(binary.Right);

                // Handle null comparisons → IsNullCheckExpr
                if (right is LiteralExpr { IsNull: true })
                {
                    if (binary.Operator == "==")
                        return new IsNullCheckExpr(left, isNegated: false);
                    if (binary.Operator == "!=")
                        return new IsNullCheckExpr(left, isNegated: true);
                }

                if (left is LiteralExpr { IsNull: true })
                {
                    if (binary.Operator == "==")
                        return new IsNullCheckExpr(right, isNegated: false);
                    if (binary.Operator == "!=")
                        return new IsNullCheckExpr(right, isNegated: true);
                }

                var op = MapBinaryOperator(binary.Operator);
                if (op == null)
                    return new SqlRawExpr(binary.ToString());

                return new BinaryOpExpr(left, op.Value, right);
            }

            case SyntacticUnary unary:
            {
                var operand = Convert(unary.Operand);
                return unary.Operator switch
                {
                    "!" => new UnaryOpExpr(SqlUnaryOperator.Not, operand),
                    "-" => new UnaryOpExpr(SqlUnaryOperator.Negate, operand),
                    "+" => operand,
                    _ => new SqlRawExpr(unary.ToString())
                };
            }

            case SyntacticMethodCall methodCall:
            {
                SqlExpr? target = methodCall.Target != null ? Convert(methodCall.Target) : null;
                var args = new List<SqlExpr>();
                foreach (var arg in methodCall.Arguments)
                    args.Add(Convert(arg));

                return ConvertMethodCall(target, methodCall.MethodName, args);
            }

            case SyntacticMemberAccess memberAccess:
            {
                var target = Convert(memberAccess.Target);
                if (target is ColumnRefExpr colRef && memberAccess.MemberName == "Id")
                {
                    return new ColumnRefExpr(colRef.ParameterName, colRef.PropertyName, nestedProperty: "Id");
                }
                return new SqlRawExpr(memberAccess.ToString());
            }

            case SyntacticCapturedVariable capturedVar:
                return new CapturedValueExpr(
                    capturedVar.VariableName,
                    capturedVar.SyntaxText,
                    "object",
                    capturedVar.ExpressionPath);

            case SyntacticUnknown unknown:
                return new SqlRawExpr(unknown.SyntaxText);

            default:
                return new SqlRawExpr(expression.ToString());
        }
    }

    private static SqlExpr ConvertMethodCall(SqlExpr? target, string methodName, List<SqlExpr> args)
    {
        // String methods on column references
        if (target is ColumnRefExpr colRef)
        {
            switch (methodName)
            {
                case "Contains" when args.Count == 1:
                    return CreateLikeExpr(colRef, args[0], "%", "%");
                case "StartsWith" when args.Count == 1:
                    return CreateLikeExpr(colRef, args[0], "", "%");
                case "EndsWith" when args.Count == 1:
                    return CreateLikeExpr(colRef, args[0], "%", "");
                case "ToLower":
                case "ToLowerInvariant":
                    return new FunctionCallExpr("LOWER", new SqlExpr[] { colRef });
                case "ToUpper":
                case "ToUpperInvariant":
                    return new FunctionCallExpr("UPPER", new SqlExpr[] { colRef });
                case "Trim":
                    return new FunctionCallExpr("TRIM", new SqlExpr[] { colRef });
                case "TrimStart":
                    return new FunctionCallExpr("LTRIM", new SqlExpr[] { colRef });
                case "TrimEnd":
                    return new FunctionCallExpr("RTRIM", new SqlExpr[] { colRef });
            }
        }

        // Sql.* aggregate functions
        if (target is CapturedValueExpr cv && cv.VariableName == "Sql")
        {
            return MapSqlFunction(methodName, args);
        }

        // Collection Contains on captured variables
        if (methodName == "Contains" && args.Count == 1 && target is CapturedValueExpr)
            return new SqlRawExpr($"{target}.Contains(...)");

        // Aggregate functions without target (static calls)
        if (target == null)
            return MapSqlFunction(methodName, args);

        return new SqlRawExpr($"{methodName}(...)");
    }

    private static SqlExpr MapSqlFunction(string methodName, List<SqlExpr> args)
    {
        switch (methodName)
        {
            case "Count" when args.Count == 0:
                return new FunctionCallExpr("COUNT", new SqlExpr[] { new SqlRawExpr("*") }, isAggregate: true);
            case "Count" when args.Count >= 1:
                return new FunctionCallExpr("COUNT", new SqlExpr[] { args[0] }, isAggregate: true);
            case "Sum" when args.Count >= 1:
                return new FunctionCallExpr("SUM", new SqlExpr[] { args[0] }, isAggregate: true);
            case "Avg" when args.Count >= 1:
                return new FunctionCallExpr("AVG", new SqlExpr[] { args[0] }, isAggregate: true);
            case "Min" when args.Count >= 1:
                return new FunctionCallExpr("MIN", new SqlExpr[] { args[0] }, isAggregate: true);
            case "Max" when args.Count >= 1:
                return new FunctionCallExpr("MAX", new SqlExpr[] { args[0] }, isAggregate: true);
            default:
                return new SqlRawExpr($"Sql.{methodName}(...)");
        }
    }

    private static SqlExpr CreateLikeExpr(SqlExpr operand, SqlExpr pattern, string prefix, string suffix)
    {
        bool needsEscape = false;
        if (pattern is LiteralExpr literal && literal.ClrType == "string")
        {
            var escaped = Translation.SqlLikeHelpers.EscapeLikeMetaChars(literal.SqlText);
            needsEscape = escaped != literal.SqlText;
            if (needsEscape)
                pattern = new LiteralExpr(escaped, "string");
        }

        return new LikeExpr(
            operand, pattern,
            likePrefix: string.IsNullOrEmpty(prefix) ? null : prefix,
            likeSuffix: string.IsNullOrEmpty(suffix) ? null : suffix,
            needsEscape: needsEscape);
    }

    private static SqlBinaryOperator? MapBinaryOperator(string op)
    {
        return op switch
        {
            "==" => SqlBinaryOperator.Equal,
            "!=" => SqlBinaryOperator.NotEqual,
            "<" => SqlBinaryOperator.LessThan,
            "<=" => SqlBinaryOperator.LessThanOrEqual,
            ">" => SqlBinaryOperator.GreaterThan,
            ">=" => SqlBinaryOperator.GreaterThanOrEqual,
            "&&" => SqlBinaryOperator.And,
            "||" => SqlBinaryOperator.Or,
            "+" => SqlBinaryOperator.Add,
            "-" => SqlBinaryOperator.Subtract,
            "*" => SqlBinaryOperator.Multiply,
            "/" => SqlBinaryOperator.Divide,
            "%" => SqlBinaryOperator.Modulo,
            "&" => SqlBinaryOperator.BitwiseAnd,
            "|" => SqlBinaryOperator.BitwiseOr,
            "^" => SqlBinaryOperator.BitwiseXor,
            _ => null
        };
    }
}
