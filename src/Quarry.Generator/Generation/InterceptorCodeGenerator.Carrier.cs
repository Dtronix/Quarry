using System.Collections.Generic;
using System.Text;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;
using Quarry.Generators.Translation;

namespace Quarry.Generators.Generation;

internal static partial class InterceptorCodeGenerator
{
    /// <summary>
    /// Checks whether a chain's execution terminal would pass the generation checks
    /// in the terminal generator methods. If not, the chain must not be carrier-eligible
    /// because clause interceptors would create carriers with no terminal to consume them.
    /// </summary>
    /// <remarks>
    /// This method delegates to per-kind predicates (<see cref="CanEmitReaderTerminal"/>,
    /// <see cref="CanEmitScalarTerminal"/>, <see cref="CanEmitNonQueryTerminal"/>,
    /// <see cref="CanEmitInsertTerminal"/>) that are also used as guards in the terminal
    /// generators themselves, ensuring the eligibility logic stays in sync.
    /// </remarks>
    private static bool WouldExecutionTerminalBeEmitted(PrebuiltChainInfo chain)
    {
        if (chain.Analysis.UnmatchedMethodNames != null)
            return false;

        var site = chain.Analysis.ExecutionSite;
        return site.Kind switch
        {
            InterceptorKind.ExecuteFetchAll or InterceptorKind.ExecuteFetchFirst
                or InterceptorKind.ExecuteFetchFirstOrDefault or InterceptorKind.ExecuteFetchSingle
                or InterceptorKind.ToAsyncEnumerable
                => CanEmitReaderTerminal(chain),
            InterceptorKind.ExecuteScalar
                => CanEmitScalarTerminal(chain),
            InterceptorKind.ExecuteNonQuery
                => CanEmitNonQueryTerminal(chain),
            InterceptorKind.InsertExecuteNonQuery or InterceptorKind.InsertExecuteScalar
                or InterceptorKind.InsertToDiagnostics
                => CanEmitInsertTerminal(chain),
            _ => true
        };
    }

    /// <summary>
    /// Returns true if a reader-based terminal (FetchAll, FetchFirst, etc.) can be emitted.
    /// Used by both carrier eligibility gating and the terminal generators.
    /// </summary>
    private static bool CanEmitReaderTerminal(PrebuiltChainInfo chain)
    {
        var rawResult = ResolveExecutionResultType(
            chain.Analysis.ExecutionSite.ResultTypeName, chain.ResultTypeName, chain.ProjectionInfo);
        if (string.IsNullOrEmpty(rawResult))
            return false;
        if (chain.ReaderDelegateCode == null)
            return false;
        if (chain.ProjectionInfo != null && chain.ProjectionInfo.Columns.Any(c =>
            c.SqlExpression != null && !string.IsNullOrEmpty(c.ColumnName)))
            return false;
        return true;
    }

    /// <summary>
    /// Returns true if a scalar terminal (ExecuteScalar) can be emitted.
    /// Used by both carrier eligibility gating and the terminal generators.
    /// </summary>
    private static bool CanEmitScalarTerminal(PrebuiltChainInfo chain)
    {
        var rawResult = ResolveExecutionResultType(
            chain.Analysis.ExecutionSite.ResultTypeName, chain.ResultTypeName, chain.ProjectionInfo);
        return !string.IsNullOrEmpty(rawResult);
    }

    /// <summary>
    /// Returns true if a non-query terminal (DELETE/UPDATE ExecuteNonQuery) can be emitted.
    /// Used by both carrier eligibility gating and the terminal generators.
    /// </summary>
    private static bool CanEmitNonQueryTerminal(PrebuiltChainInfo chain)
    {
        return !chain.SqlMap.Values.Any(v => string.IsNullOrWhiteSpace(v.Sql)
            || (chain.QueryKind == QueryKind.Update && v.Sql.Contains("SET  ")));
    }

    /// <summary>
    /// Returns true if an insert terminal can be emitted.
    /// Used by both carrier eligibility gating and the terminal generators.
    /// </summary>
    private static bool CanEmitInsertTerminal(PrebuiltChainInfo chain)
    {
        return !chain.SqlMap.Values.Any(v => string.IsNullOrWhiteSpace(v.Sql));
    }

