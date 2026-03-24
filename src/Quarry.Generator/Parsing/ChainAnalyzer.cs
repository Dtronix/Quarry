using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Shared.Migration;
using Quarry.Generators.Sql;
using Quarry.Generators.Translation;

namespace Quarry.Generators.Parsing;

/// <summary>
/// Analyzes query chains from TranslatedCallSite data to produce QueryPlan instances.
/// No SemanticModel or syntax tree walking — all metadata comes from RawCallSite fields
/// populated during Stage 2 discovery.
/// </summary>
internal static class ChainAnalyzer
{
    /// <summary>
    /// Test capture hook: when non-null, Analyze() appends results here.
    /// Set from test code before running the generator, read after.
    /// </summary>
    [ThreadStatic]
    internal static List<AnalyzedChain>? TestCapturedChains;

    /// <summary>
    /// Maximum number of conditional bits before downgrading from tier 1 to tier 2.
    /// 4 bits = up to 16 dispatch variants.
    /// </summary>
    private const int MaxTier1Bits = 4;

    /// <summary>
    /// Maximum nesting depth of if-blocks before abandoning analysis.
    /// </summary>
    private const int MaxIfNestingDepth = 2;

    /// <summary>
    /// Analyzes all chains from the collected translated call sites.
    /// Groups by ChainId, identifies execution terminals, classifies tiers,
    /// and builds QueryPlan instances.
    /// </summary>
    public static IReadOnlyList<AnalyzedChain> Analyze(
        ImmutableArray<TranslatedCallSite> sites,
        EntityRegistry registry,
        CancellationToken ct)
    {
        // Group sites by ChainId
        var chains = new Dictionary<string, List<TranslatedCallSite>>(StringComparer.Ordinal);
        var unchained = new List<TranslatedCallSite>();

        foreach (var site in sites)
        {
            ct.ThrowIfCancellationRequested();
            var chainId = site.Bound.Raw.ChainId;
            if (chainId != null)
            {
                if (!chains.TryGetValue(chainId, out var list))
                {
                    list = new List<TranslatedCallSite>();
                    chains[chainId] = list;
                }
                list.Add(site);
            }
            else
            {
                unchained.Add(site);
            }
        }

        var results = new List<AnalyzedChain>();

        foreach (var kvp in chains)
        {
            ct.ThrowIfCancellationRequested();
            var chainSites = kvp.Value;

            try
            {
                var analyzed = AnalyzeChainGroup(chainSites, registry, ct);
                if (analyzed != null)
                    results.Add(analyzed);
            }
            catch
            {
            }
        }

        TestCapturedChains?.AddRange(results);

        return results;
    }

