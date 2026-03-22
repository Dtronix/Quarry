using System;
using System.Collections.Generic;
using System.Threading;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;
using Quarry.Generators.Translation;

namespace Quarry.Generators.IR;

/// <summary>
/// Translates a bound call site into a fully-translated call site
/// with resolved SQL expression, parameters, and type metadata.
/// Wraps SqlExprBinder/SqlExprRenderer pipeline; handles parameter extraction,
/// KeyTypeName/ValueTypeName resolution, and clause-specific enrichment.
/// </summary>
internal static class CallSiteTranslator
{
    /// <summary>
    /// Translates a bound call site into a fully-translated call site.
    /// For clause-bearing sites, runs the SqlExpr bind → extract parameters → render pipeline.
    /// For non-clause sites, produces a TranslatedCallSite with null Clause.
    /// </summary>
    public static TranslatedCallSite Translate(
        BoundCallSite bound,
        CancellationToken ct)
    {
        return Translate(bound, null, ct);
    }

    /// <summary>
    /// Translates a bound call site with access to the entity registry for subquery resolution.
    /// </summary>
    public static TranslatedCallSite Translate(
        BoundCallSite bound,
        EntityRegistry? registry,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var raw = bound.Raw;

        // Non-clause sites: Limit, Offset, Distinct, WithTimeout, ChainRoot,
        // execution terminals, insert/delete transitions
        if (raw.Expression == null || !IsClauseBearingKind(raw.Kind))
        {
            return new TranslatedCallSite(bound);
        }

        // Attempt clause translation via SqlExpr pipeline
        try
        {
            return TranslateClause(bound, registry, ct);
        }
        catch
        {
            // Translation failed — produce a TranslatedCallSite with null Clause.
            // QRY019 diagnostic will be reported in the collected stage.
            return new TranslatedCallSite(bound);
        }
    }

    private static TranslatedCallSite TranslateClause(BoundCallSite bound, EntityRegistry? registry, CancellationToken ct)
    {
        var raw = bound.Raw;
        var expression = raw.Expression!;
        var clauseKind = raw.ClauseKind ?? ClauseKind.Where;

        // Check for unsupported SqlRawExpr nodes before attempting translation
        if (ContainsUnsupportedRawExpr(expression))
        {
            return new TranslatedCallSite(bound);
        }

        // Build column lookup for key/value type resolution
        var columnLookup = new Dictionary<string, ColumnInfo>(StringComparer.Ordinal);
        foreach (var col in bound.Entity.Columns)
            columnLookup[col.PropertyName] = col;

        // Step 1: Bind column references
        var inBooleanContext = clauseKind == ClauseKind.Where || clauseKind == ClauseKind.Having;

        // Reconstruct EntityInfo for the binder (it needs EntityInfo, not EntityRef)
        // For now, use the entity columns from EntityRef to create a minimal EntityInfo
        var entityInfo = ReconstructEntityInfo(bound);
        if (entityInfo == null)
        {
            return new TranslatedCallSite(bound);
        }

        // Determine lambda parameter name from the expression tree.
        // If the expression has no column references (e.g., u => true), use a placeholder
        // since the binder won't need to resolve any columns.
        var lambdaParamName = ExtractLambdaParameterName(expression);
        if (lambdaParamName == null)
        {
            // Expressions without column references (literals, constants) don't need
            // column resolution. Use a placeholder name for the binder.
            lambdaParamName = "_";
        }

        // For Join clauses, build joined entity lookup so the binder can resolve
        // columns from both the primary and joined entities (e.g., (u, o) => u.Id == o.Id)
        Dictionary<string, EntityInfo>? joinedEntities = null;
        Dictionary<string, string>? tableAliases = null;
        if (clauseKind == ClauseKind.Join && bound.JoinedEntity != null)
        {
            var joinedEntityInfo = ReconstructEntityInfoFromRef(bound.JoinedEntity);
            if (joinedEntityInfo != null)
            {
                var joinParamName = ExtractSecondLambdaParameterName(expression);
                if (joinParamName != null)
                {
                    joinedEntities = new Dictionary<string, EntityInfo>(StringComparer.Ordinal)
                    {
                        [joinParamName] = joinedEntityInfo
                    };
                    // Assign table aliases: t0 for primary, t1 for joined
                    tableAliases = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [lambdaParamName] = "t0",
                        [joinParamName] = "t1"
                    };
                }
            }
        }
        // For non-join clauses in a join context (WHERE/SELECT/OrderBy after JOIN),
        // set up joined entity info by matching ColumnRefExpr parameter names to entities.
        else if (clauseKind != ClauseKind.Join && bound.JoinedEntities != null && bound.JoinedEntities.Count >= 2)
        {
            var paramEntityMapping = ResolveJoinParameterMapping(expression, bound);
            if (paramEntityMapping != null)
            {
                lambdaParamName = paramEntityMapping.Value.PrimaryParam;
                joinedEntities = paramEntityMapping.Value.JoinedEntities;
                tableAliases = paramEntityMapping.Value.TableAliases;
            }
        }

