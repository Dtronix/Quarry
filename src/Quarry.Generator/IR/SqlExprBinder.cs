using System;
using System.Collections.Generic;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;

namespace Quarry.Generators.IR;

/// <summary>
/// Resolves column references in SqlExpr trees against entity metadata.
/// Transforms ColumnRefExpr nodes to ResolvedColumnExpr.
/// </summary>
internal static class SqlExprBinder
{
    /// <summary>
    /// Resolves all ColumnRefExpr nodes to ResolvedColumnExpr.
    /// </summary>
    /// <param name="expr">The expression to bind.</param>
    /// <param name="primaryEntity">Primary entity metadata.</param>
    /// <param name="dialect">SQL dialect for quoting.</param>
    /// <param name="lambdaParameterName">The lambda parameter name (e.g., "u").</param>
    /// <param name="joinedEntities">Joined entities by parameter name.</param>
    /// <param name="tableAliases">Table aliases by parameter name.</param>
    /// <param name="inBooleanContext">Whether the expression is in a WHERE/HAVING context.</param>
    /// <returns>Bound expression with resolved columns, or original if binding fails.</returns>
    public static SqlExpr Bind(
        SqlExpr expr,
        EntityInfo primaryEntity,
        SqlDialect dialect,
        string lambdaParameterName,
        IReadOnlyDictionary<string, EntityInfo>? joinedEntities = null,
        IReadOnlyDictionary<string, string>? tableAliases = null,
        bool inBooleanContext = false)
    {
        var columnLookup = BuildColumnLookup(primaryEntity);
        var ctx = new BindContext(primaryEntity, dialect, lambdaParameterName, columnLookup, joinedEntities, tableAliases);
        return BindExpr(expr, ctx, inBooleanContext);
    }

    private sealed class BindContext
    {
        public EntityInfo PrimaryEntity { get; }
        public SqlDialect Dialect { get; }
        public string LambdaParameterName { get; }
        public Dictionary<string, ColumnInfo> ColumnLookup { get; }
        public IReadOnlyDictionary<string, EntityInfo>? JoinedEntities { get; }
        public IReadOnlyDictionary<string, string>? TableAliases { get; }
        public bool HasJoins => JoinedEntities != null && JoinedEntities.Count > 0;
        public bool HasAliases => TableAliases != null && TableAliases.Count > 0;

        public BindContext(
            EntityInfo primaryEntity,
            SqlDialect dialect,
            string lambdaParameterName,
            Dictionary<string, ColumnInfo> columnLookup,
            IReadOnlyDictionary<string, EntityInfo>? joinedEntities,
            IReadOnlyDictionary<string, string>? tableAliases)
        {
            PrimaryEntity = primaryEntity;
            Dialect = dialect;
            LambdaParameterName = lambdaParameterName;
            ColumnLookup = columnLookup;
            JoinedEntities = joinedEntities;
            TableAliases = tableAliases;
        }
    }

    private static SqlExpr BindExpr(SqlExpr expr, BindContext ctx, bool inBooleanContext)
    {
        switch (expr)
        {
            case ColumnRefExpr colRef:
                return BindColumnRef(colRef, ctx, inBooleanContext);

            case BooleanColumnExpr boolCol:
                return BindColumnRef(boolCol.Column, ctx, boolCol.InBooleanContext);

            case BinaryOpExpr bin:
            {
                var left = BindExpr(bin.Left, ctx, false);
                var right = BindExpr(bin.Right, ctx, false);
                if (ReferenceEquals(left, bin.Left) && ReferenceEquals(right, bin.Right))
                    return bin;
                return new BinaryOpExpr(left, bin.Operator, right);
            }

            case UnaryOpExpr unary:
            {
                var operand = BindExpr(unary.Operand, ctx, false);
                if (ReferenceEquals(operand, unary.Operand))
                    return unary;
                return new UnaryOpExpr(unary.Operator, operand);
            }

            case FunctionCallExpr func:
            {
                var boundArgs = BindList(func.Arguments, ctx);
                if (boundArgs == null)
                    return func;
                return new FunctionCallExpr(func.FunctionName, boundArgs, func.IsAggregate);
            }

            case InExpr inExpr:
            {
                var operand = BindExpr(inExpr.Operand, ctx, false);
                var values = BindList(inExpr.Values, ctx);
                if (ReferenceEquals(operand, inExpr.Operand) && values == null)
                    return inExpr;
                return new InExpr(operand, values ?? inExpr.Values, inExpr.IsNegated);
            }

            case IsNullCheckExpr isNull:
            {
                var operand = BindExpr(isNull.Operand, ctx, false);
                if (ReferenceEquals(operand, isNull.Operand))
                    return isNull;
                return new IsNullCheckExpr(operand, isNull.IsNegated);
            }

            case LikeExpr like:
            {
                var operand = BindExpr(like.Operand, ctx, false);
                var pattern = BindExpr(like.Pattern, ctx, false);
                if (ReferenceEquals(operand, like.Operand) && ReferenceEquals(pattern, like.Pattern))
                    return like;
                return new LikeExpr(operand, pattern, like.IsNegated, like.LikePrefix, like.LikeSuffix, like.NeedsEscape);
            }

            case ExprListExpr list:
            {
                var bound = BindList(list.Expressions, ctx);
                if (bound == null) return list;
                return new ExprListExpr(bound);
            }

            // Terminal nodes that don't need binding
            case ResolvedColumnExpr:
            case ParamSlotExpr:
            case LiteralExpr:
            case CapturedValueExpr:
            case SqlRawExpr:
                return expr;

            default:
                return expr;
        }
    }

