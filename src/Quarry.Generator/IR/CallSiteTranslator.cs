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

        // UpdateSetAction: Action<T> lambdas can't be fully parsed into SqlExpr.
        // Pass through SetActionAssignments from discovery as a TranslatedClause.
        // However, assignments with column expressions (e.g., e.EndTime - e.StartTime + captured)
        // need binding and parameter extraction through the SqlExpr pipeline.
        if (raw.Kind == InterceptorKind.UpdateSetAction && raw.SetActionAssignments != null)
        {
            var hasColumnExprs = false;
            foreach (var a in raw.SetActionAssignments)
            {
                if (a.HasColumnExpression) { hasColumnExprs = true; break; }
            }

            if (hasColumnExprs)
            {
                return TranslateSetActionWithColumnExpressions(bound, raw);
            }

            var clause = new TranslatedClause(
                ClauseKind.Set,
                new LiteralExpr("1", "int"), // placeholder — not used for SetAction
                raw.SetActionParameters ?? (IReadOnlyList<Translation.ParameterInfo>)Array.Empty<Translation.ParameterInfo>(),
                isSuccess: true,
                setAssignments: raw.SetActionAssignments);
            return new TranslatedCallSite(bound, clause);
        }

        // Non-clause sites: Limit, Offset, Distinct, WithTimeout, ChainRoot,
        // execution terminals, insert/delete transitions, Trace
        if (raw.Expression == null || !IsClauseBearingKind(raw.Kind))
        {
            return new TranslatedCallSite(bound);
        }

        // Navigation joins (u => u.Orders) need special handling — synthesize ON clause
        // from entity navigation metadata instead of translating the expression.
        if (raw.IsNavigationJoin && raw.Kind is InterceptorKind.Join or InterceptorKind.LeftJoin or InterceptorKind.RightJoin)
        {
            return TranslateNavigationJoin(bound);
        }

        // Attempt clause translation via SqlExpr pipeline
        try
        {
            return TranslateClause(bound, registry, ct);
        }
        catch (Exception ex)
        {
            // Translation failed — produce a failed clause so QRY019 is emitted.
            TraceCapture.Log(raw.UniqueId, $"Translation failed: {ex.GetType().Name}: {ex.Message}");
            var clauseKind = raw.ClauseKind ?? ClauseKind.Where;
            var failedClause = new TranslatedClause(
                clauseKind,
                new LiteralExpr("1", "int"),
                Array.Empty<Translation.ParameterInfo>(),
                isSuccess: false,
                errorMessage: $"{clauseKind} clause translation failed: {ex.Message}");
            return new TranslatedCallSite(bound, failedClause);
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
            var failedClause = new TranslatedClause(
                raw.ClauseKind ?? ClauseKind.Where,
                new LiteralExpr("1", "int"),
                Array.Empty<Translation.ParameterInfo>(),
                isSuccess: false,
                errorMessage: $"{raw.ClauseKind ?? ClauseKind.Where} clause contains an expression that cannot be translated to SQL");
            return new TranslatedCallSite(bound, failedClause);
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
            var failedClause = new TranslatedClause(
                clauseKind,
                new LiteralExpr("1", "int"),
                Array.Empty<Translation.ParameterInfo>(),
                isSuccess: false,
                errorMessage: $"{clauseKind} clause could not resolve entity metadata for column binding");
            return new TranslatedCallSite(bound, failedClause);
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
        if (clauseKind == ClauseKind.Join && bound.JoinedEntities != null && bound.JoinedEntities.Count >= 2)
        {
            // Multi-entity join: resolve param-to-entity mapping for JOIN ON clause.
            // The new entity (bound.JoinedEntity) is the last one; use it to disambiguate.
            var joinMapping = ResolveJoinOnParameterMapping(expression, bound);
            if (joinMapping != null)
            {
                lambdaParamName = joinMapping.Value.PrimaryParam;
                joinedEntities = joinMapping.Value.JoinedEntities;
                tableAliases = joinMapping.Value.TableAliases;
            }
        }
        else if (clauseKind == ClauseKind.Join && bound.JoinedEntity != null)
        {
            // 2-entity join fallback (no JoinedEntities list)
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
            entityLookup: registry?.ByEntityName,
            implicitJoins: out var implicitJoins);

        // Step 2: Extract parameters
        int paramIndex = 0;
        var parameters = new List<ParameterInfo>();
        boundExpr = SqlExprClauseTranslator.ExtractParametersPublic(boundExpr, parameters, ref paramIndex);

        // Step 2a: Resolve unresolved collection element types from supplemental compilation.
        // During Stage 2 discovery, entity types from Pipeline A (RegisterSourceOutput) are not
        // in the semantic model, so captured collection variables may stay typed as "object".
        // CapturedVariableTypes (resolved via supplemental compilation in Stage 2.5) has the
        // correct types — use them to fix the element type before the column-metadata fallback.
        EnrichCollectionFromCapturedVarTypes(parameters, raw.CapturedVariableTypes);

        // Step 2b: Enrich collection parameters with element type from column metadata.
        // The InExpr operand is a ResolvedColumnExpr whose column type matches the element type.
        EnrichCollectionElementTypes(boundExpr, parameters, columnLookup);

        // Step 3: Render to SQL
        var sql = SqlExprRenderer.Render(boundExpr, bound.Dialect, useGenericParamFormat: true);

        if (string.IsNullOrEmpty(sql))
        {
            var failedClause = new TranslatedClause(
                clauseKind,
                new LiteralExpr("1", "int"),
                Array.Empty<Translation.ParameterInfo>(),
                isSuccess: false,
                errorMessage: $"{clauseKind} clause rendered to empty SQL");
            return new TranslatedCallSite(bound, failedClause);
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
            InterceptorKind.CrossJoin => JoinClauseKind.Cross,
            InterceptorKind.FullOuterJoin => JoinClauseKind.FullOuter,
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
            joinedSchemaName: joinedSchemaName,
            implicitJoins: implicitJoins.Count > 0 ? implicitJoins : null);

        return new TranslatedCallSite(bound, clause, keyTypeName, valueTypeName);
    }

    /// <summary>
    /// Translates SetAction assignments that contain column expressions (e.g., e.EndTime - e.StartTime + captured).
    /// Binds column references and extracts parameters for each computed assignment.
    /// Parameters are collected in assignment order to match the SQL rendering order.
    /// </summary>
    private static TranslatedCallSite TranslateSetActionWithColumnExpressions(
        BoundCallSite bound, RawCallSite raw)
    {
        var entityInfo = ReconstructEntityInfo(bound);
        if (entityInfo == null)
        {
            // Can't bind columns — fall back to passthrough
            var fallbackClause = new TranslatedClause(
                ClauseKind.Set, new LiteralExpr("1", "int"),
                raw.SetActionParameters ?? (IReadOnlyList<Translation.ParameterInfo>)Array.Empty<Translation.ParameterInfo>(),
                isSuccess: true,
                setAssignments: raw.SetActionAssignments!);
            return new TranslatedCallSite(bound, fallbackClause);
        }

        // Determine lambda parameter name from the first column expression
        string? lambdaParamName = null;
        foreach (var a in raw.SetActionAssignments!)
        {
            if (a.ColumnExpressionLambdaParam != null)
            {
                lambdaParamName = a.ColumnExpressionLambdaParam;
                break;
            }
        }
        lambdaParamName ??= "_";

        // Process assignments in order, creating new assignment objects with bound expressions.
        // Parameters are interleaved in assignment order to match SQL rendering.
        var allParameters = new List<Translation.ParameterInfo>();
        var boundAssignments = new List<Models.SetActionAssignment>();
        int paramIndex = 0;
        int discoveryParamIdx = 0; // index into raw.SetActionParameters

        foreach (var assignment in raw.SetActionAssignments)
        {
            if (assignment.HasColumnExpression)
            {
                // Re-parse the expression text into a syntax tree, then through SqlExprParser
                var exprSyntax = Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression(assignment.ColumnExpressionText!);
                var lambdaParams = new HashSet<string>(StringComparer.Ordinal) { lambdaParamName };
                var parsedExpr = SqlExprParser.ParseWithPathTracking(exprSyntax, lambdaParams);

                // Bind column references to resolved column names
                var boundExpr = SqlExprBinder.Bind(
                    parsedExpr,
                    entityInfo,
                    bound.Dialect,
                    lambdaParamName,
                    inBooleanContext: false);

                // Extract captured variables as parameters
                boundExpr = SqlExprClauseTranslator.ExtractParametersPublic(boundExpr, allParameters, ref paramIndex);

                // Create new assignment with bound expression stored on BoundValueExpression
                var boundAssignment = new Models.SetActionAssignment(
                    assignment.ColumnSql, assignment.ValueTypeName, assignment.CustomTypeMappingClass,
                    columnExpressionText: assignment.ColumnExpressionText,
                    columnExpressionLambdaParam: assignment.ColumnExpressionLambdaParam);
                boundAssignment.BoundValueExpression = boundExpr;
                boundAssignments.Add(boundAssignment);
            }
            else if (!assignment.IsInlined && raw.SetActionParameters != null
                     && discoveryParamIdx < raw.SetActionParameters.Count)
            {
                // Simple captured variable — take next discovery-time param
                var p = raw.SetActionParameters[discoveryParamIdx];
                var remapped = new Translation.ParameterInfo(
                    paramIndex, $"@p{paramIndex}", p.ClrType, p.ValueExpression,
                    isCaptured: p.IsCaptured, expressionPath: p.ExpressionPath);
                remapped.CapturedFieldName = p.CapturedFieldName;
                remapped.CapturedFieldType = p.CapturedFieldType;
                allParameters.Add(remapped);
                paramIndex++;
                discoveryParamIdx++;
                boundAssignments.Add(assignment);
            }
            else
            {
                // Inlined constant — pass through unchanged
                boundAssignments.Add(assignment);
            }
        }

        var clause = new TranslatedClause(
            ClauseKind.Set,
            new LiteralExpr("1", "int"),
            allParameters,
            isSuccess: true,
            setAssignments: boundAssignments);
        return new TranslatedCallSite(bound, clause);
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
            case NavigationAccessExpr nav:
                return nav.SourceParameterName;
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
            location: Microsoft.CodeAnalysis.Location.None,
            singleNavigations: entity.SingleNavigations,
            throughNavigations: entity.ThroughNavigations);
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
            location: Microsoft.CodeAnalysis.Location.None,
            singleNavigations: entity.SingleNavigations,
            throughNavigations: entity.ThroughNavigations);
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
    /// For JOIN ON clauses with 3+ entities, resolves parameter mapping using
    /// the ordered lambda parameter names stored during discovery.
    /// JoinedEntities contains PREVIOUS entities; JoinedEntity is the NEW entity.
    /// Lambda parameter position maps directly to entity index.
    /// </summary>
    private static JoinParameterMapping? ResolveJoinOnParameterMapping(SqlExpr expression, BoundCallSite bound)
    {
        // Use stored ordered lambda parameter names if available
        var orderedParams = bound.Raw.LambdaParameterNames;
        if (!orderedParams.HasValue || orderedParams.Value.Length < 2)
            return ResolveJoinParameterMapping(expression, bound);

        // Build the FULL entity list: previous entities + new entity
        var allEntities = new List<EntityRef>(bound.JoinedEntities!);
        if (bound.JoinedEntity != null)
            allEntities.Add(bound.JoinedEntity);

        var joinedEntitiesDict = new Dictionary<string, EntityInfo>(StringComparer.Ordinal);
        var tableAliasesDict = new Dictionary<string, string>(StringComparer.Ordinal);

        var paramNames = orderedParams.Value;
        for (int i = 0; i < paramNames.Length && i < allEntities.Count; i++)
        {
            tableAliasesDict[paramNames[i]] = $"t{i}";
            if (i > 0)
            {
                var entityInfo = ReconstructEntityInfoFromRef(allEntities[i]);
                if (entityInfo != null)
                    joinedEntitiesDict[paramNames[i]] = entityInfo;
            }
        }

        return new JoinParameterMapping(paramNames[0], joinedEntitiesDict, tableAliasesDict);
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
            InterceptorKind.CrossJoin => true,
            InterceptorKind.FullOuterJoin => true,
            _ => false
        };
    }

    /// <summary>
    /// Resolves unresolved collection element types using CapturedVariableTypes from the
    /// supplemental compilation (Stage 2.5). During Stage 2 discovery, entity types from
    /// Pipeline A are not in the semantic model, so captured collection variables may have
    /// ClrType = "object" and ExtractElementType returns null. CapturedVariableTypes has
    /// the correct resolved types from the supplemental compilation.
    /// </summary>
    private static void EnrichCollectionFromCapturedVarTypes(
        List<ParameterInfo> parameters,
        IReadOnlyDictionary<string, string>? capturedVarTypes)
    {
        if (capturedVarTypes == null) return;

        for (int i = 0; i < parameters.Count; i++)
        {
            var p = parameters[i];
            if (!p.IsCollection || p.CapturedFieldName == null)
                continue;

            // Only enrich when the primary path failed (null) or produced an unresolved
            // type parameter (e.g., "TSource" from Roslyn error recovery when entity types
            // aren't available at Stage 2). Skip when the primary path already resolved a
            // concrete type to avoid format differences (CapturedVariableTypes uses
            // FullyQualifiedFormat with "global::" prefixes).
            if (p.CollectionElementType != null && !IsUnresolvedTypeParameter(p.CollectionElementType))
                continue;

            if (capturedVarTypes.TryGetValue(p.CapturedFieldName, out var resolvedType))
            {
                var elementType = SqlExprClauseTranslator.ExtractElementType(resolvedType);
                if (elementType != null)
                    p.CollectionElementType = elementType;
            }
        }
    }

    /// <summary>
    /// Detects unresolved generic type parameters from Roslyn error recovery
    /// (e.g., "TSource", "TResult", "T"). These appear when the semantic model
    /// can't infer generic type arguments due to missing entity types at Stage 2.
    /// </summary>
    private static bool IsUnresolvedTypeParameter(string typeName)
    {
        // Resolved types contain '.', '?', '<', '[' or are known C# keywords
        if (typeName.IndexOfAny(new[] { '.', '?', '<', '[' }) >= 0)
            return false;

        // Known C# primitive/keyword type aliases are resolved
        switch (typeName)
        {
            case "bool": case "byte": case "sbyte": case "char":
            case "short": case "ushort": case "int": case "uint":
            case "long": case "ulong": case "float": case "double":
            case "decimal": case "string": case "object": case "nint": case "nuint":
                return false;
        }

        // Single-word identifiers starting with uppercase that aren't known types
        // are likely type parameters (T, TSource, TResult, TKey, etc.)
        return typeName.Length > 0 && char.IsUpper(typeName[0]);
    }

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
                    var strippedName = resolvedCol.QuotedColumnName.Trim('"', '`', '[', ']');
                    foreach (var kvp in columnLookup)
                    {
                        var col = kvp.Value;
                        if (col.ColumnName == strippedName || kvp.Key == strippedName)
                        {
                            elementType = col.FullClrType ?? col.ClrType;
                            // ColumnInfo stores nullable value types without the '?' suffix
                            // (e.g., Col<long?> → ClrType="long", IsNullable=true).
                            // Reconstruct the nullable type so the carrier field/cast uses
                            // IReadOnlyList<long?> instead of IReadOnlyList<long>.
                            if (col.IsNullable && col.IsValueType && !elementType.EndsWith("?"))
                                elementType += "?";
                            break;
                        }
                    }
                }
                // Apply element type to collection parameters that don't already have one.
                // Skip parameters where ExtractElementType already resolved the type from
                // the collection's own CLR type — that is more accurate than column inference.
                if (elementType != null)
                {
                    foreach (var val in inExpr.Values)
                    {
                        if (val is ParamSlotExpr param && param.IsCollection)
                        {
                            if (param.LocalIndex < parameters.Count && parameters[param.LocalIndex].IsCollection
                                && parameters[param.LocalIndex].CollectionElementType == null)
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

    /// <summary>
    /// Translates a navigation join (e.g., u => u.Orders) by synthesizing the ON clause
    /// from entity navigation metadata (FK relationship).
    /// </summary>
    private static TranslatedCallSite TranslateNavigationJoin(BoundCallSite bound)
    {
        var raw = bound.Raw;
        var navPropertyName = raw.Expression is ColumnRefExpr colRef ? colRef.PropertyName : null;
        if (navPropertyName == null)
            return new TranslatedCallSite(bound);

        // Find the navigation in the entity metadata
        NavigationInfo? nav = null;
        foreach (var n in bound.Entity.Navigations)
        {
            if (n.PropertyName == navPropertyName)
            {
                nav = n;
                break;
            }
        }
        if (nav == null)
            return new TranslatedCallSite(bound);

        // Find the FK column name on the related entity and the PK column on the parent entity
        // Navigation FK references a column on the related entity pointing to the parent's PK.
        // The FK property name on the related entity (e.g., "UserId") maps to a column.
        var fkPropertyName = nav.ForeignKeyPropertyName;

        // Find the matching PK column on the parent entity (same property name as FK)
        string? pkColumnName = null;
        foreach (var col in bound.Entity.Columns)
        {
            if (col.PropertyName == fkPropertyName)
            {
                pkColumnName = col.ColumnName;
                break;
            }
        }
        if (pkColumnName == null)
            return new TranslatedCallSite(bound);

        // Find the FK column name on the joined entity
        string? fkColumnName = null;
        if (bound.JoinedEntity != null)
        {
            foreach (var col in bound.JoinedEntity.Columns)
            {
                if (col.PropertyName == fkPropertyName)
                {
                    fkColumnName = col.ColumnName;
                    break;
                }
            }
        }
        if (fkColumnName == null)
            return new TranslatedCallSite(bound);

        // Build the ON clause: "t0"."PkCol" = "t1"."FkCol"
        // Use the same format as SqlExprBinder: QuotedColumnName includes the table qualifier prefix
        var leftQualifier = SqlFormatting.QuoteIdentifier(bound.Dialect, "t0");
        var rightQualifier = SqlFormatting.QuoteIdentifier(bound.Dialect, "t1");
        var leftCol = new ResolvedColumnExpr(
            $"{leftQualifier}.{SqlFormatting.QuoteIdentifier(bound.Dialect, pkColumnName)}", leftQualifier);
        var rightCol = new ResolvedColumnExpr(
            $"{rightQualifier}.{SqlFormatting.QuoteIdentifier(bound.Dialect, fkColumnName)}", rightQualifier);
        var onExpr = new BinaryOpExpr(leftCol, SqlBinaryOperator.Equal, rightCol);

        // Get the joined table name
        var joinedTableName = bound.JoinedEntity?.TableName;
        var joinedSchemaName = bound.JoinedEntity?.SchemaName;

        var joinKind = raw.Kind switch
        {
            InterceptorKind.LeftJoin => JoinClauseKind.Left,
            InterceptorKind.RightJoin => JoinClauseKind.Right,
            InterceptorKind.FullOuterJoin => JoinClauseKind.FullOuter,
            _ => JoinClauseKind.Inner
        };

        var clause = new TranslatedClause(
            kind: ClauseKind.Join,
            resolvedExpression: onExpr,
            parameters: Array.Empty<ParameterInfo>(),
            joinKind: joinKind,
            joinedTableName: joinedTableName,
            joinedSchemaName: joinedSchemaName,
            tableAlias: "t1");

        return new TranslatedCallSite(bound, clause);
    }
}
