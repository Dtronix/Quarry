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
        return Bind(expr, primaryEntity, dialect, lambdaParameterName, joinedEntities, tableAliases, inBooleanContext, entityLookup, out _);
    }

    /// <summary>
    /// Binds SQL expression tree, resolving column references and navigation accesses.
    /// Returns implicit joins collected from One&lt;T&gt; navigation access.
    /// </summary>
    public static SqlExpr Bind(
        SqlExpr expr,
        EntityInfo primaryEntity,
        SqlDialect dialect,
        string lambdaParameterName,
        IReadOnlyDictionary<string, EntityInfo>? joinedEntities,
        IReadOnlyDictionary<string, string>? tableAliases,
        bool inBooleanContext,
        IReadOnlyDictionary<string, EntityInfo>? entityLookup,
        out List<ImplicitJoinInfo> implicitJoins)
    {
        var columnLookup = BuildColumnLookup(primaryEntity);
        var ctx = new BindContext(primaryEntity, dialect, lambdaParameterName, columnLookup, joinedEntities, tableAliases, entityLookup);
        var result = BindExpr(expr, ctx, inBooleanContext);
        implicitJoins = ctx.ImplicitJoins;
        return result;
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
        public int ImplicitJoinAliasCounter { get; set; }
        public bool HasJoins => JoinedEntities != null && JoinedEntities.Count > 0;
        public bool HasAliases => TableAliases != null && TableAliases.Count > 0;

        /// <summary>
        /// Accumulated implicit joins from One&lt;T&gt; navigation access.
        /// </summary>
        public List<ImplicitJoinInfo> ImplicitJoins { get; } = new();

        /// <summary>
        /// Deduplication key for implicit joins: (sourceAlias, fkColumnName, targetEntity) → alias.
        /// </summary>
        public Dictionary<(string sourceAlias, string fkColumn, string targetEntity), string> ImplicitJoinAliases { get; } = new();

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
                // AND/OR children are in boolean context (each side must evaluate to bool),
                // so bare boolean columns like u.IsActive get wrapped as "col = 1"/"col = TRUE".
                var childBoolCtx = bin.Operator == SqlBinaryOperator.And || bin.Operator == SqlBinaryOperator.Or;
                var left = BindExpr(bin.Left, ctx, childBoolCtx);
                var right = BindExpr(bin.Right, ctx, childBoolCtx);
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

            case NavigationAccessExpr navAccess:
                return BindNavigationAccess(navAccess, ctx, inBooleanContext);

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

        // Check for HasManyThrough skip-navigation first — a property can have both
        // a NavigationInfo (for entity property generation) and a ThroughNavigationInfo
        // (for junction-based subquery expansion). The ThroughNavigationInfo takes priority.
        ThroughNavigationInfo? throughNav = null;
        foreach (var tn in outerEntity.ThroughNavigations)
        {
            if (tn.PropertyName == sub.NavigationPropertyName)
            {
                throughNav = tn;
                break;
            }
        }

        if (throughNav != null)
        {
            // Find the junction entity's Many<T> navigation
            foreach (var n in outerEntity.Navigations)
            {
                if (n.PropertyName == throughNav.JunctionNavigationName)
                {
                    nav = n;
                    break;
                }
            }
        }
        else
        {
            foreach (var n in outerEntity.Navigations)
            {
                if (n.PropertyName == sub.NavigationPropertyName)
                {
                    nav = n;
                    break;
                }
            }
        }

        if (nav == null) return sub;

        // Look up target entity (the junction entity for through-navigations)
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

        // For HasManyThrough: the predicate binds against the *target* entity, not the junction.
        // We add an implicit join from junction → target inside the subquery.
        EntityInfo? throughTargetEntity = null;
        if (throughNav != null && ctx.EntityLookup.TryGetValue(throughNav.TargetEntityName, out var tte))
        {
            throughTargetEntity = tte;
        }

        // Bind predicate and/or selector if present
        SqlExpr? boundPredicate = sub.Predicate;
        SqlExpr? boundSelector = sub.Selector;
        BindContext? innerCtx = null;
        if ((boundPredicate != null || boundSelector != null) && sub.InnerParameterName != null)
        {
            // For through-navigations, bind predicate in the target entity context
            var predicateEntity = throughTargetEntity ?? targetEntity;
            var innerColumnLookup = BuildColumnLookup(predicateEntity);

            // Override: inner parameter columns should be qualified with the subquery alias
            // We create a custom table aliases map for the inner context
            var innerAliases = new Dictionary<string, string>(StringComparer.Ordinal);

            if (throughTargetEntity != null)
            {
                // For HasManyThrough: predicate columns resolve on the implicit join alias
                // We'll set the alias after creating the implicit join below
                innerAliases[sub.InnerParameterName] = alias; // placeholder until joinAlias is computed below; dictionary is shared by reference with BindContext
            }
            else
            {
                innerAliases[sub.InnerParameterName] = alias;
            }

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
                predicateEntity, ctx.Dialect, sub.InnerParameterName, innerColumnLookup,
                innerJoined, innerAliases, ctx.EntityLookup);
            innerCtx.SubqueryAliasCounter = ctx.SubqueryAliasCounter;

            // For HasManyThrough: add implicit join from junction to target
            if (throughTargetEntity != null)
            {
                // Find the One<T> navigation on the junction entity (targetEntity is the junction)
                SingleNavigationInfo? oneNav = null;
                foreach (var sn in targetEntity.SingleNavigations)
                {
                    if (sn.PropertyName == throughNav!.TargetNavigationName)
                    {
                        oneNav = sn;
                        break;
                    }
                }

                if (oneNav != null)
                {
                    // Resolve FK on junction → PK on target
                    string? junctionFkCol = null;
                    foreach (var col in targetEntity.Columns)
                    {
                        if (col.PropertyName == oneNav.ForeignKeyPropertyName)
                        {
                            junctionFkCol = col.ColumnName;
                            break;
                        }
                    }
                    if (junctionFkCol == null) junctionFkCol = oneNav.ForeignKeyPropertyName;

                    string? targetPkCol = null;
                    foreach (var col in throughTargetEntity.Columns)
                    {
                        if (col.Kind == Quarry.Shared.Migration.ColumnKind.PrimaryKey)
                        {
                            targetPkCol = col.ColumnName;
                            break;
                        }
                    }

                    if (targetPkCol != null)
                    {
                        var joinAlias = $"j{innerCtx.ImplicitJoinAliasCounter++}";
                        var joinKind = oneNav.IsNullableFk ? JoinClauseKind.Left : JoinClauseKind.Inner;
                        innerCtx.ImplicitJoins.Add(new ImplicitJoinInfo(
                            sourceAlias: alias,
                            fkColumnName: junctionFkCol,
                            fkColumnQuoted: QuoteIdentifier(junctionFkCol, ctx.Dialect),
                            targetTableName: throughTargetEntity.TableName,
                            targetTableQuoted: QuoteIdentifier(throughTargetEntity.TableName, ctx.Dialect),
                            targetSchemaQuoted: null,
                            targetAlias: joinAlias,
                            targetPkColumnQuoted: QuoteIdentifier(targetPkCol, ctx.Dialect),
                            joinKind: joinKind,
                            targetPkColumnName: targetPkCol));

                        // Override the predicate's table alias to the implicit join alias.
                        // innerAliases is shared by reference with innerCtx.TableAliases,
                        // so this mutation is visible without rebuilding the context.
                        innerAliases[sub.InnerParameterName] = joinAlias;
                    }
                }
            }

            if (boundPredicate != null)
                boundPredicate = BindExpr(boundPredicate, innerCtx, false);
            if (boundSelector != null)
                boundSelector = BindExpr(boundSelector, innerCtx, false);
            ctx.SubqueryAliasCounter = innerCtx.SubqueryAliasCounter;
        }

        // For HasManyThrough without predicate or selector: still create junction→target implicit join
        // so that Count()/Any()/All() operate against the target table, not the junction.
        List<ImplicitJoinInfo>? noPredicateThroughJoins = null;
        if (throughTargetEntity != null && innerCtx == null)
        {
            SingleNavigationInfo? oneNav = null;
            foreach (var sn in targetEntity.SingleNavigations)
            {
                if (sn.PropertyName == throughNav!.TargetNavigationName)
                {
                    oneNav = sn;
                    break;
                }
            }

            if (oneNav != null)
            {
                string? junctionFkCol = null;
                foreach (var col in targetEntity.Columns)
                {
                    if (col.PropertyName == oneNav.ForeignKeyPropertyName)
                    {
                        junctionFkCol = col.ColumnName;
                        break;
                    }
                }
                if (junctionFkCol == null) junctionFkCol = oneNav.ForeignKeyPropertyName;

                string? targetPkCol = null;
                foreach (var col in throughTargetEntity.Columns)
                {
                    if (col.Kind == Quarry.Shared.Migration.ColumnKind.PrimaryKey)
                    {
                        targetPkCol = col.ColumnName;
                        break;
                    }
                }

                if (targetPkCol != null)
                {
                    var joinAlias = "j0";
                    var joinKind = oneNav.IsNullableFk ? JoinClauseKind.Left : JoinClauseKind.Inner;
                    noPredicateThroughJoins = new List<ImplicitJoinInfo>
                    {
                        new ImplicitJoinInfo(
                            sourceAlias: alias,
                            fkColumnName: junctionFkCol,
                            fkColumnQuoted: QuoteIdentifier(junctionFkCol, ctx.Dialect),
                            targetTableName: throughTargetEntity.TableName,
                            targetTableQuoted: QuoteIdentifier(throughTargetEntity.TableName, ctx.Dialect),
                            targetSchemaQuoted: null,
                            targetAlias: joinAlias,
                            targetPkColumnQuoted: QuoteIdentifier(targetPkCol, ctx.Dialect),
                            joinKind: joinKind,
                            targetPkColumnName: targetPkCol)
                    };
                }
            }
        }

        var resolved = new SubqueryExpr(
            sub.OuterParameterName,
            sub.NavigationPropertyName,
            sub.SubqueryKind,
            boundPredicate,
            sub.InnerParameterName,
            innerTableQuoted,
            innerAliasQuoted,
            correlationSql,
            selector: boundSelector);

        // Propagate implicit joins from subquery predicate binding or no-predicate through-join
        if (innerCtx != null && innerCtx.ImplicitJoins.Count > 0)
        {
            resolved = resolved.WithImplicitJoins(innerCtx.ImplicitJoins);
        }
        else if (noPredicateThroughJoins != null)
        {
            resolved = resolved.WithImplicitJoins(noPredicateThroughJoins);
        }

        return resolved;
    }

    /// <summary>
    /// Resolves a NavigationAccessExpr by looking up One&lt;T&gt; navigation metadata,
    /// registering implicit joins, and producing a ResolvedColumnExpr.
    /// </summary>
    private static SqlExpr BindNavigationAccess(NavigationAccessExpr navAccess, BindContext ctx, bool inBooleanContext)
    {
        if (ctx.EntityLookup == null)
            return new SqlRawExpr($"/* unresolved navigation: no entity lookup */");

        // Determine the starting entity and alias
        EntityInfo? currentEntity = null;
        string? currentAlias = null;

        if (navAccess.SourceParameterName == ctx.LambdaParameterName)
        {
            currentEntity = ctx.PrimaryEntity;
            if (ctx.TableAliases != null && ctx.TableAliases.TryGetValue(navAccess.SourceParameterName, out var a))
                currentAlias = a;
            else
                // Always use "t0" for the primary table since the assembler will alias it as "t0"
                // when implicit joins are present (and navigation access always creates implicit joins).
                currentAlias = "t0";
        }
        else if (ctx.JoinedEntities != null && ctx.JoinedEntities.TryGetValue(navAccess.SourceParameterName, out var je))
        {
            currentEntity = je;
            if (ctx.TableAliases != null && ctx.TableAliases.TryGetValue(navAccess.SourceParameterName, out var ja))
                currentAlias = ja;
            else
                currentAlias = je.TableName;
        }

        if (currentEntity == null || currentAlias == null)
            return new SqlRawExpr($"/* unresolved navigation: unknown parameter '{navAccess.SourceParameterName}' */");

        // Process each navigation hop
        foreach (var hopName in navAccess.NavigationHops)
        {
            // Look up SingleNavigationInfo on the current entity
            SingleNavigationInfo? singleNav = null;
            foreach (var sn in currentEntity.SingleNavigations)
            {
                if (sn.PropertyName == hopName)
                {
                    singleNav = sn;
                    break;
                }
            }

            if (singleNav == null)
                return new SqlRawExpr($"/* unresolved navigation: '{hopName}' is not a One<T> navigation on '{currentEntity.EntityName}' */");

            // Look up the target entity
            if (!ctx.EntityLookup.TryGetValue(singleNav.TargetEntityName, out var targetEntity))
                return new SqlRawExpr($"/* unresolved navigation: target entity '{singleNav.TargetEntityName}' not found */");

            // Resolve FK column name
            string? fkColumnName = null;
            foreach (var col in currentEntity.Columns)
            {
                if (col.PropertyName == singleNav.ForeignKeyPropertyName)
                {
                    fkColumnName = col.ColumnName;
                    break;
                }
            }
            if (fkColumnName == null) fkColumnName = singleNav.ForeignKeyPropertyName;

            // Resolve PK column name on target entity
            string? pkColumnName = null;
            foreach (var col in targetEntity.Columns)
            {
                if (col.Kind == Quarry.Shared.Migration.ColumnKind.PrimaryKey)
                {
                    pkColumnName = col.ColumnName;
                    break;
                }
            }
            if (pkColumnName == null)
                return new SqlRawExpr($"/* unresolved navigation: no PK on '{targetEntity.EntityName}' */");

            // Deduplication check
            var dedupKey = (currentAlias, fkColumnName, singleNav.TargetEntityName);
            if (!ctx.ImplicitJoinAliases.TryGetValue(dedupKey, out var joinAlias))
            {
                // Allocate a new implicit join
                joinAlias = $"j{ctx.ImplicitJoinAliasCounter++}";
                ctx.ImplicitJoinAliases[dedupKey] = joinAlias;

                var joinKind = singleNav.IsNullableFk ? JoinClauseKind.Left : JoinClauseKind.Inner;
                ctx.ImplicitJoins.Add(new ImplicitJoinInfo(
                    sourceAlias: currentAlias,
                    fkColumnName: fkColumnName,
                    fkColumnQuoted: QuoteIdentifier(fkColumnName, ctx.Dialect),
                    targetTableName: targetEntity.TableName,
                    targetTableQuoted: QuoteIdentifier(targetEntity.TableName, ctx.Dialect),
                    targetSchemaQuoted: null,
                    targetAlias: joinAlias,
                    targetPkColumnQuoted: QuoteIdentifier(pkColumnName, ctx.Dialect),
                    joinKind: joinKind,
                    targetPkColumnName: pkColumnName));
            }

            // Advance to the target entity for the next hop
            currentEntity = targetEntity;
            currentAlias = joinAlias;
        }

        // Resolve the final property on the target entity
        var finalPropName = navAccess.FinalPropertyName;
        var finalColumnLookup = BuildColumnLookup(currentEntity);
        if (!finalColumnLookup.TryGetValue(finalPropName, out var finalColumn))
            return new SqlRawExpr($"/* unresolved column: '{finalPropName}' on '{currentEntity.EntityName}' */");

        var quotedColumn = QuoteIdentifier(finalColumn.ColumnName, ctx.Dialect);
        var tableQualifier = QuoteIdentifier(currentAlias, ctx.Dialect);
        var resolvedExpr = new ResolvedColumnExpr($"{tableQualifier}.{quotedColumn}");

        // Boolean context wrapping
        if (inBooleanContext && finalColumn.ClrType == "bool")
        {
            var boolValue = FormatBoolean(true, ctx.Dialect);
            return new ResolvedColumnExpr($"{tableQualifier}.{quotedColumn} = {boolValue}");
        }

        return resolvedExpr;
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