    /// <summary>
    /// Resolves the carrier base class name for a chain, handling tuple result types correctly.
    /// Must be called from InterceptorCodeGenerator where SanitizeTupleResultType is accessible.
    /// </summary>
    internal static string ResolveCarrierBaseClass(PrebuiltChainInfo chain)
    {
        var entityType = GetShortTypeName(chain.EntityTypeName);

        // Modification chains use modification-specific base classes
        if (chain.QueryKind == QueryKind.Delete)
            return $"DeleteCarrierBase<{entityType}>";
        if (chain.QueryKind == QueryKind.Update)
            return $"UpdateCarrierBase<{entityType}>";
        if (chain.QueryKind == QueryKind.Insert)
            return $"InsertCarrierBase<{entityType}>";

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
    /// Generates a carrier Delete/Update transition interceptor.
    /// .Delete()/.Update() on IEntityAccessor is a noop on the carrier path —
    /// the carrier already implements both IEntityAccessor and IDeleteBuilder/IUpdateBuilder.
    /// </summary>
    private static void GenerateCarrierTransitionInterceptor(
        StringBuilder sb, UsageSiteInfo site, string methodName)
    {
        var entityType = GetShortTypeName(site.EntityTypeName);
        var returnType = site.Kind == InterceptorKind.DeleteTransition
            ? $"IDeleteBuilder<{entityType}>"
            : $"IUpdateBuilder<{entityType}>";

        sb.AppendLine($"    public static {returnType} {methodName}(");
        sb.AppendLine($"        this IEntityAccessor<{entityType}> builder)");
        sb.AppendLine($"    {{");
        sb.AppendLine($"        return Unsafe.As<{returnType}>(builder);");
        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Generates a carrier Insert transition interceptor.
    /// .Insert(entity) on IEntityAccessor stores the entity on the carrier and
    /// returns it as IInsertBuilder.
    /// </summary>
    private static void GenerateCarrierInsertTransitionInterceptor(
        StringBuilder sb, UsageSiteInfo site, string methodName, CarrierClassInfo carrier)
    {
        var entityType = GetShortTypeName(site.EntityTypeName);

        sb.AppendLine($"    public static IInsertBuilder<{entityType}> {methodName}(");
        sb.AppendLine($"        this IEntityAccessor<{entityType}> builder,");
        sb.AppendLine($"        {entityType} entity)");
        sb.AppendLine($"    {{");
        sb.AppendLine($"        var __c = Unsafe.As<{carrier.ClassName}>(builder);");
        sb.AppendLine($"        __c.Entity = entity;");
        sb.AppendLine($"        return Unsafe.As<IInsertBuilder<{entityType}>>(__c);");
        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Generates an All() transition interceptor.
    /// .All() on IDeleteBuilder/IUpdateBuilder transitions to IExecutableDeleteBuilder/IExecutableUpdateBuilder.
    /// On the carrier path: noop cast (carrier implements both interfaces).
    /// On the non-carrier path: delegates to the real All() method.
    /// </summary>
    private static void GenerateAllTransitionInterceptor(
        StringBuilder sb, UsageSiteInfo site, string methodName,
        CarrierClassInfo? carrier)
    {
        var entityType = GetShortTypeName(site.EntityTypeName);
        // Determine receiver and return types based on builder kind
        var isDelete = site.BuilderKind is BuilderKind.Delete or BuilderKind.ExecutableDelete;
        var receiverType = isDelete
            ? $"IDeleteBuilder<{entityType}>"
            : $"IUpdateBuilder<{entityType}>";
        var returnType = isDelete
            ? $"IExecutableDeleteBuilder<{entityType}>"
            : $"IExecutableUpdateBuilder<{entityType}>";
        var concreteType = isDelete
            ? $"DeleteBuilder<{entityType}>"
            : $"UpdateBuilder<{entityType}>";

        sb.AppendLine($"    public static {returnType} {methodName}(");
        sb.AppendLine($"        this {receiverType} builder)");
        sb.AppendLine($"    {{");

        if (carrier != null)
        {
            // Carrier path: noop cast
            sb.AppendLine($"        return Unsafe.As<{returnType}>(builder);");
        }
        else
        {
            // Non-carrier path: delegate to the real All() method
            sb.AppendLine($"        return Unsafe.As<{concreteType}>(builder).All();");
        }

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
            if (clause.Site.Kind == InterceptorKind.UpdateSetPoco && clause.Site.UpdateInfo != null)
                globalParamOffset += clause.Site.UpdateInfo.Columns.Count;
            else if (clause.Site.ClauseInfo != null)
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

        // Extract and bind parameters using FieldInfo extraction with carrier-owned statics
        var clauseInfo = site.ClauseInfo;
        if (clauseInfo != null && clauseInfo.Parameters.Count > 0)
        {
            // Single-pass partition: scalar vs collection params
            var scalarParams = new List<ParameterInfo>(clauseInfo.Parameters.Count);
            var collectionParams = new List<ParameterInfo>();
            foreach (var p in clauseInfo.Parameters)
            {
                if (p.ExpressionPath == "__CONTAINS_COLLECTION__")
                    collectionParams.Add(p);
                else
                    scalarParams.Add(p);
            }

            if (hasResolvableCapturedParams && scalarParams.Any(p => p.IsCaptured && p.CanGenerateDirectPath))
            {
                // Remap FieldInfo references from interceptor-class statics to carrier-class statics
                var carrierFields = new List<CachedExtractorField>();
                foreach (var mf in methodFields)
                {
                    var globalIdx = globalParamOffset + mf.ParameterIndex;
                    var carrierFieldName = $"{carrier.ClassName}.F{globalIdx}";
                    carrierFields.Add(new CachedExtractorField(
                        carrierFieldName, mf.MethodName, mf.ParameterIndex, mf.ExpressionPath));
                }
                GenerateCachedExtraction(sb, carrierFields);
            }

            var allParams = clauseInfo.Parameters.OrderBy(p => p.Index).ToList();
            for (int i = 0; i < allParams.Count; i++)
            {
                var p = allParams[i];
                var globalIdx = globalParamOffset + i;
                if (globalIdx >= chain.ChainParameters.Count) continue;
                var carrierParam = chain.ChainParameters[globalIdx];

                if (p.ExpressionPath == "__CONTAINS_COLLECTION__")
                {
                    // Collection parameter: extract from MethodCallExpression in the expression tree.
                    // Contains() compiles as either instance (Object) or extension (Arguments[0]).
                    EmitCollectionContainsExtraction(sb, globalIdx, carrierParam);
                }
                else
                {
                    // Set clauses: the value comes from the 'value' method parameter,
                    // not from an inlined literal or captured closure.
                    var isSetClause = site.ClauseInfo is SetClauseInfo;
                    var extractExpr = isSetClause ? "value"
                        : (p.IsCaptured ? $"p{p.Index}" : p.ValueExpression);
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
        sb.AppendLine($"/// <remarks>Chain: Carrier-Optimized PrebuiltDispatch (1 allocation: carrier)</remarks>");
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

        // Emit instance fields (typed params, mask, limit, offset, timeout)
        foreach (var field in info.Fields)
        {
            // Collection fields are non-nullable reference types — use null! to suppress CS8618
            // since they are always assigned by the clause interceptor before the terminal reads them.
            var initializer = field.Role == FieldRole.Collection ? " = null!" : "";
            sb.AppendLine($"    internal {field.TypeName} {field.Name}{initializer};");
        }

        // Emit static fields (FieldInfo caches for captured params)
        foreach (var staticField in info.StaticFields)
        {
            sb.AppendLine($"    internal static {staticField.TypeName} {staticField.Name};");
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

        var isReadOnly = chain.Analysis.ExecutionSite.Kind is InterceptorKind.ToDiagnostics;
        if (!isReadOnly)
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
    /// Emits the common carrier terminal preamble: Unsafe.As cast, optional OpId, and SQL dispatch.
    /// </summary>
    private static void EmitCarrierPreamble(
        StringBuilder sb, CarrierClassInfo carrier, PrebuiltChainInfo chain,
        bool emitOpId = true)
    {
        sb.AppendLine($"        var __c = Unsafe.As<{carrier.ClassName}>(builder);");
        if (emitOpId)
            sb.AppendLine("        var __opId = OpId.Next();");
        EmitCarrierSqlDispatch(sb, chain);
    }

    /// <summary>
    /// Emits parameter value extraction into __pVal* local variables.
    /// These locals can be consumed by either <see cref="EmitCarrierCommandBinding"/> (execution)
    /// or <see cref="EmitDiagnosticParameterArray"/> (diagnostics).
    /// </summary>
    private static void EmitCarrierParameterLocals(
        StringBuilder sb, PrebuiltChainInfo chain, CarrierClassInfo carrier)
    {
        var paramCount = chain.ChainParameters.Count;
        var hasLimitField = HasCarrierField(carrier, FieldRole.Limit);
        var hasOffsetField = HasCarrierField(carrier, FieldRole.Offset);

        for (int i = 0; i < paramCount; i++)
        {
            var param = chain.ChainParameters[i];
            // Collection parameters are handled by EmitCollectionExpansion via __col* locals
            if (param.IsCollection) continue;
            sb.AppendLine($"        var __pVal{i} = {GetParameterValueExpression(param, i)};");
        }

        if (hasLimitField)
            sb.AppendLine("        var __pValL = (object)__c.Limit;");
        if (hasOffsetField)
            sb.AppendLine("        var __pValO = (object)__c.Offset;");
    }

    /// <summary>
    /// Emits DbCommand creation and binds __pVal* locals to parameters.
    /// Must be called after <see cref="EmitCarrierParameterLocals"/>.
    /// </summary>
    private static void EmitCarrierCommandBinding(
        StringBuilder sb, PrebuiltChainInfo chain, CarrierClassInfo carrier,
        string timeoutExpr)
    {
        sb.AppendLine("        var __cmd = __c.Ctx.Connection.CreateCommand();");
        sb.AppendLine("        __cmd.CommandText = sql;");
        sb.AppendLine($"        __cmd.CommandTimeout = (int)({timeoutExpr}).TotalSeconds;");

        var dialectLiteral = GetDialectLiteral(chain.Dialect);
        var paramCount = chain.ChainParameters.Count;
        var hasLimitField = HasCarrierField(carrier, FieldRole.Limit);
        var hasOffsetField = HasCarrierField(carrier, FieldRole.Offset);

        for (int i = 0; i < paramCount; i++)
        {
            var param = chain.ChainParameters[i];
            sb.AppendLine($"        var __p{i} = __cmd.CreateParameter();");
            sb.AppendLine($"        __p{i}.ParameterName = \"@p{i}\";");
            sb.AppendLine($"        __p{i}.Value = __pVal{i};");

            if (param.TypeMapping != null)
            {
                var mappingField = GetMappingFieldName(param.TypeMapping);
                sb.AppendLine($"        ({mappingField} as IDialectAwareTypeMapping)?.ConfigureParameter({dialectLiteral}, __p{i});");
            }

            sb.AppendLine($"        __cmd.Parameters.Add(__p{i});");
        }

        var nextIdx = paramCount;
        if (hasLimitField)
        {
            sb.AppendLine($"        var __pL = __cmd.CreateParameter();");
            sb.AppendLine($"        __pL.ParameterName = \"@p{nextIdx}\";");
            sb.AppendLine($"        __pL.Value = __pValL;");
            sb.AppendLine($"        __cmd.Parameters.Add(__pL);");
            nextIdx++;
        }
        if (hasOffsetField)
        {
            sb.AppendLine($"        var __pO = __cmd.CreateParameter();");
            sb.AppendLine($"        __pO.ParameterName = \"@p{nextIdx}\";");
            sb.AppendLine($"        __pO.Value = __pValO;");
            sb.AppendLine($"        __cmd.Parameters.Add(__pO);");
        }
    }

    /// <summary>
    /// Emits a carrier execution terminal with inline per-parameter DbCommand binding.
    /// Terminal owns: OpId, SQL/param logging, command creation, param binding.
    /// Executor owns: connection open, execution, materialization, completion logging.
    /// </summary>
    private static void EmitCarrierExecutionTerminal(
        StringBuilder sb, CarrierClassInfo carrier, PrebuiltChainInfo chain,
        string? readerExpression, string executorMethod)
    {
        EmitCarrierPreamble(sb, carrier, chain);

        // SQL logging
        sb.AppendLine("        if (LogManager.IsEnabled(LogLevel.Debug, QueryLog.CategoryName))");
        sb.AppendLine("            QueryLog.SqlGenerated(__opId, sql);");

        // Parameter logging
        EmitInlineParameterLogging(sb, chain);

        // Parameter value extraction + command binding
        EmitCarrierParameterLocals(sb, chain, carrier);
        var timeoutExpr = HasCarrierField(carrier, FieldRole.Timeout)
            ? "__c.Timeout ?? __c.Ctx!.DefaultTimeout"
            : "__c.Ctx!.DefaultTimeout";
        EmitCarrierCommandBinding(sb, chain, carrier, timeoutExpr);

        // Executor call
        var readerArg = readerExpression != null ? $", {readerExpression}" : "";
        sb.AppendLine($"        return QueryExecutor.{executorMethod}(__opId, __c.Ctx, __cmd{readerArg}, cancellationToken);");
    }

    /// <summary>
    /// Emits a carrier non-query execution terminal (DELETE/UPDATE) with inline binding.
    /// </summary>
    private static void EmitCarrierNonQueryTerminal(
        StringBuilder sb, CarrierClassInfo carrier, PrebuiltChainInfo chain)
    {
        EmitCarrierPreamble(sb, carrier, chain);

        sb.AppendLine("        if (LogManager.IsEnabled(LogLevel.Debug, QueryLog.CategoryName))");
        sb.AppendLine("            QueryLog.SqlGenerated(__opId, sql);");

        EmitInlineParameterLogging(sb, chain);

        EmitCarrierParameterLocals(sb, chain, carrier);
        var timeoutExpr = HasCarrierField(carrier, FieldRole.Timeout)
            ? "__c.Timeout ?? __c.Ctx!.DefaultTimeout"
            : "__c.Ctx!.DefaultTimeout";
        EmitCarrierCommandBinding(sb, chain, carrier, timeoutExpr);

        sb.AppendLine("        return QueryExecutor.ExecuteCarrierNonQueryWithCommandAsync(__opId, __c.Ctx, __cmd, cancellationToken);");
    }

    /// <summary>
    /// Emits inline per-parameter logging (sensitivity-aware).
    /// </summary>
    private static void EmitInlineParameterLogging(StringBuilder sb, PrebuiltChainInfo chain)
    {
        var paramCount = chain.ChainParameters.Count;
        var hasLimitField = chain.Analysis.Clauses.Any(c => c.Role == ClauseRole.Limit);
        var hasOffsetField = chain.Analysis.Clauses.Any(c => c.Role == ClauseRole.Offset);
        var totalParams = paramCount + (hasLimitField ? 1 : 0) + (hasOffsetField ? 1 : 0);

        if (totalParams == 0)
            return;

        sb.AppendLine("        if (LogManager.IsEnabled(LogLevel.Trace, ParameterLog.CategoryName))");
        sb.AppendLine("        {");
        for (int i = 0; i < paramCount; i++)
        {
            var param = chain.ChainParameters[i];
            if (param.IsSensitive)
            {
                sb.AppendLine($"            ParameterLog.BoundSensitive(__opId, {i});");
            }
            else
            {
                // Entity-sourced params read from Entity field, not P{n}.
                // Box to object? to handle value types (bool, int, etc.) that can't use ?.ToString().
                if (param.EntityPropertyExpression != null)
                    sb.AppendLine($"            ParameterLog.Bound(__opId, {i}, ((object?){param.EntityPropertyExpression})?.ToString() ?? \"null\");");
                else
                    sb.AppendLine($"            ParameterLog.Bound(__opId, {i}, __c.P{i}?.ToString() ?? \"null\");");
            }
        }
        var nextLogIdx = paramCount;
        if (hasLimitField)
        {
            sb.AppendLine($"            ParameterLog.Bound(__opId, {nextLogIdx}, __c.Limit.ToString());");
            nextLogIdx++;
        }
        if (hasOffsetField)
        {
            sb.AppendLine($"            ParameterLog.Bound(__opId, {nextLogIdx}, __c.Offset.ToString());");
        }
        sb.AppendLine("        }");
    }


    /// <summary>
    /// Gets the inline value expression for a parameter based on its type classification.
    /// </summary>
    private static string GetParameterValueExpression(ChainParameterInfo param, int index)
    {
        // Entity-sourced parameter (SetPoco): read from Entity field, not P{n}
        if (param.EntityPropertyExpression != null)
        {
            // TypeMapping is already baked into the EntityPropertyExpression
            if (param.TypeMapping != null)
                return $"(object?){param.EntityPropertyExpression} ?? DBNull.Value";
            return $"(object?){param.EntityPropertyExpression} ?? DBNull.Value";
        }

        // Mapped type: use ToDb() conversion
        if (param.TypeMapping != null)
            return $"(object?){GetMappingFieldName(param.TypeMapping)}.ToDb(__c.P{index}) ?? DBNull.Value";

        // Enum with known underlying type: inline cast to underlying integral type
        if (param.IsEnum && param.EnumUnderlyingType != null)
        {
            if (!param.TypeName.EndsWith("?"))
                return $"(object)({param.EnumUnderlyingType})__c.P{index}";

            // Nullable enum: HasValue check + underlying cast
            return $"__c.P{index}.HasValue ? (object)({param.EnumUnderlyingType})__c.P{index}.Value : DBNull.Value";
        }

        // Default: null-safe boxing (handles simple types, nullable value types, reference types,
        // and enums without a resolved underlying type)
        return $"(object?)__c.P{index} ?? DBNull.Value";
    }

    /// <summary>
    /// Emits a carrier insert execution terminal with inline per-parameter DbCommand binding.
    /// Extracts entity properties directly from the carrier's Entity field.
    /// </summary>
    private static void EmitCarrierInsertTerminal(
        StringBuilder sb, CarrierClassInfo carrier, PrebuiltChainInfo chain,
        string executorMethod, bool isScalar = false)
    {
        sb.AppendLine($"        var __c = Unsafe.As<{carrier.ClassName}>(builder);");

        sb.AppendLine("        var __opId = OpId.Next();");
        EmitCarrierSqlDispatch(sb, chain);

        sb.AppendLine("        if (LogManager.IsEnabled(LogLevel.Debug, QueryLog.CategoryName))");
        sb.AppendLine("            QueryLog.SqlGenerated(__opId, sql);");

        // Command creation + inline parameter binding from entity properties
        var timeoutExpr = HasCarrierField(carrier, FieldRole.Timeout)
            ? "__c.Timeout ?? __c.Ctx!.DefaultTimeout"
            : "__c.Ctx!.DefaultTimeout";
        sb.AppendLine("        var __cmd = __c.Ctx.Connection.CreateCommand();");
        sb.AppendLine("        __cmd.CommandText = sql;");
        sb.AppendLine($"        __cmd.CommandTimeout = (int)({timeoutExpr}).TotalSeconds;");

        // Bind entity properties as parameters using InsertInfo
        var insertInfo = chain.Analysis.ExecutionSite.InsertInfo;
        if (insertInfo != null)
        {
            var entityType = GetShortTypeName(chain.EntityTypeName);
            for (int i = 0; i < insertInfo.Columns.Count; i++)
            {
                var col = insertInfo.Columns[i];
                var valueExpr = GetColumnValueExpression("__c.Entity!", col.PropertyName, col.IsForeignKey, col.CustomTypeMappingClass);
                sb.AppendLine($"        var __p{i} = __cmd.CreateParameter();");
                sb.AppendLine($"        __p{i}.ParameterName = \"@p{i}\";");
                sb.AppendLine($"        __p{i}.Value = (object?){valueExpr} ?? DBNull.Value;");
                sb.AppendLine($"        __cmd.Parameters.Add(__p{i});");
            }

            // Parameter logging
            if (insertInfo.Columns.Count > 0)
            {
                sb.AppendLine("        if (LogManager.IsEnabled(LogLevel.Trace, ParameterLog.CategoryName))");
                sb.AppendLine("        {");
                for (int i = 0; i < insertInfo.Columns.Count; i++)
                {
                    var col = insertInfo.Columns[i];
                    if (col.IsSensitive)
                        sb.AppendLine($"            ParameterLog.BoundSensitive(__opId, {i});");
                    else
                        sb.AppendLine($"            ParameterLog.Bound(__opId, {i}, __p{i}.Value?.ToString() ?? \"null\");");
                }
                sb.AppendLine("        }");
            }
        }

        sb.AppendLine($"        return QueryExecutor.{executorMethod}(__opId, __c.Ctx, __cmd, cancellationToken);");
    }

    #region Carrier helpers

    /// <summary>
    /// Emits code to extract a collection parameter from a Contains() call.
    /// For public static fields/properties, emits direct access. Otherwise delegates
    /// to the runtime <c>ExpressionHelper.ExtractContainsCollection</c> helper.
    /// </summary>
    private static void EmitCollectionContainsExtraction(
        StringBuilder sb, int globalIdx, ChainParameterInfo carrierParam)
    {
        var fieldType = carrierParam.ElementTypeName != null
            ? $"System.Collections.Generic.IReadOnlyList<{carrierParam.ElementTypeName}>"
            : carrierParam.TypeName;

        if (carrierParam.IsDirectAccessible && carrierParam.CollectionAccessExpression != null)
        {
            // Direct access path: public static field/property — no reflection needed
            sb.AppendLine($"        __c.P{globalIdx} = ({fieldType}){carrierParam.CollectionAccessExpression};");
        }
        else
        {
            // Runtime helper path: unwrap expression tree and extract via reflection
            sb.AppendLine($"        __c.P{globalIdx} = Quarry.Internal.ExpressionHelper.ExtractContainsCollection<{fieldType}>((System.Linq.Expressions.MethodCallExpression)expr.Body);");
        }
    }

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
        var hasCollections = chain.ChainParameters.Any(p => p.IsCollection);

        if (chain.SqlMap.Count == 1)
        {
            foreach (var kvp in chain.SqlMap)
            {
                if (hasCollections)
                    sb.AppendLine($"        var sql = @\"{EscapeStringLiteral(kvp.Value.Sql)}\";");
                else
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

        // Emit collection parameter expansion code
        if (hasCollections)
        {
            EmitCollectionExpansion(sb, chain);
        }
    }

    /// <summary>
    /// Emits code to expand collection parameter tokens in the SQL template.
    /// Replaces <c>{__COL_P{n}__}</c> tokens with expanded parameter placeholders
    /// based on the runtime collection sizes.
    /// </summary>
    private static void EmitCollectionExpansion(StringBuilder sb, PrebuiltChainInfo chain)
    {
        foreach (var param in chain.ChainParameters)
        {
            if (!param.IsCollection) continue;

            sb.AppendLine($"        var __col{param.Index} = __c.P{param.Index};");
            sb.AppendLine($"        var __col{param.Index}Len = __col{param.Index}.Count;");

            var dialectPrefix = chain.Dialect switch
            {
                SqlDialect.PostgreSQL => "$",
                _ => "@p"
            };
            var isPostgres = chain.Dialect == SqlDialect.PostgreSQL;

            // Build expanded placeholder string: "@p0, @p1, @p2" or "$1, $2, $3" or "?, ?, ?"
            var isMySQL = chain.Dialect == SqlDialect.MySQL;
            sb.AppendLine($"        var __col{param.Index}Parts = new string[__col{param.Index}Len];");
            sb.AppendLine($"        for (int __i = 0; __i < __col{param.Index}Len; __i++)");
            if (isMySQL)
                sb.AppendLine($"            __col{param.Index}Parts[__i] = \"?\";");
            else if (isPostgres)
                sb.AppendLine($"            __col{param.Index}Parts[__i] = \"$\" + (__i + 1);");
            else
                sb.AppendLine($"            __col{param.Index}Parts[__i] = \"{dialectPrefix}\" + __i;");
            sb.AppendLine($"        sql = sql.Replace(\"{{__COL_P{param.Index}__}}\", string.Join(\", \", __col{param.Index}Parts));");
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
