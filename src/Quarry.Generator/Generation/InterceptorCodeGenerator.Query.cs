using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;
using Quarry;
using Quarry.Generators.Projection;
using Quarry.Generators.Translation;

namespace Quarry.Generators.Generation;

internal static partial class InterceptorCodeGenerator
{
    /// <summary>
    /// Generates an interceptor method for a usage site.
    /// </summary>
    private static void GenerateInterceptorMethod(
        StringBuilder sb,
        UsageSiteInfo site,
        List<CachedExtractorField> staticFields,
        Dictionary<string, PrebuiltChainInfo> chainLookup,
        Dictionary<string, int> clauseBitMap,
        Dictionary<string, PrebuiltChainInfo> chainClauseLookup,
        HashSet<string> firstClauseIds,
        Dictionary<string, (CarrierClassInfo Carrier, PrebuiltChainInfo Chain)>? carrierLookup = null,
        Dictionary<string, (CarrierClassInfo Carrier, PrebuiltChainInfo Chain)>? carrierClauseLookup = null,
        HashSet<string>? carrierFirstClauseIds = null)
    {
        // Check if this site belongs to a carrier-optimized chain
        var isCarrierSite = false;
        CarrierClassInfo? carrierInfo = null;
        PrebuiltChainInfo? carrierChain = null;

        if (carrierLookup != null && carrierLookup.TryGetValue(site.UniqueId, out var carrierExec))
        {
            isCarrierSite = true;
            carrierInfo = carrierExec.Carrier;
            carrierChain = carrierExec.Chain;
        }
        else if (carrierClauseLookup != null && carrierClauseLookup.TryGetValue(site.UniqueId, out var carrierClause))
        {
            isCarrierSite = true;
            carrierInfo = carrierClause.Carrier;
            carrierChain = carrierClause.Chain;
        }

        // Check for skippable Select sites BEFORE emitting the attribute
        if (site.Kind == InterceptorKind.Select && ShouldSkipSelectInterceptor(site))
        {
            return; // Don't emit anything for this site
        }

        // Limit/Offset/Distinct/WithTimeout are tracked for chain analysis only on the non-carrier path.
        // On the carrier path, they need interceptors to set carrier fields or act as noops.
        if (site.Kind is InterceptorKind.Limit or InterceptorKind.Offset or InterceptorKind.Distinct or InterceptorKind.WithTimeout)
        {
            if (!isCarrierSite)
                return;
        }

        // For execution interceptors: only emit if we have a pre-built chain for this site
        // AND we can resolve the result type. Otherwise skip — the built-in execution methods
        // work correctly via the Select interceptor.
        if (site.Kind is InterceptorKind.ExecuteFetchAll or InterceptorKind.ExecuteFetchFirst
            or InterceptorKind.ExecuteFetchFirstOrDefault or InterceptorKind.ExecuteFetchSingle
            or InterceptorKind.ToAsyncEnumerable)
        {
            if (!chainLookup.TryGetValue(site.UniqueId, out var chain))
                return;
            // Skip chains with unmatched fluent methods (Limit, Offset, AddWhereClause, etc.)
            // whose effects are invisible to the pre-built SQL.
            if (chain.Analysis.UnmatchedMethodNames != null)
                return;
            // Resolve the best available result type. The site/chain result types come from the
            // semantic model and may be empty or broken for source-generated entity types.
            // The enriched ProjectionInfo.ResultTypeName is rebuilt from entity metadata and is
            // authoritative for tuples and single-column projections.
            var rawResult = ResolveExecutionResultType(site.ResultTypeName, chain.ResultTypeName, chain.ProjectionInfo);
            if (string.IsNullOrEmpty(rawResult))
                return;
            // Must have a reader delegate
            if (chain.ReaderDelegateCode == null)
                return;
            // Skip chains with projections that have SqlExpression set alongside ColumnName.
            // CompileTimeSqlBuilder uses SqlExpression (e.g., "LOWER(col)") but the runtime path
            // uses ColumnName and quotes it. These produce different SQL.
            // Only allow execution interceptors when the SQL will be byte-identical.
            if (chain.ProjectionInfo != null)
            {
                var hasAmbiguousColumns = chain.ProjectionInfo.Columns.Any(c =>
                    c.SqlExpression != null && !string.IsNullOrEmpty(c.ColumnName));
                if (hasAmbiguousColumns)
                    return;
            }
        }
        else if (site.Kind is InterceptorKind.ExecuteScalar)
        {
            if (!chainLookup.TryGetValue(site.UniqueId, out var scalarChain))
                return;
            if (scalarChain.Analysis.UnmatchedMethodNames != null)
                return;
            // ExecuteScalar doesn't need a reader delegate — just pre-built SQL
            var rawScalarResult = ResolveExecutionResultType(site.ResultTypeName, scalarChain.ResultTypeName, scalarChain.ProjectionInfo);
            if (string.IsNullOrEmpty(rawScalarResult))
                return;
        }
        else if (site.Kind is InterceptorKind.ExecuteNonQuery)
        {
            if (!chainLookup.TryGetValue(site.UniqueId, out var nqChain))
                return;
            // Validate that all SQL variants are non-empty and well-formed
            if (nqChain.SqlMap.Values.Any(v => string.IsNullOrWhiteSpace(v.Sql)
                || (nqChain.QueryKind == QueryKind.Update && v.Sql.Contains("SET  "))))
                return;
        }
        else if (site.Kind is InterceptorKind.ToSql)
        {
            if (!chainLookup.TryGetValue(site.UniqueId, out var toSqlChain))
                return;
            if (toSqlChain.Analysis.UnmatchedMethodNames != null)
                return;
        }

        // Skip clause interceptors where the clause could not be translated to SQL.
        // The original runtime method will run instead. A QRY019 diagnostic is reported separately.
        if (ShouldSkipNonTranslatableClause(site))
        {
            return;
        }

        var methodName = $"{site.MethodName}_{site.UniqueId}";

        // Determine chain analysis status for remarks
        PrebuiltChainInfo? chainForRemarks = null;
        chainLookup.TryGetValue(site.UniqueId, out chainForRemarks);
        if (chainForRemarks == null)
            chainClauseLookup.TryGetValue(site.UniqueId, out chainForRemarks);

        // XML documentation
        sb.AppendLine($"    /// <summary>");
        sb.AppendLine($"    /// Intercepts {site.MethodName}() call at {GetRelativePath(site.FilePath)}:{site.Line}:{site.Column}");
        sb.AppendLine($"    /// </summary>");
        if (chainForRemarks != null)
            sb.AppendLine($"    /// <remarks>Chain: Fully Analyzed ({chainForRemarks.Analysis.Tier})</remarks>");
        else
            sb.AppendLine($"    /// <remarks>Chain: Not Analyzed (standalone interceptor)</remarks>");

        // InterceptsLocation attribute - use new format with version and data if available
        if (!string.IsNullOrEmpty(site.InterceptableLocationData))
        {
            sb.AppendLine($"    [InterceptsLocation({site.InterceptableLocationVersion}, \"{site.InterceptableLocationData}\")]");
        }
        else
        {
            // Fallback: skip this site if we don't have valid location data
            sb.AppendLine($"    // WARNING: Could not generate InterceptsLocation for this call site");
            return;
        }

        // Check if this is on a joined builder type
        var isJoinedBuilder = site.JoinedEntityTypeNames != null && site.JoinedEntityTypeNames.Count >= 2;

        // Look up clause bitmask bit index for conditional clause interceptors
        clauseBitMap.TryGetValue(site.UniqueId, out var rawBit);
        int? clauseBit = clauseBitMap.ContainsKey(site.UniqueId) ? rawBit : null;

        // Resolve prebuilt chain membership for clause sites
        chainClauseLookup.TryGetValue(site.UniqueId, out var prebuiltClauseChain);
        var isFirstClauseInChain = prebuiltClauseChain != null && firstClauseIds.Contains(site.UniqueId);

        // For carrier chains, override isFirstInChain with the carrier-specific entry point.
        // In conditional chains, this is the first UNCONDITIONAL clause (not necessarily Clauses[0]).
        if (isCarrierSite && carrierFirstClauseIds != null)
        {
            isFirstClauseInChain = carrierFirstClauseIds.Contains(site.UniqueId);
        }

        // Generate method based on kind
        switch (site.Kind)
        {
            case InterceptorKind.Select:
                if (isJoinedBuilder)
                    GenerateJoinedSelectInterceptor(sb, site, methodName, prebuiltClauseChain, isFirstClauseInChain, carrierInfo);
                else
                    GenerateSelectInterceptor(sb, site, methodName, prebuiltClauseChain, isFirstClauseInChain, carrierInfo);
                break;

            case InterceptorKind.Where:
                if (isJoinedBuilder)
                    GenerateJoinedWhereInterceptor(sb, site, methodName, staticFields, clauseBit, prebuiltClauseChain, isFirstClauseInChain, carrierInfo);
                else
                    GenerateWhereInterceptor(sb, site, methodName, staticFields, clauseBit, prebuiltClauseChain, isFirstClauseInChain, carrierInfo);
                break;

            case InterceptorKind.OrderBy:
            case InterceptorKind.ThenBy:
                if (isJoinedBuilder)
                    GenerateJoinedOrderByInterceptor(sb, site, methodName, clauseBit, prebuiltClauseChain, isFirstClauseInChain, carrierInfo);
                else
                    GenerateOrderByInterceptor(sb, site, methodName, clauseBit, prebuiltClauseChain, isFirstClauseInChain, carrierInfo);
                break;

            case InterceptorKind.GroupBy:
                GenerateGroupByInterceptor(sb, site, methodName, clauseBit, prebuiltClauseChain, isFirstClauseInChain, carrierInfo);
                break;

            case InterceptorKind.Having:
                GenerateHavingInterceptor(sb, site, methodName, clauseBit, prebuiltClauseChain, isFirstClauseInChain, carrierInfo);
                break;

            case InterceptorKind.Set:
                GenerateSetInterceptor(sb, site, methodName, clauseBit, prebuiltClauseChain, isFirstClauseInChain, carrier: carrierInfo);
                break;

            case InterceptorKind.Join:
            case InterceptorKind.LeftJoin:
            case InterceptorKind.RightJoin:
                GenerateJoinInterceptor(sb, site, methodName, prebuiltClauseChain, isFirstClauseInChain, carrierInfo);
                break;

            case InterceptorKind.ExecuteFetchAll:
            case InterceptorKind.ExecuteFetchFirst:
            case InterceptorKind.ExecuteFetchFirstOrDefault:
            case InterceptorKind.ExecuteFetchSingle:
            case InterceptorKind.ExecuteScalar:
            case InterceptorKind.ToAsyncEnumerable:
                if (chainLookup.TryGetValue(site.UniqueId, out var selectChain))
                {
                    if (selectChain.IsJoinChain)
                        GeneratePrebuiltJoinExecutionInterceptor(sb, site, methodName, selectChain, carrierInfo);
                    else
                        GeneratePrebuiltSelectExecutionInterceptor(sb, site, methodName, selectChain, carrierInfo);
                }
                break;

            case InterceptorKind.ExecuteNonQuery:
                if (chainLookup.TryGetValue(site.UniqueId, out var nonQueryChain))
                    GeneratePrebuiltNonQueryExecutionInterceptor(sb, site, methodName, nonQueryChain, carrierInfo);
                break;

            case InterceptorKind.ToSql:
                if (chainLookup.TryGetValue(site.UniqueId, out var toSqlChain))
                    GeneratePrebuiltToSqlInterceptor(sb, site, methodName, toSqlChain, carrierInfo);
                break;

            case InterceptorKind.DeleteWhere:
                GenerateDeleteWhereInterceptor(sb, site, methodName, staticFields, clauseBit, prebuiltClauseChain, isFirstClauseInChain, carrier: carrierInfo);
                break;

            case InterceptorKind.UpdateSet:
                GenerateUpdateSetInterceptor(sb, site, methodName, clauseBit, prebuiltClauseChain, isFirstClauseInChain, carrier: carrierInfo);
                break;

            case InterceptorKind.UpdateSetPoco:
                GenerateUpdateSetPocoInterceptor(sb, site, methodName);
                break;

            case InterceptorKind.UpdateWhere:
                GenerateUpdateWhereInterceptor(sb, site, methodName, staticFields, clauseBit, prebuiltClauseChain, isFirstClauseInChain, carrier: carrierInfo);
                break;

            case InterceptorKind.InsertExecuteNonQuery:
                GenerateInsertExecuteNonQueryInterceptor(sb, site, methodName);
                break;

            case InterceptorKind.InsertExecuteScalar:
                GenerateInsertExecuteScalarInterceptor(sb, site, methodName);
                break;

            case InterceptorKind.InsertToSql:
                GenerateInsertToSqlInterceptor(sb, site, methodName);
                break;

            case InterceptorKind.RawSqlAsync:
                GenerateRawSqlAsyncInterceptor(sb, site, methodName);
                break;

            case InterceptorKind.RawSqlScalarAsync:
                GenerateRawSqlScalarAsyncInterceptor(sb, site, methodName);
                break;

            case InterceptorKind.Limit:
            case InterceptorKind.Offset:
                if (carrierInfo != null && carrierChain != null)
                    GenerateCarrierPaginationInterceptor(sb, site, methodName, carrierInfo, carrierChain);
                break;

            case InterceptorKind.Distinct:
                if (carrierInfo != null && carrierChain != null)
                    GenerateCarrierDistinctInterceptor(sb, site, methodName, carrierInfo, carrierChain);
                break;

            case InterceptorKind.WithTimeout:
                if (carrierInfo != null && carrierChain != null)
                    GenerateCarrierWithTimeoutInterceptor(sb, site, methodName, carrierInfo, carrierChain);
                break;

            default:
                GeneratePlaceholderInterceptor(sb, site, methodName);
                break;
        }

        sb.AppendLine();
    }

