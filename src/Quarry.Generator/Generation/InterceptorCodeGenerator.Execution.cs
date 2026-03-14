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
        PrebuiltChainInfo chain)
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
        PrebuiltChainInfo chain)
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
        PrebuiltChainInfo chain)
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

        // Cast to concrete type (receiver is always an interface)
        sb.AppendLine($"        var __b = Unsafe.As<{concreteBuilderTypeName}>(builder);");
        var builderVar = "__b";

        // Dispatch table: switch on ClauseMask
        GenerateDispatchTable(sb, chain.SqlMap, builderVar);

        sb.AppendLine($"        return {builderVar}.ExecuteWithPrebuiltSqlAsync(sql, cancellationToken);");
        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Generates a tier 1 ToSql interceptor that returns the pre-built SQL string directly.
    /// Handles SELECT, DELETE, and UPDATE chains, including joined queries.
    /// </summary>
    private static void GeneratePrebuiltToSqlInterceptor(
        StringBuilder sb,
        UsageSiteInfo site,
        string methodName,
        PrebuiltChainInfo chain)
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
                // Projected joined builder: IJoinedQueryBuilder<T1, T2, TResult>
                var resultType = GetShortTypeName(
                    ResolveExecutionResultType(site.ResultTypeName, chain.ResultTypeName, chain.ProjectionInfo)
                    ?? chain.ResultTypeName);
                thisParamType = $"{thisType}<{joinTypeArgs}, {resultType}>";
                concreteParamType = $"{concreteType}<{joinTypeArgs}, {resultType}>";
            }
            else
            {
                // Unprojected joined builder: IJoinedQueryBuilder<T1, T2>
                thisParamType = $"{thisType}<{joinTypeArgs}>";
                concreteParamType = $"{concreteType}<{joinTypeArgs}>";
            }
        }
        else if (chain.QueryKind == QueryKind.Select)
        {
            // SELECT: ToSql can be on IQueryBuilder<T> (unprojected) or IQueryBuilder<T, TResult> (projected)
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

        sb.AppendLine($"    public static string {methodName}(");
        sb.AppendLine($"        this {thisParamType} builder)");
        sb.AppendLine($"    {{");

        if (chain.SqlMap.Count == 1)
        {
            // Single variant — return const directly, no cast needed
            foreach (var kvp in chain.SqlMap)
            {
                var escapedSql = EscapeStringLiteral(kvp.Value.Sql);
                sb.AppendLine($"        return @\"{escapedSql}\";");
            }
        }
        else
        {
            // Multiple variants — need concrete type for ClauseMask access
            sb.AppendLine($"        return Unsafe.As<{concreteParamType}>(builder).ClauseMask switch");
            sb.AppendLine($"        {{");
            foreach (var kvp in chain.SqlMap.OrderBy(k => k.Key))
            {
                var escapedSql = EscapeStringLiteral(kvp.Value.Sql);
                sb.AppendLine($"            {kvp.Key}UL => @\"{escapedSql}\",");
            }
            sb.AppendLine($"            _ => throw new InvalidOperationException(\"Unexpected ClauseMask\")");
            sb.AppendLine($"        }};");
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