    /// <summary>
    /// Binds a list of expressions, returning null if nothing changed.
    /// </summary>
    private static IReadOnlyList<SqlExpr>? BindList(IReadOnlyList<SqlExpr> exprs, BindContext ctx)
    {
        SqlExpr[]? result = null;
        for (int i = 0; i < exprs.Count; i++)
        {
            var bound = BindExpr(exprs[i], ctx, false);
            if (!ReferenceEquals(bound, exprs[i]))
            {
                if (result == null)
                {
                    result = new SqlExpr[exprs.Count];
                    for (int j = 0; j < i; j++)
                        result[j] = exprs[j];
                }
            }
            if (result != null)
                result[i] = bound;
        }
        return result;
    }

    private static SqlExpr BindColumnRef(ColumnRefExpr colRef, BindContext ctx, bool inBooleanContext)
    {
        var paramName = colRef.ParameterName;
        var propertyName = colRef.PropertyName;

        // Handle Ref<T,K>.Id → strip the nested property, resolve FK column
        if (colRef.NestedProperty == "Id")
        {
            // propertyName is the FK property name
        }

        // Determine which entity this column belongs to
        ColumnInfo? column = null;
        string? tableQualifier = null;

        if (paramName == ctx.LambdaParameterName)
        {
            // Primary entity
            ctx.ColumnLookup.TryGetValue(propertyName, out column);

            if (ctx.HasJoins || ctx.HasAliases)
            {
                if (ctx.TableAliases != null && ctx.TableAliases.TryGetValue(paramName, out var alias))
                    tableQualifier = QuoteIdentifier(alias, ctx.Dialect);
                else
                    tableQualifier = QuoteIdentifier(ctx.PrimaryEntity.TableName, ctx.Dialect);
            }
        }
        else if (ctx.JoinedEntities != null && ctx.JoinedEntities.TryGetValue(paramName, out var joinedEntity))
        {
            // Joined entity
            var joinLookup = BuildColumnLookup(joinedEntity);
            joinLookup.TryGetValue(propertyName, out column);

            if (ctx.TableAliases != null && ctx.TableAliases.TryGetValue(paramName, out var alias))
                tableQualifier = QuoteIdentifier(alias, ctx.Dialect);
            else
                tableQualifier = QuoteIdentifier(joinedEntity.TableName, ctx.Dialect);
        }

        if (column == null)
        {
            // Column not found — return error marker
            return new SqlRawExpr($"/* unresolved: {paramName}.{propertyName} */");
        }

        var quotedColumn = QuoteIdentifier(column.ColumnName, ctx.Dialect);

        // Boolean column in WHERE context → column = TRUE/1
        if (inBooleanContext && (column.ClrType == "bool" || column.ClrType == "Boolean"))
        {
            var boolLiteral = FormatBoolean(true, ctx.Dialect);
            var resolvedCol = tableQualifier != null
                ? new ResolvedColumnExpr($"{tableQualifier}.{quotedColumn}", tableQualifier)
                : new ResolvedColumnExpr(quotedColumn);
            return new BinaryOpExpr(resolvedCol, SqlBinaryOperator.Equal, new LiteralExpr(boolLiteral, "bool"));
        }

        if (tableQualifier != null)
            return new ResolvedColumnExpr($"{tableQualifier}.{quotedColumn}", tableQualifier);

        return new ResolvedColumnExpr(quotedColumn);
    }

    private static Dictionary<string, ColumnInfo> BuildColumnLookup(EntityInfo entity)
    {
        var lookup = new Dictionary<string, ColumnInfo>(StringComparer.Ordinal);
        foreach (var col in entity.Columns)
        {
            lookup[col.PropertyName] = col;
        }
        return lookup;
    }

    internal static string QuoteIdentifier(string identifier, SqlDialect dialect)
    {
        return dialect switch
        {
            SqlDialect.SQLite => $"\"{identifier}\"",
            SqlDialect.PostgreSQL => $"\"{identifier}\"",
            SqlDialect.MySQL => $"`{identifier}`",
            SqlDialect.SqlServer => $"[{identifier}]",
            _ => $"\"{identifier}\""
        };
    }

    internal static string FormatBoolean(bool value, SqlDialect dialect)
    {
        return dialect switch
        {
            SqlDialect.PostgreSQL => value ? "TRUE" : "FALSE",
            _ => value ? "1" : "0"
        };
    }
}