    /// <summary>
    /// Generates a Select() interceptor with column list and typed reader delegate.
    /// </summary>
    private static void GenerateSelectInterceptor(StringBuilder sb, UsageSiteInfo site, string methodName,
        PrebuiltChainInfo? prebuiltChain = null, bool isFirstInChain = false, CarrierClassInfo? carrier = null)
    {
        var entityType = GetShortTypeName(site.EntityTypeName);
        var projection = site.ProjectionInfo;

        // Simplified prebuilt chain path: AsProjected instead of SelectWithReader
        if (prebuiltChain != null && projection != null && projection.IsOptimalPath && projection.Columns.Count > 0)
        {
            var resultType = GetShortTypeName(projection.ResultTypeName);
            var thisType = site.BuilderTypeName;
            var concreteType = ToConcreteTypeName(thisType);
            sb.AppendLine($"    public static {thisType}<{entityType}, {resultType}> {methodName}(");
            sb.AppendLine($"        this {thisType}<{entityType}> builder,");
            sb.AppendLine($"        Func<{entityType}, {resultType}> _)");
            sb.AppendLine($"    {{");

            if (carrier != null)
            {
                var targetInterface = $"IQueryBuilder<{entityType}, {resultType}>";
                if (isFirstInChain)
                {
                    // Compute carrier site params
                    var siteParams = new List<ChainParameterInfo>();
                    var globalParamOffset = 0;
                    foreach (var clause in prebuiltChain.Analysis.Clauses)
                    {
                        if (clause.Site.UniqueId == site.UniqueId)
                        {
                            if (clause.Site.ClauseInfo != null)
                                for (int i = 0; i < clause.Site.ClauseInfo.Parameters.Count && globalParamOffset + i < prebuiltChain.ChainParameters.Count; i++)
                                    siteParams.Add(prebuiltChain.ChainParameters[globalParamOffset + i]);
                            break;
                        }
                        if (clause.Site.ClauseInfo != null)
                            globalParamOffset += clause.Site.ClauseInfo.Parameters.Count;
                    }
                    int? clauseBit = null;
                    EmitCarrierChainEntry(sb, carrier, prebuiltChain, site, $"QueryBuilder<{entityType}>", targetInterface, clauseBit, siteParams, globalParamOffset);
                }
                else
                {
                    EmitCarrierSelect(sb, targetInterface);
                }
                sb.AppendLine($"    }}");
                return;
            }

            sb.AppendLine($"        var __b = Unsafe.As<{concreteType}<{entityType}>>(builder);");
            if (isFirstInChain && prebuiltChain.MaxParameterCount > 0)
                sb.AppendLine($"        __b.AllocatePrebuiltParams({prebuiltChain.MaxParameterCount});");
            sb.AppendLine($"        return __b.AsProjected<{resultType}>();");
            sb.AppendLine($"    }}");
            return;
        }

        // If we have projection info and it's optimal, generate the full interceptor
        if (projection != null &&
            projection.IsOptimalPath &&
            projection.Columns.Count > 0)
        {
            GenerateOptimalSelectInterceptor(sb, site, methodName, entityType, projection);
        }
        else
        {
            // Fallback - delegate to the original method
            GenerateFallbackSelectInterceptor(sb, site, methodName, entityType);
        }
    }

