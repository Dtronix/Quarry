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
    /// <param name="config">Dialect configuration for formatting.</param>
    /// <param name="parameterBaseIndex">Base index for parameter placeholders.</param>
    /// <param name="useGenericParamFormat">When true, always uses @p{n} format for parameters
    /// regardless of dialect. Used by SqlExprClauseTranslator where dialect-specific parameter
    /// formatting is deferred to the SQL assembly stage.</param>
    /// <returns>The rendered SQL string.</returns>
    public static string Render(SqlExpr expr, SqlDialectConfig config, int parameterBaseIndex = 0, bool useGenericParamFormat = false, bool stripOuterParens = false)
    {
        var sb = new StringBuilder();
        RenderTo(sb, expr, config, parameterBaseIndex, useGenericParamFormat, stripOuterParens);
        return sb.ToString();
    }

    /// <summary>
    /// Backwards-compatible overload accepting a bare <see cref="SqlDialect"/>. Wraps it in a
    /// default <see cref="SqlDialectConfig"/> and forwards. Callers that don't yet have a
    /// <see cref="SqlDialectConfig"/> can keep passing the bare enum.
    /// </summary>
    public static string Render(SqlExpr expr, SqlDialect dialect, int parameterBaseIndex = 0, bool useGenericParamFormat = false, bool stripOuterParens = false)
        => Render(expr, new SqlDialectConfig(dialect), parameterBaseIndex, useGenericParamFormat, stripOuterParens);

    /// <summary>
    /// Renders a SqlExpr tree into an existing StringBuilder, avoiding an intermediate string allocation.
    /// Callers that already have a StringBuilder (e.g., SqlAssembler) can use this overload directly.
    /// </summary>
    public static void RenderTo(StringBuilder sb, SqlExpr expr, SqlDialectConfig config, int parameterBaseIndex = 0, bool useGenericParamFormat = false, bool stripOuterParens = false)
    {
        RenderExpr(expr, config, parameterBaseIndex, sb, useGenericParamFormat);
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
    }

    /// <summary>
    /// Backwards-compatible overload accepting a bare <see cref="SqlDialect"/>. Wraps it in a
    /// default <see cref="SqlDialectConfig"/> and forwards.
    /// </summary>
    public static void RenderTo(StringBuilder sb, SqlExpr expr, SqlDialect dialect, int parameterBaseIndex = 0, bool useGenericParamFormat = false, bool stripOuterParens = false)
        => RenderTo(sb, expr, new SqlDialectConfig(dialect), parameterBaseIndex, useGenericParamFormat, stripOuterParens);

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

    private static void RenderExpr(SqlExpr expr, SqlDialectConfig config, int paramBase, StringBuilder sb, bool genericParams = false)
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
                AppendParameterPlaceholder(param, config, paramBase, sb, genericParams);
                break;

            case LiteralExpr literal:
                RenderLiteral(literal, config, sb);
                break;

            case BinaryOpExpr bin:
                RenderBinary(bin, config, paramBase, sb, genericParams);
                break;

            case UnaryOpExpr unary:
                RenderUnary(unary, config, paramBase, sb, genericParams);
                break;

            case FunctionCallExpr func:
                RenderFunction(func, config, paramBase, sb, genericParams);
                break;

            case InExpr inExpr:
                RenderIn(inExpr, config, paramBase, sb, genericParams);
                break;

            case IsNullCheckExpr isNull:
                RenderIsNull(isNull, config, paramBase, sb, genericParams);
                break;

            case LikeExpr like:
                RenderLike(like, config, paramBase, sb, genericParams);
                break;

            case SubqueryExpr subquery:
                RenderSubquery(subquery, config, paramBase, sb, genericParams);
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
                    RenderExpr(rawCall.Arguments[i], config, paramBase, argSb, genericParams);
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
                    RenderExpr(list.Expressions[i], config, paramBase, sb, genericParams);
                }
                break;

        }
    }

    private static void AppendParameterPlaceholder(ParamSlotExpr param, SqlDialectConfig config, int paramBase, StringBuilder sb, bool genericParams = false)
    {
        var idx = paramBase + param.LocalIndex;
        if (!genericParams && config.Dialect == SqlDialect.PostgreSQL)
        {
            sb.Append('$').Append(idx + 1); // PostgreSQL uses 1-based $1, $2, ...
        }
        else if (!genericParams && config.Dialect == SqlDialect.MySQL)
        {
            sb.Append('?'); // MySQL uses positional ? parameters
        }
        else
        {
            sb.Append("@p").Append(idx);
        }
    }

    private static void RenderLiteral(LiteralExpr literal, SqlDialectConfig config, StringBuilder sb)
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
                sb.Append(SqlExprBinder.FormatBoolean(isTrueish, config.Dialect));
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

    private static void RenderBinary(BinaryOpExpr bin, SqlDialectConfig config, int paramBase, StringBuilder sb, bool genericParams = false)
    {
        // Handle string concatenation for different dialects
        if (bin.Operator == SqlBinaryOperator.Add)
        {
            // Check if this could be string concatenation — if either side is a string column
            // This is a heuristic; the exact check would require type info
            // For now, render normally and let the dialect-specific formatting happen at a higher level
        }

        sb.Append('(');
        RenderExpr(bin.Left, config, paramBase, sb, genericParams);
        sb.Append(' ');
        sb.Append(GetSqlOperator(bin.Operator));
        sb.Append(' ');
        RenderExpr(bin.Right, config, paramBase, sb, genericParams);
        sb.Append(')');
    }

    private static void RenderUnary(UnaryOpExpr unary, SqlDialectConfig config, int paramBase, StringBuilder sb, bool genericParams = false)
    {
        switch (unary.Operator)
        {
            case SqlUnaryOperator.Not:
                sb.Append("NOT (");
                RenderExpr(unary.Operand, config, paramBase, sb, genericParams);
                sb.Append(')');
                break;

            case SqlUnaryOperator.Negate:
                sb.Append('-');
                RenderExpr(unary.Operand, config, paramBase, sb, genericParams);
                break;
        }
    }

    private static void RenderFunction(FunctionCallExpr func, SqlDialectConfig config, int paramBase, StringBuilder sb, bool genericParams = false)
    {
        sb.Append(func.FunctionName);
        sb.Append('(');
        for (int i = 0; i < func.Arguments.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            RenderExpr(func.Arguments[i], config, paramBase, sb, genericParams);
        }
        sb.Append(')');
    }

    private static void RenderIn(InExpr inExpr, SqlDialectConfig config, int paramBase, StringBuilder sb, bool genericParams = false)
    {
        RenderExpr(inExpr.Operand, config, paramBase, sb, genericParams);
        sb.Append(inExpr.IsNegated ? " NOT IN (" : " IN (");
        for (int i = 0; i < inExpr.Values.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            RenderExpr(inExpr.Values[i], config, paramBase, sb, genericParams);
        }
        sb.Append(')');
    }

    private static void RenderIsNull(IsNullCheckExpr isNull, SqlDialectConfig config, int paramBase, StringBuilder sb, bool genericParams = false)
    {
        RenderExpr(isNull.Operand, config, paramBase, sb, genericParams);
        sb.Append(isNull.IsNegated ? " IS NOT NULL" : " IS NULL");
    }

    private static void RenderLike(LikeExpr like, SqlDialectConfig config, int paramBase, StringBuilder sb, bool genericParams = false)
    {
        RenderExpr(like.Operand, config, paramBase, sb, genericParams);
        sb.Append(like.IsNegated ? " NOT LIKE " : " LIKE ");

        // On default-mode MySQL (sql_mode does NOT include NO_BACKSLASH_ESCAPES),
        // backslash inside a string literal is itself an escape character. Single-
        // backslash forms — e.g. '\_' or ESCAPE '\' — get parsed as the escaped
        // character, which either drops the backslash semantically (the LIKE-meta
        // escape collapses) or causes a 1064 syntax error (the ESCAPE clause's
        // closing quote gets consumed as an escaped char). Doubling the backslashes
        // emits e.g. '\\_' / ESCAPE '\\' which MySQL parses to a single literal '\',
        // matching what consumers would write by hand for that server config.
        var doubleBackslashes = config.Dialect == SqlDialect.MySQL && config.MySqlBackslashEscapes;

        // Build the pattern with prefix/suffix using dialect-appropriate concatenation
        var hasPrefix = !string.IsNullOrEmpty(like.LikePrefix);
        var hasSuffix = !string.IsNullOrEmpty(like.LikeSuffix);

        // When the pattern is a string literal, fold prefix + value + suffix into a single SQL literal
        if ((hasPrefix || hasSuffix) && like.Pattern is LiteralExpr literalPattern
            && literalPattern.ClrType == "string" && !literalPattern.IsNull)
        {
            var escaped = literalPattern.SqlText.Replace("'", "''");
            if (doubleBackslashes) escaped = escaped.Replace("\\", "\\\\");
            sb.Append('\'');
            if (hasPrefix) sb.Append(like.LikePrefix);
            sb.Append(escaped);
            if (hasSuffix) sb.Append(like.LikeSuffix);
            sb.Append('\'');
        }
        else if (!hasPrefix && !hasSuffix)
        {
            // Bare literal pattern (no prefix/suffix folding) still needs backslash
            // doubling for MySQL+default. Parameter-bound and column-ref patterns
            // bypass string-literal parsing, so they need no transformation.
            if (doubleBackslashes && like.Pattern is LiteralExpr bareLit
                && bareLit.ClrType == "string" && !bareLit.IsNull)
            {
                var bareEscaped = bareLit.SqlText.Replace("'", "''").Replace("\\", "\\\\");
                sb.Append('\'').Append(bareEscaped).Append('\'');
            }
            else
            {
                RenderExpr(like.Pattern, config, paramBase, sb, genericParams);
            }
        }
        else
        {
            var parts = new List<string>();
            if (hasPrefix) parts.Add($"'{like.LikePrefix}'");

            var patternSb = new StringBuilder();
            RenderExpr(like.Pattern, config, paramBase, patternSb, genericParams);
            parts.Add(patternSb.ToString());

            if (hasSuffix) parts.Add($"'{like.LikeSuffix}'");

            if (parts.Count == 1)
            {
                sb.Append(parts[0]);
            }
            else
            {
                switch (config.Dialect)
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
            sb.Append(doubleBackslashes ? " ESCAPE '\\\\'" : " ESCAPE '\\'");
        }
    }

    private static void RenderSubquery(SubqueryExpr sub, SqlDialectConfig config, int paramBase, StringBuilder sb, bool genericParams)
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
                AppendImplicitJoins(sub, config, sb);
                sb.Append(" WHERE ");
                sb.Append(sub.CorrelationSql);
                AppendSubqueryPredicate(sub.Predicate, " AND ", config, paramBase, sb, genericParams);
                sb.Append(')');
                break;

            case SubqueryKind.All:
                sb.Append("NOT EXISTS (SELECT 1 FROM ");
                sb.Append(sub.InnerTableQuoted);
                sb.Append(" AS ");
                sb.Append(sub.InnerAliasQuoted);
                AppendImplicitJoins(sub, config, sb);
                sb.Append(" WHERE ");
                sb.Append(sub.CorrelationSql);
                AppendSubqueryPredicate(sub.Predicate, " AND NOT ", config, paramBase, sb, genericParams);
                sb.Append(')');
                break;

            case SubqueryKind.Count:
                sb.Append("(SELECT COUNT(*) FROM ");
                sb.Append(sub.InnerTableQuoted);
                sb.Append(" AS ");
                sb.Append(sub.InnerAliasQuoted);
                AppendImplicitJoins(sub, config, sb);
                sb.Append(" WHERE ");
                sb.Append(sub.CorrelationSql);
                AppendSubqueryPredicate(sub.Predicate, " AND ", config, paramBase, sb, genericParams);
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
                    RenderExpr(sub.Selector, config, paramBase, sb, genericParams);
                else
                    sb.Append('*');
                sb.Append(") FROM ");
                sb.Append(sub.InnerTableQuoted);
                sb.Append(" AS ");
                sb.Append(sub.InnerAliasQuoted);
                AppendImplicitJoins(sub, config, sb);
                sb.Append(" WHERE ");
                sb.Append(sub.CorrelationSql);
                sb.Append(')');
                break;
        }
    }

    private static void AppendImplicitJoins(SubqueryExpr sub, SqlDialectConfig config, StringBuilder sb)
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
            sb.Append(SqlFormatting.QuoteIdentifier(config.Dialect, join.TargetAlias));
            sb.Append(" ON ");
            sb.Append(SqlFormatting.QuoteIdentifier(config.Dialect, join.SourceAlias));
            sb.Append('.');
            sb.Append(join.FkColumnQuoted);
            sb.Append(" = ");
            sb.Append(SqlFormatting.QuoteIdentifier(config.Dialect, join.TargetAlias));
            sb.Append('.');
            sb.Append(join.TargetPkColumnQuoted);
        }
    }

    private static void AppendSubqueryPredicate(SqlExpr? predicate, string conjunction, SqlDialectConfig config, int paramBase, StringBuilder sb, bool genericParams)
    {
        if (predicate == null) return;

        sb.Append(conjunction);
        if (predicate is BinaryOpExpr)
        {
            // BinaryOp already renders with outer parens: (left op right)
            RenderExpr(predicate, config, paramBase, sb, genericParams);
        }
        else
        {
            sb.Append('(');
            RenderExpr(predicate, config, paramBase, sb, genericParams);
            sb.Append(')');
        }
    }

    /// <summary>
    /// Returns the SQL operator text for a <see cref="SqlBinaryOperator"/>. Shared with the
    /// Sql.Raw projection walker (<c>ProjectionAnalyzer.RenderRawArgNode</c>) so both paths
    /// stay in sync as the operator enum evolves. Throws on unknown operators rather than
    /// emitting a sentinel string — silently rendering an unrecognized operator into SQL
    /// would reintroduce silent-wrong-SQL failure modes.
    /// </summary>
    internal static string GetSqlOperator(SqlBinaryOperator op)
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
            _ => throw new System.ArgumentOutOfRangeException(nameof(op), op, "Unsupported SqlBinaryOperator."),
        };
    }
}
