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
        bool inBooleanContext = false,
        IReadOnlyDictionary<string, EntityInfo>? entityLookup = null)
    {
        var columnLookup = BuildColumnLookup(primaryEntity);
        var ctx = new BindContext(primaryEntity, dialect, lambdaParameterName, columnLookup, joinedEntities, tableAliases, entityLookup);
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
        public IReadOnlyDictionary<string, EntityInfo>? EntityLookup { get; }
        public int SubqueryAliasCounter { get; set; }
        public bool HasJoins => JoinedEntities != null && JoinedEntities.Count > 0;
        public bool HasAliases => TableAliases != null && TableAliases.Count > 0;

        // Cached column lookups for joined entities to avoid rebuilding per column ref
        private Dictionary<string, Dictionary<string, ColumnInfo>>? _joinedColumnLookups;

        public BindContext(
            EntityInfo primaryEntity,
            SqlDialect dialect,
            string lambdaParameterName,
            Dictionary<string, ColumnInfo> columnLookup,
            IReadOnlyDictionary<string, EntityInfo>? joinedEntities,
            IReadOnlyDictionary<string, string>? tableAliases,
            IReadOnlyDictionary<string, EntityInfo>? entityLookup = null)
        {
            PrimaryEntity = primaryEntity;
            Dialect = dialect;
            LambdaParameterName = lambdaParameterName;
            ColumnLookup = columnLookup;
            JoinedEntities = joinedEntities;
            TableAliases = tableAliases;
            EntityLookup = entityLookup;
        }

        /// <summary>
        /// Gets a cached column lookup for a joined entity.
        /// </summary>
        public Dictionary<string, ColumnInfo> GetJoinedColumnLookup(string paramName, EntityInfo joinedEntity)
        {
            _joinedColumnLookups ??= new Dictionary<string, Dictionary<string, ColumnInfo>>(StringComparer.Ordinal);
            if (!_joinedColumnLookups.TryGetValue(paramName, out var lookup))
            {
                lookup = BuildColumnLookup(joinedEntity);
                _joinedColumnLookups[paramName] = lookup;
            }
            return lookup;
        }
    }

    private static SqlExpr BindExpr(SqlExpr expr, BindContext ctx, bool inBooleanContext)
    {
        switch (expr)
        {
            case ColumnRefExpr colRef:
                return BindColumnRef(colRef, ctx, inBooleanContext);

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

            case SubqueryExpr sub:
                return BindSubquery(sub, ctx);

            case RawCallExpr rawCall:
            {
                var boundArgs = BindList(rawCall.Arguments, ctx);
                if (boundArgs == null)
                    return rawCall;
                return new RawCallExpr(rawCall.Template, boundArgs);
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

        // For Ref<T,K>.Id access, the PropertyName already refers to the FK column
        // (e.g., "UserId"). The NestedProperty flag is informational only — no stripping needed
        // because both SqlExprParser and SyntacticExpressionAdapter store the base property name.

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
            // Joined entity — use cached column lookup
            var joinLookup = ctx.GetJoinedColumnLookup(paramName, joinedEntity);
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

        // Boolean column in WHERE context → "column" = TRUE/1
        // Emitted as SqlRawExpr to match the old SyntacticClauseTranslator output format
        // (no extra parentheses around the comparison).
        if (inBooleanContext && (column.ClrType == "bool" || column.ClrType == "Boolean"))
        {
            var boolLiteral = FormatBoolean(true, ctx.Dialect);
            var colSql = tableQualifier != null ? $"{tableQualifier}.{quotedColumn}" : quotedColumn;
            return new SqlRawExpr($"{colSql} = {boolLiteral}");
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

    private static SqlExpr BindSubquery(SubqueryExpr sub, BindContext ctx)
    {
        if (sub.IsResolved) return sub;
        if (ctx.EntityLookup == null) return sub;

        // Find navigation property on the primary entity (or joined entity)
        NavigationInfo? nav = null;
        EntityInfo? outerEntity = null;

        if (sub.OuterParameterName == ctx.LambdaParameterName)
        {
            outerEntity = ctx.PrimaryEntity;
        }
        else if (ctx.JoinedEntities != null && ctx.JoinedEntities.TryGetValue(sub.OuterParameterName, out var je))
        {
            outerEntity = je;
        }

        if (outerEntity == null) return sub;

        foreach (var n in outerEntity.Navigations)
        {
            if (n.PropertyName == sub.NavigationPropertyName)
            {
                nav = n;
                break;
            }
        }

        if (nav == null) return sub;

        // Look up target entity
        if (!ctx.EntityLookup.TryGetValue(nav.RelatedEntityName, out var targetEntity))
            return sub;

        // Resolve FK column name from property name
        string? fkColumnName = null;
        var targetColumnLookup = BuildColumnLookup(targetEntity);
        if (targetColumnLookup.TryGetValue(nav.ForeignKeyPropertyName, out var fkCol))
            fkColumnName = fkCol.ColumnName;
        else
            fkColumnName = nav.ForeignKeyPropertyName; // fallback

        // Resolve PK column name from outer entity
        string? pkColumnName = null;
        foreach (var col in outerEntity.Columns)
        {
            if (col.Kind == Quarry.Shared.Migration.ColumnKind.PrimaryKey)
            {
                pkColumnName = col.ColumnName;
                break;
            }
        }
        if (pkColumnName == null) return sub;

        var alias = $"sq{ctx.SubqueryAliasCounter++}";
        var innerTableQuoted = QuoteIdentifier(targetEntity.TableName, ctx.Dialect);
        var innerAliasQuoted = QuoteIdentifier(alias, ctx.Dialect);

        // Build correlation: inner.FK = outer.PK
        var fkQuoted = QuoteIdentifier(fkColumnName, ctx.Dialect);
        var pkQuoted = QuoteIdentifier(pkColumnName, ctx.Dialect);
        var outerQualifier = GetOuterQualifier(sub.OuterParameterName, ctx);
        var correlationSql = $"{innerAliasQuoted}.{fkQuoted} = {outerQualifier}.{pkQuoted}";

        // Bind predicate if present
        SqlExpr? boundPredicate = sub.Predicate;
        if (boundPredicate != null && sub.InnerParameterName != null)
        {
            // Create a child context for the inner entity
            var innerColumnLookup = BuildColumnLookup(targetEntity);
            var innerCtx = new BindContext(
                targetEntity, ctx.Dialect, sub.InnerParameterName, innerColumnLookup,
                ctx.JoinedEntities, ctx.TableAliases, ctx.EntityLookup);
            innerCtx.SubqueryAliasCounter = ctx.SubqueryAliasCounter;

            // Override: inner parameter columns should be qualified with the subquery alias
            // We create a custom table aliases map for the inner context
            var innerAliases = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [sub.InnerParameterName] = alias
            };
            // Also add outer aliases so outer refs resolve correctly
            if (ctx.TableAliases != null)
            {
                foreach (var kv in ctx.TableAliases)
                    innerAliases[kv.Key] = kv.Value;
            }
            // Map the outer parameter to its table
            if (!innerAliases.ContainsKey(sub.OuterParameterName))
            {
                if (sub.OuterParameterName == ctx.LambdaParameterName)
                    innerAliases[sub.OuterParameterName] = ctx.PrimaryEntity.TableName;
                else if (ctx.JoinedEntities != null && ctx.JoinedEntities.TryGetValue(sub.OuterParameterName, out var oje))
                    innerAliases[sub.OuterParameterName] = oje.TableName;
            }

            var innerJoined = new Dictionary<string, EntityInfo>(StringComparer.Ordinal);
            if (ctx.JoinedEntities != null)
            {
                foreach (var kv in ctx.JoinedEntities)
                    innerJoined[kv.Key] = kv.Value;
            }
            // Add outer entity so outer param refs resolve
            if (sub.OuterParameterName == ctx.LambdaParameterName)
                innerJoined[sub.OuterParameterName] = ctx.PrimaryEntity;

            innerCtx = new BindContext(
                targetEntity, ctx.Dialect, sub.InnerParameterName, innerColumnLookup,
                innerJoined, innerAliases, ctx.EntityLookup);
            innerCtx.SubqueryAliasCounter = ctx.SubqueryAliasCounter;

            boundPredicate = BindExpr(boundPredicate, innerCtx, false);
            ctx.SubqueryAliasCounter = innerCtx.SubqueryAliasCounter;
        }

        return new SubqueryExpr(
            sub.OuterParameterName,
            sub.NavigationPropertyName,
            sub.SubqueryKind,
            boundPredicate,
            sub.InnerParameterName,
            innerTableQuoted,
            innerAliasQuoted,
            correlationSql);
    }

    private static string GetOuterQualifier(string outerParamName, BindContext ctx)
    {
        if (ctx.TableAliases != null && ctx.TableAliases.TryGetValue(outerParamName, out var alias))
            return QuoteIdentifier(alias, ctx.Dialect);
        if (outerParamName == ctx.LambdaParameterName)
            return QuoteIdentifier(ctx.PrimaryEntity.TableName, ctx.Dialect);
        if (ctx.JoinedEntities != null && ctx.JoinedEntities.TryGetValue(outerParamName, out var je))
            return QuoteIdentifier(je.TableName, ctx.Dialect);
        return QuoteIdentifier(outerParamName, ctx.Dialect);
    }
}
