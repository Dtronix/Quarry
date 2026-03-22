using System.Collections.Generic;
using System.Text;
using Quarry.Generators.Sql;

namespace Quarry.Generators.IR;

/// <summary>
/// Renders bound SqlExpr trees to dialect-specific SQL strings.
/// Replaces the SQL generation logic in ExpressionSyntaxTranslator and SyntacticClauseTranslator.
/// </summary>
internal static class SqlExprRenderer
{
    /// <summary>
    /// Renders an expression to a SQL string.
    /// </summary>
    /// <param name="expr">The bound expression to render.</param>
    /// <param name="dialect">SQL dialect for formatting.</param>
    /// <param name="parameterBaseIndex">Base index for parameter placeholders.</param>
    /// <param name="useGenericParamFormat">When true, always uses @p{n} format for parameters
    /// regardless of dialect. Used by SqlExprClauseTranslator where dialect-specific parameter
    /// formatting is deferred to the SQL assembly stage.</param>
    /// <returns>The rendered SQL string.</returns>
    public static string Render(SqlExpr expr, SqlDialect dialect, int parameterBaseIndex = 0, bool useGenericParamFormat = false)
    {
        var sb = new StringBuilder();
        RenderExpr(expr, dialect, parameterBaseIndex, sb, useGenericParamFormat);
        return sb.ToString();
    }

    /// <summary>
    /// Collects all ParamSlotExpr nodes from an expression tree (in order).
    /// </summary>
    public static List<ParamSlotExpr> CollectParameters(SqlExpr expr)
    {
        var result = new List<ParamSlotExpr>();
        CollectParamsRecursive(expr, result);
        return result;
    }

    private static void CollectParamsRecursive(SqlExpr expr, List<ParamSlotExpr> result)
    {
        switch (expr)
        {
            case ParamSlotExpr param:
                result.Add(param);
                break;

            case BinaryOpExpr bin:
                CollectParamsRecursive(bin.Left, result);
                CollectParamsRecursive(bin.Right, result);
                break;

            case UnaryOpExpr unary:
                CollectParamsRecursive(unary.Operand, result);
                break;

            case FunctionCallExpr func:
                foreach (var arg in func.Arguments)
                    CollectParamsRecursive(arg, result);
                break;

            case InExpr inExpr:
                CollectParamsRecursive(inExpr.Operand, result);
                foreach (var val in inExpr.Values)
                    CollectParamsRecursive(val, result);
                break;

            case IsNullCheckExpr isNull:
                CollectParamsRecursive(isNull.Operand, result);
                break;

            case LikeExpr like:
                CollectParamsRecursive(like.Operand, result);
                CollectParamsRecursive(like.Pattern, result);
                break;

            case ExprListExpr list:
                foreach (var e in list.Expressions)
                    CollectParamsRecursive(e, result);
                break;
        }
    }

    private static void RenderExpr(SqlExpr expr, SqlDialect dialect, int paramBase, StringBuilder sb, bool genericParams = false)
    {
        switch (expr)
        {
            case ResolvedColumnExpr col:
                sb.Append(col.QuotedColumnName);
                break;

            case ColumnRefExpr colRef:
                // Unresolved column — shouldn't happen after binding, but handle gracefully
                sb.Append(colRef.PropertyName);
                break;

            case ParamSlotExpr param:
                AppendParameterPlaceholder(param, dialect, paramBase, sb, genericParams);
                break;

            case LiteralExpr literal:
                RenderLiteral(literal, dialect, sb);
                break;

            case BinaryOpExpr bin:
                RenderBinary(bin, dialect, paramBase, sb, genericParams);
                break;

            case UnaryOpExpr unary:
                RenderUnary(unary, dialect, paramBase, sb, genericParams);
                break;

            case FunctionCallExpr func:
                RenderFunction(func, dialect, paramBase, sb, genericParams);
                break;

            case InExpr inExpr:
                RenderIn(inExpr, dialect, paramBase, sb, genericParams);
                break;

            case IsNullCheckExpr isNull:
                RenderIsNull(isNull, dialect, paramBase, sb, genericParams);
                break;

            case LikeExpr like:
                RenderLike(like, dialect, paramBase, sb, genericParams);
                break;

            case CapturedValueExpr:
                // CapturedValueExpr should have been converted to ParamSlotExpr during translation
                sb.Append("/* unresolved captured value */");
                break;

            case SqlRawExpr raw:
                sb.Append(raw.SqlText);
                break;

            case ExprListExpr list:
                for (int i = 0; i < list.Expressions.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    RenderExpr(list.Expressions[i], dialect, paramBase, sb, genericParams);
                }
                break;

        }
    }

    private static void AppendParameterPlaceholder(ParamSlotExpr param, SqlDialect dialect, int paramBase, StringBuilder sb, bool genericParams = false)
    {
        var idx = paramBase + param.LocalIndex;
        if (!genericParams && dialect == SqlDialect.PostgreSQL)
        {
            sb.Append('$').Append(idx + 1); // PostgreSQL uses 1-based $1, $2, ...
        }
        else if (!genericParams && dialect == SqlDialect.MySQL)
        {
            sb.Append('?'); // MySQL uses positional ? parameters
        }
        else
        {
            sb.Append("@p").Append(idx);
        }
    }

    private static void RenderLiteral(LiteralExpr literal, SqlDialect dialect, StringBuilder sb)
    {
        if (literal.IsNull)
        {
            sb.Append("NULL");
            return;
        }

        switch (literal.ClrType)
        {
            case "bool":
                // Normalize boolean literals to dialect-specific format
                var isTrueish = literal.SqlText == "TRUE" || literal.SqlText == "true" || literal.SqlText == "1";
                sb.Append(SqlExprBinder.FormatBoolean(isTrueish, dialect));
                break;

            case "string":
                // String literals: escape and single-quote
                var escaped = literal.SqlText.Replace("'", "''");
                sb.Append('\'').Append(escaped).Append('\'');
                break;

            case "char":
                var charEscaped = literal.SqlText.Replace("'", "''");
                sb.Append('\'').Append(charEscaped).Append('\'');
                break;

            default:
                // Numeric and other literals
                sb.Append(literal.SqlText);
                break;
        }
    }

