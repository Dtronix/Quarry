using System.Collections.Generic;
using System.Text;
using Quarry.Generators.Models;
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
    public static string Render(SqlExpr expr, SqlDialect dialect, int parameterBaseIndex = 0, bool useGenericParamFormat = false, bool stripOuterParens = false)
    {
        var sb = new StringBuilder();
        RenderExpr(expr, dialect, parameterBaseIndex, sb, useGenericParamFormat);
        if (stripOuterParens && sb.Length >= 2 && sb[0] == '(' && sb[sb.Length - 1] == ')')
        {
            // Verify the outer parens are matching (not just coincidental)
            int depth = 0;
            bool matching = true;
            for (int i = 0; i < sb.Length - 1; i++)
            {
                if (sb[i] == '(') depth++;
                else if (sb[i] == ')') depth--;
                if (depth == 0 && i < sb.Length - 1) { matching = false; break; }
            }
            if (matching)
            {
                sb.Remove(sb.Length - 1, 1);
                sb.Remove(0, 1);
            }
        }
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

            case SubqueryExpr sub:
                if (sub.Predicate != null)
                    CollectParamsRecursive(sub.Predicate, result);
                if (sub.Selector != null)
                    CollectParamsRecursive(sub.Selector, result);
                break;

            case RawCallExpr rawCall:
                foreach (var arg in rawCall.Arguments)
                    CollectParamsRecursive(arg, result);
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

            case SubqueryExpr subquery:
                RenderSubquery(subquery, dialect, paramBase, sb, genericParams);
                break;

            case CapturedValueExpr:
                // CapturedValueExpr should have been converted to ParamSlotExpr during translation
                sb.Append("/* unresolved captured value */");
                break;

            case SqlRawExpr raw:
                sb.Append(raw.SqlText);
                break;

            case NavigationAccessExpr nav:
                // Should be resolved to ResolvedColumnExpr during binding; if unresolved, render diagnostic
                sb.Append($"/* unresolved navigation: {nav.SourceParameterName}.{string.Join(".", nav.NavigationHops)}.{nav.FinalPropertyName} */");
                break;

            case RawCallExpr rawCall:
            {
                // Render each argument and substitute {0}, {1} placeholders in the template.
                // Uses C#-style format placeholders to avoid confusion with SQL parameter syntax.
                var renderedArgs = new string[rawCall.Arguments.Count];
                for (int i = 0; i < rawCall.Arguments.Count; i++)
                {
                    var argSb = new StringBuilder();
                    RenderExpr(rawCall.Arguments[i], dialect, paramBase, argSb, genericParams);
                    renderedArgs[i] = argSb.ToString();
                }

                var template = rawCall.Template;
                var result = new StringBuilder(template.Length + 32);
                int pos = 0;
                while (pos < template.Length)
                {
                    if (template[pos] == '{')
                    {
                        int numStart = pos + 1;
                        int numEnd = numStart;
                        while (numEnd < template.Length && template[numEnd] >= '0' && template[numEnd] <= '9')
                            numEnd++;
                        if (numEnd > numStart && numEnd < template.Length && template[numEnd] == '}'
                            && int.TryParse(template.Substring(numStart, numEnd - numStart), out int argIdx)
                            && argIdx >= 0 && argIdx < renderedArgs.Length)
                        {
                            result.Append(renderedArgs[argIdx]);
                            pos = numEnd + 1; // skip past closing '}'
                            continue;
                        }
                    }
                    result.Append(template[pos]);
                    pos++;
                }
                sb.Append(result);
                break;
            }

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

        // When the pattern is a string literal, fold prefix + value + suffix into a single SQL literal
        if ((hasPrefix || hasSuffix) && like.Pattern is LiteralExpr literalPattern
            && literalPattern.ClrType == "string" && !literalPattern.IsNull)
        {
            var escaped = literalPattern.SqlText.Replace("'", "''");
            sb.Append('\'');
            if (hasPrefix) sb.Append(like.LikePrefix);
            sb.Append(escaped);
            if (hasSuffix) sb.Append(like.LikeSuffix);
            sb.Append('\'');
        }
        else if (!hasPrefix && !hasSuffix)
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

    private static void RenderSubquery(SubqueryExpr sub, SqlDialect dialect, int paramBase, StringBuilder sb, bool genericParams)
    {
        if (!sub.IsResolved)
        {
            sb.Append($"/* unresolved subquery: {sub.OuterParameterName}.{sub.NavigationPropertyName}.{sub.SubqueryKind} */");
            return;
        }

        switch (sub.SubqueryKind)
        {
            case SubqueryKind.Exists:
                sb.Append("EXISTS (SELECT 1 FROM ");
                sb.Append(sub.InnerTableQuoted);
                sb.Append(" AS ");
                sb.Append(sub.InnerAliasQuoted);
                AppendImplicitJoins(sub, dialect, sb);
                sb.Append(" WHERE ");
                sb.Append(sub.CorrelationSql);
                AppendSubqueryPredicate(sub.Predicate, " AND ", dialect, paramBase, sb, genericParams);
                sb.Append(')');
                break;

            case SubqueryKind.All:
                sb.Append("NOT EXISTS (SELECT 1 FROM ");
                sb.Append(sub.InnerTableQuoted);
                sb.Append(" AS ");
                sb.Append(sub.InnerAliasQuoted);
                AppendImplicitJoins(sub, dialect, sb);
                sb.Append(" WHERE ");
                sb.Append(sub.CorrelationSql);
                AppendSubqueryPredicate(sub.Predicate, " AND NOT ", dialect, paramBase, sb, genericParams);
                sb.Append(')');
                break;

            case SubqueryKind.Count:
                sb.Append("(SELECT COUNT(*) FROM ");
                sb.Append(sub.InnerTableQuoted);
                sb.Append(" AS ");
                sb.Append(sub.InnerAliasQuoted);
                AppendImplicitJoins(sub, dialect, sb);
                sb.Append(" WHERE ");
                sb.Append(sub.CorrelationSql);
                AppendSubqueryPredicate(sub.Predicate, " AND ", dialect, paramBase, sb, genericParams);
                sb.Append(')');
                break;

            case SubqueryKind.Sum:
            case SubqueryKind.Min:
            case SubqueryKind.Max:
            case SubqueryKind.Avg:
                var aggFunc = sub.SubqueryKind switch
                {
                    SubqueryKind.Sum => "SUM",
                    SubqueryKind.Min => "MIN",
                    SubqueryKind.Max => "MAX",
                    SubqueryKind.Avg => "AVG",
                    _ => "SUM"
                };
                sb.Append("(SELECT ");
                sb.Append(aggFunc);
                sb.Append('(');
                if (sub.Selector != null)
                    RenderExpr(sub.Selector, dialect, paramBase, sb, genericParams);
                else
                    sb.Append('*');
                sb.Append(") FROM ");
                sb.Append(sub.InnerTableQuoted);
                sb.Append(" AS ");
                sb.Append(sub.InnerAliasQuoted);
                AppendImplicitJoins(sub, dialect, sb);
                sb.Append(" WHERE ");
                sb.Append(sub.CorrelationSql);
                sb.Append(')');
                break;
        }
    }

    private static void AppendImplicitJoins(SubqueryExpr sub, SqlDialect dialect, StringBuilder sb)
    {
        if (sub.ImplicitJoins == null || sub.ImplicitJoins.Count == 0)
            return;

        foreach (var join in sub.ImplicitJoins)
        {
            sb.Append(' ');
            sb.Append(join.JoinKind == JoinClauseKind.Left ? "LEFT JOIN" : "INNER JOIN");
            sb.Append(' ');
            sb.Append(join.TargetTableQuoted);
            sb.Append(" AS ");
            sb.Append(SqlFormatting.QuoteIdentifier(dialect, join.TargetAlias));
            sb.Append(" ON ");
            sb.Append(SqlFormatting.QuoteIdentifier(dialect, join.SourceAlias));
            sb.Append('.');
            sb.Append(join.FkColumnQuoted);
            sb.Append(" = ");
            sb.Append(SqlFormatting.QuoteIdentifier(dialect, join.TargetAlias));
            sb.Append('.');
            sb.Append(join.TargetPkColumnQuoted);
        }
    }

    private static void AppendSubqueryPredicate(SqlExpr? predicate, string conjunction, SqlDialect dialect, int paramBase, StringBuilder sb, bool genericParams)
    {
        if (predicate == null) return;

        sb.Append(conjunction);
        if (predicate is BinaryOpExpr)
        {
            // BinaryOp already renders with outer parens: (left op right)
            RenderExpr(predicate, dialect, paramBase, sb, genericParams);
        }
        else
        {
            sb.Append('(');
            RenderExpr(predicate, dialect, paramBase, sb, genericParams);
            sb.Append(')');
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