    /// <summary>
    /// Analyzes a single chain group (all sites sharing the same ChainId).
    /// </summary>
    private static AnalyzedChain? AnalyzeChainGroup(
        List<TranslatedCallSite> chainSites,
        EntityRegistry registry,
        CancellationToken ct)
    {
        // Find the execution terminal, detect .Trace()/.Prepare(), and collect clause sites
        TranslatedCallSite? executionSite = null;
        TranslatedCallSite? prepareSite = null;
        var clauseSites = new List<TranslatedCallSite>();
        var preparedTerminals = new List<TranslatedCallSite>();
        bool isTraced = false;
        int executionCount = 0;

        foreach (var site in chainSites)
        {
            if (site.Bound.Raw.Kind == InterceptorKind.Prepare)
            {
                prepareSite = site;
            }
            else if (site.Bound.Raw.IsPreparedTerminal)
            {
                // Terminal called on a PreparedQuery variable
                preparedTerminals.Add(site);
            }
            else if (IsExecutionKind(site.Bound.Raw.Kind))
            {
                executionSite = site;
                executionCount++;
            }
            else if (site.Bound.Raw.Kind == InterceptorKind.Trace)
            {
                isTraced = true;
                // Trace sites are excluded from clause processing
            }
            else
            {
                clauseSites.Add(site);
            }
        }

        // Handle .Prepare() chains
        if (prepareSite != null)
        {
            if (preparedTerminals.Count == 0)
            {
                // QRY036: no terminals on PreparedQuery — dead code
                // Still return null so the chain doesn't get an interceptor
                return null;
            }

            if (preparedTerminals.Count == 1)
            {
                // Single-terminal collapse: treat as if .Prepare() didn't exist
                executionSite = preparedTerminals[0];
                executionCount = 1;
                preparedTerminals.Clear();
                prepareSite = null;
                // Fall through to normal single-terminal processing
            }
            else
            {
                // Multi-terminal: use the first terminal as the execution site for plan building,
                // but record all terminals for the emitter
                executionSite = preparedTerminals[0];
                executionCount = 1;
                // Fall through to normal processing — PreparedTerminals will be set on AnalyzedChain
            }
        }

        if (executionSite == null)
            return null;

        // Detect forked chains (multiple execution terminals sharing one ChainId)
        // Note: prepared multi-terminal chains are NOT forks — they're intentional
        if (executionCount > 1)
        {
            // Extract variable name from the ChainId (format: "filepath:offset:varName")
            var chainId = executionSite.Bound.Raw.ChainId;
            string? varName = null;
            if (chainId != null)
            {
                var lastColon = chainId.LastIndexOf(':');
                if (lastColon >= 0)
                    varName = chainId.Substring(lastColon + 1);
            }
            return MakeRuntimeBuildChain(executionSite, clauseSites,
                "Forked query chain", registry, isTraced, forkedVariableName: varName);
        }

        // Sort clause sites by source location for deterministic ordering
        clauseSites.Sort((a, b) =>
        {
            var cmp = a.Bound.Raw.Line.CompareTo(b.Bound.Raw.Line);
            if (cmp != 0) return cmp;
            return a.Bound.Raw.Column.CompareTo(b.Bound.Raw.Column);
        });

        // Check for disqualifiers from RawCallSite flags
        var disqualifyReason = CheckDisqualifiers(chainSites);
        if (disqualifyReason != null)
        {
            return MakeRuntimeBuildChain(executionSite, clauseSites, disqualifyReason, registry, isTraced);
        }

        ct.ThrowIfCancellationRequested();

        // For navigation join chains, synthetically discovered post-join sites may not have
        // JoinedEntityTypeNames (Roslyn couldn't resolve the post-join call's receiver type).
        // Build the names from the Join clause site's entity + joined entity and propagate.
        IReadOnlyList<string>? resolvedJoinNames = null;
        IReadOnlyList<EntityRef>? resolvedJoinEntities = null;
        foreach (var site in clauseSites)
        {
            if (site.Bound.Raw.Kind is InterceptorKind.Join or InterceptorKind.LeftJoin or InterceptorKind.RightJoin
                && site.Bound.JoinedEntity != null)
            {
                // First check if the Join already has JoinedEntityTypeNames from discovery
                if (site.Bound.JoinedEntityTypeNames != null && site.Bound.JoinedEntityTypeNames.Count >= 2)
                {
                    resolvedJoinNames = site.Bound.JoinedEntityTypeNames;
                    resolvedJoinEntities = site.Bound.JoinedEntities;
                }
                else
                {
                    // Build from entity + joinedEntity (navigation join case)
                    resolvedJoinNames = new List<string> { site.Bound.Raw.EntityTypeName, site.Bound.JoinedEntity.EntityName };
                    resolvedJoinEntities = new List<EntityRef> { site.Bound.Entity, site.Bound.JoinedEntity };
                }
                break;
            }
        }
        if (resolvedJoinNames != null)
        {
            if (executionSite.Bound.JoinedEntityTypeNames == null)
                executionSite = executionSite.WithJoinedEntityTypeNames(resolvedJoinNames, resolvedJoinEntities);
            // Only propagate to sites AFTER the join — pre-join sites use single-entity builder types
            bool seenJoin = false;
            for (int i = 0; i < clauseSites.Count; i++)
            {
                if (clauseSites[i].Bound.Raw.Kind is InterceptorKind.Join or InterceptorKind.LeftJoin or InterceptorKind.RightJoin)
                {
                    seenJoin = true;
                    continue;
                }
                if (seenJoin && clauseSites[i].Bound.JoinedEntityTypeNames == null)
                {
                    clauseSites[i] = clauseSites[i].WithJoinedEntityTypeNames(resolvedJoinNames, resolvedJoinEntities);
                }
            }
        }

        // Identify conditional clauses from ConditionalInfo
        var conditionalTerms = new List<ConditionalTerm>();
        var bitIndex = 0;
        var branchGroups = new Dictionary<string, List<(TranslatedCallSite Site, int BitIndex)>>(StringComparer.Ordinal);

        foreach (var site in clauseSites)
        {
            var condInfo = site.Bound.Raw.ConditionalInfo;
            if (condInfo == null)
                continue;

            // Check nesting depth
            if (condInfo.NestingDepth > MaxIfNestingDepth)
            {
                return MakeRuntimeBuildChain(executionSite, clauseSites, "Conditional nesting depth exceeds maximum", registry, isTraced);
            }

            var role = MapInterceptorKindToClauseRole(site.Bound.Raw.Kind);
            if (role == null)
                continue;

            conditionalTerms.Add(new ConditionalTerm(bitIndex, role.Value));

            // Group by condition text for mutual exclusivity detection
            if (!branchGroups.TryGetValue(condInfo.ConditionText, out var group))
            {
                group = new List<(TranslatedCallSite, int)>();
                branchGroups[condInfo.ConditionText] = group;
            }
            group.Add((site, bitIndex));
            bitIndex++;
        }

        // Determine tier
        var totalBits = bitIndex;
        OptimizationTier tier;
        if (totalBits <= MaxTier1Bits)
            tier = OptimizationTier.PrebuiltDispatch;
        else
            tier = OptimizationTier.PrequotedFragments;

        ct.ThrowIfCancellationRequested();

        // Compute possible masks
        var possibleMasks = tier == OptimizationTier.PrebuiltDispatch
            ? EnumerateMaskCombinations(conditionalTerms, branchGroups, clauseSites)
            : Array.Empty<ulong>();

        // Collect unmatched method names (sites not in the chain that are tracked but not intercepted)
        // In the new pipeline, all sites in the chain are matched by ChainId — unmatched is N/A.
        // But we track Limit/Offset/Distinct/WithTimeout which have no clause translation.
        // New batch insert chains (BatchInsertColumnSelector → Values → terminal) are fully tracked.
        IReadOnlyList<string>? unmatchedMethodNames = null;

        // Build QueryPlan terms from TranslatedClause data
        var whereTerms = new List<WhereTerm>();
        var orderTerms = new List<OrderTerm>();
        var groupByExprs = new List<SqlExpr>();
        var havingExprs = new List<SqlExpr>();
        var setTerms = new List<SetTerm>();
        var joinPlans = new List<JoinPlan>();
        var insertColumns = new List<InsertColumn>();
        var parameters = new List<QueryParameter>();
        var paramGlobalIndex = 0;
        PaginationPlan? pagination = null;
        var hasLimit = false;
        var hasOffset = false;
        int? limitLiteral = null;
        int? offsetLiteral = null;
        bool isDistinct = false;
        bool hasSelectClause = false;
        SelectProjection? projection = null;
        var primaryTable = new TableRef(
            executionSite.Bound.TableName,
            executionSite.Bound.SchemaName);

        // Determine query kind from execution site
        var queryKind = DetermineQueryKind(executionSite.Bound.Raw.Kind, executionSite.Bound.Raw.BuilderKind);

        // Process clause sites to build terms
        var consumedConditionalTerms = new HashSet<int>();
        for (int i = 0; i < clauseSites.Count; i++)
        {
            var site = clauseSites[i];
            var raw = site.Bound.Raw;
            var kind = raw.Kind;
            var role = MapInterceptorKindToClauseRole(kind);
            int? clauseBitIndex = null;

            // Check if this clause is conditional
            if (raw.ConditionalInfo != null)
            {
                // Find its bit index — match by role and consume each term only once
                for (int ci = 0; ci < conditionalTerms.Count; ci++)
                {
                    if (conditionalTerms[ci].Role == role && !consumedConditionalTerms.Contains(ci))
                    {
                        clauseBitIndex = conditionalTerms[ci].BitIndex;
                        consumedConditionalTerms.Add(ci);
                        break;
                    }
                }
            }

            if (site.Clause != null && site.Clause.IsSuccess)
            {
                var clause = site.Clause;
                var expr = clause.ResolvedExpression;

                // Remap parameters and enrich with column metadata (IsEnum, IsSensitive)
                var clauseParams = RemapParameters(clause.Parameters, ref paramGlobalIndex);
                EnrichParametersFromColumns(clauseParams, expr, executionSite.Bound.Entity, resolvedJoinEntities);
                parameters.AddRange(clauseParams);

                switch (clause.Kind)
                {
                    case ClauseKind.Where:
                        whereTerms.Add(new WhereTerm(expr, clauseBitIndex));
                        break;

                    case ClauseKind.OrderBy:
                        orderTerms.Add(new OrderTerm(expr, clause.IsDescending, clauseBitIndex));
                        break;

                    case ClauseKind.GroupBy:
                        groupByExprs.Add(expr);
                        break;

                    case ClauseKind.Having:
                        havingExprs.Add(expr);
                        break;

                    case ClauseKind.Set:
                        if (clause.SetAssignments != null)
                        {
                            // SetAction: multiple assignments. Parameters were remapped above,
                            // so walk backwards from paramGlobalIndex to assign each non-inlined
                            // assignment its correct parameter slot.
                            var setParamCount = clauseParams.Count;
                            var nextSetParamIdx = paramGlobalIndex - setParamCount;
                            foreach (var assignment in clause.SetAssignments)
                            {
                                // Quote the column name — ColumnSql stores the unquoted property name
                                var quotedCol = Quarry.Generators.Sql.SqlFormatting.QuoteIdentifier(site.Bound.Dialect, assignment.ColumnSql);
                                var col = new ResolvedColumnExpr(quotedCol);
                                SqlExpr valueExpr;
                                if (assignment.IsInlined && assignment.InlinedSqlValue != null)
                                {
                                    // Detect boolean literals for dialect-specific formatting
                                    var inlinedVal = assignment.InlinedSqlValue;
                                    var lowerVal = inlinedVal.ToLowerInvariant();
                                    var clrType = (lowerVal == "true" || lowerVal == "false") ? "bool" : "object";
                                    valueExpr = new LiteralExpr(inlinedVal, clrType);
                                }
                                else
                                {
                                    // Parameter reference — each non-inlined gets the next slot
                                    valueExpr = new ParamSlotExpr(nextSetParamIdx, "object", "@p" + nextSetParamIdx);
                                    nextSetParamIdx++;
                                }
                                setTerms.Add(new SetTerm(col, valueExpr, assignment.CustomTypeMappingClass, clauseBitIndex));
                            }
                        }
                        else
                        {
                            // Single Set: column = value from the expression
                            // The lambda u => u.Column produces the column reference only.
                            // The value parameter is the second arg to Set(), handled at runtime
                            // by the emitter via SetClauseInfo.ValueParameterIndex.
                            var col = new ResolvedColumnExpr(SqlExprRenderer.Render(expr, site.Bound.Dialect));
                            // Use the next available parameter index for the value slot
                            var valueIdx = clauseParams.Count > 0 ? paramGlobalIndex - 1 : paramGlobalIndex;
                            var valExpr = new ParamSlotExpr(valueIdx, "object", "@p" + valueIdx);
                            setTerms.Add(new SetTerm(col, valExpr, clause.CustomTypeMappingClass, clauseBitIndex));
                        }
                        break;

                    case ClauseKind.Join:
                        var joinTable = new TableRef(
                            clause.JoinedTableName ?? "",
                            clause.JoinedSchemaName,
                            clause.TableAlias);
                        var joinKind = clause.JoinKind ?? JoinClauseKind.Inner;
                        joinPlans.Add(new JoinPlan(joinKind, joinTable, expr, raw.IsNavigationJoin));
                        break;
                }
            }
            else if (kind == InterceptorKind.UpdateSetAction && raw.SetActionAssignments != null)
            {
                // SetAction (Action<T> lambda): parameters and assignments stored on RawCallSite
                // because Action<T> can't be parsed to SqlExpr.
                if (raw.SetActionParameters != null)
                {
                    var clauseParams = RemapParameters(raw.SetActionParameters, ref paramGlobalIndex);
                    parameters.AddRange(clauseParams);
                }

                // Build set terms from assignments. Non-inlined assignments consume parameters
                // in order — track which parameter index each non-inlined assignment gets.
                // After RemapParameters, paramGlobalIndex was incremented by SetActionParameters.Count.
                // So the first SetAction parameter's global index = paramGlobalIndex - SetActionParameters.Count.
                var setParamCount = raw.SetActionParameters?.Count ?? 0;
                var nextParamIdx = paramGlobalIndex - setParamCount;

                foreach (var assignment in raw.SetActionAssignments)
                {
                    // Quote the column name using the dialect — SetActionAssignment.ColumnSql
                    // stores the unquoted property name from discovery
                    var quotedCol = Quarry.Generators.Sql.SqlFormatting.QuoteIdentifier(site.Bound.Dialect, assignment.ColumnSql);
                    var col = new ResolvedColumnExpr(quotedCol);
                    SqlExpr valueExpr;
                    if (assignment.IsInlined && assignment.InlinedSqlValue != null)
                    {
                        // Detect boolean literals for dialect-specific formatting
                        var inlinedVal = assignment.InlinedSqlValue;
                        var lowerVal = inlinedVal.ToLowerInvariant();
                        var clrType = (lowerVal == "true" || lowerVal == "false") ? "bool" : "object";
                        valueExpr = new LiteralExpr(inlinedVal, clrType);
                    }
                    else
                    {
                        valueExpr = new ParamSlotExpr(nextParamIdx, "object", "@p" + nextParamIdx);
                        nextParamIdx++;
                    }
                    setTerms.Add(new SetTerm(col, valueExpr, assignment.CustomTypeMappingClass, clauseBitIndex));
                }
            }
            else if (kind == InterceptorKind.UpdateSetPoco && site.Bound.UpdateInfo != null)
            {
                // UpdateSetPoco: build SET terms from UpdateInfo columns.
                // Each column gets a parameter slot with a local index (0-based
                // within this clause) so the assembler can renumber them in SQL order.
                var updateInfo = site.Bound.UpdateInfo;
                foreach (var col in updateInfo.Columns)
                {
                    var colExpr = new ResolvedColumnExpr(col.QuotedColumnName);
                    // Each SET value is a standalone expression with one param at LocalIndex=0.
                    // The assembler's paramBase handles the actual position in the SQL output.
                    var valExpr = new ParamSlotExpr(0, col.ClrType, "@p0");
                    setTerms.Add(new SetTerm(colExpr, valExpr, col.CustomTypeMappingClass, clauseBitIndex));
                    parameters.Add(new QueryParameter(
                        paramGlobalIndex,
                        col.ClrType,
                        $"entity.{col.PropertyName}",
                        typeMappingClass: col.CustomTypeMappingClass,
                        isSensitive: col.IsSensitive,
                        entityPropertyExpression: $"__c.Entity.{col.PropertyName}"));
                    paramGlobalIndex++;
                }
            }
            else if (kind == InterceptorKind.Limit)
            {
                hasLimit = true;
                limitLiteral = raw.ConstantIntValue;
            }
            else if (kind == InterceptorKind.Offset)
            {
                hasOffset = true;
                offsetLiteral = raw.ConstantIntValue;
            }
            else if (kind == InterceptorKind.Distinct)
            {
                isDistinct = true;
            }
            else if (kind == InterceptorKind.Select && raw.ProjectionInfo != null)
            {
                hasSelectClause = true;
                projection = BuildProjection(raw.ProjectionInfo, executionSite, registry);
            }
        }

        // Build pagination
        if (hasLimit || hasOffset)
        {
            pagination = new PaginationPlan(
                literalLimit: limitLiteral,
                literalOffset: offsetLiteral,
                limitParamIndex: hasLimit && limitLiteral == null ? paramGlobalIndex++ : (int?)null,
                offsetParamIndex: hasOffset && offsetLiteral == null ? paramGlobalIndex++ : (int?)null);
        }

        // Default projection if none specified
        if (projection == null)
        {
            projection = new SelectProjection(
                ProjectionKind.Entity,
                executionSite.Bound.Raw.ResultTypeName ?? executionSite.Bound.Raw.EntityTypeName,
                Array.Empty<ProjectedColumn>(),
                isIdentity: true);
        }

        // Enrich identity projections with entity columns so SqlAssembler renders
        // explicit column names instead of SELECT *.
        // Only for chains that have an explicit Select clause (hasSelectClause flag).
        // Discovery may produce wrong columns (e.g. computed properties like DisplayLabel),
        // so we always use the authoritative entity column metadata from EntityRef.
        if (hasSelectClause && projection.IsIdentity)
        {
            var entityRef = executionSite.Bound.Entity;
            if (entityRef != null && entityRef.Columns.Count > 0)
            {
                var entityCols = new List<ProjectedColumn>();
                var ord = 0;
                foreach (var ec in entityRef.Columns)
                {
                    entityCols.Add(new ProjectedColumn(
                        propertyName: ec.PropertyName,
                        columnName: ec.ColumnName,
                        clrType: ec.ClrType,
                        fullClrType: ec.FullClrType,
                        isNullable: ec.IsNullable,
                        ordinal: ord++,
                        customTypeMapping: ec.CustomTypeMappingClass,
                        isValueType: ec.IsValueType,
                        readerMethodName: ec.DbReaderMethodName ?? ec.ReaderMethodName,
                        isForeignKey: ec.Kind == ColumnKind.ForeignKey,
                        foreignKeyEntityName: ec.ReferencedEntityName,
                        isEnum: ec.IsEnum));
                }
                projection = new SelectProjection(
                    projection.Kind,
                    projection.ResultTypeName,
                    entityCols,
                    customEntityReaderClass: entityRef.CustomEntityReaderClass,
                    isIdentity: true);
            }
        }

        // Handle insert columns
        if ((queryKind == QueryKind.Insert || queryKind == QueryKind.BatchInsert) && executionSite.Bound.InsertInfo != null)
        {
            var insertInfo = executionSite.Bound.InsertInfo;
            for (int c = 0; c < insertInfo.Columns.Count; c++)
            {
                var col = insertInfo.Columns[c];
                insertColumns.Add(new InsertColumn(col.QuotedColumnName, paramGlobalIndex++));
            }
        }

        // Default to identity projection (whole entity) when no Select clause was found
        if (projection == null)
        {
            projection = new SelectProjection(
                ProjectionKind.Entity,
                executionSite.Bound.Raw.ResultTypeName ?? executionSite.Bound.Raw.EntityTypeName,
                Array.Empty<ProjectedColumn>(),
                isIdentity: true);
        }

        var plan = new QueryPlan(
            kind: queryKind,
            primaryTable: primaryTable,
            joins: joinPlans,
            whereTerms: whereTerms,
            orderTerms: orderTerms,
            groupByExprs: groupByExprs,
            havingExprs: havingExprs,
            projection: projection,
            pagination: pagination,
            isDistinct: isDistinct,
            setTerms: setTerms,
            insertColumns: insertColumns,
            conditionalTerms: conditionalTerms,
            possibleMasks: possibleMasks,
            parameters: parameters,
            tier: tier,
            unmatchedMethodNames: unmatchedMethodNames);

        // Trace logging: only for traced chains. Reconstruct per-site discovery/binding/
        // translation traces from the TranslatedCallSite data, then log chain-level analysis.
        if (isTraced)
        {
            var chainUid = executionSite.Bound.Raw.UniqueId;

            // Per-site retroactive trace (discovery + binding + translation)
            foreach (var site in clauseSites)
            {
                LogSiteTrace(chainUid, site);
            }
            LogSiteTrace(chainUid, executionSite);

            // Chain-level analysis trace
            LogChainTrace(chainUid, plan, executionSite);
        }

        return new AnalyzedChain(plan, executionSite, clauseSites, isTraced,
            preparedTerminals: preparedTerminals.Count > 1 ? preparedTerminals : null,
            prepareSite: prepareSite);
    }