    private static void RenderBinary(BinaryOpExpr bin, SqlDialect dialect, int paramBase, StringBuilder sb, bool genericParams = false)
    {
        // Handle string concatenation for different dialects
        if (bin.Operator == SqlBinaryOperator.Add)
        {
            // Check if this could be string concatenation — if either side is a string column
            // This is a heuristic; the exact check would require type info
            // For now, render normally and let the dialect-specific formatting happen at a higher level
        }

        sb.Append('(');
        RenderExpr(bin.Left, dialect, paramBase, sb, genericParams);
        sb.Append(' ');
        sb.Append(GetSqlOperator(bin.Operator));
        sb.Append(' ');
        RenderExpr(bin.Right, dialect, paramBase, sb, genericParams);
        sb.Append(')');
    }

    private static void RenderUnary(UnaryOpExpr unary, SqlDialect dialect, int paramBase, StringBuilder sb, bool genericParams = false)
    {
        switch (unary.Operator)
        {
            case SqlUnaryOperator.Not:
                sb.Append("NOT (");
                RenderExpr(unary.Operand, dialect, paramBase, sb, genericParams);
                sb.Append(')');
                break;

            case SqlUnaryOperator.Negate:
                sb.Append('-');
                RenderExpr(unary.Operand, dialect, paramBase, sb, genericParams);
                break;
        }
    }

    private static void RenderFunction(FunctionCallExpr func, SqlDialect dialect, int paramBase, StringBuilder sb, bool genericParams = false)
    {
        sb.Append(func.FunctionName);
        sb.Append('(');
        for (int i = 0; i < func.Arguments.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            RenderExpr(func.Arguments[i], dialect, paramBase, sb, genericParams);
        }
        sb.Append(')');
    }

    private static void RenderIn(InExpr inExpr, SqlDialect dialect, int paramBase, StringBuilder sb, bool genericParams = false)
    {
        RenderExpr(inExpr.Operand, dialect, paramBase, sb, genericParams);
        sb.Append(inExpr.IsNegated ? " NOT IN (" : " IN (");
        for (int i = 0; i < inExpr.Values.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            RenderExpr(inExpr.Values[i], dialect, paramBase, sb, genericParams);
        }
        sb.Append(')');
    }

    private static void RenderIsNull(IsNullCheckExpr isNull, SqlDialect dialect, int paramBase, StringBuilder sb, bool genericParams = false)
    {
        RenderExpr(isNull.Operand, dialect, paramBase, sb, genericParams);
        sb.Append(isNull.IsNegated ? " IS NOT NULL" : " IS NULL");
    }

    private static void RenderLike(LikeExpr like, SqlDialect dialect, int paramBase, StringBuilder sb, bool genericParams = false)
    {
        RenderExpr(like.Operand, dialect, paramBase, sb, genericParams);
        sb.Append(like.IsNegated ? " NOT LIKE " : " LIKE ");

        // Build the pattern with prefix/suffix using dialect-appropriate concatenation
        var hasPrefix = !string.IsNullOrEmpty(like.LikePrefix);
        var hasSuffix = !string.IsNullOrEmpty(like.LikeSuffix);

        if (!hasPrefix && !hasSuffix)
        {
            RenderExpr(like.Pattern, dialect, paramBase, sb, genericParams);
        }
        else
        {
            var parts = new List<string>();
            if (hasPrefix) parts.Add($"'{like.LikePrefix}'");

            var patternSb = new StringBuilder();
            RenderExpr(like.Pattern, dialect, paramBase, patternSb, genericParams);
            parts.Add(patternSb.ToString());

            if (hasSuffix) parts.Add($"'{like.LikeSuffix}'");

            if (parts.Count == 1)
            {
                sb.Append(parts[0]);
            }
            else
            {
                switch (dialect)
                {
                    case SqlDialect.MySQL:
                        sb.Append("CONCAT(").Append(string.Join(", ", parts)).Append(')');
                        break;
                    case SqlDialect.SqlServer:
                        sb.Append(string.Join(" + ", parts));
                        break;
                    default: // SQLite, PostgreSQL
                        sb.Append(string.Join(" || ", parts));
                        break;
                }
            }
        }

        if (like.NeedsEscape)
        {
            sb.Append(" ESCAPE '\\'");
        }
    }

    private static string GetSqlOperator(SqlBinaryOperator op)
    {
        return op switch
        {
            SqlBinaryOperator.Equal => "=",
            SqlBinaryOperator.NotEqual => "<>",
            SqlBinaryOperator.LessThan => "<",
            SqlBinaryOperator.GreaterThan => ">",
            SqlBinaryOperator.LessThanOrEqual => "<=",
            SqlBinaryOperator.GreaterThanOrEqual => ">=",
            SqlBinaryOperator.And => "AND",
            SqlBinaryOperator.Or => "OR",
            SqlBinaryOperator.Add => "+",
            SqlBinaryOperator.Subtract => "-",
            SqlBinaryOperator.Multiply => "*",
            SqlBinaryOperator.Divide => "/",
            SqlBinaryOperator.Modulo => "%",
            SqlBinaryOperator.BitwiseAnd => "&",
            SqlBinaryOperator.BitwiseOr => "|",
            SqlBinaryOperator.BitwiseXor => "^",
            _ => "?"
        };
    }
}
