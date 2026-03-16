using System.Collections.Generic;
using System.Text;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;

namespace Quarry.Generators.Generation;

internal static partial class InterceptorCodeGenerator
{
    /// <summary>
    /// Checks whether a chain's execution terminal would pass the generation checks
    /// in GenerateInterceptorMethod. If not, the chain must not be carrier-eligible
    /// because clause interceptors would create carriers with no terminal to consume them.
    /// </summary>
    private static bool WouldExecutionTerminalBeEmitted(PrebuiltChainInfo chain)
    {
        var site = chain.Analysis.ExecutionSite;

        if (chain.Analysis.UnmatchedMethodNames != null)
            return false;

        if (site.Kind is InterceptorKind.ExecuteFetchAll or InterceptorKind.ExecuteFetchFirst
            or InterceptorKind.ExecuteFetchFirstOrDefault or InterceptorKind.ExecuteFetchSingle
            or InterceptorKind.ToAsyncEnumerable)
        {
            var rawResult = ResolveExecutionResultType(site.ResultTypeName, chain.ResultTypeName, chain.ProjectionInfo);
            if (string.IsNullOrEmpty(rawResult))
                return false;
            if (chain.ReaderDelegateCode == null)
                return false;
            if (chain.ProjectionInfo != null && chain.ProjectionInfo.Columns.Any(c =>
                c.SqlExpression != null && !string.IsNullOrEmpty(c.ColumnName)))
                return false;
        }
        else if (site.Kind is InterceptorKind.ExecuteScalar)
        {
            var rawResult = ResolveExecutionResultType(site.ResultTypeName, chain.ResultTypeName, chain.ProjectionInfo);
            if (string.IsNullOrEmpty(rawResult))
                return false;
        }
        else if (site.Kind is InterceptorKind.ExecuteNonQuery)
        {
            if (chain.SqlMap.Values.Any(v => string.IsNullOrWhiteSpace(v.Sql)
                || (chain.QueryKind == QueryKind.Update && v.Sql.Contains("SET  "))))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Resolves the carrier base class name for a chain, handling tuple result types correctly.
    /// Must be called from InterceptorCodeGenerator where SanitizeTupleResultType is accessible.
    /// </summary>
    internal static string ResolveCarrierBaseClass(PrebuiltChainInfo chain)
    {
        var entityType = GetShortTypeName(chain.EntityTypeName);

        // Delete/Update chains use modification-specific base classes
        if (chain.QueryKind == QueryKind.Delete)
            return $"DeleteCarrierBase<{entityType}>";
        if (chain.QueryKind == QueryKind.Update)
            return $"UpdateCarrierBase<{entityType}>";

        var hasSelect = chain.Analysis.Clauses.Any(c => c.Role == ClauseRole.Select);
        var joinCount = chain.IsJoinChain ? (chain.JoinedEntityTypeNames?.Count ?? 1) - 1 : 0;

        string? resultType = null;
        if (hasSelect && chain.ResultTypeName != null)
        {
            // Use the same resolution pipeline as execution terminals for tuple safety
            var resolved = ResolveExecutionResultType(
                chain.Analysis.ExecutionSite.ResultTypeName,
                chain.ResultTypeName,
                chain.ProjectionInfo);
            if (!string.IsNullOrEmpty(resolved))
                resultType = SanitizeTupleResultType(GetShortTypeName(resolved!));
        }

        if (joinCount == 0)
        {
            return resultType != null
                ? $"CarrierBase<{entityType}, {resultType}>"
                : $"CarrierBase<{entityType}>";
        }

        var joinedTypes = chain.JoinedEntityTypeNames!.Select(GetShortTypeName).ToArray();
        var joinedStr = string.Join(", ", joinedTypes);

        if (resultType != null)
        {
            return joinCount switch
            {
                1 => $"JoinedCarrierBase<{joinedStr}, {resultType}>",
                2 => $"JoinedCarrierBase3<{joinedStr}, {resultType}>",
                3 => $"JoinedCarrierBase4<{joinedStr}, {resultType}>",
                _ => $"JoinedCarrierBase<{joinedStr}, {resultType}>"
            };
        }

        return joinCount switch
        {
            1 => $"JoinedCarrierBase<{joinedStr}>",
            2 => $"JoinedCarrierBase3<{joinedStr}>",
            3 => $"JoinedCarrierBase4<{joinedStr}>",
            _ => $"JoinedCarrierBase<{joinedStr}>"
        };
    }

    private static string GetJoinedConcreteBuilderTypeName(int entityCount, string[] entityTypes)
    {
        return entityCount switch
        {
            2 => $"JoinedQueryBuilder<{entityTypes[0]}, {entityTypes[1]}>",
            3 => $"JoinedQueryBuilder3<{entityTypes[0]}, {entityTypes[1]}, {entityTypes[2]}>",
            4 => $"JoinedQueryBuilder4<{entityTypes[0]}, {entityTypes[1]}, {entityTypes[2]}, {entityTypes[3]}>",
            _ => $"JoinedQueryBuilder<{string.Join(", ", entityTypes)}>"
        };
    }

    /// <summary>
    /// Generates a carrier ChainRoot interceptor (e.g., db.Users()).
    /// Creates the carrier directly from the context — zero QueryBuilder allocation.
    /// </summary>
    private static void GenerateCarrierChainRootInterceptor(
        StringBuilder sb, UsageSiteInfo site, string methodName,
        CarrierClassInfo carrier, PrebuiltChainInfo chain)
    {
        var entityType = GetShortTypeName(site.EntityTypeName);
        var contextClass = site.ContextClassName ?? "QuarryContext";

        // Return type matches the context method: IEntityAccessor<T>
        sb.AppendLine($"    public static IEntityAccessor<{entityType}> {methodName}(");
        sb.AppendLine($"        this {contextClass} @this)");
        sb.AppendLine($"    {{");
        sb.AppendLine($"        return new {carrier.ClassName} {{ Ctx = (IQueryExecutionContext)@this }};");
        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Generates a carrier Limit/Offset interceptor method.
    /// </summary>
    private static void GenerateCarrierPaginationInterceptor(
        StringBuilder sb, UsageSiteInfo site, string methodName,
        CarrierClassInfo carrier, PrebuiltChainInfo chain)
    {
        var entityType = GetShortTypeName(site.EntityTypeName);
        var receiverType = ResolveCarrierReceiverType(site, entityType, chain);

        sb.AppendLine($"    public static {receiverType} {methodName}(");
        sb.AppendLine($"        this {receiverType} builder, int count)");
        sb.AppendLine($"    {{");

        var fieldName = site.Kind == InterceptorKind.Limit ? "Limit" : "Offset";

        // Check if this is a runtime value (carrier has the field) or constant (noop)
        if (HasCarrierField(carrier, site.Kind == InterceptorKind.Limit ? FieldRole.Limit : FieldRole.Offset))
        {
            sb.AppendLine($"        Unsafe.As<{carrier.ClassName}>(builder).{fieldName} = count;");
        }

        sb.AppendLine($"        return builder;");
        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Generates a carrier Distinct interceptor method (always noop — Distinct is baked into SQL).
    /// </summary>
    private static void GenerateCarrierDistinctInterceptor(
        StringBuilder sb, UsageSiteInfo site, string methodName,
        CarrierClassInfo carrier, PrebuiltChainInfo? chain = null)
    {
        var entityType = GetShortTypeName(site.EntityTypeName);
        var receiverType = ResolveCarrierReceiverType(site, entityType, chain);
        // IEntityAccessor<T>.Distinct() returns IQueryBuilder<T>, not IEntityAccessor<T>
        var returnTypeName = site.BuilderTypeName is "IEntityAccessor" or "EntityAccessor"
            ? $"IQueryBuilder<{entityType}>" : receiverType;

        sb.AppendLine($"    public static {returnTypeName} {methodName}(");
        sb.AppendLine($"        this {receiverType} builder)");
        sb.AppendLine($"    {{");
        if (returnTypeName != receiverType)
            sb.AppendLine($"        return Unsafe.As<{returnTypeName}>(builder);");
        else
            sb.AppendLine($"        return builder;");
        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Generates a carrier WithTimeout interceptor method.
    /// </summary>
    private static void GenerateCarrierWithTimeoutInterceptor(
        StringBuilder sb, UsageSiteInfo site, string methodName,
        CarrierClassInfo carrier, PrebuiltChainInfo? chain = null)
    {
        var entityType = GetShortTypeName(site.EntityTypeName);
        var receiverType = ResolveCarrierReceiverType(site, entityType, chain);

        sb.AppendLine($"    public static {receiverType} {methodName}(");
        sb.AppendLine($"        this {receiverType} builder, TimeSpan timeout)");
        sb.AppendLine($"    {{");

        if (HasCarrierField(carrier, FieldRole.Timeout))
        {
            sb.AppendLine($"        Unsafe.As<{carrier.ClassName}>(builder).Timeout = timeout;");
        }

        sb.AppendLine($"        return builder;");
        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Resolves the receiver interface type for a carrier interceptor method.
    /// Uses the chain's resolved result type (via ProjectionInfo) to avoid broken tuple types
    /// from the semantic model on non-clause sites like Limit/Offset.
    /// </summary>
    private static string ResolveCarrierReceiverType(UsageSiteInfo site, string entityType, PrebuiltChainInfo? chain = null)
    {
        // If the site's receiver is IEntityAccessor, use that as the receiver type
        if (site.BuilderTypeName is "IEntityAccessor" or "EntityAccessor")
            return $"IEntityAccessor<{entityType}>";

        // Resolve result type: use chain's enriched projection for broken tuples,
        // but preserve element names (no sanitization) since interceptors need exact type match.
        string? resultType = null;
        if (site.ResultTypeName != null)
        {
            var resolved = chain != null
                ? ResolveExecutionResultType(site.ResultTypeName, chain.ResultTypeName, chain.ProjectionInfo)
                : site.ResultTypeName;
            if (!string.IsNullOrEmpty(resolved))
                resultType = GetShortTypeName(resolved!);
        }

        if (site.JoinedEntityTypeNames != null && site.JoinedEntityTypeNames.Count >= 2)
        {
            var joinedTypes = site.JoinedEntityTypeNames.Select(GetShortTypeName).ToArray();
            if (resultType != null)
            {
                return site.JoinedEntityTypeNames.Count switch
                {
                    2 => $"IJoinedQueryBuilder<{joinedTypes[0]}, {joinedTypes[1]}, {resultType}>",
                    3 => $"IJoinedQueryBuilder3<{joinedTypes[0]}, {joinedTypes[1]}, {joinedTypes[2]}, {resultType}>",
                    4 => $"IJoinedQueryBuilder4<{joinedTypes[0]}, {joinedTypes[1]}, {joinedTypes[2]}, {joinedTypes[3]}, {resultType}>",
                    _ => $"IJoinedQueryBuilder<{string.Join(", ", joinedTypes)}, {resultType}>"
                };
            }
            return site.JoinedEntityTypeNames.Count switch
            {
                2 => $"IJoinedQueryBuilder<{joinedTypes[0]}, {joinedTypes[1]}>",
                3 => $"IJoinedQueryBuilder3<{joinedTypes[0]}, {joinedTypes[1]}, {joinedTypes[2]}>",
                4 => $"IJoinedQueryBuilder4<{joinedTypes[0]}, {joinedTypes[1]}, {joinedTypes[2]}, {joinedTypes[3]}>",
                _ => $"IJoinedQueryBuilder<{string.Join(", ", joinedTypes)}>"
            };
        }

        if (resultType != null)
            return $"IQueryBuilder<{entityType}, {resultType}>";

        return $"IQueryBuilder<{entityType}>";
    }

    /// <summary>
    /// Emits a complete carrier clause body for Where/Having/OrderBy/GroupBy interceptors.
    /// This method handles the full body: carrier creation (first-in-chain), parameter extraction
    /// using the same FieldInfo mechanism as the prebuilt path, clause bit setting, and return.
    /// </summary>
    /// <param name="sb">String builder</param>
    /// <param name="carrier">Carrier class info</param>
    /// <param name="chain">Prebuilt chain info</param>
    /// <param name="site">Usage site info</param>
    /// <param name="clauseBit">Conditional clause bit index, if applicable</param>
    /// <param name="isFirstInChain">Whether this is the first clause in the chain</param>
    /// <param name="concreteBuilderType">Concrete builder type for Unsafe.As cast (first-in-chain only)</param>
    /// <param name="returnInterface">Return interface type (first-in-chain only)</param>
    /// <param name="hasResolvableCapturedParams">Whether the clause has captured params needing FieldInfo extraction</param>
    /// <param name="methodFields">Cached static fields for this method's parameter extraction</param>
    private static void EmitCarrierClauseBody(
        StringBuilder sb, CarrierClassInfo carrier, PrebuiltChainInfo chain,
        UsageSiteInfo site, int? clauseBit, bool isFirstInChain,
        string concreteBuilderType, string returnInterface,
        bool hasResolvableCapturedParams, List<CachedExtractorField> methodFields)
    {
        // Compute global parameter offset for this clause's params
        var globalParamOffset = 0;
        foreach (var clause in chain.Analysis.Clauses)
        {
            if (clause.Site.UniqueId == site.UniqueId)
                break;
            if (clause.Site.ClauseInfo != null)
                globalParamOffset += clause.Site.ClauseInfo.Parameters.Count;
        }

        if (isFirstInChain)
        {
            sb.AppendLine($"        var __b = Unsafe.As<{concreteBuilderType}>(builder);");
            sb.AppendLine($"        var __c = new {carrier.ClassName} {{ Ctx = __b.State.ExecutionContext }};");
        }
        else
        {
            sb.AppendLine($"        var __c = Unsafe.As<{carrier.ClassName}>(builder);");
        }

        // Extract and bind parameters using same FieldInfo extraction as prebuilt path
        var clauseInfo = site.ClauseInfo;
        if (clauseInfo != null && clauseInfo.Parameters.Count > 0)
        {
            if (hasResolvableCapturedParams)
                GenerateCachedExtraction(sb, methodFields);

            var allParams = clauseInfo.Parameters.OrderBy(p => p.Index).ToList();
            for (int i = 0; i < allParams.Count; i++)
            {
                var p = allParams[i];
                var extractExpr = p.IsCaptured ? $"p{p.Index}" : p.ValueExpression;
                var globalIdx = globalParamOffset + i;
                if (globalIdx < chain.ChainParameters.Count)
                {
                    var carrierParam = chain.ChainParameters[globalIdx];
                    sb.AppendLine($"        __c.P{globalIdx} = ({carrierParam.TypeName}){extractExpr}!;");
                }
            }
        }

        if (clauseBit.HasValue)
            sb.AppendLine($"        __c.Mask |= unchecked(({GetMaskType(chain)})(1 << {clauseBit.Value}));");

        // Always use Unsafe.As for the return — handles interface crossings
        // (e.g., IUpdateBuilder → IExecutableUpdateBuilder after Where)
        if (isFirstInChain)
        {
            sb.AppendLine($"        return Unsafe.As<{returnInterface}>(__c);");
        }
        else
        {
            sb.AppendLine($"        return Unsafe.As<{returnInterface}>(builder);");
        }
    }

    /// <summary>
    /// Emits a carrier class at namespace scope (outside the static interceptor class).
    /// </summary>
    private static void EmitCarrierClass(StringBuilder sb, CarrierClassInfo info)
    {
        sb.AppendLine($"/// <remarks>Chain: Carrier-Optimized PrebuiltDispatch (2 allocations: carrier + param array)</remarks>");
        sb.Append($"file sealed class {info.ClassName}");

        if (info.ImplementedInterfaces.Count > 0)
        {
            sb.Append(" : ");
            for (int i = 0; i < info.ImplementedInterfaces.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(info.ImplementedInterfaces[i]);
            }
        }

        sb.AppendLine();
        sb.AppendLine("{");

        // Emit fields
        foreach (var field in info.Fields)
        {
            sb.AppendLine($"    internal {field.TypeName} {field.Name};");
        }

        // Emit dead methods (explicit interface impls that throw)
        foreach (var stub in info.DeadMethods)
        {
            sb.AppendLine();
            EmitDeadInterfaceMethod(sb, stub);
        }

        sb.AppendLine("}");
        sb.AppendLine();
    }

    /// <summary>
    /// Emits an explicit interface method implementation that throws InvalidOperationException.
    /// </summary>
    private static void EmitDeadInterfaceMethod(StringBuilder sb, CarrierInterfaceStub stub)
    {
        sb.AppendLine($"    {stub.FullSignature}");
        sb.AppendLine($"        => throw new InvalidOperationException(\"Method {stub.MethodName} is not used in this carrier-optimized chain.\");");
    }

    /// <summary>
    /// Emits the carrier chain entry interceptor (first clause in chain).
    /// Creates the carrier and extracts ExecutionContext from the incoming builder.
    /// </summary>
    private static void EmitCarrierChainEntry(
        StringBuilder sb, CarrierClassInfo carrier, PrebuiltChainInfo chain,
        UsageSiteInfo site, string builderTypeName, string returnInterface,
        int? bitIndex, IReadOnlyList<ChainParameterInfo> siteParams, int globalParamOffset)
    {
        // Cast incoming builder to concrete type to extract ExecutionContext
        sb.AppendLine($"        var __b = Unsafe.As<{builderTypeName}>(builder);");
        sb.Append($"        var __c = new {carrier.ClassName} {{ ");

        var isToSqlOnly = chain.Analysis.ExecutionSite.Kind == InterceptorKind.ToSql;
        if (!isToSqlOnly)
        {
            sb.Append("Ctx = __b.State.ExecutionContext");
        }

        sb.AppendLine(" };");

        // Bind parameters if this clause has any
        EmitCarrierParamBindings(sb, siteParams, globalParamOffset);

        // Set clause bit if conditional
        if (bitIndex.HasValue)
        {
            sb.AppendLine($"        __c.Mask |= unchecked(({GetMaskType(chain)})(1 << {bitIndex.Value}));");
        }

        sb.AppendLine($"        return Unsafe.As<{returnInterface}>(__c);");
    }

    /// <summary>
    /// Emits a carrier parameter-binding interceptor (Where, Having with params).
    /// </summary>
    private static void EmitCarrierParamBind(
        StringBuilder sb, CarrierClassInfo carrier, PrebuiltChainInfo chain,
        int? bitIndex, IReadOnlyList<ChainParameterInfo> siteParams, int globalParamOffset)
    {
        sb.AppendLine($"        var __c = Unsafe.As<{carrier.ClassName}>(builder);");

        EmitCarrierParamBindings(sb, siteParams, globalParamOffset);

        if (bitIndex.HasValue)
        {
            sb.AppendLine($"        __c.Mask |= unchecked(({GetMaskType(chain)})(1 << {bitIndex.Value}));");
        }

        sb.AppendLine("        return builder;");
    }

    /// <summary>
    /// Emits a carrier noop interceptor (Join, unconditional OrderBy/ThenBy/GroupBy, Distinct).
    /// </summary>
    private static void EmitCarrierNoop(
        StringBuilder sb, CarrierClassInfo carrier, PrebuiltChainInfo chain,
        int? bitIndex)
    {
        if (bitIndex.HasValue)
        {
            sb.AppendLine($"        Unsafe.As<{carrier.ClassName}>(builder).Mask |= unchecked(({GetMaskType(chain)})(1 << {bitIndex.Value}));");
        }

        sb.AppendLine("        return builder;");
    }

    /// <summary>
    /// Emits a carrier Select interceptor (interface crossing via Unsafe.As).
    /// </summary>
    private static void EmitCarrierSelect(StringBuilder sb, string targetInterface)
    {
        sb.AppendLine($"        return Unsafe.As<{targetInterface}>(builder);");
    }

    /// <summary>
    /// Emits a carrier pagination bind (Limit/Offset with runtime value).
    /// </summary>
    private static void EmitCarrierPaginationBind(
        StringBuilder sb, CarrierClassInfo carrier, string fieldName, string valueExpression)
    {
        sb.AppendLine($"        Unsafe.As<{carrier.ClassName}>(builder).{fieldName} = {valueExpression};");
        sb.AppendLine("        return builder;");
    }

    /// <summary>
    /// Emits a carrier WithTimeout interceptor.
    /// </summary>
    private static void EmitCarrierWithTimeout(
        StringBuilder sb, CarrierClassInfo carrier, string timeoutExpression)
    {
        sb.AppendLine($"        Unsafe.As<{carrier.ClassName}>(builder).Timeout = {timeoutExpression};");
        sb.AppendLine("        return builder;");
    }

    /// <summary>
    /// Emits a carrier execution terminal interceptor.
    /// </summary>
    private static void EmitCarrierExecutionTerminal(
        StringBuilder sb, CarrierClassInfo carrier, PrebuiltChainInfo chain,
        string readerExpression, string executorMethod)
    {
        sb.AppendLine($"        var __c = Unsafe.As<{carrier.ClassName}>(builder);");

        // Timeout resolution
        var hasTimeout = HasCarrierField(carrier, FieldRole.Timeout);
        if (hasTimeout)
        {
            sb.AppendLine("        var __timeout = __c.Timeout ?? __c.Ctx!.DefaultTimeout;");
        }
        else
        {
            sb.AppendLine("        var __timeout = __c.Ctx!.DefaultTimeout;");
        }

        // SQL dispatch
        EmitCarrierSqlDispatch(sb, chain);

        // Parameter array fill
        var paramCount = chain.ChainParameters.Count;
        var hasLimitField = HasCarrierField(carrier, FieldRole.Limit);
        var hasOffsetField = HasCarrierField(carrier, FieldRole.Offset);
        var totalParamCount = paramCount + (hasLimitField ? 1 : 0) + (hasOffsetField ? 1 : 0);

        if (totalParamCount > 0)
        {
            sb.AppendLine($"        var __args = new object?[{totalParamCount}];");
            for (int i = 0; i < paramCount; i++)
            {
                var param = chain.ChainParameters[i];
                if (param.TypeMapping != null)
                {
                    sb.AppendLine($"        __args[{i}] = s_{param.TypeMapping}.ToDb(__c.P{i});");
                }
                else
                {
                    sb.AppendLine($"        __args[{i}] = __c.P{i};");
                }
            }
            var nextIdx = paramCount;
            if (hasLimitField)
            {
                sb.AppendLine($"        __args[{nextIdx}] = __c.Limit;");
                nextIdx++;
            }
            if (hasOffsetField)
            {
                sb.AppendLine($"        __args[{nextIdx}] = __c.Offset;");
            }

            var dialectStr = GetDialectLiteral(chain.Dialect);
            sb.AppendLine($"        return QueryExecutor.{executorMethod}(__c.Ctx, sql, {dialectStr}, __args, {readerExpression}, __timeout, cancellationToken);");
        }
        else
        {
            // Zero-param: use regular overload with empty array
            var dialectStr = GetDialectLiteral(chain.Dialect);
            sb.AppendLine($"        return QueryExecutor.{executorMethod}(__c.Ctx, sql, {dialectStr}, System.Array.Empty<object?>(), {readerExpression}, __timeout, cancellationToken);");
        }
    }

    /// <summary>
    /// Emits a carrier non-query execution terminal (DELETE/UPDATE).
    /// </summary>
    private static void EmitCarrierNonQueryTerminal(
        StringBuilder sb, CarrierClassInfo carrier, PrebuiltChainInfo chain)
    {
        sb.AppendLine($"        var __c = Unsafe.As<{carrier.ClassName}>(builder);");

        var hasTimeout = HasCarrierField(carrier, FieldRole.Timeout);
        if (hasTimeout)
        {
            sb.AppendLine("        var __timeout = __c.Timeout ?? __c.Ctx!.DefaultTimeout;");
        }
        else
        {
            sb.AppendLine("        var __timeout = __c.Ctx!.DefaultTimeout;");
        }

        EmitCarrierSqlDispatch(sb, chain);

        var paramCount = chain.ChainParameters.Count;
        if (paramCount > 0)
        {
            sb.AppendLine($"        var __args = new object?[{paramCount}];");
            for (int i = 0; i < paramCount; i++)
            {
                var param = chain.ChainParameters[i];
                if (param.TypeMapping != null)
                {
                    sb.AppendLine($"        __args[{i}] = s_{param.TypeMapping}.ToDb(__c.P{i});");
                }
                else
                {
                    sb.AppendLine($"        __args[{i}] = __c.P{i};");
                }
            }

            var dialectStr = GetDialectLiteral(chain.Dialect);
            sb.AppendLine($"        return QueryExecutor.ExecuteCarrierNonQueryAsync(__c.Ctx, sql, {dialectStr}, __args, __timeout, cancellationToken);");
        }
        else
        {
            var dialectStr = GetDialectLiteral(chain.Dialect);
            sb.AppendLine($"        return QueryExecutor.ExecuteCarrierNonQueryAsync(__c.Ctx, sql, {dialectStr}, System.Array.Empty<object?>(), __timeout, cancellationToken);");
        }
    }

    /// <summary>
    /// Emits a carrier ToSql terminal.
    /// </summary>
    private static void EmitCarrierToSqlTerminal(
        StringBuilder sb, CarrierClassInfo carrier, PrebuiltChainInfo chain)
    {
        if (chain.SqlMap.Count == 1)
        {
            // Single variant — no carrier access needed
            foreach (var kvp in chain.SqlMap)
            {
                sb.AppendLine($"        return @\"{EscapeStringLiteral(kvp.Value.Sql)}\";");
            }
        }
        else
        {
            sb.AppendLine($"        var __c = Unsafe.As<{carrier.ClassName}>(builder);");
            sb.AppendLine("        return __c.Mask switch");
            sb.AppendLine("        {");
            foreach (var kvp in chain.SqlMap)
            {
                sb.AppendLine($"            {kvp.Key} => @\"{EscapeStringLiteral(kvp.Value.Sql)}\",");
            }
            sb.AppendLine("            _ => throw new InvalidOperationException(\"Unexpected ClauseMask value.\")");
            sb.AppendLine("        };");
        }
    }

    #region Carrier helpers

    private static void EmitCarrierParamBindings(
        StringBuilder sb, IReadOnlyList<ChainParameterInfo> siteParams, int globalParamOffset)
    {
        for (int i = 0; i < siteParams.Count; i++)
        {
            var param = siteParams[i];
            sb.AppendLine($"        __c.P{globalParamOffset + i} = {param.ValueExpression};");
        }
    }

    private static void EmitCarrierSqlDispatch(StringBuilder sb, PrebuiltChainInfo chain)
    {
        if (chain.SqlMap.Count == 1)
        {
            foreach (var kvp in chain.SqlMap)
            {
                sb.AppendLine($"        const string sql = @\"{EscapeStringLiteral(kvp.Value.Sql)}\";");
            }
        }
        else
        {
            sb.AppendLine("        var sql = __c.Mask switch");
            sb.AppendLine("        {");
            foreach (var kvp in chain.SqlMap)
            {
                sb.AppendLine($"            {kvp.Key} => @\"{EscapeStringLiteral(kvp.Value.Sql)}\",");
            }
            sb.AppendLine("            _ => throw new InvalidOperationException(\"Unexpected ClauseMask value.\")");
            sb.AppendLine("        };");
        }
    }

    private static bool HasCarrierField(CarrierClassInfo carrier, FieldRole role)
    {
        foreach (var field in carrier.Fields)
        {
            if (field.Role == role)
                return true;
        }
        return false;
    }

    private static string GetMaskType(PrebuiltChainInfo chain)
    {
        var bitCount = chain.Analysis.ConditionalClauses.Count;
        return bitCount <= 8 ? "byte" : bitCount <= 16 ? "ushort" : "uint";
    }

    private static string GetDialectLiteral(SqlDialect dialect)
    {
        return dialect switch
        {
            SqlDialect.PostgreSQL => "SqlDialect.PostgreSQL",
            SqlDialect.SqlServer => "SqlDialect.SqlServer",
            SqlDialect.SQLite => "SqlDialect.SQLite",
            SqlDialect.MySQL => "SqlDialect.MySQL",
            _ => $"(SqlDialect){(int)dialect}"
        };
    }

    #endregion
}