    /// <summary>
    /// Remaps clause-local parameters to global parameter indices.
    /// </summary>
    private static List<QueryParameter> RemapParameters(
        IReadOnlyList<ParameterInfo> clauseParams,
        ref int globalIndex)
    {
        var result = new List<QueryParameter>(clauseParams.Count);
        foreach (var p in clauseParams)
        {
            result.Add(new QueryParameter(
                globalIndex: globalIndex++,
                clrType: p.ClrType,
                valueExpression: p.ValueExpression,
                isCaptured: p.IsCaptured,
                expressionPath: p.ExpressionPath,
                isCollection: p.IsCollection,
                elementTypeName: p.CollectionElementType,
                typeMappingClass: p.CustomTypeMappingClass,
                isEnum: p.IsEnum,
                enumUnderlyingType: p.EnumUnderlyingType,
                needsFieldInfoCache: p.IsCaptured && p.CanGenerateDirectPath,
                isDirectAccessible: false, // Computed during carrier analysis
                collectionAccessExpression: null)); // Computed during carrier analysis
        }
        return result;
    }

    /// <summary>
    /// Enriches clause parameters with column metadata (IsEnum, IsSensitive, EnumUnderlyingType)
    /// by walking the resolved expression tree to find column-parameter pairs, then looking up
    /// column metadata from the entity definition.
    /// </summary>
    private static void EnrichParametersFromColumns(
        List<QueryParameter> clauseParams,
        SqlExpr? expression,
        EntityRef? entity,
        IReadOnlyList<EntityRef>? joinedEntities)
    {
        if (expression == null || clauseParams.Count == 0)
            return;

        // Build column lookup by unquoted column name from all available entities
        var columnLookup = new Dictionary<string, ColumnInfo>(StringComparer.OrdinalIgnoreCase);
        if (entity != null)
        {
            foreach (var col in entity.Columns)
                columnLookup[col.ColumnName] = col;
        }
        if (joinedEntities != null)
        {
            foreach (var je in joinedEntities)
            {
                foreach (var col in je.Columns)
                {
                    // Don't overwrite — primary entity columns take precedence
                    if (!columnLookup.ContainsKey(col.ColumnName))
                        columnLookup[col.ColumnName] = col;
                }
            }
        }

        if (columnLookup.Count == 0)
            return;

        // Walk expression tree to find column-param pairs
        var paramColumnMap = new Dictionary<int, ColumnInfo>();
        WalkExprForColumnParamPairs(expression, columnLookup, paramColumnMap);

        if (paramColumnMap.Count == 0)
            return;

        // Enrich parameters with column metadata
        for (int i = 0; i < clauseParams.Count; i++)
        {
            var p = clauseParams[i];
            // ParamSlotExpr local indices correspond to clause-local ordering (0, 1, 2...).
            // clauseParams[i] was created from clauseParams[i] in RemapParameters, so
            // the local index is just i.
            if (!paramColumnMap.TryGetValue(i, out var col))
                continue;

            var isEnum = col.IsEnum || p.IsEnum;
            var isSensitive = col.Modifiers.IsSensitive || p.IsSensitive;

            // Skip if nothing changed
            if (isEnum == p.IsEnum && isSensitive == p.IsSensitive)
                continue;

            clauseParams[i] = new QueryParameter(
                globalIndex: p.GlobalIndex,
                clrType: p.ClrType,
                valueExpression: p.ValueExpression,
                isCaptured: p.IsCaptured,
                expressionPath: p.ExpressionPath,
                isCollection: p.IsCollection,
                elementTypeName: p.ElementTypeName,
                typeMappingClass: p.TypeMappingClass,
                isEnum: isEnum,
                enumUnderlyingType: p.EnumUnderlyingType,
                isSensitive: isSensitive,
                entityPropertyExpression: p.EntityPropertyExpression,
                needsFieldInfoCache: p.NeedsFieldInfoCache,
                isDirectAccessible: p.IsDirectAccessible,
                collectionAccessExpression: p.CollectionAccessExpression);
        }
    }