        var boundExpr = SqlExprBinder.Bind(
            expression,
            entityInfo,
            bound.Dialect,
            lambdaParamName,
            joinedEntities: joinedEntities,
            tableAliases: tableAliases,
            inBooleanContext: inBooleanContext,
            entityLookup: registry?.ByEntityName);

        // Step 2: Extract parameters
        int paramIndex = 0;
        var parameters = new List<ParameterInfo>();
        boundExpr = SqlExprClauseTranslator.ExtractParametersPublic(boundExpr, parameters, ref paramIndex);

        // Step 2b: Enrich collection parameters with element type from column metadata.
        // The InExpr operand is a ResolvedColumnExpr whose column type matches the element type.
        EnrichCollectionElementTypes(boundExpr, parameters, columnLookup);

        // Step 3: Render to SQL
        var sql = SqlExprRenderer.Render(boundExpr, bound.Dialect, useGenericParamFormat: true);

        if (string.IsNullOrEmpty(sql))
        {
            return new TranslatedCallSite(bound);
        }

        // Resolve key type for OrderBy/ThenBy/GroupBy
        string? keyTypeName = null;
        if (clauseKind == ClauseKind.OrderBy || clauseKind == ClauseKind.GroupBy)
        {
            keyTypeName = ResolveKeyType(expression, columnLookup);
        }

        // Resolve value type for Set
        string? valueTypeName = null;
        if (clauseKind == ClauseKind.Set)
        {
            valueTypeName = ResolveKeyType(expression, columnLookup);
        }

        // Build the translated clause
        var joinKind = raw.Kind switch
        {
            InterceptorKind.Join => (JoinClauseKind?)JoinClauseKind.Inner,
            InterceptorKind.LeftJoin => JoinClauseKind.Left,
            InterceptorKind.RightJoin => JoinClauseKind.Right,
            _ => null
        };

        string? joinedTableName = null;
        string? joinedSchemaName = null;
        if (joinKind != null && bound.JoinedEntity != null)
        {
            joinedTableName = bound.JoinedEntity.TableName;
            joinedSchemaName = bound.JoinedEntity.SchemaName;
        }

        var clause = new TranslatedClause(
            kind: clauseKind,
            resolvedExpression: boundExpr,
            parameters: parameters,
            isSuccess: true,
            isDescending: raw.IsDescending,
            joinKind: joinKind,
            joinedTableName: joinedTableName,
            joinedSchemaName: joinedSchemaName);

