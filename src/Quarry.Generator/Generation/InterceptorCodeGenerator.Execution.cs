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
    // ───────────────────────────────────────────────────────────────
    // Pre-built SQL Execution Interceptors (Tier 1)
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a tier 1 execution interceptor for SELECT queries (ExecuteFetchAll, ExecuteFetchFirst, etc.).
    /// Contains a dispatch table that maps ClauseMask → pre-built SQL string literal.
    /// </summary>
    private static void GeneratePrebuiltSelectExecutionInterceptor(
        StringBuilder sb,
        UsageSiteInfo site,
        string methodName,
        PrebuiltChainInfo chain,
        CarrierClassInfo? carrier = null)
    {
        var entityType = GetShortTypeName(chain.EntityTypeName);

        // Resolve result type using the same logic as the gate in GenerateInterceptorMethod.
        // ResolveExecutionResultType returns the enriched type for tuples and single-column
        // projections, which already has correct element types and names.
        var rawResultType = ResolveExecutionResultType(
            site.ResultTypeName, chain.ResultTypeName, chain.ProjectionInfo);

        // Don't strip tuple element names — CS9148 requires exact match including names.
        var resultType = !string.IsNullOrEmpty(rawResultType)
            ? GetShortTypeName(rawResultType!)
            : entityType;

        // Determine return type and executor method from the execution kind.
        // When the chain has parameters (MaxParameterCount > 0), use the prebuilt params variants
        // which hydrate parameters from the pre-allocated array at terminal time.
        var usePrebuiltParams = chain.MaxParameterCount > 0;
        string returnType;
        string executorMethod;
        bool hasReader = true;

        switch (site.Kind)
        {
            case InterceptorKind.ExecuteFetchAll:
                returnType = $"Task<List<{resultType}>>";
                executorMethod = usePrebuiltParams ? "ExecuteWithPrebuiltParamsAsync" : "ExecuteWithPrebuiltSqlAsync";
                break;
            case InterceptorKind.ExecuteFetchFirst:
                returnType = $"Task<{resultType}>";
                executorMethod = usePrebuiltParams ? "ExecuteFirstWithPrebuiltParamsAsync" : "ExecuteFirstWithPrebuiltSqlAsync";
                break;
            case InterceptorKind.ExecuteFetchFirstOrDefault:
                returnType = $"Task<{resultType}?>";
                executorMethod = usePrebuiltParams ? "ExecuteFirstOrDefaultWithPrebuiltParamsAsync" : "ExecuteFirstOrDefaultWithPrebuiltSqlAsync";
                break;
            case InterceptorKind.ExecuteFetchSingle:
                returnType = $"Task<{resultType}>";
                executorMethod = usePrebuiltParams ? "ExecuteSingleWithPrebuiltParamsAsync" : "ExecuteSingleWithPrebuiltSqlAsync";
                break;
            case InterceptorKind.ExecuteScalar:
                // ExecuteScalar has a type parameter TScalar
                returnType = $"Task<TScalar>";
                executorMethod = usePrebuiltParams ? "ExecuteScalarWithPrebuiltParamsAsync<TScalar>" : "ExecuteScalarWithPrebuiltSqlAsync<TScalar>";
                hasReader = false;
                break;
            case InterceptorKind.ToAsyncEnumerable:
                returnType = $"IAsyncEnumerable<{resultType}>";
                executorMethod = usePrebuiltParams ? "ToAsyncEnumerableWithPrebuiltParams" : "ToAsyncEnumerableWithPrebuiltSql";
                break;
            default:
                return;
        }

        var thisType = site.BuilderTypeName;
        var concreteType = ToConcreteTypeName(thisType);

        // Method signature
        if (site.Kind == InterceptorKind.ExecuteScalar)
        {
            // ExecuteScalar has arity 3: class-level TEntity+TResult + method-level TScalar.
            // The interceptor must match this total arity.
            sb.AppendLine($"    public static {returnType} {methodName}<TEntity, TResult, TScalar>(");
            sb.AppendLine($"        this {thisType}<TEntity, TResult> builder,");
            sb.AppendLine($"        CancellationToken cancellationToken = default) where TEntity : class");
        }
        else
        {
            sb.AppendLine($"    public static {returnType} {methodName}(");
            sb.AppendLine($"        this {thisType}<{entityType}, {resultType}> builder,");
            sb.AppendLine($"        CancellationToken cancellationToken = default)");
        }

        sb.AppendLine($"    {{");

        if (carrier != null)
        {
            // Guard: use the same predicates as WouldExecutionTerminalBeEmitted
            var canEmit = site.Kind == InterceptorKind.ExecuteScalar
                ? CanEmitScalarTerminal(chain)
                : CanEmitReaderTerminal(chain);

            if (canEmit)
            {
                var carrierExecutorMethod = site.Kind switch
                {
                    InterceptorKind.ExecuteFetchAll => $"ExecuteCarrierWithCommandAsync<{resultType}>",
                    InterceptorKind.ExecuteFetchFirst => $"ExecuteCarrierFirstWithCommandAsync<{resultType}>",
                    InterceptorKind.ExecuteFetchFirstOrDefault => $"ExecuteCarrierFirstOrDefaultWithCommandAsync<{resultType}>",
                    InterceptorKind.ExecuteFetchSingle => $"ExecuteCarrierSingleWithCommandAsync<{resultType}>",
                    InterceptorKind.ExecuteScalar => "ExecuteCarrierScalarWithCommandAsync<TScalar>",
                    InterceptorKind.ToAsyncEnumerable => $"ToCarrierAsyncEnumerableWithCommandAsync<{resultType}>",
                    _ => ""
                };
                var readerCode = site.Kind == InterceptorKind.ExecuteScalar ? null : chain.ReaderDelegateCode;
                EmitCarrierExecutionTerminal(sb, carrier, chain, readerCode, carrierExecutorMethod);
                sb.AppendLine($"    }}");
                return;
            }
        }

        // Cast to concrete type (receiver is always an interface)
        if (site.Kind == InterceptorKind.ExecuteScalar)
        {
            sb.AppendLine($"        var __b = Unsafe.As<{concreteType}<TEntity, TResult>>(builder);");
        }
        else
        {
            sb.AppendLine($"        var __b = Unsafe.As<{concreteType}<{entityType}, {resultType}>>(builder);");
        }
        var builderVar = "__b";

        // Dispatch table: switch on ClauseMask
        GenerateDispatchTable(sb, chain.SqlMap, builderVar);

        // Call the executor
        if (hasReader && chain.ReaderDelegateCode != null)
        {
            sb.AppendLine($"        return {builderVar}.{executorMethod}(sql, {chain.ReaderDelegateCode}, cancellationToken);");
        }
        else if (hasReader)
        {
            // Fallback: no reader available — shouldn't happen for tier 1 SELECT, but be safe
            sb.AppendLine($"        return {builderVar}.{executorMethod}(sql, cancellationToken);");
        }
        else
        {
            // ExecuteScalar: no reader delegate
            sb.AppendLine($"        return {builderVar}.{executorMethod}(sql, cancellationToken);");
        }

        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Generates a tier 1 execution interceptor for joined query execution (multi-entity SELECT).
    /// </summary>
    private static void GeneratePrebuiltJoinExecutionInterceptor(
        StringBuilder sb,
        UsageSiteInfo site,
        string methodName,
        PrebuiltChainInfo chain,
        CarrierClassInfo? carrier = null)
    {
        var joinedNames = chain.JoinedEntityTypeNames!;
        var entityCount = joinedNames.Count;
        var builderName = GetJoinedBuilderTypeName(entityCount);
        var concreteBuilderName = ToConcreteTypeName(builderName);
        var thisType = site.BuilderTypeName;
        var thisBuilderName = builderName;
        var entityTypes = joinedNames.Select(n => GetShortTypeName(n)).ToList();
        var entityTypeArgs = string.Join(", ", entityTypes);

        var rawResultType = ResolveExecutionResultType(
            site.ResultTypeName, chain.ResultTypeName, chain.ProjectionInfo);
        var resultType = !string.IsNullOrEmpty(rawResultType)
            ? GetShortTypeName(rawResultType!)
            : entityTypes[0];

        var usePrebuiltParams = chain.MaxParameterCount > 0;
        string returnType;
        string executorMethod;
        bool hasReader = true;

        switch (site.Kind)
        {
            case InterceptorKind.ExecuteFetchAll:
                returnType = $"Task<List<{resultType}>>";
                executorMethod = usePrebuiltParams ? "ExecuteWithPrebuiltParamsAsync" : "ExecuteWithPrebuiltSqlAsync";
                break;
            case InterceptorKind.ExecuteFetchFirst:
                returnType = $"Task<{resultType}>";
                executorMethod = usePrebuiltParams ? "ExecuteFirstWithPrebuiltParamsAsync" : "ExecuteFirstWithPrebuiltSqlAsync";
                break;
            case InterceptorKind.ExecuteFetchFirstOrDefault:
                returnType = $"Task<{resultType}?>";
                executorMethod = usePrebuiltParams ? "ExecuteFirstOrDefaultWithPrebuiltParamsAsync" : "ExecuteFirstOrDefaultWithPrebuiltSqlAsync";
                break;
            case InterceptorKind.ExecuteFetchSingle:
                returnType = $"Task<{resultType}>";
                executorMethod = usePrebuiltParams ? "ExecuteSingleWithPrebuiltParamsAsync" : "ExecuteSingleWithPrebuiltSqlAsync";
                break;
            case InterceptorKind.ExecuteScalar:
                returnType = $"Task<TScalar>";
                executorMethod = usePrebuiltParams ? "ExecuteScalarWithPrebuiltParamsAsync<TScalar>" : "ExecuteScalarWithPrebuiltSqlAsync<TScalar>";
                hasReader = false;
                break;
            case InterceptorKind.ToAsyncEnumerable:
                returnType = $"IAsyncEnumerable<{resultType}>";
                executorMethod = usePrebuiltParams ? "ToAsyncEnumerableWithPrebuiltParams" : "ToAsyncEnumerableWithPrebuiltSql";
                break;
            default:
                return;
        }

        // Method signature with joined builder receiver type
        if (site.Kind == InterceptorKind.ExecuteScalar)
        {
            sb.AppendLine($"    public static {returnType} {methodName}<{entityTypeArgs}, TResult, TScalar>(");
            sb.AppendLine($"        this {thisBuilderName}<{entityTypeArgs}, TResult> builder,");
            sb.AppendLine($"        CancellationToken cancellationToken = default) where TScalar : struct");
        }
        else
        {
            sb.AppendLine($"    public static {returnType} {methodName}(");
            sb.AppendLine($"        this {thisBuilderName}<{entityTypeArgs}, {resultType}> builder,");
            sb.AppendLine($"        CancellationToken cancellationToken = default)");
        }

        sb.AppendLine($"    {{");

        if (carrier != null && CanEmitReaderTerminal(chain))
        {
            var carrierExecutorMethod = site.Kind switch
            {
                InterceptorKind.ExecuteFetchAll => $"ExecuteCarrierWithCommandAsync<{resultType}>",
                InterceptorKind.ExecuteFetchFirst => $"ExecuteCarrierFirstWithCommandAsync<{resultType}>",
                InterceptorKind.ExecuteFetchFirstOrDefault => $"ExecuteCarrierFirstOrDefaultWithCommandAsync<{resultType}>",
                InterceptorKind.ExecuteFetchSingle => $"ExecuteCarrierSingleWithCommandAsync<{resultType}>",
                InterceptorKind.ToAsyncEnumerable => $"ToCarrierAsyncEnumerableWithCommandAsync<{resultType}>",
                _ => ""
            };
            EmitCarrierExecutionTerminal(sb, carrier, chain, chain.ReaderDelegateCode, carrierExecutorMethod);
            sb.AppendLine($"    }}");
            return;
        }

        // Cast to concrete type (receiver is always an interface)
        if (site.Kind == InterceptorKind.ExecuteScalar)
        {
            sb.AppendLine($"        var __b = Unsafe.As<{concreteBuilderName}<{entityTypeArgs}, TResult>>(builder);");
        }
        else
        {
            sb.AppendLine($"        var __b = Unsafe.As<{concreteBuilderName}<{entityTypeArgs}, {resultType}>>(builder);");
        }
        var builderVar = "__b";

        // Dispatch table: switch on ClauseMask
        GenerateDispatchTable(sb, chain.SqlMap, builderVar);

        // Call the executor
        if (hasReader && chain.ReaderDelegateCode != null)
        {
            sb.AppendLine($"        return {builderVar}.{executorMethod}(sql, {chain.ReaderDelegateCode}, cancellationToken);");
        }
        else if (hasReader)
        {
            sb.AppendLine($"        return {builderVar}.{executorMethod}(sql, cancellationToken);");
        }
        else
        {
            sb.AppendLine($"        return {builderVar}.{executorMethod}(sql, cancellationToken);");
        }

        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Generates a tier 1 execution interceptor for non-query operations (DELETE/UPDATE ExecuteNonQueryAsync).
    /// </summary>
    private static void GeneratePrebuiltNonQueryExecutionInterceptor(
        StringBuilder sb,
        UsageSiteInfo site,
        string methodName,
        PrebuiltChainInfo chain,
        CarrierClassInfo? carrier = null)
    {
        var entityType = GetShortTypeName(chain.EntityTypeName);

        var thisType = site.BuilderTypeName;

        // Determine builder type name based on query kind
        string concreteBuilderTypeName;
        string thisBuilderTypeName;
        switch (chain.QueryKind)
        {
            case QueryKind.Delete:
                concreteBuilderTypeName = $"ExecutableDeleteBuilder<{entityType}>";
                thisBuilderTypeName = $"IExecutableDeleteBuilder<{entityType}>";
                break;
            case QueryKind.Update:
                concreteBuilderTypeName = $"ExecutableUpdateBuilder<{entityType}>";
                thisBuilderTypeName = $"IExecutableUpdateBuilder<{entityType}>";
                break;
            default:
                return;
        }

        sb.AppendLine($"    public static Task<int> {methodName}(");
        sb.AppendLine($"        this {thisBuilderTypeName} builder,");
        sb.AppendLine($"        CancellationToken cancellationToken = default)");
        sb.AppendLine($"    {{");

        if (carrier != null && CanEmitNonQueryTerminal(chain))
        {
            EmitCarrierNonQueryTerminal(sb, carrier, chain);
            sb.AppendLine($"    }}");
            return;
        }

        // Cast to concrete type (receiver is always an interface)
        sb.AppendLine($"        var __b = Unsafe.As<{concreteBuilderTypeName}>(builder);");
        var builderVar = "__b";

        // Dispatch table: switch on ClauseMask
        GenerateDispatchTable(sb, chain.SqlMap, builderVar);

        sb.AppendLine($"        return {builderVar}.ExecuteWithPrebuiltSqlAsync(sql, cancellationToken);");
        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Generates a tier 1 ToDiagnostics interceptor that returns a QueryDiagnostics with pre-built SQL
    /// and optimization metadata.
    /// </summary>
    private static void GeneratePrebuiltToDiagnosticsInterceptor(
        StringBuilder sb,
        UsageSiteInfo site,
        string methodName,
        PrebuiltChainInfo chain,
        CarrierClassInfo? carrier = null)
    {
        var entityType = GetShortTypeName(chain.EntityTypeName);
        var thisType = site.BuilderTypeName;
        var concreteType = ToConcreteTypeName(thisType);

        // Determine the full this-parameter type based on query kind and builder shape
        string thisParamType;
        string concreteParamType;

        if (chain.IsJoinChain)
        {
            var joinedNames = chain.JoinedEntityTypeNames!;
            var joinTypeArgs = string.Join(", ", joinedNames.Select(GetShortTypeName));

            if (chain.ResultTypeName != null)
            {
                var resultType = GetShortTypeName(
                    ResolveExecutionResultType(site.ResultTypeName, chain.ResultTypeName, chain.ProjectionInfo)
                    ?? chain.ResultTypeName);
                thisParamType = $"{thisType}<{joinTypeArgs}, {resultType}>";
                concreteParamType = $"{concreteType}<{joinTypeArgs}, {resultType}>";
            }
            else
            {
                thisParamType = $"{thisType}<{joinTypeArgs}>";
                concreteParamType = $"{concreteType}<{joinTypeArgs}>";
            }
        }
        else if (chain.QueryKind == QueryKind.Select)
        {
            if (chain.ResultTypeName != null)
            {
                var resultType = GetShortTypeName(
                    ResolveExecutionResultType(site.ResultTypeName, chain.ResultTypeName, chain.ProjectionInfo)
                    ?? chain.ResultTypeName);
                thisParamType = $"{thisType}<{entityType}, {resultType}>";
                concreteParamType = $"{concreteType}<{entityType}, {resultType}>";
            }
            else
            {
                thisParamType = $"{thisType}<{entityType}>";
                concreteParamType = $"{concreteType}<{entityType}>";
            }
        }
        else if (chain.QueryKind == QueryKind.Delete)
        {
            thisParamType = $"IExecutableDeleteBuilder<{entityType}>";
            concreteParamType = $"ExecutableDeleteBuilder<{entityType}>";
        }
        else if (chain.QueryKind == QueryKind.Update)
        {
            thisParamType = $"IExecutableUpdateBuilder<{entityType}>";
            concreteParamType = $"ExecutableUpdateBuilder<{entityType}>";
        }
        else
        {
            return;
        }

        var diagnosticKind = chain.QueryKind switch
        {
            QueryKind.Select => "DiagnosticQueryKind.Select",
            QueryKind.Delete => "DiagnosticQueryKind.Delete",
            QueryKind.Update => "DiagnosticQueryKind.Update",
            _ => "DiagnosticQueryKind.Select"
        };

        var isCarrierOptimized = carrier != null ? "true" : "false";

        sb.AppendLine($"    public static QueryDiagnostics {methodName}(");
        sb.AppendLine($"        this {thisParamType} builder)");
        sb.AppendLine($"    {{");

        if (carrier != null)
        {
            EmitCarrierToDiagnosticsTerminal(sb, carrier, chain, diagnosticKind, isCarrierOptimized);
            sb.AppendLine($"    }}");
            return;
        }

        // Non-carrier path: parameters aren't available in a structured way on the concrete builder,
        // so emit empty params. Clause diagnostics are available from compile-time metadata.
        if (chain.SqlMap.Count == 1)
        {
            foreach (var kvp in chain.SqlMap)
            {
                var escapedSql = EscapeStringLiteral(kvp.Value.Sql);
                sb.AppendLine($"        const string sql = @\"{escapedSql}\";");
            }
        }
        else
        {
            sb.AppendLine($"        var __b = Unsafe.As<{concreteParamType}>(builder);");
            sb.AppendLine($"        var sql = __b.ClauseMask switch");
            sb.AppendLine($"        {{");
            foreach (var kvp in chain.SqlMap.OrderBy(k => k.Key))
            {
                var escapedSql = EscapeStringLiteral(kvp.Value.Sql);
                sb.AppendLine($"            {kvp.Key}UL => @\"{escapedSql}\",");
            }
            sb.AppendLine($"            _ => throw new InvalidOperationException(\"Unexpected ClauseMask\")");
            sb.AppendLine($"        }};");
        }

        EmitNonCarrierDiagnosticClauseArray(sb, chain, concreteParamType);
        sb.AppendLine($"        return new QueryDiagnostics(sql, Array.Empty<DiagnosticParameter>(), {diagnosticKind}, SqlDialect.{chain.Dialect}, \"{EscapeStringLiteral(chain.TableName)}\", DiagnosticOptimizationTier.PrebuiltDispatch, {isCarrierOptimized}, __clauses);");

        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Generates a tier 1 ToDiagnostics interceptor for INSERT chains.
    /// </summary>
    private static void GenerateInsertToDiagnosticsInterceptor(
        StringBuilder sb,
        UsageSiteInfo site,
        string methodName,
        PrebuiltChainInfo? chain,
        CarrierClassInfo? carrier = null)
    {
        var entityType = GetShortTypeName(site.EntityTypeName);
        var isCarrierOptimized = carrier != null ? "true" : "false";

        sb.AppendLine($"    public static QueryDiagnostics {methodName}(");
        sb.AppendLine($"        this IInsertBuilder<{entityType}> builder)");
        sb.AppendLine($"    {{");

        if (chain != null && chain.SqlMap.Count > 0 && carrier != null)
        {
            EmitCarrierInsertToDiagnosticsTerminal(sb, carrier, chain);
        }
        else if (chain != null && chain.SqlMap.Count > 0)
        {
            foreach (var kvp in chain.SqlMap)
            {
                var escapedSql = EscapeStringLiteral(kvp.Value.Sql);
                sb.AppendLine($"        return new QueryDiagnostics(@\"{escapedSql}\", Array.Empty<DiagnosticParameter>(), DiagnosticQueryKind.Insert, SqlDialect.{chain.Dialect}, \"{EscapeStringLiteral(chain.TableName)}\", DiagnosticOptimizationTier.PrebuiltDispatch, {isCarrierOptimized});");
            }
        }
        else
        {
            // No prebuilt chain — fallback to runtime
            sb.AppendLine($"        return Unsafe.As<InsertBuilder<{entityType}>>(builder).ToDiagnostics();");
        }

        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Emits a carrier insert ToDiagnostics terminal.
    /// Extracts entity property values into DiagnosticParameter[] using the same access patterns
    /// as <see cref="EmitCarrierInsertTerminal"/>.
    /// </summary>
    private static void EmitCarrierInsertToDiagnosticsTerminal(
        StringBuilder sb, CarrierClassInfo carrier, PrebuiltChainInfo chain)
    {
        EmitCarrierPreamble(sb, carrier, chain, emitOpId: false);

        var insertInfo = chain.Analysis.ExecutionSite.InsertInfo;
        if (insertInfo != null && insertInfo.Columns.Count > 0)
        {
            sb.AppendLine("        var __params = new DiagnosticParameter[]");
            sb.AppendLine("        {");
            for (int i = 0; i < insertInfo.Columns.Count; i++)
            {
                var col = insertInfo.Columns[i];
                var valueExpr = GetColumnValueExpression("__c.Entity!", col.PropertyName, col.IsForeignKey, col.CustomTypeMappingClass);
                sb.AppendLine($"            new(\"@p{i}\", (object?){valueExpr} ?? DBNull.Value),");
            }
            sb.AppendLine("        };");
        }
        else
        {
            sb.AppendLine("        var __params = Array.Empty<DiagnosticParameter>();");
        }

        sb.AppendLine($"        return new QueryDiagnostics(sql, __params, DiagnosticQueryKind.Insert, SqlDialect.{chain.Dialect}, \"{EscapeStringLiteral(chain.TableName)}\", DiagnosticOptimizationTier.PrebuiltDispatch, true);");
    }

    /// <summary>
    /// Emits a carrier ToDiagnostics terminal with full parameter and clause diagnostic output.
    /// Uses the shared preamble for cast + SQL dispatch, then builds DiagnosticParameter[]
    /// and ClauseDiagnostic[] arrays from carrier state and compile-time clause metadata.
    /// </summary>
    private static void EmitCarrierToDiagnosticsTerminal(
        StringBuilder sb, CarrierClassInfo carrier, PrebuiltChainInfo chain,
        string diagnosticKind, string isCarrierOptimized)
    {
        EmitCarrierPreamble(sb, carrier, chain, emitOpId: false);
        EmitCarrierParameterLocals(sb, chain, carrier);
        EmitDiagnosticClauseArray(sb, chain, carrier);
        sb.AppendLine($"        return new QueryDiagnostics(sql, Array.Empty<DiagnosticParameter>(), {diagnosticKind}, SqlDialect.{chain.Dialect}, \"{EscapeStringLiteral(chain.TableName)}\", DiagnosticOptimizationTier.PrebuiltDispatch, {isCarrierOptimized}, __clauses);");
    }

    /// <summary>
    /// Returns true if a clause role should be included in diagnostic output.
    /// Excludes transition roles and state-management clauses (ChainRoot, WithTimeout, etc.).
    /// </summary>
    private static bool IsDiagnosticClauseRole(ClauseRole role)
        => role is ClauseRole.Select or ClauseRole.Where or ClauseRole.OrderBy
            or ClauseRole.ThenBy or ClauseRole.GroupBy or ClauseRole.Having
            or ClauseRole.Join or ClauseRole.Set or ClauseRole.Limit or ClauseRole.Offset
            or ClauseRole.Distinct or ClauseRole.DeleteWhere or ClauseRole.UpdateWhere
            or ClauseRole.UpdateSet;

    /// <summary>
    /// Emits a ClauseDiagnostic[] array from compile-time clause metadata and runtime clause mask.
    /// When a carrier is provided, each clause gets per-clause DiagnosticParameter[] referencing
    /// the __pVal* locals. Skips transition roles and state-management clauses.
    /// </summary>
    private static void EmitDiagnosticClauseArray(
        StringBuilder sb, PrebuiltChainInfo chain, CarrierClassInfo? carrier = null)
    {
        var diagnosticClauses = chain.Analysis.Clauses
            .Where(c => IsDiagnosticClauseRole(c.Role))
            .ToList();

        if (diagnosticClauses.Count == 0)
        {
            sb.AppendLine("        var __clauses = Array.Empty<ClauseDiagnostic>();");
            return;
        }

        // Track global parameter offset to map each clause to its __pVal* locals
        var globalParamOffset = 0;
        // Pre-compute per-clause offsets by walking ALL clauses (including non-diagnostic ones)
        var clauseParamOffsets = new Dictionary<string, int>();
        foreach (var clause in chain.Analysis.Clauses)
        {
            clauseParamOffsets[clause.Site.UniqueId] = globalParamOffset;
            if (clause.Site.ClauseInfo != null)
                globalParamOffset += clause.Site.ClauseInfo.Parameters.Count;
        }

        // Compute pagination parameter indices (they follow chain params)
        var paginationBaseIdx = chain.ChainParameters.Count;
        var hasLimitField = carrier != null && HasCarrierField(carrier, FieldRole.Limit);
        var hasOffsetField = carrier != null && HasCarrierField(carrier, FieldRole.Offset);

        // Hoist mask type outside the loop — same for all conditional clauses in the chain
        var maskType = diagnosticClauses.Any(c => c.IsConditional) ? GetMaskType(chain) : null;

        // Pre-compute runtime clause SQL for clauses with collection parameters.
        // These need token expansion using __col{n}Parts from EmitCollectionExpansion.
        for (int clauseIdx = 0; clauseIdx < diagnosticClauses.Count; clauseIdx++)
        {
            var clause = diagnosticClauses[clauseIdx];
            if (carrier == null || clause.Site.ClauseInfo?.Parameters.Any(p => p.IsCollection) != true)
                continue;

            var sqlFrag = clause.Site.ClauseInfo?.SqlFragment ?? "";
            var offset = clauseParamOffsets[clause.Site.UniqueId];
            var tokenizedFrag = sqlFrag;

            foreach (var p in clause.Site.ClauseInfo!.Parameters.Where(p => p.IsCollection))
            {
                var globalIdx = offset + p.Index;
                var token = $"{{__COL_P{globalIdx}__}}";
                if (chain.Dialect == SqlDialect.MySQL)
                {
                    var qCount = 0;
                    for (int ci = 0; ci < tokenizedFrag.Length; ci++)
                    {
                        if (tokenizedFrag[ci] == '?')
                        {
                            if (qCount == p.Index)
                            {
                                tokenizedFrag = tokenizedFrag.Substring(0, ci) + token + tokenizedFrag.Substring(ci + 1);
                                break;
                            }
                            qCount++;
                        }
                    }
                }
                else
                {
                    var placeholder = chain.Dialect switch
                    {
                        SqlDialect.PostgreSQL => $"${globalIdx + 1}",
                        _ => $"@p{globalIdx}"
                    };
                    tokenizedFrag = tokenizedFrag.Replace(placeholder, token);
                }
            }

            sb.AppendLine($"        var __clauseSql{clauseIdx} = @\"{EscapeStringLiteral(tokenizedFrag)}\";");
            foreach (var p in clause.Site.ClauseInfo!.Parameters.Where(p => p.IsCollection))
            {
                var globalIdx = offset + p.Index;
                sb.AppendLine($"        __clauseSql{clauseIdx} = __clauseSql{clauseIdx}.Replace(\"{{__COL_P{globalIdx}__}}\", string.Join(\", \", __col{globalIdx}Parts));");
            }
        }

        sb.AppendLine("        var __clauses = new ClauseDiagnostic[]");
        sb.AppendLine("        {");
        foreach (var clause in diagnosticClauses)
        {
            var clauseType = clause.Role.ToString();
            var sqlFragment = clause.Site.ClauseInfo?.SqlFragment ?? "";
            var escapedFragment = EscapeStringLiteral(sqlFragment);
            var isConditional = clause.IsConditional ? "true" : "false";

            string isActive;
            if (!clause.IsConditional)
            {
                isActive = "true";
            }
            else
            {
                isActive = $"(__c.Mask & unchecked(({maskType})(1 << {clause.BitIndex!.Value}))) != 0";
            }

            // Emit per-clause parameters when carrier provides __pVal* locals
            var clauseParamCount = clause.Site.ClauseInfo?.Parameters.Count ?? 0;
            string paramsArg;

            // Check if this clause has collection parameters (runtime-expanded SQL)
            var hasCollectionParam = carrier != null && clause.Site.ClauseInfo?.Parameters.Any(p => p.IsCollection) == true;
            // Use runtime variable for collection clauses, compile-time literal otherwise
            var clauseSqlExpr = hasCollectionParam
                ? $"__clauseSql{diagnosticClauses.IndexOf(clause)}"
                : $"@\"{escapedFragment}\"";

            if (hasCollectionParam)
            {
                paramsArg = "";
            }
            else if (carrier != null && clause.Role == ClauseRole.Limit && hasLimitField)
            {
                paramsArg = $", parameters: new DiagnosticParameter[] {{ new(\"@p{paginationBaseIdx}\", __pValL) }}";
            }
            else if (carrier != null && clause.Role == ClauseRole.Offset && hasOffsetField)
            {
                var offsetIdx = paginationBaseIdx + (hasLimitField ? 1 : 0);
                paramsArg = $", parameters: new DiagnosticParameter[] {{ new(\"@p{offsetIdx}\", __pValO) }}";
            }
            else if (carrier != null && clauseParamCount > 0)
            {
                var offset = clauseParamOffsets[clause.Site.UniqueId];
                var paramEntries = new List<string>();
                for (int i = 0; i < clauseParamCount; i++)
                {
                    paramEntries.Add($"new(\"@p{offset + i}\", __pVal{offset + i})");
                }
                paramsArg = $", parameters: new DiagnosticParameter[] {{ {string.Join(", ", paramEntries)} }}";
            }
            else
            {
                paramsArg = "";
            }

            sb.AppendLine($"            new(\"{clauseType}\", {clauseSqlExpr}, isConditional: {isConditional}, isActive: {isActive}{paramsArg}),");
        }
        sb.AppendLine("        };");
    }

    /// <summary>
    /// Emits a ClauseDiagnostic[] array for non-carrier prebuilt chains.
    /// Uses __b.ClauseMask for conditional clause IsActive checks (single-variant chains have no __b).
    /// </summary>
    private static void EmitNonCarrierDiagnosticClauseArray(
        StringBuilder sb, PrebuiltChainInfo chain, string concreteParamType)
    {
        var diagnosticClauses = chain.Analysis.Clauses
            .Where(c => IsDiagnosticClauseRole(c.Role))
            .ToList();

        if (diagnosticClauses.Count == 0)
        {
            sb.AppendLine("        var __clauses = Array.Empty<ClauseDiagnostic>();");
            return;
        }

        // For multi-variant chains, we need __b for ClauseMask access.
        // For single-variant chains, __b may not exist — all conditional clauses default to active
        // since the single SQL variant was selected at compile time.
        var hasConditional = diagnosticClauses.Any(c => c.IsConditional);
        var needsMaskAccess = hasConditional && chain.SqlMap.Count > 1;

        sb.AppendLine("        var __clauses = new ClauseDiagnostic[]");
        sb.AppendLine("        {");
        foreach (var clause in diagnosticClauses)
        {
            var clauseType = clause.Role.ToString();
            var sqlFragment = clause.Site.ClauseInfo?.SqlFragment ?? "";
            var escapedFragment = EscapeStringLiteral(sqlFragment);
            var isConditional = clause.IsConditional ? "true" : "false";

            string isActive;
            if (!clause.IsConditional)
            {
                isActive = "true";
            }
            else if (needsMaskAccess)
            {
                isActive = $"(__b.ClauseMask & {(1UL << clause.BitIndex!.Value)}UL) != 0";
            }
            else
            {
                // Single-variant chain: conditional clause is active if it was included in the SQL
                isActive = "true";
            }

            sb.AppendLine($"            new(\"{clauseType}\", @\"{escapedFragment}\", isConditional: {isConditional}, isActive: {isActive}),");
        }
        sb.AppendLine("        };");
    }

    /// <summary>
    /// Generates a runtime-delegating ToDiagnostics interceptor when no prebuilt chain exists.
    /// Casts to the concrete builder and calls its runtime ToDiagnostics() implementation.
    /// </summary>
    private static void GenerateRuntimeToDiagnosticsInterceptor(
        StringBuilder sb,
        UsageSiteInfo site,
        string methodName)
    {
        var entityType = GetShortTypeName(site.EntityTypeName);
        var thisType = site.BuilderTypeName;
        var concreteType = ToConcreteTypeName(thisType);

        // Determine the full this-parameter type and concrete type
        string thisParamType;
        string concreteParamType;

        if (site.JoinedEntityTypeNames != null && site.JoinedEntityTypeNames.Count >= 2)
        {
            var joinTypeArgs = string.Join(", ", site.JoinedEntityTypeNames.Select(GetShortTypeName));
            thisParamType = $"{thisType}<{joinTypeArgs}>";
            concreteParamType = $"{concreteType}<{joinTypeArgs}>";
        }
        else if (IsEntityAccessorType(thisType))
        {
            thisParamType = $"{thisType}<{entityType}>";
            concreteParamType = $"{concreteType}<{entityType}>";
        }
        else if (thisType.Contains("DeleteBuilder"))
        {
            thisParamType = $"IExecutableDeleteBuilder<{entityType}>";
            concreteParamType = $"ExecutableDeleteBuilder<{entityType}>";
        }
        else if (thisType.Contains("UpdateBuilder"))
        {
            thisParamType = $"IExecutableUpdateBuilder<{entityType}>";
            concreteParamType = $"ExecutableUpdateBuilder<{entityType}>";
        }
        else
        {
            thisParamType = $"{thisType}<{entityType}>";
            concreteParamType = $"{concreteType}<{entityType}>";
        }

        sb.AppendLine($"    public static QueryDiagnostics {methodName}(");
        sb.AppendLine($"        this {thisParamType} builder)");
        sb.AppendLine($"    {{");

        if (IsEntityAccessorType(thisType))
        {
            sb.AppendLine($"        return ((EntityAccessor<{entityType}>)(object)builder).ToDiagnostics();");
        }
        else
        {
            sb.AppendLine($"        return Unsafe.As<{concreteParamType}>(builder).ToDiagnostics();");
        }

        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Generates the dispatch table switch expression that maps ClauseMask values
    /// to pre-built SQL string literals.
    /// </summary>
    private static void GenerateDispatchTable(
        StringBuilder sb,
        Dictionary<ulong, PrebuiltSqlResult> sqlMap,
        string builderVar = "builder")
    {
        Debug.Assert(sqlMap.Count > 0, "Dispatch table must have at least one SQL variant.");

        if (sqlMap.Count == 1)
        {
            // Single variant — no switch needed, use const
            foreach (var kvp in sqlMap)
            {
                var escapedSql = EscapeStringLiteral(kvp.Value.Sql);
                sb.AppendLine($"        const string sql = @\"{escapedSql}\";");
            }
        }
        else
        {
            // Multiple variants — switch expression on ClauseMask
            sb.AppendLine($"        var sql = {builderVar}.ClauseMask switch");
            sb.AppendLine($"        {{");

            foreach (var kvp in sqlMap.OrderBy(k => k.Key))
            {
                var escapedSql = EscapeStringLiteral(kvp.Value.Sql);
                sb.AppendLine($"            {kvp.Key}UL => @\"{escapedSql}\",");
            }

            sb.AppendLine($"            _ => throw new InvalidOperationException(\"Unexpected ClauseMask: \" + {builderVar}.ClauseMask)");
            sb.AppendLine($"        }};");
        }
    }
}