    /// <summary>
    /// Recursively walks an expression tree to find BinaryOpExpr/InExpr/LikeExpr nodes
    /// where one side is a ResolvedColumnExpr and the other contains a ParamSlotExpr.
    /// Records the mapping from param local index to the matched ColumnInfo.
    /// </summary>
    private static void WalkExprForColumnParamPairs(
        SqlExpr expr,
        Dictionary<string, ColumnInfo> columnLookup,
        Dictionary<int, ColumnInfo> paramColumnMap)
    {
        switch (expr)
        {
            case BinaryOpExpr bin:
                // Check if this is a column = param or param = column comparison
                TryMatchColumnParam(bin.Left, bin.Right, columnLookup, paramColumnMap);
                TryMatchColumnParam(bin.Right, bin.Left, columnLookup, paramColumnMap);
                // Recurse into both sides for nested expressions (e.g., AND/OR)
                WalkExprForColumnParamPairs(bin.Left, columnLookup, paramColumnMap);
                WalkExprForColumnParamPairs(bin.Right, columnLookup, paramColumnMap);
                break;

            case InExpr inExpr:
                // IN clause: operand is the column, values contain params
                if (inExpr.Operand is ResolvedColumnExpr inCol)
                {
                    var colInfo = LookupColumn(inCol, columnLookup);
                    if (colInfo != null)
                    {
                        foreach (var val in inExpr.Values)
                        {
                            if (val is ParamSlotExpr paramSlot)
                                paramColumnMap[paramSlot.LocalIndex] = colInfo;
                        }
                    }
                }
                break;

            case LikeExpr like:
                // LIKE: operand is the column, pattern contains param
                if (like.Operand is ResolvedColumnExpr likeCol)
                {
                    var colInfo = LookupColumn(likeCol, columnLookup);
                    if (colInfo != null && like.Pattern is ParamSlotExpr likeParam)
                        paramColumnMap[likeParam.LocalIndex] = colInfo;
                }
                break;

            case UnaryOpExpr unary:
                WalkExprForColumnParamPairs(unary.Operand, columnLookup, paramColumnMap);
                break;

            case FunctionCallExpr func:
                foreach (var arg in func.Arguments)
                    WalkExprForColumnParamPairs(arg, columnLookup, paramColumnMap);
                break;

            case IsNullCheckExpr isNull:
                WalkExprForColumnParamPairs(isNull.Operand, columnLookup, paramColumnMap);
                break;
        }
    }

    /// <summary>
    /// If columnSide is a ResolvedColumnExpr and paramSide is (or contains) a ParamSlotExpr,
    /// records the column-param mapping.
    /// </summary>
    private static void TryMatchColumnParam(
        SqlExpr columnSide,
        SqlExpr paramSide,
        Dictionary<string, ColumnInfo> columnLookup,
        Dictionary<int, ColumnInfo> paramColumnMap)
    {
        if (columnSide is not ResolvedColumnExpr colExpr)
            return;

        var colInfo = LookupColumn(colExpr, columnLookup);
        if (colInfo == null)
            return;

        // Direct param
        if (paramSide is ParamSlotExpr paramSlot)
        {
            paramColumnMap[paramSlot.LocalIndex] = colInfo;
            return;
        }

        // Param wrapped in function call (e.g., LOWER(@p0))
        if (paramSide is FunctionCallExpr funcExpr)
        {
            foreach (var arg in funcExpr.Arguments)
            {
                if (arg is ParamSlotExpr funcParam)
                    paramColumnMap[funcParam.LocalIndex] = colInfo;
            }
        }
    }

    /// <summary>
    /// Strips dialect-specific quotes from a column name and looks it up in the column dictionary.
    /// </summary>
    private static ColumnInfo? LookupColumn(ResolvedColumnExpr colExpr, Dictionary<string, ColumnInfo> columnLookup)
    {
        var quoted = colExpr.QuotedColumnName;
        if (quoted.Length < 2)
            return null;

        // Strip quotes: "col" → col, `col` → col, [col] → col
        var first = quoted[0];
        string unquoted;
        if (first == '[')
            unquoted = quoted.Substring(1, quoted.Length - 2); // [col]
        else if (first == '"' || first == '`')
            unquoted = quoted.Substring(1, quoted.Length - 2); // "col" or `col`
        else
            unquoted = quoted;

        columnLookup.TryGetValue(unquoted, out var col);
        return col;
    }

