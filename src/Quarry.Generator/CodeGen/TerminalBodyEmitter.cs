using System.Collections.Generic;
using System.Linq;
using System.Text;
using Quarry.Generators.Generation;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Generators.Projection;
using Quarry.Generators.Sql;

namespace Quarry.Generators.CodeGen;

/// <summary>
/// Emits interceptor method bodies for execution terminal sites (FetchAll,
/// FetchFirst, FetchFirstOrDefault, FetchSingle, ExecuteScalar,
/// ExecuteNonQuery, ToAsyncEnumerable, ToDiagnostics).
/// Handles both carrier-path (direct DbCommand) and non-carrier-path
/// (prebuilt SQL dispatch) emission.
/// </summary>
/// <remarks>
/// Ported from InterceptorCodeGenerator.Execution.cs and
/// InterceptorCodeGenerator.Modifications.cs (insert terminals).
/// </remarks>
internal static class TerminalBodyEmitter
{
    /// <summary>
    /// Emits a tier 1 execution interceptor for SELECT queries (ExecuteFetchAll, ExecuteFetchFirst, etc.).
    /// Contains a dispatch table that maps ClauseMask to pre-built SQL string literal.
    /// </summary>
    public static void EmitReaderTerminal(
        StringBuilder sb,
        TranslatedCallSite site,
        string methodName,
        AssembledPlan chain,
        CarrierPlan? carrier = null)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(chain.EntityTypeName);

        // Resolve result type using the same logic as the gate in GenerateInterceptorMethod.
        var rawResultType = InterceptorCodeGenerator.ResolveExecutionResultType(
            site.ResultTypeName, chain.ResultTypeName, chain.ProjectionInfo);

        // Don't strip tuple element names — CS9148 requires exact match including names.
        var resultType = !string.IsNullOrEmpty(rawResultType)
            ? InterceptorCodeGenerator.GetShortTypeName(rawResultType!)
            : entityType;

        // Determine return type and executor method from the execution kind.
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
        var concreteType = InterceptorCodeGenerator.ToConcreteTypeName(thisType);