        return new TranslatedCallSite(bound, clause, keyTypeName, valueTypeName);
    }

    /// <summary>
    /// Extracts the first lambda parameter name from the SqlExpr tree.
    /// Walks ColumnRefExpr nodes to find the parameter name.
    /// </summary>
    private static string? ExtractLambdaParameterName(SqlExpr expr)
    {
        switch (expr)
        {
            case ColumnRefExpr colRef:
                return colRef.ParameterName;
            case BinaryOpExpr bin:
                return ExtractLambdaParameterName(bin.Left) ?? ExtractLambdaParameterName(bin.Right);
            case UnaryOpExpr unary:
                return ExtractLambdaParameterName(unary.Operand);
            case FunctionCallExpr func:
                foreach (var arg in func.Arguments)
                {
                    var name = ExtractLambdaParameterName(arg);
                    if (name != null) return name;
                }
                return null;
            case InExpr inExpr:
                return ExtractLambdaParameterName(inExpr.Operand);
            case IsNullCheckExpr isNull:
                return ExtractLambdaParameterName(isNull.Operand);
            case LikeExpr like:
                return ExtractLambdaParameterName(like.Operand);
            case SubqueryExpr sub:
                return sub.OuterParameterName;
            case RawCallExpr rawCall:
                foreach (var arg in rawCall.Arguments)
                {
                    var name = ExtractLambdaParameterName(arg);
                    if (name != null) return name;
                }
                return null;
            default:
                return null;
        }
    }

    /// <summary>
    /// Extracts the second lambda parameter name from a binary expression.
    /// For join conditions like (u, o) => u.Id == o.Id, returns "o".
    /// </summary>
    private static string? ExtractSecondLambdaParameterName(SqlExpr expr)
    {
        var first = ExtractLambdaParameterName(expr);
        if (first == null) return null;
        return ExtractOtherParameterName(expr, first);
    }

    private static string? ExtractOtherParameterName(SqlExpr expr, string exclude)
    {
        switch (expr)
        {
            case ColumnRefExpr colRef:
                return colRef.ParameterName != exclude ? colRef.ParameterName : null;
            case BinaryOpExpr bin:
                return ExtractOtherParameterName(bin.Left, exclude) ?? ExtractOtherParameterName(bin.Right, exclude);
            case UnaryOpExpr unary:
                return ExtractOtherParameterName(unary.Operand, exclude);
            case FunctionCallExpr func:
                foreach (var arg in func.Arguments)
                {
                    var name = ExtractOtherParameterName(arg, exclude);
                    if (name != null) return name;
                }
                return null;
            default:
                return null;
        }
    }

    /// <summary>
    /// Reconstructs an EntityInfo from an EntityRef (for joined entities).
    /// </summary>
    private static EntityInfo? ReconstructEntityInfoFromRef(EntityRef entity)
    {
        if (entity.Columns.Count == 0 && entity.TableName == "")
            return null;

        return new EntityInfo(
            entityName: entity.EntityName,
            schemaClassName: "",
            schemaNamespace: entity.SchemaNamespace ?? "",
            tableName: entity.TableName,
            namingStyle: Quarry.Shared.Migration.NamingStyleKind.SnakeCase,
            columns: entity.Columns,
            navigations: entity.Navigations,
            indexes: Array.Empty<IndexInfo>(),
            location: Microsoft.CodeAnalysis.Location.None);
    }

    /// <summary>
    /// Reconstructs an EntityInfo from the BoundCallSite's EntityRef.
    /// The SqlExprBinder requires EntityInfo, so we bridge from EntityRef.
    /// </summary>
    private static EntityInfo? ReconstructEntityInfo(BoundCallSite bound)
    {
        var entity = bound.Entity;
        if (entity.Columns.Count == 0 && entity.TableName == "")
            return null; // Empty entity ref — unresolved

        return new EntityInfo(
            entityName: entity.EntityName,
            schemaClassName: "",
            schemaNamespace: entity.SchemaNamespace ?? "",
            tableName: entity.TableName,
            namingStyle: Quarry.Shared.Migration.NamingStyleKind.SnakeCase,
            columns: entity.Columns,
            navigations: entity.Navigations,
            indexes: Array.Empty<IndexInfo>(),
            location: Microsoft.CodeAnalysis.Location.None);
    }

    private readonly struct JoinParameterMapping
    {
        public readonly string PrimaryParam;
        public readonly Dictionary<string, EntityInfo> JoinedEntities;
        public readonly Dictionary<string, string> TableAliases;

        public JoinParameterMapping(string primaryParam,
            Dictionary<string, EntityInfo> joinedEntities,
            Dictionary<string, string> tableAliases)
        {
            PrimaryParam = primaryParam;
            JoinedEntities = joinedEntities;
            TableAliases = tableAliases;
        }
    }

    /// <summary>
    /// For non-join clauses in a join context, resolves which lambda parameter names
    /// map to which entities by checking ColumnRefExpr property names against entity columns.
    /// </summary>
    private static JoinParameterMapping? ResolveJoinParameterMapping(SqlExpr expression, BoundCallSite bound)
    {
        var allParams = new HashSet<string>(StringComparer.Ordinal);
        CollectParameterNames(expression, allParams);
        if (allParams.Count == 0)
            return null;

        // Build column lookup for each entity in the join
        // JoinedEntities[0] = primary entity, [1] = first joined, etc.
        var entityColumnSets = new List<HashSet<string>>(bound.JoinedEntities!.Count);
        for (int i = 0; i < bound.JoinedEntities.Count; i++)
        {
            var colSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var col in bound.JoinedEntities[i].Columns)
                colSet.Add(col.PropertyName);
            entityColumnSets.Add(colSet);
        }

        // Map each parameter name to its entity index
        var paramToEntityIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var paramName in allParams)
        {
            var referencedProps = new HashSet<string>(StringComparer.Ordinal);
            CollectPropertyNamesForParam(expression, paramName, referencedProps);

            for (int i = 0; i < entityColumnSets.Count; i++)
            {
                bool matches = false;
                foreach (var prop in referencedProps)
                {
                    if (entityColumnSets[i].Contains(prop))
                    {
                        matches = true;
                        break;
                    }
                }
                if (matches)
                {
                    paramToEntityIndex[paramName] = i;
                    break;
                }
            }
        }

        // Determine primary param (maps to entity index 0)
        string? primaryParam = null;
        foreach (var kvp in paramToEntityIndex)
        {
            if (kvp.Value == 0)
            {
                primaryParam = kvp.Key;
                break;
            }
        }

        // If no param maps to primary entity, pick the first unused param name or use placeholder
        if (primaryParam == null)
        {
            // The primary entity's param isn't in the expression (e.g., (u, o) => o.Total > 50)
            // Use a placeholder that won't conflict
            primaryParam = "_qprimary";
        }

        // Build joinedEntities and tableAliases
        var joinedEntities = new Dictionary<string, EntityInfo>(StringComparer.Ordinal);
        var tableAliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [primaryParam] = "t0"
        };

        foreach (var kvp in paramToEntityIndex)
        {
            if (kvp.Value > 0)
            {
                var entityRef = bound.JoinedEntities[kvp.Value];
                var entityInfo = ReconstructEntityInfoFromRef(entityRef);
                if (entityInfo != null)
                {
                    joinedEntities[kvp.Key] = entityInfo;
                    tableAliases[kvp.Key] = "t" + kvp.Value;
                }
            }
        }

        // Even if no joined entity columns are used in this clause, we still need
        // the table aliases so the primary entity columns get the "t0" prefix
        return new JoinParameterMapping(primaryParam, joinedEntities, tableAliases);
    }

    /// <summary>
    /// Collects all unique lambda parameter names from ColumnRefExpr nodes in the tree.
    /// </summary>
    private static void CollectParameterNames(SqlExpr expr, HashSet<string> names)
    {
        switch (expr)
        {
            case ColumnRefExpr colRef:
                names.Add(colRef.ParameterName);
                break;
            case BinaryOpExpr bin:
                CollectParameterNames(bin.Left, names);
                CollectParameterNames(bin.Right, names);
                break;
            case UnaryOpExpr unary:
                CollectParameterNames(unary.Operand, names);
                break;
            case FunctionCallExpr func:
                foreach (var arg in func.Arguments)
                    CollectParameterNames(arg, names);
                break;
            case InExpr inExpr:
                CollectParameterNames(inExpr.Operand, names);
                foreach (var val in inExpr.Values)
                    CollectParameterNames(val, names);
                break;
            case IsNullCheckExpr isNull:
                CollectParameterNames(isNull.Operand, names);
                break;
            case LikeExpr like:
                CollectParameterNames(like.Operand, names);
                CollectParameterNames(like.Pattern, names);
                break;
            case ExprListExpr list:
                foreach (var e in list.Expressions)
                    CollectParameterNames(e, names);
                break;
            case SubqueryExpr sub:
                if (sub.Predicate != null)
                    CollectParameterNames(sub.Predicate, names);
                break;
        }
    }

    /// <summary>
    /// Collects all property names referenced by ColumnRefExpr nodes for a specific parameter name.
    /// </summary>
    private static void CollectPropertyNamesForParam(SqlExpr expr, string paramName, HashSet<string> props)
    {
        switch (expr)
        {
            case ColumnRefExpr colRef:
                if (colRef.ParameterName == paramName)
                    props.Add(colRef.PropertyName);
                break;
            case BinaryOpExpr bin:
                CollectPropertyNamesForParam(bin.Left, paramName, props);
                CollectPropertyNamesForParam(bin.Right, paramName, props);
                break;
            case UnaryOpExpr unary:
                CollectPropertyNamesForParam(unary.Operand, paramName, props);
                break;
            case FunctionCallExpr func:
                foreach (var arg in func.Arguments)
                    CollectPropertyNamesForParam(arg, paramName, props);
                break;
            case InExpr inExpr:
                CollectPropertyNamesForParam(inExpr.Operand, paramName, props);
                foreach (var val in inExpr.Values)
                    CollectPropertyNamesForParam(val, paramName, props);
                break;
            case IsNullCheckExpr isNull:
                CollectPropertyNamesForParam(isNull.Operand, paramName, props);
                break;
            case LikeExpr like:
                CollectPropertyNamesForParam(like.Operand, paramName, props);
                CollectPropertyNamesForParam(like.Pattern, paramName, props);
                break;
            case ExprListExpr list:
                foreach (var e in list.Expressions)
                    CollectPropertyNamesForParam(e, paramName, props);
                break;
            case SubqueryExpr sub:
                if (sub.Predicate != null)
                    CollectPropertyNamesForParam(sub.Predicate, paramName, props);
                break;
        }
    }

    /// <summary>
    /// Resolves the CLR type of a key/value expression from column metadata.
    /// </summary>
    private static string? ResolveKeyType(SqlExpr expr, Dictionary<string, ColumnInfo> columnLookup)
    {
        if (expr is ColumnRefExpr colRef)
        {
            if (columnLookup.TryGetValue(colRef.PropertyName, out var column))
                return column.FullClrType;
        }
        return null;
    }

    /// <summary>
    /// Checks if an InterceptorKind represents a clause-bearing method.
    /// </summary>
    private static bool IsClauseBearingKind(InterceptorKind kind)
    {
        return kind switch
        {
            InterceptorKind.Where => true,
            InterceptorKind.DeleteWhere => true,
            InterceptorKind.UpdateWhere => true,
            InterceptorKind.OrderBy => true,
            InterceptorKind.ThenBy => true,
            InterceptorKind.GroupBy => true,
            InterceptorKind.Having => true,
            InterceptorKind.Set => true,
            InterceptorKind.UpdateSet => true,
            InterceptorKind.UpdateSetAction => true,
            InterceptorKind.Join => true,
            InterceptorKind.LeftJoin => true,
            InterceptorKind.RightJoin => true,
            _ => false
        };
    }

    /// <summary>
    /// Checks whether the SqlExpr tree contains unsupported SqlRawExpr nodes.
    /// </summary>
    /// <summary>
    /// Enriches collection parameters' element types by looking up the InExpr operand column type.
    /// When collection.Contains(column) produces InExpr(ResolvedColumn, [ParamSlot(isCollection)]),
    /// the element type equals the column's CLR type.
    /// </summary>
    private static void EnrichCollectionElementTypes(
        SqlExpr expr,
        List<ParameterInfo> parameters,
        Dictionary<string, ColumnInfo> columnLookup)
    {
        // Find collection parameters that need element type resolution
        var collectionParams = new List<int>();
        for (int i = 0; i < parameters.Count; i++)
            if (parameters[i].IsCollection && parameters[i].CollectionElementType == null)
                collectionParams.Add(i);

        if (collectionParams.Count == 0) return;

        // Walk the expression tree to find InExpr nodes with collection ParamSlotExpr values
        FindCollectionElementTypes(expr, parameters, columnLookup);
    }

    private static void FindCollectionElementTypes(
        SqlExpr expr,
        List<ParameterInfo> parameters,
        Dictionary<string, ColumnInfo> columnLookup)
    {
        switch (expr)
        {
            case InExpr inExpr:
            {
                // If operand is a resolved column, use its type as the element type
                string? elementType = null;
                if (inExpr.Operand is ResolvedColumnExpr resolvedCol)
                {
                    // Look up the column's CLR type by matching the quoted name
                    foreach (var kvp in columnLookup)
                    {
                        var col = kvp.Value;
                        if (col.ColumnName == resolvedCol.QuotedColumnName.Trim('"', '`', '[', ']') ||
                            col.PropertyName == kvp.Key)
                        {
                            elementType = col.FullClrType ?? col.ClrType;
                            break;
                        }
                    }
                }
                // Apply element type to collection parameters in the values list
                if (elementType != null)
                {
                    foreach (var val in inExpr.Values)
                    {
                        if (val is ParamSlotExpr param && param.IsCollection)
                        {
                            if (param.LocalIndex < parameters.Count && parameters[param.LocalIndex].IsCollection)
                            {
                                parameters[param.LocalIndex].CollectionElementType = elementType;
                            }
                        }
                    }
                }
                break;
            }
            case BinaryOpExpr bin:
                FindCollectionElementTypes(bin.Left, parameters, columnLookup);
                FindCollectionElementTypes(bin.Right, parameters, columnLookup);
                break;
            case UnaryOpExpr unary:
                FindCollectionElementTypes(unary.Operand, parameters, columnLookup);
                break;
        }
    }

    private static bool ContainsUnsupportedRawExpr(SqlExpr expr)
    {
        switch (expr)
        {
            case SqlRawExpr raw:
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
            case SubqueryExpr sub:
                if (sub.Predicate != null)
                    return ContainsUnsupportedRawExpr(sub.Predicate);
                return false;
            default:
                return false;
        }
    }
}