    /// <summary>
    /// Builds a SelectProjection from ProjectionInfo, enriching columns with entity metadata.
    /// During discovery, the source generator can't see its own generated entity types, so
    /// ProjectionInfo columns may have empty ClrType/ColumnName. We fix these by cross-referencing
    /// with EntityRef.Columns which has the authoritative column metadata from schema analysis.
    /// For multi-entity (joined) projections, resolves all joined entities from the registry.
    /// </summary>
    private static SelectProjection BuildProjection(ProjectionInfo projInfo, TranslatedCallSite executionSite, EntityRegistry registry)
    {
        // Build column lookups for enrichment
        // For joined queries, build per-tableAlias lookups from all joined entities
        var joinedEntityTypeNames = executionSite.Bound.JoinedEntityTypeNames;
        var isJoined = joinedEntityTypeNames != null && joinedEntityTypeNames.Count >= 2;

        Dictionary<string, Dictionary<string, ColumnInfo>>? perAliasLookup = null;
        Dictionary<string, ColumnInfo>? entityColumnLookup = null;
        var entityRef = executionSite.Bound.Entity;

        if (isJoined)
        {
            // Multi-entity: build per-tableAlias column lookups
            perAliasLookup = new Dictionary<string, Dictionary<string, ColumnInfo>>(StringComparer.Ordinal);
            for (int i = 0; i < joinedEntityTypeNames!.Count; i++)
            {
                var alias = $"t{i}";
                var entry = registry.Resolve(joinedEntityTypeNames[i]);
                if (entry != null)
                {
                    var lookup = new Dictionary<string, ColumnInfo>(StringComparer.Ordinal);
                    foreach (var ec in EntityRef.FromEntityInfo(entry.Entity).Columns)
                        lookup[ec.PropertyName] = ec;
                    perAliasLookup[alias] = lookup;
                }
            }
        }
        else if (entityRef != null && entityRef.Columns.Count > 0)
        {
            // Single-entity: flat lookup
            entityColumnLookup = new Dictionary<string, ColumnInfo>(StringComparer.Ordinal);
            foreach (var ec in entityRef.Columns)
                entityColumnLookup[ec.PropertyName] = ec;
        }

        var columns = new List<ProjectedColumn>();
        if (projInfo.Columns != null)
        {
            foreach (var col in projInfo.Columns)
            {
                // Aggregate columns with unresolved type ("object"): resolve from the
                // referenced entity column. During discovery, Min/Max default to "object"
                // because the semantic model can't resolve generated entity property types.
                if (col.IsAggregateFunction && IsUnresolvedAggregateType(col.ClrType) && col.SqlExpression != null)
                {
                    var resolvedType = TryResolveAggregateTypeFromSql(col.SqlExpression, entityColumnLookup, perAliasLookup, col.TableAlias);
                    if (resolvedType != null)
                    {
                        columns.Add(new ProjectedColumn(
                            propertyName: col.PropertyName,
                            columnName: col.ColumnName,
                            clrType: resolvedType,
                            fullClrType: resolvedType,
                            isNullable: col.IsNullable,
                            ordinal: col.Ordinal,
                            alias: col.Alias,
                            sqlExpression: col.SqlExpression,
                            isAggregateFunction: true,
                            isValueType: true,
                            readerMethodName: GetReaderMethodForType(resolvedType),
                            tableAlias: col.TableAlias));
                        continue;
                    }
                }

                if (NeedsEnrichment(col))
                {
                    ColumnInfo? entityCol = null;

                    if (isJoined && perAliasLookup != null && col.TableAlias != null)
                    {
                        // Multi-entity: match by TableAlias + PropertyName
                        if (perAliasLookup.TryGetValue(col.TableAlias, out var aliasLookup))
                            aliasLookup.TryGetValue(col.PropertyName, out entityCol);
                    }
                    else if (entityColumnLookup != null)
                    {
                        // Single-entity: match by PropertyName
                        entityColumnLookup.TryGetValue(col.PropertyName, out entityCol);
                    }

                    if (entityCol != null)
                    {
                        columns.Add(new ProjectedColumn(
                            propertyName: col.PropertyName,
                            columnName: entityCol.ColumnName,
                            clrType: string.IsNullOrWhiteSpace(col.ClrType) ? entityCol.ClrType : col.ClrType,
                            fullClrType: string.IsNullOrWhiteSpace(col.FullClrType) ? entityCol.FullClrType : col.FullClrType,
                            isNullable: entityCol.IsNullable,
                            ordinal: col.Ordinal,
                            alias: col.Alias,
                            sqlExpression: col.SqlExpression,
                            isAggregateFunction: col.IsAggregateFunction,
                            customTypeMapping: entityCol.CustomTypeMappingClass ?? col.CustomTypeMapping,
                            isValueType: entityCol.IsValueType,
                            readerMethodName: entityCol.DbReaderMethodName ?? entityCol.ReaderMethodName,
                            tableAlias: col.TableAlias,
                            isForeignKey: entityCol.Kind == ColumnKind.ForeignKey,
                            foreignKeyEntityName: entityCol.ReferencedEntityName,
                            isEnum: entityCol.IsEnum));
                        continue;
                    }
                }
                columns.Add(col);
            }
        }

        // Rebuild result type name from enriched columns
        var resultTypeName = projInfo.ResultTypeName ?? executionSite.Bound.Raw.ResultTypeName ?? executionSite.Bound.Raw.EntityTypeName;
        if (projInfo.Kind == ProjectionKind.Tuple && columns.Count > 0)
        {
            var rebuilt = BuildTupleResultTypeName(columns);
            if (!string.IsNullOrEmpty(rebuilt))
                resultTypeName = rebuilt;
        }
        else if (projInfo.Kind == ProjectionKind.SingleColumn && columns.Count == 1)
        {
            var col = columns[0];
            var colType = !string.IsNullOrWhiteSpace(col.ClrType) ? col.ClrType : col.FullClrType;
            if (!string.IsNullOrWhiteSpace(colType) && colType != "?" && colType != "object")
            {
                if (col.IsNullable && !colType.EndsWith("?"))
                    colType += "?";
                resultTypeName = colType;
            }
        }
        // Fix unresolved "?" result type by checking enriched columns
        if (resultTypeName == "?" && columns.Count > 0)
        {
            if (columns.Count == 1)
            {
                var col = columns[0];
                var colType = !string.IsNullOrWhiteSpace(col.ClrType) ? col.ClrType : col.FullClrType;
                if (!string.IsNullOrWhiteSpace(colType) && colType != "?")
                    resultTypeName = col.IsNullable && !colType.EndsWith("?") ? colType + "?" : colType;
            }
            else
            {
                var rebuilt = BuildTupleResultTypeName(columns);
                if (!string.IsNullOrEmpty(rebuilt))
                    resultTypeName = rebuilt;
            }
        }

        return new SelectProjection(
            kind: projInfo.Kind,
            resultTypeName: resultTypeName,
            columns: columns,
            customEntityReaderClass: projInfo.CustomEntityReaderClass ?? entityRef?.CustomEntityReaderClass,
            isIdentity: projInfo.Kind == ProjectionKind.Entity);
    }

    /// <summary>
    /// Checks if a projected column needs enrichment (has missing type or column name info).
    /// </summary>
    private static bool NeedsEnrichment(ProjectedColumn col)
    {
        // Aggregates have empty ColumnName by design — don't trigger enrichment for them
        if (col.IsAggregateFunction)
            return string.IsNullOrWhiteSpace(col.ClrType);
        return string.IsNullOrWhiteSpace(col.ClrType)
            || string.IsNullOrWhiteSpace(col.ColumnName);
    }

    /// <summary>
    /// Checks if an aggregate's CLR type is unresolved and needs enrichment.
    /// </summary>
    private static bool IsUnresolvedAggregateType(string clrType)
    {
        return clrType == "object" || clrType == "?" || string.IsNullOrWhiteSpace(clrType);
    }

    /// <summary>
    /// Public entry point for resolving aggregate type from SQL (used by bridge enrichment).
    /// </summary>
    internal static string? TryResolveAggregateTypeFromSqlPublic(
        string sqlExpression,
        Dictionary<string, ColumnInfo> entityColumnLookup)
    {
        return TryResolveAggregateTypeFromSql(sqlExpression, entityColumnLookup, null, null);
    }

    /// <summary>
    /// Public entry point for getting reader method (used by bridge enrichment).
    /// </summary>
    internal static string GetReaderMethodForTypePublic(string clrType)
    {
        return GetReaderMethodForType(clrType);
    }

    /// <summary>
    /// Tries to resolve the CLR type for an aggregate column by extracting the referenced
    /// column name from the SQL expression and looking it up in entity column metadata.
    /// E.g., SUM("Total") → extract "Total" → look up Total column → type is "decimal".
    /// </summary>
    private static string? TryResolveAggregateTypeFromSql(
        string sqlExpression,
        Dictionary<string, ColumnInfo>? entityColumnLookup,
        Dictionary<string, Dictionary<string, ColumnInfo>>? perAliasLookup,
        string? tableAlias)
    {
        // Extract the column name from expressions like: SUM("Total"), MIN(t0."Total"),
        // SUM(`Total`), MIN([Total])
        var propName = ExtractColumnNameFromAggregateSql(sqlExpression);
        if (propName == null)
            return null;

        ColumnInfo? col = null;
        if (tableAlias != null && perAliasLookup != null)
        {
            if (perAliasLookup.TryGetValue(tableAlias, out var aliasLookup))
                aliasLookup.TryGetValue(propName, out col);
        }
        else if (entityColumnLookup != null)
        {
            entityColumnLookup.TryGetValue(propName, out col);
        }

        if (col != null && !string.IsNullOrWhiteSpace(col.ClrType) && col.ClrType != "object")
            return col.ClrType;

        return null;
    }

