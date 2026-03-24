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
        CarrierPlan carrier)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(chain.EntityTypeName);

        // Resolve result type using the same logic as the gate in GenerateInterceptorMethod.
        var rawResultType = InterceptorCodeGenerator.ResolveExecutionResultType(
            site.ResultTypeName, chain.ResultTypeName, chain.ProjectionInfo);

        // Don't strip tuple element names — CS9148 requires exact match including names.
        var resultType = !string.IsNullOrEmpty(rawResultType)
            ? InterceptorCodeGenerator.GetShortTypeName(rawResultType!)
            : entityType;

        // Determine return type from the execution kind.
        string returnType = site.Kind switch
        {
            InterceptorKind.ExecuteFetchAll => $"Task<List<{resultType}>>",
            InterceptorKind.ExecuteFetchFirst => $"Task<{resultType}>",
            InterceptorKind.ExecuteFetchFirstOrDefault => $"Task<{resultType}?>",
            InterceptorKind.ExecuteFetchSingle => $"Task<{resultType}>",
            InterceptorKind.ExecuteScalar => $"Task<TScalar>",
            InterceptorKind.ToAsyncEnumerable => $"IAsyncEnumerable<{resultType}>",
            _ => ""
        };
        if (string.IsNullOrEmpty(returnType)) return;

        var thisType = site.BuilderTypeName;

        // Method signature — use PreparedQuery<TResult> as receiver for prepared terminals
        if (site.IsPreparedTerminal)
        {
            sb.AppendLine($"    public static {returnType} {methodName}(");
            sb.AppendLine($"        this PreparedQuery<{resultType}> builder,");
            sb.AppendLine($"        CancellationToken cancellationToken = default)");
        }
        else if (site.Kind == InterceptorKind.ExecuteScalar)
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
    }

    /// <summary>
    /// Emits a tier 1 execution interceptor for joined query execution (multi-entity SELECT).
    /// </summary>
    public static void EmitJoinReaderTerminal(
        StringBuilder sb,
        TranslatedCallSite site,
        string methodName,
        AssembledPlan chain,
        CarrierPlan carrier)
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

        string returnType = site.Kind switch
        {
            InterceptorKind.ExecuteFetchAll => $"Task<List<{resultType}>>",
            InterceptorKind.ExecuteFetchFirst => $"Task<{resultType}>",
            InterceptorKind.ExecuteFetchFirstOrDefault => $"Task<{resultType}?>",
            InterceptorKind.ExecuteFetchSingle => $"Task<{resultType}>",
            InterceptorKind.ExecuteScalar => $"Task<TScalar>",
            InterceptorKind.ToAsyncEnumerable => $"IAsyncEnumerable<{resultType}>",
            _ => ""
        };
        if (string.IsNullOrEmpty(returnType)) return;

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
    }

    /// <summary>
    /// Emits a tier 1 execution interceptor for non-query operations (DELETE/UPDATE ExecuteNonQueryAsync).
    /// </summary>
    public static void EmitNonQueryTerminal(
        StringBuilder sb,
        TranslatedCallSite site,
        string methodName,
        AssembledPlan chain,
        CarrierPlan carrier)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(chain.EntityTypeName);

        var thisType = site.BuilderTypeName;

        // Determine builder type name based on query kind
        string thisBuilderTypeName;
        switch (chain.QueryKind)
        {
            case QueryKind.Delete:
                thisBuilderTypeName = $"IExecutableDeleteBuilder<{entityType}>";
                break;
            case QueryKind.Update:
                thisBuilderTypeName = $"IExecutableUpdateBuilder<{entityType}>";
                break;
            default:
                return;
        }

        // Method signature — use PreparedQuery<int> as receiver for prepared terminals
        if (site.IsPreparedTerminal)
        {
            sb.AppendLine($"    public static Task<int> {methodName}(");
            sb.AppendLine($"        this PreparedQuery<int> builder,");
            sb.AppendLine($"        CancellationToken cancellationToken = default)");
        }
        else
        {
            sb.AppendLine($"    public static Task<int> {methodName}(");
            sb.AppendLine($"        this {thisBuilderTypeName} builder,");
            sb.AppendLine($"        CancellationToken cancellationToken = default)");
        }
        sb.AppendLine($"    {{");

        CarrierEmitter.EmitCarrierNonQueryTerminal(sb, carrier, chain);
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
        CarrierPlan carrier)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(chain.EntityTypeName);
        var thisType = site.BuilderTypeName;

        // Determine the full this-parameter type based on query kind and builder shape
        string thisParamType;

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
            }
            else
            {
                thisParamType = $"{thisType}<{joinTypeArgs}>";
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
            }
            else
            {
                thisParamType = $"{thisType}<{entityType}>";
            }
        }
        else if (chain.QueryKind == QueryKind.Delete)
        {
            thisParamType = $"IExecutableDeleteBuilder<{entityType}>";
        }
        else if (chain.QueryKind == QueryKind.Update)
        {
            thisParamType = $"IExecutableUpdateBuilder<{entityType}>";
        }
        else if (chain.QueryKind == QueryKind.Insert)
        {
            thisParamType = $"IInsertBuilder<{entityType}>";
        }
        else if (chain.QueryKind == QueryKind.BatchInsert)
        {
            thisParamType = $"IExecutableBatchInsert<{entityType}>";
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
            QueryKind.Insert or QueryKind.BatchInsert => "DiagnosticQueryKind.Insert",
            _ => "DiagnosticQueryKind.Select"
        };

        var isCarrierOptimized = "true";

        // Override receiver type for prepared terminals
        if (site.IsPreparedTerminal)
        {
            var prepResultType = chain.ResultTypeName != null
                ? InterceptorCodeGenerator.GetShortTypeName(
                    InterceptorCodeGenerator.ResolveExecutionResultType(site.ResultTypeName, chain.ResultTypeName, chain.ProjectionInfo)
                    ?? chain.ResultTypeName)
                : entityType;
            if (chain.QueryKind is QueryKind.Delete or QueryKind.Update or QueryKind.Insert or QueryKind.BatchInsert)
                thisParamType = "PreparedQuery<int>";
            else
                thisParamType = $"PreparedQuery<{prepResultType}>";
        }

        sb.AppendLine($"    public static QueryDiagnostics {methodName}(");
        sb.AppendLine($"        this {thisParamType} builder)");
        sb.AppendLine($"    {{");

        CarrierEmitter.EmitCarrierToDiagnosticsTerminal(sb, carrier, chain, diagnosticKind, isCarrierOptimized);
        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Emits an InsertBuilder ExecuteNonQueryAsync() interceptor.
    /// </summary>
    public static void EmitInsertNonQueryTerminal(StringBuilder sb, TranslatedCallSite site, string methodName,
        AssembledPlan? prebuiltChain, CarrierPlan carrier)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);
        var insertInfo = site.InsertInfo ?? prebuiltChain?.PrepareSite?.InsertInfo;

        string receiverType = site.IsPreparedTerminal
            ? "PreparedQuery<int>"
            : $"IInsertBuilder<{entityType}>";

        sb.AppendLine($"    public static Task<int> {methodName}(");
        sb.AppendLine($"        this {receiverType} builder,");
        sb.AppendLine($"        CancellationToken cancellationToken = default)");
        sb.AppendLine($"    {{");

        if (insertInfo != null && insertInfo.Columns.Count > 0 && prebuiltChain != null)
        {
            CarrierEmitter.EmitCarrierInsertTerminal(sb, carrier, prebuiltChain,
                "ExecuteCarrierNonQueryWithCommandAsync");
        }
        else
        {
            sb.AppendLine($"        throw new NotSupportedException(\"Insert interceptor not generated — missing column analysis.\");");
        }

        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Emits an InsertBuilder ExecuteScalarAsync() interceptor for identity return.
    /// </summary>
    public static void EmitInsertScalarTerminal(StringBuilder sb, TranslatedCallSite site, string methodName,
        AssembledPlan? prebuiltChain, CarrierPlan carrier)
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

        if (insertInfo != null && insertInfo.Columns.Count > 0 && prebuiltChain != null)
        {
            CarrierEmitter.EmitCarrierInsertTerminal(sb, carrier, prebuiltChain,
                "ExecuteCarrierScalarWithCommandAsync<TKey>", isScalar: true);
        }
        else
        {
            sb.AppendLine($"        throw new NotSupportedException(\"Insert scalar interceptor not generated — missing column analysis.\");");
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
        CarrierPlan carrier)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);

        sb.AppendLine($"    public static QueryDiagnostics {methodName}(");
        sb.AppendLine($"        this IInsertBuilder<{entityType}> builder)");
        sb.AppendLine($"    {{");

        if (chain != null && chain.SqlVariants.Count > 0)
        {
            CarrierEmitter.EmitCarrierInsertToDiagnosticsTerminal(sb, carrier, chain);
        }
        else
        {
            sb.AppendLine($"        throw new NotSupportedException(\"Insert diagnostics interceptor not generated — missing chain analysis.\");");
        }

        sb.AppendLine($"    }}");
    }

    // ── Batch Insert Terminals ──

    /// <summary>
    /// Emits a batch insert ExecuteNonQueryAsync terminal.
    /// </summary>
    public static void EmitBatchInsertNonQueryTerminal(StringBuilder sb, TranslatedCallSite site, string methodName,
        AssembledPlan? chain, CarrierPlan carrier)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);

        string receiverType = site.IsPreparedTerminal
            ? "PreparedQuery<int>"
            : $"IExecutableBatchInsert<{entityType}>";

        sb.AppendLine($"    public static Task<int> {methodName}(");
        sb.AppendLine($"        this {receiverType} builder,");
        sb.AppendLine($"        CancellationToken cancellationToken = default)");
        sb.AppendLine($"    {{");

        if (chain != null)
        {
            EmitBatchInsertCarrierTerminal(sb, carrier, chain, "ExecuteCarrierNonQueryWithCommandAsync", entityType);
        }
        else
        {
            sb.AppendLine($"        throw new NotSupportedException(\"Batch insert interceptor not generated — missing chain analysis.\");");
        }

        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Emits a batch insert ExecuteScalarAsync terminal.
    /// </summary>
    public static void EmitBatchInsertScalarTerminal(StringBuilder sb, TranslatedCallSite site, string methodName,
        AssembledPlan? chain, CarrierPlan carrier)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);

        sb.AppendLine($"    public static Task<TKey> {methodName}<T, TKey>(");
        sb.AppendLine($"        this IExecutableBatchInsert<T> builder,");
        sb.AppendLine($"        CancellationToken cancellationToken = default) where T : class");
        sb.AppendLine($"    {{");

        if (chain != null)
        {
            EmitBatchInsertCarrierTerminal(sb, carrier, chain, "ExecuteCarrierScalarWithCommandAsync<TKey>", entityType);
        }
        else
        {
            sb.AppendLine($"        throw new NotSupportedException(\"Batch insert scalar interceptor not generated — missing chain analysis.\");");
        }

        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Emits a batch insert ToDiagnostics terminal.
    /// </summary>
    public static void EmitBatchInsertDiagnosticsTerminal(StringBuilder sb, TranslatedCallSite site, string methodName,
        AssembledPlan? chain, CarrierPlan carrier)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);

        string receiverType = site.IsPreparedTerminal
            ? "PreparedQuery<int>"
            : $"IExecutableBatchInsert<{entityType}>";

        sb.AppendLine($"    public static QueryDiagnostics {methodName}(");
        sb.AppendLine($"        this {receiverType} builder)");
        sb.AppendLine($"    {{");

        if (chain != null && chain.SqlVariants.Count > 0)
        {
            sb.AppendLine($"        var __c = Unsafe.As<{carrier.ClassName}>(builder);");

            // Build SQL with a single preview row
            var sqlPrefix = chain.SqlVariants.Values.First().Sql;
            var escapedPrefix = InterceptorCodeGenerator.EscapeStringLiteral(sqlPrefix);
            var returningSuffix = chain.BatchInsertReturningSuffix != null
                ? $"@\"{InterceptorCodeGenerator.EscapeStringLiteral(chain.BatchInsertReturningSuffix)}\""
                : "null";

            sb.AppendLine($"        var sql = Quarry.Internal.BatchInsertSqlBuilder.Build(@\"{escapedPrefix}\", 1, {chain.BatchInsertColumnsPerRow}, SqlDialect.{chain.Dialect}, {returningSuffix});");
            sb.AppendLine($"        return new QueryDiagnostics(sql, Array.Empty<DiagnosticParameter>(), DiagnosticQueryKind.Insert, SqlDialect.{chain.Dialect}, \"{InterceptorCodeGenerator.EscapeStringLiteral(chain.TableName)}\", DiagnosticOptimizationTier.PrebuiltDispatch, true);");
        }
        else
        {
            sb.AppendLine($"        throw new NotSupportedException(\"Batch insert diagnostics interceptor not generated — missing chain analysis.\");");
        }

        sb.AppendLine($"    }}");
    }


    /// <summary>
    /// Shared helper that emits the carrier execution terminal body for batch insert NonQuery/Scalar.
    /// </summary>
    private static void EmitBatchInsertCarrierTerminal(
        StringBuilder sb, CarrierPlan carrier, AssembledPlan chain,
        string executorMethod, string entityType)
    {
        var insertInfo = chain.ExecutionSite.InsertInfo ?? chain.PrepareSite?.InsertInfo;
        if (insertInfo == null || insertInfo.Columns.Count == 0) return;

        sb.AppendLine($"        var __c = Unsafe.As<{carrier.ClassName}>(builder);");
        sb.AppendLine("        var __opId = OpId.Next();");

        // Materialize entities
        sb.AppendLine($"        var __entities = System.Linq.Enumerable.ToList(__c.BatchEntities!);");
        sb.AppendLine($"        var __entityCount = __entities.Count;");

        // Build SQL from prefix template
        var sqlPrefix = chain.SqlVariants.Values.First().Sql;
        var escapedPrefix = InterceptorCodeGenerator.EscapeStringLiteral(sqlPrefix);
        var returningSuffix = chain.BatchInsertReturningSuffix != null
            ? $"@\"{InterceptorCodeGenerator.EscapeStringLiteral(chain.BatchInsertReturningSuffix)}\""
            : "null";

        sb.AppendLine($"        var sql = Quarry.Internal.BatchInsertSqlBuilder.Build(@\"{escapedPrefix}\", __entityCount, {chain.BatchInsertColumnsPerRow}, SqlDialect.{chain.Dialect}, {returningSuffix});");

        // SQL logging
        sb.AppendLine("        if (LogManager.IsEnabled(LogLevel.Debug, QueryLog.CategoryName))");
        sb.AppendLine("            QueryLog.SqlGenerated(__opId, sql);");

        // Command creation and parameter binding
        var timeoutExpr = carrier.Fields.Any(f => f.Role == FieldRole.Timeout)
            ? "__c.Timeout ?? __c.Ctx!.DefaultTimeout"
            : "__c.Ctx!.DefaultTimeout";
        sb.AppendLine("        var __cmd = __c.Ctx!.Connection.CreateCommand();");
        sb.AppendLine("        __cmd.CommandText = sql;");
        sb.AppendLine($"        __cmd.CommandTimeout = (int)({timeoutExpr}).TotalSeconds;");

        // Bind parameters from entities
        var convertBool = InterceptorCodeGenerator.RequiresBoolToIntConversion(chain.Dialect);
        sb.AppendLine("        var __paramIdx = 0;");
        sb.AppendLine("        for (int __row = 0; __row < __entityCount; __row++)");
        sb.AppendLine("        {");
        sb.AppendLine($"            var __entity = __entities[__row];");

        for (int i = 0; i < insertInfo.Columns.Count; i++)
        {
            var col = insertInfo.Columns[i];
            var needsIntType = col.IsEnum || (col.IsBoolean && convertBool);
            var valueExpr = InterceptorCodeGenerator.GetColumnValueExpression("__entity", col.PropertyName, col.IsForeignKey, col.CustomTypeMappingClass, col.IsBoolean, col.IsEnum, col.IsNullable, convertBool);
            sb.AppendLine($"            {{");
            sb.AppendLine($"                var __p = __cmd.CreateParameter();");
            sb.AppendLine($"                __p.ParameterName = \"@p\" + __paramIdx;");
            sb.AppendLine($"                __p.Value = (object?){valueExpr} ?? DBNull.Value;");
            if (needsIntType)
                sb.AppendLine($"                __p.DbType = System.Data.DbType.Int32;");
            sb.AppendLine($"                __cmd.Parameters.Add(__p);");
            sb.AppendLine($"                __paramIdx++;");
            sb.AppendLine($"            }}");
        }

        sb.AppendLine("        }");

        sb.AppendLine($"        return QueryExecutor.{executorMethod}(__opId, __c.Ctx, __cmd, cancellationToken);");
    }

    /// <summary>
    /// Emits a .Prepare() interceptor.
    /// For single-terminal collapse (most common case), this simply Unsafe.As casts the builder
    /// to PreparedQuery&lt;TResult&gt; — the terminal interceptor casts it back, resulting in zero overhead.
    /// </summary>
    public static void EmitPrepareInterceptor(
        StringBuilder sb,
        TranslatedCallSite site,
        string methodName,
        AssembledPlan? chain = null,
        CarrierPlan? carrier = null)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);

        // Use chain's resolved result type if available (handles tuple element names correctly)
        var rawResultType = chain != null
            ? InterceptorCodeGenerator.ResolveExecutionResultType(site.ResultTypeName, chain.ResultTypeName, chain.ProjectionInfo)
            : site.ResultTypeName;
        var resultType = !string.IsNullOrEmpty(rawResultType)
            ? InterceptorCodeGenerator.GetShortTypeName(rawResultType!)
            : entityType;

        // Determine the builder interface type for the receiver
        var thisType = site.BuilderTypeName;

        // Determine the PreparedQuery result type and receiver signature based on builder kind
        string preparedResultType;
        string receiverType;

        switch (site.BuilderKind)
        {
            case BuilderKind.Delete:
            case BuilderKind.ExecutableDelete:
                preparedResultType = "int";
                receiverType = $"{thisType}<{entityType}>";
                break;
            case BuilderKind.Update:
            case BuilderKind.ExecutableUpdate:
                preparedResultType = "int";
                receiverType = $"{thisType}<{entityType}>";
                break;
            case BuilderKind.Insert:
            case BuilderKind.BatchInsert:
            case BuilderKind.ExecutableBatchInsert:
                preparedResultType = "int";
                receiverType = $"{thisType}<{entityType}>";
                break;
            case BuilderKind.JoinedQuery:
                if (chain != null && chain.JoinedEntityTypeNames != null)
                {
                    var joinTypeArgs = string.Join(", ",
                        chain.JoinedEntityTypeNames.Select(InterceptorCodeGenerator.GetShortTypeName));
                    if (site.ResultTypeName != null)
                    {
                        preparedResultType = resultType;
                        receiverType = $"{thisType}<{joinTypeArgs}, {resultType}>";
                    }
                    else
                    {
                        preparedResultType = resultType;
                        receiverType = $"{thisType}<{joinTypeArgs}>";
                    }
                }
                else
                {
                    preparedResultType = resultType;
                    receiverType = $"{thisType}<{entityType}>";
                }
                break;
            default:
                preparedResultType = resultType;
                if (site.ResultTypeName != null)
                    receiverType = $"{thisType}<{entityType}, {resultType}>";
                else
                    receiverType = $"{thisType}<{entityType}>";
                break;
        }

        sb.AppendLine($"    public static PreparedQuery<{preparedResultType}> {methodName}(");
        sb.AppendLine($"        this {receiverType} builder)");
        sb.AppendLine($"    {{");

        // Zero-overhead: cast builder to PreparedQuery<TResult> via Unsafe.As.
        // The prepared terminal interceptor casts it back to the concrete builder type.
        sb.AppendLine($"        return Unsafe.As<PreparedQuery<{preparedResultType}>>(builder);");

        sb.AppendLine($"    }}");
    }
}
