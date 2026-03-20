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

            // Step 2: Bind column references
            var inBooleanContext = pending.Kind == ClauseKind.Where || pending.Kind == ClauseKind.Having;
            var bound = SqlExprBinder.Bind(
                sqlExpr,
                _entityInfo,
                _dialect,
                pending.LambdaParameterName,
                inBooleanContext: inBooleanContext);

            // Step 3: Extract parameters from CapturedValueExpr nodes (convert to ParamSlotExpr)
            int paramIndex = 0;
            var parameters = new List<ParameterInfo>();
            bound = ExtractParameters(bound, parameters, ref paramIndex);

            // Step 4: Render to SQL
            var sql = SqlExprRenderer.Render(bound, _dialect);

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
    /// Walks the SqlExpr tree and replaces CapturedValueExpr nodes with ParamSlotExpr,
    /// collecting ParameterInfo for each.
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
}