    /// <summary>
    /// Extracts a column/property name from an aggregate SQL expression.
    /// Handles: SUM("Total"), MIN(t0."Total"), AVG(`Total`), MAX([Total]).
    /// For COUNT(*), returns null.
    /// </summary>
    private static string? ExtractColumnNameFromAggregateSql(string sql)
    {
        // Skip COUNT(*)
        if (sql.Contains("*"))
            return null;

        // Find the innermost quoted identifier
        int start = -1, end = -1;
        for (int i = sql.Length - 1; i >= 0; i--)
        {
            if (end < 0 && (sql[i] == '"' || sql[i] == '`' || sql[i] == ']'))
            {
                end = i;
                var closeChar = sql[i] == ']' ? '[' : sql[i];
                for (int j = i - 1; j >= 0; j--)
                {
                    if (sql[j] == closeChar)
                    {
                        start = j + 1;
                        break;
                    }
                }
                break;
            }
        }

        if (start >= 0 && end > start)
            return sql.Substring(start, end - start);

        return null;
    }

    /// <summary>
    /// Gets the DbDataReader method for a CLR type.
    /// </summary>
    private static string GetReaderMethodForType(string clrType)
    {
        return clrType switch
        {
            "int" or "Int32" => "GetInt32",
            "long" or "Int64" => "GetInt64",
            "decimal" or "Decimal" => "GetDecimal",
            "double" or "Double" => "GetDouble",
            "float" or "Single" => "GetFloat",
            "string" or "String" => "GetString",
            "bool" or "Boolean" => "GetBoolean",
            "DateTime" => "GetDateTime",
            "Guid" => "GetGuid",
            "byte" or "Byte" => "GetByte",
            "short" or "Int16" => "GetInt16",
            _ => "GetValue"
        };
    }

    /// <summary>
    /// Builds a tuple result type name from enriched columns.
    /// </summary>
    private static string BuildTupleResultTypeName(List<ProjectedColumn> columns)
    {
        var parts = new List<string>();
        foreach (var col in columns)
        {
            var typeName = col.ClrType;
            if (string.IsNullOrWhiteSpace(typeName))
                typeName = col.FullClrType;
            if (string.IsNullOrWhiteSpace(typeName))
                return ""; // Can't build a valid type name

            if (col.IsNullable && !typeName.EndsWith("?"))
                typeName += "?";

            // Omit default ItemN names
            var isDefaultName = col.PropertyName.StartsWith("Item") &&
                int.TryParse(col.PropertyName.Substring(4), out var idx) &&
                idx == col.Ordinal + 1;

            parts.Add(isDefaultName ? typeName : $"{typeName} {col.PropertyName}");
        }
        return $"({string.Join(", ", parts)})";
    }

    /// <summary>
    /// Checks for disqualifying conditions from RawCallSite flags.
    /// </summary>
    private static string? CheckDisqualifiers(List<TranslatedCallSite> chainSites)
    {
        foreach (var site in chainSites)
        {
            var raw = site.Bound.Raw;
            if (raw.IsInsideLoop)
                return "Chain contains a clause inside a loop body";
            if (raw.IsCapturedInLambda)
                return "Chain variable captured in a lambda expression";
            if (raw.IsPassedAsArgument)
                return "Chain variable passed as argument to non-Quarry method or captured in lambda";
            if (raw.IsAssignedFromNonQuarryMethod)
                return "Chain variable assigned from non-Quarry method";
        }
        return null;
    }

    /// <summary>
    /// Determines the QueryKind from the execution terminal's InterceptorKind.
    /// </summary>
    private static QueryKind DetermineQueryKind(InterceptorKind kind, BuilderKind builderKind)
    {
        if (kind == InterceptorKind.InsertExecuteNonQuery ||
            kind == InterceptorKind.InsertExecuteScalar ||
            kind == InterceptorKind.InsertToDiagnostics)
            return QueryKind.Insert;

        if (kind == InterceptorKind.BatchInsertExecuteNonQuery ||
            kind == InterceptorKind.BatchInsertExecuteScalar ||
            kind == InterceptorKind.BatchInsertToDiagnostics)
            return QueryKind.BatchInsert;

        return builderKind switch
        {
            BuilderKind.Delete or BuilderKind.ExecutableDelete => QueryKind.Delete,
            BuilderKind.Update or BuilderKind.ExecutableUpdate => QueryKind.Update,
            _ => QueryKind.Select
        };
    }

    /// <summary>
    /// Enumerates all possible ClauseMask values from conditional terms and branch groups.
    /// </summary>
    private static IReadOnlyList<ulong> EnumerateMaskCombinations(
        List<ConditionalTerm> conditionalTerms,
        Dictionary<string, List<(TranslatedCallSite Site, int BitIndex)>> branchGroups,
        List<TranslatedCallSite> clauseSites)
    {
        if (conditionalTerms.Count == 0)
            return new[] { 0UL };

        var independentBits = new List<int>();
        var exclusiveGroups = new List<List<int>>();

        foreach (var kvp in branchGroups)
        {
            var group = kvp.Value;
            // Determine if this branch group is mutually exclusive
            var hasMutuallyExclusive = group.Any(g =>
                g.Site.Bound.Raw.ConditionalInfo?.BranchKind == BranchKind.MutuallyExclusive);

            if (hasMutuallyExclusive && group.Count >= 2)
            {
                exclusiveGroups.Add(group.Select(g => g.BitIndex).ToList());
            }
            else
            {
                independentBits.AddRange(group.Select(g => g.BitIndex));
            }
        }

        // Build combinations
        var masks = new List<ulong> { 0UL };

        // Independent bits: each can be on or off
        foreach (var bit in independentBits)
        {
            var newMasks = new List<ulong>(masks.Count * 2);
            foreach (var mask in masks)
            {
                newMasks.Add(mask);                      // bit off
                newMasks.Add(mask | (1UL << bit));       // bit on
            }
            masks = newMasks;
        }

        // Mutually exclusive groups: exactly one bit from the group is set
        foreach (var group in exclusiveGroups)
        {
            var newMasks = new List<ulong>(masks.Count * group.Count);
            foreach (var mask in masks)
            {
                foreach (var bit in group)
                {
                    newMasks.Add(mask | (1UL << bit));
                }
            }
            masks = newMasks;
        }

        return masks;
    }

    /// <summary>
    /// Creates a RuntimeBuild (tier 3) result.
    /// </summary>
    private static AnalyzedChain MakeRuntimeBuildChain(
        TranslatedCallSite executionSite,
        List<TranslatedCallSite> clauseSites,
        string reason,
        EntityRegistry? registry = null,
        bool isTraced = false,
        string? forkedVariableName = null)
    {
        var primaryTable = new TableRef(
            executionSite.Bound.TableName,
            executionSite.Bound.SchemaName);
        var queryKind = DetermineQueryKind(executionSite.Bound.Raw.Kind, executionSite.Bound.Raw.BuilderKind);

        // Even for runtime chains, enrich the Select projection so emitters can produce
        // concrete-typed interceptors (required for C# interceptor signature matching)
        SelectProjection? projection = null;
        if (registry != null)
        {
            foreach (var site in clauseSites)
            {
                if (site.Bound.Raw.Kind == InterceptorKind.Select && site.Bound.Raw.ProjectionInfo != null)
                {
                    projection = BuildProjection(site.Bound.Raw.ProjectionInfo, executionSite, registry);
                    break;
                }
            }
        }
        if (projection == null)
        {
            projection = new SelectProjection(
                ProjectionKind.Entity,
                executionSite.Bound.Raw.ResultTypeName ?? executionSite.Bound.Raw.EntityTypeName,
                Array.Empty<ProjectedColumn>(),
                isIdentity: true);
        }

        var plan = new QueryPlan(
            kind: queryKind,
            primaryTable: primaryTable,
            joins: Array.Empty<JoinPlan>(),
            whereTerms: Array.Empty<WhereTerm>(),
            orderTerms: Array.Empty<OrderTerm>(),
            groupByExprs: Array.Empty<SqlExpr>(),
            havingExprs: Array.Empty<SqlExpr>(),
            projection: projection,
            pagination: null,
            isDistinct: false,
            setTerms: Array.Empty<SetTerm>(),
            insertColumns: Array.Empty<InsertColumn>(),
            conditionalTerms: Array.Empty<ConditionalTerm>(),
            possibleMasks: Array.Empty<ulong>(),
            parameters: Array.Empty<QueryParameter>(),
            tier: OptimizationTier.RuntimeBuild,
            notAnalyzableReason: reason,
            forkedVariableName: forkedVariableName);

        return new AnalyzedChain(plan, executionSite, clauseSites, isTraced);
    }