        // Method signature
        if (site.Kind == InterceptorKind.ExecuteScalar)
        {
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
            var canEmit = site.Kind == InterceptorKind.ExecuteScalar
                ? CarrierEmitter.CanEmitScalarTerminal(chain)
                : CarrierEmitter.CanEmitReaderTerminal(chain);

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
                CarrierEmitter.EmitCarrierExecutionTerminal(sb, carrier, chain, readerCode, carrierExecutorMethod);
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
        InterceptorCodeGenerator.GenerateDispatchTable(sb, chain.SqlVariants, builderVar);

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
            // ExecuteScalar: no reader delegate
            sb.AppendLine($"        return {builderVar}.{executorMethod}(sql, cancellationToken);");
        }

        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Emits a tier 1 execution interceptor for joined query execution (multi-entity SELECT).
    /// </summary>
    public static void EmitJoinReaderTerminal(
        StringBuilder sb,
        TranslatedCallSite site,
        string methodName,
        AssembledPlan chain,
        CarrierPlan? carrier = null)
    {
        var joinedNames = chain.JoinedEntityTypeNames!;
        var entityCount = joinedNames.Count;
        var builderName = InterceptorCodeGenerator.GetJoinedBuilderTypeName(entityCount);
        var concreteBuilderName = InterceptorCodeGenerator.ToConcreteTypeName(builderName);
        var thisType = site.BuilderTypeName;
        var thisBuilderName = builderName;
        var entityTypes = joinedNames.Select(n => InterceptorCodeGenerator.GetShortTypeName(n)).ToList();
        var entityTypeArgs = string.Join(", ", entityTypes);

        var rawResultType = InterceptorCodeGenerator.ResolveExecutionResultType(
            site.ResultTypeName, chain.ResultTypeName, chain.ProjectionInfo);
        var resultType = !string.IsNullOrEmpty(rawResultType)
            ? InterceptorCodeGenerator.GetShortTypeName(rawResultType!)
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

        if (carrier != null && CarrierEmitter.CanEmitReaderTerminal(chain))
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
            CarrierEmitter.EmitCarrierExecutionTerminal(sb, carrier, chain, chain.ReaderDelegateCode, carrierExecutorMethod);
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
        InterceptorCodeGenerator.GenerateDispatchTable(sb, chain.SqlVariants, builderVar);

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
    /// Emits a tier 1 execution interceptor for non-query operations (DELETE/UPDATE ExecuteNonQueryAsync).
    /// </summary>
    public static void EmitNonQueryTerminal(
        StringBuilder sb,
        TranslatedCallSite site,
        string methodName,
        AssembledPlan chain,
        CarrierPlan? carrier = null)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(chain.EntityTypeName);

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

        if (carrier != null && CarrierEmitter.CanEmitNonQueryTerminal(chain))
        {
            CarrierEmitter.EmitCarrierNonQueryTerminal(sb, carrier, chain);
            sb.AppendLine($"    }}");
            return;
        }

        // Cast to concrete type (receiver is always an interface)
        sb.AppendLine($"        var __b = Unsafe.As<{concreteBuilderTypeName}>(builder);");
        var builderVar = "__b";

        // Dispatch table: switch on ClauseMask
        InterceptorCodeGenerator.GenerateDispatchTable(sb, chain.SqlVariants, builderVar);

        sb.AppendLine($"        return {builderVar}.ExecuteWithPrebuiltSqlAsync(sql, cancellationToken);");
        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Emits a tier 1 ToDiagnostics interceptor that returns a QueryDiagnostics with pre-built SQL
    /// and optimization metadata.
    /// </summary>
    public static void EmitDiagnosticsTerminal(
        StringBuilder sb,
        TranslatedCallSite site,
        string methodName,
        AssembledPlan chain,
        CarrierPlan? carrier = null)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(chain.EntityTypeName);
        var thisType = site.BuilderTypeName;
        var concreteType = InterceptorCodeGenerator.ToConcreteTypeName(thisType);

        // Determine the full this-parameter type based on query kind and builder shape
        string thisParamType;
        string concreteParamType;

        if (chain.IsJoinChain)
        {
            var joinedNames = chain.JoinedEntityTypeNames!;
            var joinTypeArgs = string.Join(", ", joinedNames.Select(InterceptorCodeGenerator.GetShortTypeName));

            if (chain.ResultTypeName != null)
            {
                var resultType = InterceptorCodeGenerator.GetShortTypeName(
                    InterceptorCodeGenerator.ResolveExecutionResultType(site.ResultTypeName, chain.ResultTypeName, chain.ProjectionInfo)
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
                var resultType = InterceptorCodeGenerator.GetShortTypeName(
                    InterceptorCodeGenerator.ResolveExecutionResultType(site.ResultTypeName, chain.ResultTypeName, chain.ProjectionInfo)
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
            CarrierEmitter.EmitCarrierToDiagnosticsTerminal(sb, carrier, chain, diagnosticKind, isCarrierOptimized);
            sb.AppendLine($"    }}");
            return;
        }

        // Non-carrier path: parameters aren't available in a structured way on the concrete builder,
        // so emit empty params. Clause diagnostics are available from compile-time metadata.
        if (chain.SqlVariants.Count == 1)
        {
            foreach (var kvp in chain.SqlVariants)
            {
                var escapedSql = InterceptorCodeGenerator.EscapeStringLiteral(kvp.Value.Sql);
                sb.AppendLine($"        const string sql = @\"{escapedSql}\";");
            }
        }
        else
        {
            sb.AppendLine($"        var __b = Unsafe.As<{concreteParamType}>(builder);");
            sb.AppendLine($"        var sql = __b.ClauseMask switch");
            sb.AppendLine($"        {{");
            foreach (var kvp in chain.SqlVariants.OrderBy(k => k.Key))
            {
                var escapedSql = InterceptorCodeGenerator.EscapeStringLiteral(kvp.Value.Sql);
                sb.AppendLine($"            {kvp.Key}UL => @\"{escapedSql}\",");
            }
            sb.AppendLine($"            _ => throw new InvalidOperationException(\"Unexpected ClauseMask\")");
            sb.AppendLine($"        }};");
        }

        InterceptorCodeGenerator.EmitNonCarrierDiagnosticClauseArray(sb, chain, concreteParamType);
        sb.AppendLine($"        return new QueryDiagnostics(sql, Array.Empty<DiagnosticParameter>(), {diagnosticKind}, SqlDialect.{chain.Dialect}, \"{InterceptorCodeGenerator.EscapeStringLiteral(chain.TableName)}\", DiagnosticOptimizationTier.PrebuiltDispatch, {isCarrierOptimized}, __clauses);");

        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Emits a runtime-delegating ToDiagnostics interceptor when no prebuilt chain exists.
    /// Casts to the concrete builder and calls its runtime ToDiagnostics() implementation.
    /// </summary>
    public static void EmitRuntimeDiagnosticsTerminal(
        StringBuilder sb,
        TranslatedCallSite site,
        string methodName)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);
        var thisType = site.BuilderTypeName;
        var concreteType = InterceptorCodeGenerator.ToConcreteTypeName(thisType);

        // Determine the full this-parameter type and concrete type
        string thisParamType;
        string concreteParamType;

        if (site.JoinedEntityTypeNames != null && site.JoinedEntityTypeNames.Count >= 2)
        {
            var joinTypeArgs = string.Join(", ", site.JoinedEntityTypeNames.Select(InterceptorCodeGenerator.GetShortTypeName));
            thisParamType = $"{thisType}<{joinTypeArgs}>";
            concreteParamType = $"{concreteType}<{joinTypeArgs}>";
        }
        else if (InterceptorCodeGenerator.IsEntityAccessorType(thisType))
        {
            thisParamType = $"{thisType}<{entityType}>";
            concreteParamType = $"{concreteType}<{entityType}>";
        }
        else if (site.BuilderKind is BuilderKind.Delete or BuilderKind.ExecutableDelete)
        {
            thisParamType = $"IExecutableDeleteBuilder<{entityType}>";
            concreteParamType = $"ExecutableDeleteBuilder<{entityType}>";
        }
        else if (site.BuilderKind is BuilderKind.Update or BuilderKind.ExecutableUpdate)
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

        if (InterceptorCodeGenerator.IsEntityAccessorType(thisType))
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
    /// Emits an InsertBuilder ExecuteNonQueryAsync() interceptor.
    /// </summary>
    public static void EmitInsertNonQueryTerminal(StringBuilder sb, TranslatedCallSite site, string methodName,
        AssembledPlan? prebuiltChain = null, CarrierPlan? carrier = null)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);
        var insertInfo = site.InsertInfo;

        sb.AppendLine($"    public static Task<int> {methodName}(");
        sb.AppendLine($"        this IInsertBuilder<{entityType}> builder,");
        sb.AppendLine($"        CancellationToken cancellationToken = default)");
        sb.AppendLine($"    {{");

        if (insertInfo != null && insertInfo.Columns.Count > 0)
        {
            // Carrier-optimized path
            if (carrier != null && prebuiltChain != null)
            {
                CarrierEmitter.EmitCarrierInsertTerminal(sb, carrier, prebuiltChain,
                    "ExecuteCarrierNonQueryWithCommandAsync");
                sb.AppendLine($"    }}");
                return;
            }

            sb.AppendLine($"        var __b = Unsafe.As<InsertBuilder<{entityType}>>(builder);");

            InterceptorCodeGenerator.EmitInsertColumnSetup(sb, insertInfo);

            // Extract values from each entity
            sb.AppendLine($"        foreach (var entity in __b.Entities)");
            sb.AppendLine($"        {{");
            InterceptorCodeGenerator.EmitInsertEntityBindings(sb, insertInfo, "entity", "__b", "            ");
            sb.AppendLine($"        }}");
            sb.AppendLine();

            sb.AppendLine($"        return Quarry.Internal.ModificationExecutor.ExecuteInsertNonQueryAsync(__b.State, __b.Entities, cancellationToken);");
        }
        else
        {
            // Non-translatable — skip interceptor entirely so the original method runs
            return;
        }

        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Emits an InsertBuilder ExecuteScalarAsync() interceptor for identity return.
    /// </summary>
    public static void EmitInsertScalarTerminal(StringBuilder sb, TranslatedCallSite site, string methodName,
        AssembledPlan? prebuiltChain = null, CarrierPlan? carrier = null)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);
        var insertInfo = site.InsertInfo;

        // ExecuteScalarAsync<TKey> is a generic method on generic class IInsertBuilder<T>.
        // Interceptors must match the combined arity: <T, TKey> (CS9177).
        // T is constrained to class to match IInsertBuilder<T> where T : class.
        sb.AppendLine($"    public static Task<TKey> {methodName}<T, TKey>(");
        sb.AppendLine($"        this IInsertBuilder<T> builder,");
        sb.AppendLine($"        CancellationToken cancellationToken = default) where T : class");
        sb.AppendLine($"    {{");

        if (insertInfo != null && insertInfo.Columns.Count > 0)
        {
            // Carrier-optimized path
            if (carrier != null && prebuiltChain != null)
            {
                CarrierEmitter.EmitCarrierInsertTerminal(sb, carrier, prebuiltChain,
                    "ExecuteCarrierScalarWithCommandAsync<TKey>", isScalar: true);
                sb.AppendLine($"    }}");
                return;
            }

            sb.AppendLine($"        var __b = Unsafe.As<InsertBuilder<T>>(builder);");

            InterceptorCodeGenerator.EmitInsertColumnSetup(sb, insertInfo);

            // Set identity column if present
            if (!string.IsNullOrEmpty(insertInfo.IdentityColumnName))
            {
                sb.AppendLine($"        __b.SetIdentityColumn(@\"{InterceptorCodeGenerator.EscapeStringLiteral(insertInfo.IdentityColumnName!)}\");");
                sb.AppendLine();
            }

            // Validate single entity insert
            sb.AppendLine($"        if (__b.Entities.Count != 1)");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            throw new InvalidOperationException(");
            sb.AppendLine($"                \"ExecuteScalarAsync can only be used for single entity inserts. \" +");
            sb.AppendLine($"                \"For batch inserts, use ExecuteNonQueryAsync() instead.\");");
            sb.AppendLine($"        }}");
            sb.AppendLine();

            // Cast entity to concrete type for property access.
            sb.AppendLine($"        var entity = Unsafe.As<{entityType}>(__b.Entities[0]);");
            InterceptorCodeGenerator.EmitInsertEntityBindings(sb, insertInfo, "entity", "__b", "        ");
            sb.AppendLine();

            sb.AppendLine($"        return Quarry.Internal.ModificationExecutor.ExecuteInsertScalarAsync<T, TKey>(__b.State, __b.Entities[0], cancellationToken);");
        }
        else
        {
            // Non-translatable — skip interceptor entirely so the original method runs
            return;
        }

        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Emits a tier 1 ToDiagnostics interceptor for INSERT chains.
    /// </summary>
    public static void EmitInsertDiagnosticsTerminal(
        StringBuilder sb,
        TranslatedCallSite site,
        string methodName,
        AssembledPlan? chain,
        CarrierPlan? carrier = null)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);
        var isCarrierOptimized = carrier != null ? "true" : "false";

        sb.AppendLine($"    public static QueryDiagnostics {methodName}(");
        sb.AppendLine($"        this IInsertBuilder<{entityType}> builder)");
        sb.AppendLine($"    {{");

        if (chain != null && chain.SqlVariants.Count > 0 && carrier != null)
        {
            CarrierEmitter.EmitCarrierInsertToDiagnosticsTerminal(sb, carrier, chain);
        }
        else if (chain != null && chain.SqlVariants.Count > 0)
        {
            foreach (var kvp in chain.SqlVariants)
            {
                var escapedSql = InterceptorCodeGenerator.EscapeStringLiteral(kvp.Value.Sql);
                sb.AppendLine($"        return new QueryDiagnostics(@\"{escapedSql}\", Array.Empty<DiagnosticParameter>(), DiagnosticQueryKind.Insert, SqlDialect.{chain.Dialect}, \"{InterceptorCodeGenerator.EscapeStringLiteral(chain.TableName)}\", DiagnosticOptimizationTier.PrebuiltDispatch, {isCarrierOptimized});");
            }
        }
        else
        {
            // No prebuilt chain — fallback to runtime
            sb.AppendLine($"        return Unsafe.As<InsertBuilder<{entityType}>>(builder).ToDiagnostics();");
        }

        sb.AppendLine($"    }}");
    }
}
