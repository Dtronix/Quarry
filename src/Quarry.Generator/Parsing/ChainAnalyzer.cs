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
        // Find the execution terminal, detect .Trace(), and collect clause sites
        TranslatedCallSite? executionSite = null;
        var clauseSites = new List<TranslatedCallSite>();
        bool isTraced = false;

        foreach (var site in chainSites)
        {
            if (IsExecutionKind(site.Bound.Raw.Kind))
            {
                executionSite = site;
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

        if (executionSite == null)
            return null;

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
                // Find its bit index
                for (int ci = 0; ci < conditionalTerms.Count; ci++)
                {
                    if (conditionalTerms[ci].Role == role)
                    {
                        // Match by position - conditionalTerms are in clauseSites order for conditional ones
                        clauseBitIndex = conditionalTerms[ci].BitIndex;
                        break;
                    }
                }
            }

            if (site.Clause != null && site.Clause.IsSuccess)
            {
                var clause = site.Clause;
                var expr = clause.ResolvedExpression;

                // Remap parameters
                var clauseParams = RemapParameters(clause.Parameters, ref paramGlobalIndex);
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
                            // SetAction: multiple assignments
                            foreach (var assignment in clause.SetAssignments)
                            {
                                // Parse column from assignment.ColumnSql
                                var col = new ResolvedColumnExpr(assignment.ColumnSql);
                                SqlExpr valueExpr;
                                if (assignment.IsInlined && assignment.InlinedSqlValue != null)
                                {
                                    valueExpr = new LiteralExpr(assignment.InlinedSqlValue, "object");
                                }
                                else
                                {
                                    // Parameter reference
                                    valueExpr = new ParamSlotExpr(paramGlobalIndex - 1, "object", "@p" + (paramGlobalIndex - 1));
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
                        isSensitive: col.IsSensitive));
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
        if (queryKind == QueryKind.Insert && executionSite.Bound.InsertInfo != null)
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

        // Trace logging: chain-level analysis
        var chainUid = executionSite.Bound.Raw.UniqueId;
        IR.TraceCapture.Log(chainUid, "[Trace] ChainAnalysis:");
        IR.TraceCapture.Log(chainUid, $"  tier={tier}, queryKind={queryKind}");
        IR.TraceCapture.Log(chainUid, $"  whereTerms={whereTerms.Count}, orderTerms={orderTerms.Count}, setTerms={setTerms.Count}");
        IR.TraceCapture.Log(chainUid, $"  joinPlans={joinPlans.Count}, params={parameters.Count}");
        IR.TraceCapture.Log(chainUid, $"  possibleMasks=[{string.Join(", ", possibleMasks)}]");
        IR.TraceCapture.Log(chainUid, $"  isTraced={isTraced}");

        return new AnalyzedChain(plan, executionSite, clauseSites, isTraced);
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
            if (raw.IsInsideTryCatch)
                return "Chain contains a clause inside a try/catch/finally block";
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
        bool isTraced = false)
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
            notAnalyzableReason: reason);

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
            or InterceptorKind.InsertToDiagnostics;
    }

    #region Legacy compatibility (will be removed in Step 10)

    /// <summary>
    /// Legacy entry point for the old pipeline. Returns null always since the old syntax-tree-based
    /// analysis has been removed. The old pipeline in QuarryGenerator will gracefully handle null results
    /// by skipping chain optimization for those sites.
    /// </summary>
    [Obsolete("Use Analyze(ImmutableArray<TranslatedCallSite>, EntityRegistry, CancellationToken) instead")]
    public static ChainAnalysisResult? AnalyzeChain(
        UsageSiteInfo executionSite,
        IReadOnlyList<UsageSiteInfo> allSitesInMethod,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        // The old syntax-tree-based analysis has been removed.
        // Return a RuntimeBuild result so the old pipeline falls back gracefully.
        return new ChainAnalysisResult(
            tier: OptimizationTier.RuntimeBuild,
            clauses: Array.Empty<ChainedClauseSite>(),
            executionSite: executionSite,
            conditionalClauses: Array.Empty<ConditionalClause>(),
            possibleMasks: Array.Empty<ulong>(),
            notAnalyzableReason: "Legacy chain analysis disabled — new pipeline active");
    }

    #endregion
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
        bool isTraced = false)
    {
        Plan = plan;
        ExecutionSite = executionSite;
        ClauseSites = clauseSites;
        IsTraced = isTraced;
    }

    /// <summary>The logical query plan.</summary>
    public QueryPlan Plan { get; }

    /// <summary>The execution terminal site.</summary>
    public TranslatedCallSite ExecutionSite { get; }

    /// <summary>All clause sites in the chain (in source order).</summary>
    public IReadOnlyList<TranslatedCallSite> ClauseSites { get; }

    /// <summary>Whether this chain has a .Trace() call and should emit trace comments.</summary>
    public bool IsTraced { get; }
}