    /// <summary>
    /// Maps an InterceptorKind to a ClauseRole.
    /// Returns null for kinds that are not clause roles (e.g., execution methods).
    /// </summary>
    internal static ClauseRole? MapInterceptorKindToClauseRole(InterceptorKind kind)
    {
        return kind switch
        {
            InterceptorKind.Select => ClauseRole.Select,
            InterceptorKind.Where => ClauseRole.Where,
            InterceptorKind.OrderBy => ClauseRole.OrderBy,
            InterceptorKind.ThenBy => ClauseRole.ThenBy,
            InterceptorKind.GroupBy => ClauseRole.GroupBy,
            InterceptorKind.Having => ClauseRole.Having,
            InterceptorKind.Join => ClauseRole.Join,
            InterceptorKind.LeftJoin => ClauseRole.Join,
            InterceptorKind.RightJoin => ClauseRole.Join,
            InterceptorKind.Set => ClauseRole.Set,
            InterceptorKind.DeleteWhere => ClauseRole.DeleteWhere,
            InterceptorKind.UpdateSet => ClauseRole.UpdateSet,
            InterceptorKind.UpdateSetAction => ClauseRole.UpdateSet,
            InterceptorKind.UpdateSetPoco => ClauseRole.UpdateSet,
            InterceptorKind.UpdateWhere => ClauseRole.UpdateWhere,
            InterceptorKind.Limit => ClauseRole.Limit,
            InterceptorKind.Offset => ClauseRole.Offset,
            InterceptorKind.Distinct => ClauseRole.Distinct,
            InterceptorKind.WithTimeout => ClauseRole.WithTimeout,
            InterceptorKind.ChainRoot => ClauseRole.ChainRoot,
            InterceptorKind.DeleteTransition => ClauseRole.DeleteTransition,
            InterceptorKind.UpdateTransition => ClauseRole.UpdateTransition,
            InterceptorKind.AllTransition => ClauseRole.AllTransition,
            InterceptorKind.InsertTransition => ClauseRole.InsertTransition,
            InterceptorKind.BatchInsertColumnSelector => ClauseRole.InsertTransition,
            InterceptorKind.BatchInsertValues => ClauseRole.BatchInsertValues,
            _ => null
        };
    }

    /// <summary>
    /// Checks if an InterceptorKind represents an execution method.
    /// </summary>
    internal static bool IsExecutionKind(InterceptorKind kind)
    {
        return kind is InterceptorKind.ExecuteFetchAll
            or InterceptorKind.ExecuteFetchFirst
            or InterceptorKind.ExecuteFetchFirstOrDefault
            or InterceptorKind.ExecuteFetchSingle
            or InterceptorKind.ExecuteScalar
            or InterceptorKind.ExecuteNonQuery
            or InterceptorKind.ToAsyncEnumerable
            or InterceptorKind.ToDiagnostics
            or InterceptorKind.InsertExecuteNonQuery
            or InterceptorKind.InsertExecuteScalar
            or InterceptorKind.InsertToDiagnostics
            or InterceptorKind.BatchInsertExecuteNonQuery
            or InterceptorKind.BatchInsertExecuteScalar
            or InterceptorKind.BatchInsertToDiagnostics;
    }

    /// <summary>
    /// Logs retroactive discovery/binding/translation trace for a single site.
    /// Called from ChainAnalyzer when a traced chain is detected, reconstructing
    /// trace data from the TranslatedCallSite objects already on hand.
    /// </summary>
    private static void LogSiteTrace(string chainUid, TranslatedCallSite site)
    {
        var raw = site.Bound.Raw;
        var log = IR.TraceCapture.Log;

        // ── Discovery ──
        log(chainUid, $"[Trace] Discovery ({raw.MethodName} at line {raw.Line}):");
        log(chainUid, $"  kind={raw.Kind}, builderKind={raw.BuilderKind}, isAnalyzable={raw.IsAnalyzable}");
        log(chainUid, $"  chainId={raw.ChainId ?? "(null)"}, uniqueId={raw.UniqueId}");
        log(chainUid, $"  builderType={raw.BuilderTypeName}, entityType={raw.EntityTypeName}");
        if (raw.ResultTypeName != null)
            log(chainUid, $"  resultType={raw.ResultTypeName}");
        if (raw.Expression != null)
            log(chainUid, $"  parsedExpr={FormatExpr(raw.Expression)}");
        if (raw.ClauseKind.HasValue)
            log(chainUid, $"  clauseKind={raw.ClauseKind.Value}, isDescending={raw.IsDescending}");
        if (raw.JoinedEntityTypeName != null)
            log(chainUid, $"  joinedEntityType={raw.JoinedEntityTypeName}, isNavigationJoin={raw.IsNavigationJoin}");
        if (raw.JoinedEntityTypeNames != null)
            log(chainUid, $"  joinedEntityTypes=[{string.Join(", ", raw.JoinedEntityTypeNames)}]");
        if (raw.ConditionalInfo != null)
            log(chainUid, $"  conditional: depth={raw.ConditionalInfo.NestingDepth}, condition=\"{raw.ConditionalInfo.ConditionText}\", branch={raw.ConditionalInfo.BranchKind}");
        if (raw.ConstantIntValue.HasValue)
            log(chainUid, $"  constantIntValue={raw.ConstantIntValue.Value}");
        if (raw.ProjectionInfo != null)
            log(chainUid, $"  projection: kind={raw.ProjectionInfo.Kind}, columns={raw.ProjectionInfo.Columns.Count}, resultType={raw.ProjectionInfo.ResultTypeName}");
        if (!raw.IsAnalyzable && raw.NonAnalyzableReason != null)
            log(chainUid, $"  nonAnalyzableReason={raw.NonAnalyzableReason}");
        // Disqualifiers
        if (raw.IsInsideLoop || raw.IsInsideTryCatch || raw.IsCapturedInLambda || raw.IsPassedAsArgument || raw.IsAssignedFromNonQuarryMethod)
            log(chainUid, $"  disqualifiers: loop={raw.IsInsideLoop}, tryCatch={raw.IsInsideTryCatch}, lambdaCapture={raw.IsCapturedInLambda}, passedAsArg={raw.IsPassedAsArgument}, nonQuarryAssign={raw.IsAssignedFromNonQuarryMethod}");

        // ── Binding ──
        log(chainUid, $"[Trace] Binding ({raw.MethodName}):");
        log(chainUid, $"  entity={raw.EntityTypeName}, table={site.Bound.TableName}, schema={site.Bound.SchemaName ?? "(null)"}, dialect={site.Bound.Dialect}");
        log(chainUid, $"  context={site.Bound.ContextClassName}");
        if (site.Bound.Entity != null && site.Bound.Entity.Columns.Count > 0)
            log(chainUid, $"  resolvedColumns=[{string.Join(", ", ColumnNames(site.Bound.Entity.Columns))}]");
        if (site.Bound.JoinedEntity != null)
            log(chainUid, $"  joinedEntity={site.Bound.JoinedEntity.EntityName}, joinedTable={site.Bound.JoinedEntity.TableName}");
        if (site.Bound.JoinedEntities != null)
        {
            foreach (var je in site.Bound.JoinedEntities)
                log(chainUid, $"  joinedEntity: {je.EntityName} -> {je.TableName}");
        }
        if (site.Bound.InsertInfo != null)
            log(chainUid, $"  insertInfo: columns={site.Bound.InsertInfo.Columns.Count}");
        if (site.Bound.UpdateInfo != null)
            log(chainUid, $"  updateInfo: columns={site.Bound.UpdateInfo.Columns.Count}");

        // ── Translation ──
        log(chainUid, $"[Trace] Translation ({raw.MethodName}):");
        if (site.Clause != null)
        {
            log(chainUid, $"  clauseKind={site.Clause.Kind}, isSuccess={site.Clause.IsSuccess}");
            log(chainUid, $"  resolvedExpr={FormatExpr(site.Clause.ResolvedExpression)}");
            if (site.Clause.Parameters.Count > 0)
            {
                foreach (var p in site.Clause.Parameters)
                {
                    var flags = new List<string>();
                    if (p.IsCaptured) flags.Add("captured");
                    if (p.IsCollection) flags.Add("collection");
                    log(chainUid, $"  param[{p.Index}]: type={p.ClrType}, value={p.ValueExpression}, path={p.ExpressionPath ?? "(null)"}{(flags.Count > 0 ? $", flags=[{string.Join(",", flags)}]" : "")}");
                }
            }
            else
            {
                log(chainUid, "  params=none");
            }
            if (site.Clause.JoinKind.HasValue)
                log(chainUid, $"  joinKind={site.Clause.JoinKind.Value}, joinedTable={site.Clause.JoinedTableName}, alias={site.Clause.TableAlias}");
            if (site.Clause.SetAssignments != null)
            {
                foreach (var sa in site.Clause.SetAssignments)
                    log(chainUid, $"  setAssignment: {sa.ColumnSql}={sa.InlinedSqlValue ?? "(param)"}, type={sa.ValueTypeName ?? "?"}");
            }
            if (site.Clause.ErrorMessage != null)
                log(chainUid, $"  error={site.Clause.ErrorMessage}");
        }
        else
        {
            log(chainUid, "  clause=none (non-clause site)");
        }
    }