    /// <summary>
    /// Generates an optimal Select() interceptor with compile-time column binding.
    /// </summary>
    private static void GenerateOptimalSelectInterceptor(
        StringBuilder sb,
        UsageSiteInfo site,
        string methodName,
        string entityType,
        ProjectionInfo projection)
    {
        // Generate column list SQL using the dialect resolved from the owning context
        var columnList = ReaderCodeGenerator.GenerateColumnList(projection, site.Dialect ?? SqlDialect.PostgreSQL);

        // Generate column names array
        var columnNames = ReaderCodeGenerator.GenerateColumnNamesArray(projection);

        // Generate reader delegate
        var readerDelegate = ReaderCodeGenerator.GenerateReaderDelegate(projection, entityType);

        // At this point, skippable cases (anonymous, tuple, unknown) have already been
        // filtered out by ShouldSkipSelectInterceptor, so we can generate the interceptor
        {
            var resultType = GetShortTypeName(projection.ResultTypeName);
            var thisType = site.BuilderTypeName;
            var concreteType = ToConcreteTypeName(thisType);
            sb.AppendLine($"    public static {thisType}<{entityType}, {resultType}> {methodName}(");
            sb.AppendLine($"        this {thisType}<{entityType}> builder,");
            sb.AppendLine($"        Func<{entityType}, {resultType}> _)");
            sb.AppendLine($"    {{");
            sb.AppendLine($"        // Generated column list: {columnList}");
            sb.AppendLine($"        var __b = Unsafe.As<{concreteType}<{entityType}>>(builder);");
            sb.AppendLine($"        return __b.SelectWithReader(");
            sb.AppendLine($"            {columnNames},");
            sb.AppendLine($"            {readerDelegate});");
            sb.AppendLine($"    }}");
        }
    }

