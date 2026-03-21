using System;
using System.Collections.Generic;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;
using Quarry.Generators.Translation;

namespace Quarry.Generators.IR;

/// <summary>
/// Translates SqlExpr trees through the Bind → Render pipeline to produce ClauseInfo.
/// This replaces the SyntacticClauseTranslator's translation logic with the new IR pipeline.
/// </summary>
internal sealed class SqlExprClauseTranslator
{
    private readonly EntityInfo _entityInfo;
    private readonly SqlDialect _dialect;
    private readonly Dictionary<string, ColumnInfo> _columnLookup;

    public SqlExprClauseTranslator(EntityInfo entityInfo, SqlDialect dialect)
    {
        _entityInfo = entityInfo;
        _dialect = dialect;
        _columnLookup = new Dictionary<string, ColumnInfo>(StringComparer.Ordinal);
        foreach (var col in entityInfo.Columns)
            _columnLookup[col.PropertyName] = col;
    }

    /// <summary>
    /// Translates a PendingClauseInfo (which contains a SyntacticExpression) to ClauseInfo
    /// by converting to SqlExpr, binding, and rendering.
    /// </summary>
    public ClauseInfo Translate(PendingClauseInfo pending)
    {
        try
        {
            // Step 1: Convert SyntacticExpression → SqlExpr
            var sqlExpr = SyntacticExpressionAdapter.Convert(pending.Expression);

            // Bail out if the converted tree contains SqlRawExpr nodes from unsupported expressions.
            // These indicate method calls, member accesses, or unknown syntax that the adapter
            // couldn't convert to proper IR nodes (e.g., subqueries, runtime collections).
            // The old syntactic or semantic translators may handle these.
            if (ContainsUnsupportedRawExpr(sqlExpr))
                return ClauseInfo.Failure(pending.Kind, "Expression contains unsupported nodes for SqlExpr IR");

            // Step 2: Bind column references
            var inBooleanContext = pending.Kind == ClauseKind.Where || pending.Kind == ClauseKind.Having;
            var bound = SqlExprBinder.Bind(
                sqlExpr,
                _entityInfo,
                _dialect,
                pending.LambdaParameterName,
                inBooleanContext: inBooleanContext);

            // Step 3: Extract parameters from CapturedValueExpr and string/char literals
            int paramIndex = 0;
            var parameters = new List<ParameterInfo>();
            bound = ExtractParameters(bound, parameters, ref paramIndex);

            // Step 4: Render to SQL
            // Use generic parameter format (@p{n}) because dialect-specific parameter
            // formatting ($1 for PostgreSQL, ? for MySQL) is applied later during
            // SQL assembly by SqlFragmentTemplate. Column quoting and boolean formatting
            // still use the actual dialect (already applied by SqlExprBinder in step 2).
            var sql = SqlExprRenderer.Render(bound, _dialect, useGenericParamFormat: true);

            if (string.IsNullOrEmpty(sql))
                return ClauseInfo.Failure(pending.Kind, "Failed to translate expression via SqlExpr IR");

            // Handle clause-specific types
            if (pending.Kind == ClauseKind.OrderBy || pending.Kind == ClauseKind.GroupBy)
            {
                var keyTypeName = ResolveKeyType(pending.Expression);
                return new OrderByClauseInfo(sql, pending.IsDescending, parameters, keyTypeName);
            }

            if (pending.Kind == ClauseKind.Set)
            {
                var valueTypeName = ResolveKeyType(pending.Expression);
                var valueClrType = valueTypeName ?? "object";
                var pIdx = paramIndex;
                var setParams = new List<ParameterInfo>(parameters)
                {
                    new ParameterInfo(pIdx, $"@p{pIdx}", valueClrType, "value")
                };
                return new SetClauseInfo(sql, pIdx, setParams, valueTypeName: valueTypeName);
            }

            return ClauseInfo.Success(pending.Kind, sql, parameters);
        }
        catch (Exception ex)
        {
            return ClauseInfo.Failure(pending.Kind, $"SqlExpr translation error: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks whether the SqlExpr tree contains any SqlRawExpr nodes that were created
    /// by the adapter for unsupported expressions (subqueries, runtime collections, etc.).
    /// SqlRawExpr nodes created by the binder (for boolean columns) are in the post-bind
    /// tree, so this check runs on the pre-bind tree.
    /// </summary>
    private static bool ContainsUnsupportedRawExpr(SqlExpr expr)
    {
        switch (expr)
        {
            case SqlRawExpr raw:
                // Allow "*" (used in COUNT(*)) — it's valid SQL
                return raw.SqlText != "*";

            case BinaryOpExpr bin:
                return ContainsUnsupportedRawExpr(bin.Left) || ContainsUnsupportedRawExpr(bin.Right);

            case UnaryOpExpr unary:
                return ContainsUnsupportedRawExpr(unary.Operand);

            case FunctionCallExpr func:
                foreach (var arg in func.Arguments)
                    if (ContainsUnsupportedRawExpr(arg))
                        return true;
                return false;

            case InExpr inExpr:
                if (ContainsUnsupportedRawExpr(inExpr.Operand))
                    return true;
                foreach (var val in inExpr.Values)
                    if (ContainsUnsupportedRawExpr(val))
                        return true;
                return false;

            case IsNullCheckExpr isNull:
                return ContainsUnsupportedRawExpr(isNull.Operand);

            case LikeExpr like:
                return ContainsUnsupportedRawExpr(like.Operand) || ContainsUnsupportedRawExpr(like.Pattern);

            case ExprListExpr list:
                foreach (var e in list.Expressions)
                    if (ContainsUnsupportedRawExpr(e))
                        return true;
                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// Walks the SqlExpr tree and replaces CapturedValueExpr and string/char LiteralExpr
    /// nodes with ParamSlotExpr, collecting ParameterInfo for each.
    /// String and char literals are parameterized to match the old SyntacticClauseTranslator
    /// behavior (which always passes string/char values as parameters, not inline SQL).
    /// </summary>
    private static SqlExpr ExtractParameters(SqlExpr expr, List<ParameterInfo> parameters, ref int paramIndex)
    {
        switch (expr)
        {
            case CapturedValueExpr captured:
            {
                var idx = paramIndex++;
                var name = $"@p{idx}";
                parameters.Add(new ParameterInfo(
                    idx, name, captured.ClrType, captured.SyntaxText,
                    isCaptured: true, expressionPath: captured.ExpressionPath));
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
                    newValues[i] = ExtractParameters(inExpr.Values[i], parameters, ref paramIndex);
                    if (!ReferenceEquals(newValues[i], inExpr.Values[i])) changed = true;
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
                var pattern = ExtractParameters(like.Pattern, parameters, ref paramIndex);
                if (ReferenceEquals(operand, like.Operand) && ReferenceEquals(pattern, like.Pattern))
                    return like;
                return new LikeExpr(operand, pattern, like.IsNegated, like.LikePrefix, like.LikeSuffix, like.NeedsEscape);
            }

            // Terminal nodes that don't need parameter extraction
            default:
                return expr;
        }
    }

    private string? ResolveKeyType(SyntacticExpression expression)
    {
        if (expression is SyntacticPropertyAccess propAccess)
        {
            var propertyName = propAccess.PropertyName;
            if (propertyName.EndsWith(".Id"))
                propertyName = propertyName.Substring(0, propertyName.Length - 3);
            if (_columnLookup.TryGetValue(propertyName, out var column))
                return column.FullClrType;
        }
        return null;
    }

    /// <summary>
    /// Escapes a string for use as a C# string literal value expression.
    /// Matches the behavior of SyntacticClauseTranslator.EscapeString.
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
}