    /// <summary>
    /// Logs chain-level analysis trace including joins, projections, parameters, and pagination.
    /// </summary>
    private static void LogChainTrace(string chainUid, QueryPlan plan, TranslatedCallSite executionSite)
    {
        var log = IR.TraceCapture.Log;

        log(chainUid, "[Trace] ChainAnalysis:");
        log(chainUid, $"  tier={plan.Tier}, queryKind={plan.Kind}");
        log(chainUid, $"  primaryTable={plan.PrimaryTable.TableName}, schema={plan.PrimaryTable.SchemaName ?? "(null)"}");
        log(chainUid, $"  isDistinct={plan.IsDistinct}");
        if (plan.NotAnalyzableReason != null)
            log(chainUid, $"  notAnalyzableReason={plan.NotAnalyzableReason}");
        if (plan.UnmatchedMethodNames != null)
            log(chainUid, $"  unmatchedMethods=[{string.Join(", ", plan.UnmatchedMethodNames)}]");

        // Joins
        if (plan.Joins.Count > 0)
        {
            foreach (var j in plan.Joins)
                log(chainUid, $"  join: {j.Kind} {j.Table.TableName} ON {FormatExpr(j.OnCondition)}{(j.IsNavigationJoin ? " (navigation)" : "")}");
        }

        // WHERE terms
        if (plan.WhereTerms.Count > 0)
        {
            foreach (var w in plan.WhereTerms)
                log(chainUid, $"  where: {FormatExpr(w.Condition)}{(w.BitIndex.HasValue ? $" [bit={w.BitIndex}]" : "")}");
        }

        // ORDER BY terms
        if (plan.OrderTerms.Count > 0)
        {
            foreach (var o in plan.OrderTerms)
                log(chainUid, $"  orderBy: {FormatExpr(o.Expression)} {(o.IsDescending ? "DESC" : "ASC")}{(o.BitIndex.HasValue ? $" [bit={o.BitIndex}]" : "")}");
        }

        // GROUP BY
        if (plan.GroupByExprs.Count > 0)
        {
            foreach (var g in plan.GroupByExprs)
                log(chainUid, $"  groupBy: {FormatExpr(g)}");
        }

        // HAVING
        if (plan.HavingExprs.Count > 0)
        {
            foreach (var h in plan.HavingExprs)
                log(chainUid, $"  having: {FormatExpr(h)}");
        }

        // SET terms
        if (plan.SetTerms.Count > 0)
        {
            foreach (var s in plan.SetTerms)
                log(chainUid, $"  set: {FormatExpr(s.Column)}={FormatExpr(s.Value)}");
        }

        // Projection
        if (plan.Projection != null)
        {
            log(chainUid, $"  projection: kind={plan.Projection.Kind}, resultType={plan.Projection.ResultTypeName}, identity={plan.Projection.IsIdentity}");
            foreach (var c in plan.Projection.Columns)
                log(chainUid, $"    col: {c.PropertyName} -> {c.ColumnName ?? "(null)"} [{c.ClrType}]{(c.SqlExpression != null ? $" expr={c.SqlExpression}" : "")}{(c.IsAggregateFunction ? " (aggregate)" : "")}{(c.TableAlias != null ? $" alias={c.TableAlias}" : "")}");
        }

        // Pagination
        if (plan.Pagination != null)
            log(chainUid, $"  pagination: limit={plan.Pagination.LiteralLimit?.ToString() ?? (plan.Pagination.LimitParamIndex.HasValue ? $"P{plan.Pagination.LimitParamIndex}" : "none")}, offset={plan.Pagination.LiteralOffset?.ToString() ?? (plan.Pagination.OffsetParamIndex.HasValue ? $"P{plan.Pagination.OffsetParamIndex}" : "none")}");

        // Parameters
        if (plan.Parameters.Count > 0)
        {
            log(chainUid, $"  parameters ({plan.Parameters.Count}):");
            foreach (var p in plan.Parameters)
            {
                var flags = new List<string>();
                if (p.IsCaptured) flags.Add("captured");
                if (p.IsCollection) flags.Add($"collection<{p.ElementTypeName ?? "?"}>");
                if (p.IsEnum) flags.Add($"enum({p.EnumUnderlyingType})");
                if (p.NeedsFieldInfoCache) flags.Add("fieldInfo");
                if (p.TypeMappingClass != null) flags.Add($"mapping={p.TypeMappingClass}");
                log(chainUid, $"    P{p.GlobalIndex}: type={p.ClrType}, value={p.ValueExpression}{(flags.Count > 0 ? $", [{string.Join(", ", flags)}]" : "")}");
            }
        }
        else
        {
            log(chainUid, "  parameters=none");
        }

        // Conditional terms + masks
        if (plan.ConditionalTerms.Count > 0)
        {
            foreach (var ct in plan.ConditionalTerms)
                log(chainUid, $"  conditionalTerm: bit={ct.BitIndex}");
        }
        log(chainUid, $"  possibleMasks=[{string.Join(", ", plan.PossibleMasks)}]");
    }

    /// <summary>
    /// Formats a SqlExpr for trace output. Renders to SQL using a generic parameter format
    /// for readability, falling back to type name on failure.
    /// </summary>
    private static string FormatExpr(IR.SqlExpr expr)
    {
        try
        {
            return IR.SqlExprRenderer.Render(expr, Sql.SqlDialect.PostgreSQL, useGenericParamFormat: true, stripOuterParens: true);
        }
        catch
        {
            return expr.GetType().Name;
        }
    }

    private static IEnumerable<string> ColumnNames(IReadOnlyList<Models.ColumnInfo> columns)
    {
        foreach (var c in columns)
            yield return c.PropertyName;
    }
}

/// <summary>
/// A query chain analysis result pairing a QueryPlan with its associated call sites.
/// </summary>
internal sealed class AnalyzedChain
{
    public AnalyzedChain(
        QueryPlan plan,
        TranslatedCallSite executionSite,
        IReadOnlyList<TranslatedCallSite> clauseSites,
        bool isTraced = false,
        IReadOnlyList<TranslatedCallSite>? preparedTerminals = null,
        TranslatedCallSite? prepareSite = null)
    {
        Plan = plan;
        ExecutionSite = executionSite;
        ClauseSites = clauseSites;
        IsTraced = isTraced;
        PreparedTerminals = preparedTerminals;
        PrepareSite = prepareSite;
    }

    /// <summary>The logical query plan.</summary>
    public QueryPlan Plan { get; }

    /// <summary>The execution terminal site (for single-terminal or collapsed single-prepared-terminal).</summary>
    public TranslatedCallSite ExecutionSite { get; }

    /// <summary>All clause sites in the chain (in source order).</summary>
    public IReadOnlyList<TranslatedCallSite> ClauseSites { get; }

    /// <summary>Whether this chain has a .Trace() call and should emit trace comments.</summary>
    public bool IsTraced { get; }

    /// <summary>
    /// Terminal sites called on a PreparedQuery variable. Non-null only for multi-terminal chains (N>1).
    /// When null or empty, this is a standard single-terminal chain.
    /// </summary>
    public IReadOnlyList<TranslatedCallSite>? PreparedTerminals { get; }

    /// <summary>
    /// The .Prepare() call site. Non-null only for multi-terminal chains.
    /// </summary>
    public TranslatedCallSite? PrepareSite { get; }
}