    /// <summary>
    /// Generates a fallback Select() interceptor that delegates to the original method.
    /// </summary>
    private static void GenerateFallbackSelectInterceptor(
        StringBuilder sb,
        UsageSiteInfo site,
        string methodName,
        string entityType)
    {
        // Interceptor arity must match the combined class + method type parameters.
        // QueryBuilder<T>.Select<TResult> has total arity 2 (T + TResult).
        // Use arity 2 so the interceptor can match any Select<TResult> call.
        var thisType = site.BuilderTypeName;
        var concreteType = ToConcreteTypeName(thisType);
        sb.AppendLine($"    public static {thisType}<T, TResult> {methodName}<T, TResult>(");
        sb.AppendLine($"        this {thisType}<T> builder,");
        sb.AppendLine($"        Func<T, TResult> selector) where T : class");
        sb.AppendLine($"    {{");
        sb.AppendLine($"        // Fallback path - projection not fully analyzed at compile time");
        sb.AppendLine($"        var __b = Unsafe.As<{concreteType}<T>>(builder);");
        sb.AppendLine($"        return __b.Select(selector);");
        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Generates a Where() interceptor with SQL fragment and parameter binder.
    /// </summary>
    private static void GenerateWhereInterceptor(StringBuilder sb, UsageSiteInfo site, string methodName, List<CachedExtractorField> staticFields, int? clauseBit = null,
        PrebuiltChainInfo? prebuiltChain = null, bool isFirstInChain = false, CarrierClassInfo? carrier = null)
    {
        var entityType = GetShortTypeName(site.EntityTypeName);
        var clauseInfo = site.ClauseInfo;

        // Check if there are captured parameters that need runtime extraction
        var hasAnyParams = clauseInfo?.Parameters.Count > 0;
        var hasResolvableCapturedParams = clauseInfo?.Parameters.Any(p => p.IsCaptured && p.CanGenerateDirectPath) == true;
        var exprParamName = hasResolvableCapturedParams ? "expr" : "_";

        // Emit trim suppression if we'll use FieldInfo.GetValue inline
        var methodFields = staticFields.Where(f => f.MethodName == methodName).ToList();
        if (methodFields.Count > 0)
        {
            sb.AppendLine($"    [UnconditionalSuppressMessage(\"Trimming\", \"IL2075\",");
            sb.AppendLine($"        Justification = \"Closure fields are preserved by the expression tree that references them.\")]");
        }

        var thisType = site.BuilderTypeName;
        var concreteType = ToConcreteTypeName(thisType);

        // Check if this is on QueryBuilder<T> or QueryBuilder<T, TResult>
        if (site.ResultTypeName != null)
        {
            var resultType = SanitizeTupleResultType(GetShortTypeName(site.ResultTypeName));
            sb.AppendLine($"    public static {thisType}<{entityType}, {resultType}> {methodName}(");
            sb.AppendLine($"        this {thisType}<{entityType}, {resultType}> builder,");
            sb.AppendLine($"        Expression<Func<{entityType}, bool>> {exprParamName})");
        }
        else
        {
            sb.AppendLine($"    public static {thisType}<{entityType}> {methodName}(");
            sb.AppendLine($"        this {thisType}<{entityType}> builder,");
            sb.AppendLine($"        Expression<Func<{entityType}, bool>> {exprParamName})");
        }

        sb.AppendLine($"    {{");

        if (clauseInfo == null || !clauseInfo.IsSuccess)
        {
            // Non-translatable clause — skip interceptor entirely so the original method runs
            return;
        }

        // Carrier-optimized path
        if (carrier != null && prebuiltChain != null)
        {
            var concreteBuilder = $"QueryBuilder<{entityType}>";
            var retInterface = site.ResultTypeName != null
                ? $"IQueryBuilder<{entityType}, {SanitizeTupleResultType(GetShortTypeName(site.ResultTypeName))}>"
                : $"IQueryBuilder<{entityType}>";
            EmitCarrierClauseBody(sb, carrier, prebuiltChain, site, clauseBit, isFirstInChain,
                concreteBuilder, retInterface, hasResolvableCapturedParams, methodFields);
            sb.AppendLine($"    }}");
            return;
        }

        // Cast to concrete type (receiver is always an interface)
        if (site.ResultTypeName != null)
        {
            var resultType = SanitizeTupleResultType(GetShortTypeName(site.ResultTypeName));
            sb.AppendLine($"        var __b = Unsafe.As<{concreteType}<{entityType}, {resultType}>>(builder);");
        }
        else
        {
            sb.AppendLine($"        var __b = Unsafe.As<{concreteType}<{entityType}>>(builder);");
        }
        var builderVar = "__b";

        // Simplified prebuilt chain path: BindParam instead of AddWhereClause
        if (prebuiltChain != null)
        {
            if (isFirstInChain && prebuiltChain.MaxParameterCount > 0)
                sb.AppendLine($"        {builderVar}.AllocatePrebuiltParams({prebuiltChain.MaxParameterCount});");

            if (hasAnyParams == true)
            {
                if (hasResolvableCapturedParams)
                    GenerateCachedExtraction(sb, methodFields);

                var allParams = clauseInfo!.Parameters.OrderBy(p => p.Index).ToList();
                foreach (var p in allParams)
                {
                    var expr = p.IsCaptured ? $"p{p.Index}" : p.ValueExpression;
                    sb.AppendLine($"        {builderVar}.BindParam({WrapWithToDb(expr, p)});");
                }
            }

            if (clauseBit.HasValue)
                sb.AppendLine($"        return {builderVar}.SetClauseBit({clauseBit.Value});");
            else
                sb.AppendLine($"        return {builderVar};");
            sb.AppendLine($"    }}");
            return;
        }

        {
            // Generate optimized interceptor with pre-computed SQL
            var escapedSql = EscapeStringLiteral(clauseInfo.SqlFragment);

            // Check if any captured parameters lack extraction paths (e.g., captured variables in subqueries)
            var hasUnresolvableCaptured = clauseInfo.Parameters.Any(p => p.IsCaptured && !p.CanGenerateDirectPath);

            var bitSuffix = ClauseBitSuffix(clauseBit);
            if (hasUnresolvableCaptured)
            {
                // Emit SQL-only clause (parameters cannot be extracted at compile time)
                var resolvableParams = clauseInfo.Parameters
                    .Where(p => !p.IsCaptured)
                    .OrderBy(p => p.Index)
                    .ToList();
                if (resolvableParams.Count > 0)
                {
                    var paramArgs = string.Join(", ", resolvableParams.Select(p => WrapWithToDb(p.ValueExpression, p)));
                    sb.AppendLine($"        return {builderVar}.AddWhereClause(@\"{escapedSql}\", {paramArgs}){bitSuffix};");
                }
                else
                {
                    sb.AppendLine($"        return {builderVar}.AddWhereClause(@\"{escapedSql}\"){bitSuffix};");
                }
            }
            else if (hasAnyParams)
            {
                if (hasResolvableCapturedParams)
                    GenerateCachedExtraction(sb, methodFields);

                // Build args in index order, mixing captured extractions + literal values
                var allParams = clauseInfo.Parameters.OrderBy(p => p.Index).ToList();
                var paramArgs = string.Join(", ", allParams.Select(p =>
                {
                    var expr = p.IsCaptured ? $"p{p.Index}" : p.ValueExpression;
                    return WrapWithToDb(expr, p);
                }));
                sb.AppendLine($"        return {builderVar}.AddWhereClause(@\"{escapedSql}\", {paramArgs}){bitSuffix};");
            }
            else
            {
                sb.AppendLine($"        return {builderVar}.AddWhereClause(@\"{escapedSql}\"){bitSuffix};");
            }
        }

        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Generates an OrderBy/ThenBy interceptor with SQL fragment.
    /// </summary>
    private static void GenerateOrderByInterceptor(StringBuilder sb, UsageSiteInfo site, string methodName, int? clauseBit = null,
        PrebuiltChainInfo? prebuiltChain = null, bool isFirstInChain = false, CarrierClassInfo? carrier = null)
    {
        var entityType = GetShortTypeName(site.EntityTypeName);
        var clauseInfo = site.ClauseInfo;
        var isOrderBy = site.Kind == InterceptorKind.OrderBy;

        // Use concrete key type (arity 0) when available; otherwise fall back to arity-matching
        var keyType = site.KeyTypeName != null ? GetShortTypeName(site.KeyTypeName) : null;

        var thisType = site.BuilderTypeName;
        var concreteType = ToConcreteTypeName(thisType);

        // Check if this is on QueryBuilder<T> or QueryBuilder<T, TResult>
        if (site.ResultTypeName != null)
        {
            var resultType = SanitizeTupleResultType(GetShortTypeName(site.ResultTypeName));
            if (keyType != null)
            {
                // Non-generic interceptor (arity 0) — concrete key type
                sb.AppendLine($"    public static {thisType}<{entityType}, {resultType}> {methodName}(");
                sb.AppendLine($"        this {thisType}<{entityType}, {resultType}> builder,");
                sb.AppendLine($"        Expression<Func<{entityType}, {keyType}>> _,");
                sb.AppendLine($"        Direction direction = Direction.Ascending)");
            }
            else
            {
                // Arity-matching interceptor — include all type params from class + method
                sb.AppendLine($"    public static {thisType}<T, TResult> {methodName}<T, TResult, TKey>(");
                sb.AppendLine($"        this {thisType}<T, TResult> builder,");
                sb.AppendLine($"        Expression<Func<T, TKey>> _,");
                sb.AppendLine($"        Direction direction = Direction.Ascending) where T : class");
            }
        }
        else
        {
            if (keyType != null)
            {
                // Non-generic interceptor (arity 0) — concrete key type
                sb.AppendLine($"    public static {thisType}<{entityType}> {methodName}(");
                sb.AppendLine($"        this {thisType}<{entityType}> builder,");
                sb.AppendLine($"        Expression<Func<{entityType}, {keyType}>> _,");
                sb.AppendLine($"        Direction direction = Direction.Ascending)");
            }
            else
            {
                // Arity-matching interceptor — include all type params from class + method
                sb.AppendLine($"    public static {thisType}<T> {methodName}<T, TKey>(");
                sb.AppendLine($"        this {thisType}<T> builder,");
                sb.AppendLine($"        Expression<Func<T, TKey>> _,");
                sb.AppendLine($"        Direction direction = Direction.Ascending) where T : class");
            }
        }

        sb.AppendLine($"    {{");

        // Carrier-optimized path
        // Only when concrete key type is available (open-generic fallback can't use carrier types)
        if (carrier != null && prebuiltChain != null && keyType != null)
        {
            var concreteBuilder = $"QueryBuilder<{entityType}>";
            var retInterface = site.ResultTypeName != null
                ? $"IQueryBuilder<{entityType}, {SanitizeTupleResultType(GetShortTypeName(site.ResultTypeName))}>"
                : $"IQueryBuilder<{entityType}>";
            EmitCarrierClauseBody(sb, carrier, prebuiltChain, site, clauseBit, isFirstInChain,
                concreteBuilder, retInterface, false, new List<CachedExtractorField>());
            sb.AppendLine($"    }}");
            return;
        }

        // Cast to concrete type (receiver is always an interface)
        if (site.ResultTypeName != null)
        {
            var resultType = SanitizeTupleResultType(GetShortTypeName(site.ResultTypeName));
            sb.AppendLine($"        var __b = Unsafe.As<{concreteType}<{entityType}, {resultType}>>(builder);");
        }
        else if (keyType != null)
        {
            sb.AppendLine($"        var __b = Unsafe.As<{concreteType}<{entityType}>>(builder);");
        }
        else
        {
            sb.AppendLine($"        var __b = Unsafe.As<{concreteType}<T>>(builder);");
        }
        var builderVar = "__b";

        // Simplified prebuilt chain path: passthrough (no SQL generation)
        if (prebuiltChain != null)
        {
            if (isFirstInChain && prebuiltChain.MaxParameterCount > 0)
                sb.AppendLine($"        {builderVar}.AllocatePrebuiltParams({prebuiltChain.MaxParameterCount});");
            if (clauseBit.HasValue)
            {
                sb.AppendLine($"        {builderVar}.SetClauseBit({clauseBit.Value});");
                sb.AppendLine($"        return builder;");
            }
            else
            {
                sb.AppendLine($"        return builder;");
            }
            sb.AppendLine($"    }}");
            return;
        }

        // When using open generic type params (arity-matching), the concrete return type
        // can't be implicitly converted to the open generic interface return type.
        // Cast through Unsafe.As to bridge the gap.
        var needsReturnCast = keyType == null;
        string returnCastOpen;
        string returnCastClose;
        if (needsReturnCast)
        {
            var returnType = site.ResultTypeName != null ? $"{thisType}<T, TResult>" : $"{thisType}<T>";
            returnCastOpen = $"Unsafe.As<{returnType}>(";
            returnCastClose = ")";
        }
        else
        {
            returnCastOpen = "";
            returnCastClose = "";
        }

        var bitSuffix = ClauseBitSuffix(clauseBit);
        if (clauseInfo is OrderByClauseInfo orderByInfo && orderByInfo.IsSuccess)
        {
            // Generate optimized interceptor with pre-computed SQL
            var escapedSql = EscapeStringLiteral(orderByInfo.ColumnSql);
            var directionSuffix = orderByInfo.IsDescending ? " DESC" : " ASC";
            var builderMethod = isOrderBy ? "AddOrderByClause" : "AddThenByClause";
            sb.AppendLine($"        return {returnCastOpen}{builderVar}.{builderMethod}(@\"{escapedSql}\", direction){bitSuffix}{returnCastClose};");
        }
        else if (clauseInfo != null && clauseInfo.IsSuccess)
        {
            // Non-OrderByClauseInfo but still successful
            var escapedSql = EscapeStringLiteral(clauseInfo.SqlFragment);
            var builderMethod = isOrderBy ? "AddOrderByClause" : "AddThenByClause";
            sb.AppendLine($"        return {returnCastOpen}{builderVar}.{builderMethod}(@\"{escapedSql}\", direction){bitSuffix}{returnCastClose};");
        }
        else
        {
            // Non-translatable clause — skip interceptor entirely so the original method runs
            return;
        }

        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Generates a GroupBy interceptor with SQL fragment.
    /// </summary>
    private static void GenerateGroupByInterceptor(StringBuilder sb, UsageSiteInfo site, string methodName, int? clauseBit = null,
        PrebuiltChainInfo? prebuiltChain = null, bool isFirstInChain = false, CarrierClassInfo? carrier = null)
    {
        var entityType = GetShortTypeName(site.EntityTypeName);
        var clauseInfo = site.ClauseInfo;

        // Use concrete key type (arity 0) when available; otherwise fall back to arity-matching
        var keyType = site.KeyTypeName != null ? GetShortTypeName(site.KeyTypeName) : null;

        var thisType = site.BuilderTypeName;
        var concreteType = ToConcreteTypeName(thisType);

        // Check if this is on QueryBuilder<T> or QueryBuilder<T, TResult>
        if (site.ResultTypeName != null)
        {
            var resultType = SanitizeTupleResultType(GetShortTypeName(site.ResultTypeName));
            if (keyType != null)
            {
                sb.AppendLine($"    public static {thisType}<{entityType}, {resultType}> {methodName}(");
                sb.AppendLine($"        this {thisType}<{entityType}, {resultType}> builder,");
                sb.AppendLine($"        Expression<Func<{entityType}, {keyType}>> _)");
            }
            else
            {
                sb.AppendLine($"    public static {thisType}<T, TResult> {methodName}<T, TResult, TKey>(");
                sb.AppendLine($"        this {thisType}<T, TResult> builder,");
                sb.AppendLine($"        Expression<Func<T, TKey>> _) where T : class");
            }
        }
        else
        {
            if (keyType != null)
            {
                sb.AppendLine($"    public static {thisType}<{entityType}> {methodName}(");
                sb.AppendLine($"        this {thisType}<{entityType}> builder,");
                sb.AppendLine($"        Expression<Func<{entityType}, {keyType}>> _)");
            }
            else
            {
                sb.AppendLine($"    public static {thisType}<T> {methodName}<T, TKey>(");
                sb.AppendLine($"        this {thisType}<T> builder,");
                sb.AppendLine($"        Expression<Func<T, TKey>> _) where T : class");
            }
        }

        sb.AppendLine($"    {{");

        if (clauseInfo == null || !clauseInfo.IsSuccess)
        {
            // Non-translatable clause — skip interceptor entirely so the original method runs
            return;
        }

        // Carrier-optimized path
        // Only when concrete key type is available (open-generic fallback can't use carrier types)
        if (carrier != null && prebuiltChain != null && keyType != null)
        {
            var concreteBuilder = $"QueryBuilder<{entityType}>";
            var retInterface = site.ResultTypeName != null
                ? $"IQueryBuilder<{entityType}, {SanitizeTupleResultType(GetShortTypeName(site.ResultTypeName))}>"
                : $"IQueryBuilder<{entityType}>";
            EmitCarrierClauseBody(sb, carrier, prebuiltChain, site, clauseBit, isFirstInChain,
                concreteBuilder, retInterface, false, new List<CachedExtractorField>());
            sb.AppendLine($"    }}");
            return;
        }

        // Cast to concrete type (receiver is always an interface)
        if (site.ResultTypeName != null)
        {
            var resultType = SanitizeTupleResultType(GetShortTypeName(site.ResultTypeName));
            sb.AppendLine($"        var __b = Unsafe.As<{concreteType}<{entityType}, {resultType}>>(builder);");
        }
        else if (keyType != null)
        {
            sb.AppendLine($"        var __b = Unsafe.As<{concreteType}<{entityType}>>(builder);");
        }
        else
        {
            sb.AppendLine($"        var __b = Unsafe.As<{concreteType}<T>>(builder);");
        }
        var builderVar = "__b";

        // Simplified prebuilt chain path: passthrough (no SQL generation)
        if (prebuiltChain != null)
        {
            if (isFirstInChain && prebuiltChain.MaxParameterCount > 0)
                sb.AppendLine($"        {builderVar}.AllocatePrebuiltParams({prebuiltChain.MaxParameterCount});");
            if (clauseBit.HasValue)
            {
                sb.AppendLine($"        {builderVar}.SetClauseBit({clauseBit.Value});");
                sb.AppendLine($"        return builder;");
            }
            else
            {
                sb.AppendLine($"        return builder;");
            }
            sb.AppendLine($"    }}");
            return;
        }

        {
            // Generate optimized interceptor with pre-computed SQL
            var escapedSql = EscapeStringLiteral(clauseInfo.SqlFragment);
            var bitSuffix = ClauseBitSuffix(clauseBit);

            // When using open generic type params (arity-matching), cast return through Unsafe.As
            var needsReturnCast = keyType == null;
            string returnCastOpen = "", returnCastClose = "";
            if (needsReturnCast)
            {
                var returnType = site.ResultTypeName != null ? $"{thisType}<T, TResult>" : $"{thisType}<T>";
                returnCastOpen = $"Unsafe.As<{returnType}>(";
                returnCastClose = ")";
            }

            sb.AppendLine($"        return {returnCastOpen}{builderVar}.AddGroupByClause(@\"{escapedSql}\"){bitSuffix}{returnCastClose};");
        }

        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Generates a Having interceptor with SQL fragment.
    /// </summary>
    private static void GenerateHavingInterceptor(StringBuilder sb, UsageSiteInfo site, string methodName, int? clauseBit = null,
        PrebuiltChainInfo? prebuiltChain = null, bool isFirstInChain = false, CarrierClassInfo? carrier = null)
    {
        var entityType = GetShortTypeName(site.EntityTypeName);
        var clauseInfo = site.ClauseInfo;

        var thisType = site.BuilderTypeName;
        var concreteType = ToConcreteTypeName(thisType);

        // Check if this is on QueryBuilder<T> or QueryBuilder<T, TResult>
        if (site.ResultTypeName != null)
        {
            var resultType = SanitizeTupleResultType(GetShortTypeName(site.ResultTypeName));
            sb.AppendLine($"    public static {thisType}<{entityType}, {resultType}> {methodName}(");
            sb.AppendLine($"        this {thisType}<{entityType}, {resultType}> builder,");
            sb.AppendLine($"        Expression<Func<{entityType}, bool>> _)");
        }
        else
        {
            sb.AppendLine($"    public static {thisType}<{entityType}> {methodName}(");
            sb.AppendLine($"        this {thisType}<{entityType}> builder,");
            sb.AppendLine($"        Expression<Func<{entityType}, bool>> _)");
        }

        sb.AppendLine($"    {{");

        if (clauseInfo == null || !clauseInfo.IsSuccess)
        {
            // Non-translatable clause — skip interceptor entirely so the original method runs
            return;
        }

        // Carrier-optimized path
        if (carrier != null && prebuiltChain != null)
        {
            var concreteBuilder = $"QueryBuilder<{entityType}>";
            var retInterface = site.ResultTypeName != null
                ? $"IQueryBuilder<{entityType}, {SanitizeTupleResultType(GetShortTypeName(site.ResultTypeName))}>"
                : $"IQueryBuilder<{entityType}>";
            EmitCarrierClauseBody(sb, carrier, prebuiltChain, site, clauseBit, isFirstInChain,
                concreteBuilder, retInterface, false, new List<CachedExtractorField>());
            sb.AppendLine($"    }}");
            return;
        }

        // Cast to concrete type (receiver is always an interface)
        if (site.ResultTypeName != null)
        {
            var resultType = SanitizeTupleResultType(GetShortTypeName(site.ResultTypeName));
            sb.AppendLine($"        var __b = Unsafe.As<{concreteType}<{entityType}, {resultType}>>(builder);");
        }
        else
        {
            sb.AppendLine($"        var __b = Unsafe.As<{concreteType}<{entityType}>>(builder);");
        }
        var builderVar = "__b";

        // Simplified prebuilt chain path: passthrough (no SQL generation)
        if (prebuiltChain != null)
        {
            if (isFirstInChain && prebuiltChain.MaxParameterCount > 0)
                sb.AppendLine($"        {builderVar}.AllocatePrebuiltParams({prebuiltChain.MaxParameterCount});");
            if (clauseBit.HasValue)
            {
                sb.AppendLine($"        {builderVar}.SetClauseBit({clauseBit.Value});");
                sb.AppendLine($"        return builder;");
            }
            else
            {
                sb.AppendLine($"        return builder;");
            }
            sb.AppendLine($"    }}");
            return;
        }

        {
            // Generate optimized interceptor with pre-computed SQL
            var escapedSql = EscapeStringLiteral(clauseInfo.SqlFragment);
            var bitSuffix = ClauseBitSuffix(clauseBit);
            sb.AppendLine($"        return {builderVar}.AddHavingClause(@\"{escapedSql}\"){bitSuffix};");
        }

        sb.AppendLine($"    }}");
    }
}
