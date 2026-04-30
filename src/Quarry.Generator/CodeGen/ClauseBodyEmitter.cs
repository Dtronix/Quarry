using System.Collections.Generic;
using System.Linq;
using System.Text;
using Quarry.Generators.Generation;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Generators.Projection;

namespace Quarry.Generators.CodeGen;

/// <summary>
/// Emits interceptor method bodies for clause sites (Where, OrderBy, Select,
/// GroupBy, Having, Set, Distinct, Limit, Offset, WithTimeout).
/// Emits carrier-path (field mutation + cast) method bodies.
/// </summary>
internal static class ClauseBodyEmitter
{
    /// <summary>
    /// Emits a Where clause interceptor body.
    /// Non-carrier: appends SQL fragment to QueryBuilder.
    /// Carrier: extracts params to carrier fields, sets mask bit.
    /// </summary>
    public static void EmitWhere(
        StringBuilder sb,
        TranslatedCallSite site,
        string methodName,
        List<InterceptorCodeGenerator.CachedExtractorField>? methodFields,
        int? clauseBit,
        AssembledPlan? prebuiltChain,
        bool isFirstInChain,
        CarrierPlan? carrier)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);
        var clauseInfo = site.Clause;

        var hasResolvableCapturedParams = clauseInfo?.Parameters.Any(p => p.IsCaptured && p.CanGenerateDirectPath) == true;
        var funcParamName = hasResolvableCapturedParams ? "func" : "_";

        methodFields ??= new List<InterceptorCodeGenerator.CachedExtractorField>();
        if (methodFields.Count > 0)
        {
            sb.AppendLine($"    [UnconditionalSuppressMessage(\"Trimming\", \"IL2075\",");
            sb.AppendLine($"        Justification = \"Closure field access via UnsafeAccessor is AOT-safe.\")]");
        }

        var thisType = site.BuilderTypeName;
        var returnType = InterceptorCodeGenerator.ToReturnTypeName(thisType);

        if (site.ResultTypeName != null)
        {
            var resultType = InterceptorCodeGenerator.GetShortTypeName(site.ResultTypeName);
            var receiverType = InterceptorCodeGenerator.BuildReceiverType(thisType, entityType, resultType);
            sb.AppendLine($"    public static {returnType}<{entityType}, {resultType}> {methodName}(");
            sb.AppendLine($"        this {receiverType} builder,");
            sb.AppendLine($"        Func<{entityType}, bool> {funcParamName})");
        }
        else
        {
            sb.AppendLine($"    public static {returnType}<{entityType}> {methodName}(");
            sb.AppendLine($"        this {thisType}<{entityType}> builder,");
            sb.AppendLine($"        Func<{entityType}, bool> {funcParamName})");
        }

        sb.AppendLine($"    {{");

        if (clauseInfo == null || !clauseInfo.IsSuccess)
        {
            // Fallback: return the builder unchanged with closing brace
            sb.AppendLine($"        return Unsafe.As<{returnType}<{entityType}>>(builder);");
            sb.AppendLine($"    }}");
            return;
        }

        // Carrier-optimized path
        if (carrier != null && prebuiltChain != null)
        {
            var concreteBuilder = carrier.ClassName;
            var retInterface = site.ResultTypeName != null
                ? $"IQueryBuilder<{entityType}, {InterceptorCodeGenerator.GetShortTypeName(site.ResultTypeName)}>"
                : $"IQueryBuilder<{entityType}>";
            CarrierEmitter.EmitCarrierClauseBody(sb, carrier, prebuiltChain, site, clauseBit, isFirstInChain,
                concreteBuilder, retInterface, hasResolvableCapturedParams, methodFields);
            sb.AppendLine($"    }}");
            return;
        }
    }

    /// <summary>
    /// Emits an OrderBy/ThenBy clause interceptor body.
    /// </summary>
    public static void EmitOrderBy(
        StringBuilder sb,
        TranslatedCallSite site,
        string methodName,
        int? clauseBit,
        AssembledPlan? prebuiltChain,
        bool isFirstInChain,
        CarrierPlan? carrier)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);

        var keyType = site.KeyTypeName != null ? InterceptorCodeGenerator.GetShortTypeName(site.KeyTypeName) : null;

        var thisType = site.BuilderTypeName;
        var returnType = InterceptorCodeGenerator.ToReturnTypeName(thisType);

        // Captured-variable parameters require the lambda to be named (so func.Target
        // is reachable in the body). Without this, the carrier P-fields backing the
        // ORDER BY expression would never be assigned and silently bind their default.
        var clauseInfo = site.Clause;
        var hasResolvableCapturedParams = clauseInfo?.Parameters.Any(p => p.IsCaptured && p.CanGenerateDirectPath) == true;
        var funcParamName = hasResolvableCapturedParams ? "func" : "_";

        if (hasResolvableCapturedParams)
        {
            sb.AppendLine($"    [UnconditionalSuppressMessage(\"Trimming\", \"IL2075\",");
            sb.AppendLine($"        Justification = \"Closure field access via UnsafeAccessor is AOT-safe.\")]");
        }

        if (site.ResultTypeName != null)
        {
            var resultType = InterceptorCodeGenerator.GetShortTypeName(site.ResultTypeName);
            var isBrokenTuple = InterceptorCodeGenerator.IsBrokenTupleType(resultType);
            if (isBrokenTuple)
                keyType = null;

            if (keyType != null)
            {
                var receiverType = InterceptorCodeGenerator.BuildReceiverType(thisType, entityType, resultType);
                sb.AppendLine($"    public static {returnType}<{entityType}, {resultType}> {methodName}(");
                sb.AppendLine($"        this {receiverType} builder,");
                sb.AppendLine($"        Func<{entityType}, {keyType}> {funcParamName},");
                sb.AppendLine($"        Direction direction = Direction.Ascending)");
            }
            else
            {
                var isAccessor = InterceptorCodeGenerator.IsEntityAccessorType(thisType);
                var genericReceiver = isAccessor ? $"{thisType}<T>" : $"{thisType}<T, TResult>";
                var genericReturn = $"{returnType}<T, TResult>";
                sb.AppendLine($"    public static {genericReturn} {methodName}<T, TResult, TKey>(");
                sb.AppendLine($"        this {genericReceiver} builder,");
                sb.AppendLine($"        Func<T, TKey> {funcParamName},");
                sb.AppendLine($"        Direction direction = Direction.Ascending) where T : class");
            }
        }
        else
        {
            if (keyType != null)
            {
                sb.AppendLine($"    public static {returnType}<{entityType}> {methodName}(");
                sb.AppendLine($"        this {thisType}<{entityType}> builder,");
                sb.AppendLine($"        Func<{entityType}, {keyType}> {funcParamName},");
                sb.AppendLine($"        Direction direction = Direction.Ascending)");
            }
            else
            {
                sb.AppendLine($"    public static {returnType}<T> {methodName}<T, TKey>(");
                sb.AppendLine($"        this {thisType}<T> builder,");
                sb.AppendLine($"        Func<T, TKey> {funcParamName},");
                sb.AppendLine($"        Direction direction = Direction.Ascending) where T : class");
            }
        }

        sb.AppendLine($"    {{");

        // Carrier-optimized path
        if (carrier != null && prebuiltChain != null)
        {
            if (keyType != null)
            {
                var concreteBuilder = carrier.ClassName;
                var retInterface = site.ResultTypeName != null
                    ? $"IQueryBuilder<{entityType}, {InterceptorCodeGenerator.GetShortTypeName(site.ResultTypeName)}>"
                    : $"IQueryBuilder<{entityType}>";
                CarrierEmitter.EmitCarrierClauseBody(sb, carrier, prebuiltChain, site, clauseBit, isFirstInChain,
                    concreteBuilder, retInterface, hasResolvableCapturedParams, new List<InterceptorCodeGenerator.CachedExtractorField>());
            }
            else
            {
                // Generic key type — funnel through EmitCarrierClauseBody so captured-variable
                // extraction and P-field assignment happen exactly as on the keyType-known path.
                var castTarget = site.ResultTypeName != null ? $"{returnType}<T, TResult>" : $"{returnType}<T>";
                CarrierEmitter.EmitCarrierClauseBody(sb, carrier, prebuiltChain, site, clauseBit, isFirstInChain,
                    carrier.ClassName, castTarget, hasResolvableCapturedParams, new List<InterceptorCodeGenerator.CachedExtractorField>());
            }
            sb.AppendLine($"    }}");
            return;
        }
    }

    /// <summary>
    /// Emits a Select clause interceptor body.
    /// </summary>
    public static void EmitSelect(
        StringBuilder sb,
        TranslatedCallSite site,
        string methodName,
        AssembledPlan? prebuiltChain,
        bool isFirstInChain,
        CarrierPlan? carrier)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);
        // Prefer chain's enriched ProjectionInfo over site's discovery-time projection
        // (discovery-time ResultTypeName may be '?' when entity types are generator-produced)
        var projection = (prebuiltChain?.ProjectionInfo != null && prebuiltChain.ProjectionInfo.Columns.Count > 0)
            ? prebuiltChain.ProjectionInfo
            : site.ProjectionInfo;

        // Simplified prebuilt chain path: AsProjected instead of SelectWithReader
        if (prebuiltChain != null && projection != null && projection.IsOptimalPath && projection.Columns.Count > 0 && projection.ResultTypeName != "?")
        {
            var resultType = InterceptorCodeGenerator.GetShortTypeName(projection.ResultTypeName);
            var thisType = site.BuilderTypeName;
            var returnType = InterceptorCodeGenerator.ToReturnTypeName(thisType);
            var hasProjectionCaptures = site.ProjectionInfo?.ProjectionParameters?.Any(p => p.IsCaptured) == true;
            var delegateParamName = hasProjectionCaptures ? "func" : "_";
            sb.AppendLine($"    public static {returnType}<{entityType}, {resultType}> {methodName}(");
            sb.AppendLine($"        this {thisType}<{entityType}> builder,");
            sb.AppendLine($"        Func<{entityType}, {resultType}> {delegateParamName})");
            sb.AppendLine($"    {{");

            if (carrier != null)
            {
                var targetInterface = $"IQueryBuilder<{entityType}, {resultType}>";
                if (isFirstInChain)
                {
                    var (siteParams, globalParamOffset) = TerminalEmitHelpers.ResolveSiteParams(prebuiltChain, site.UniqueId);
                    int? clauseBit = null;
                    CarrierEmitter.EmitCarrierChainEntry(sb, carrier, prebuiltChain, site, carrier.ClassName, targetInterface, clauseBit, siteParams, globalParamOffset);
                }
                else
                {
                    CarrierEmitter.EmitCarrierSelect(sb, targetInterface, carrier, prebuiltChain, site);
                }
                sb.AppendLine($"    }}");
                return;
            }
        }
    }

    /// <summary>
    /// Emits a GroupBy clause interceptor body.
    /// </summary>
    public static void EmitGroupBy(
        StringBuilder sb,
        TranslatedCallSite site,
        string methodName,
        int? clauseBit,
        AssembledPlan? prebuiltChain,
        bool isFirstInChain,
        CarrierPlan? carrier)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);
        var clauseInfo = site.Clause;

        var keyType = site.KeyTypeName != null ? InterceptorCodeGenerator.GetShortTypeName(site.KeyTypeName) : null;

        var thisType = site.BuilderTypeName;
        var returnType = InterceptorCodeGenerator.ToReturnTypeName(thisType);
        var concreteType = InterceptorCodeGenerator.ToConcreteTypeName(returnType);

        // Captured-variable parameters require the lambda to be named (so func.Target
        // is reachable in the body). Without this, carrier P-fields backing the GROUP BY
        // expression would never be assigned and silently bind their default.
        var hasResolvableCapturedParams = clauseInfo?.Parameters.Any(p => p.IsCaptured && p.CanGenerateDirectPath) == true;
        var funcParamName = hasResolvableCapturedParams ? "func" : "_";

        if (hasResolvableCapturedParams)
        {
            sb.AppendLine($"    [UnconditionalSuppressMessage(\"Trimming\", \"IL2075\",");
            sb.AppendLine($"        Justification = \"Closure field access via UnsafeAccessor is AOT-safe.\")]");
        }

        if (site.ResultTypeName != null)
        {
            var resultType = InterceptorCodeGenerator.GetShortTypeName(site.ResultTypeName);
            var isBrokenTuple = InterceptorCodeGenerator.IsBrokenTupleType(resultType);
            if (isBrokenTuple)
                keyType = null;

            if (keyType != null)
            {
                var receiverType = InterceptorCodeGenerator.BuildReceiverType(thisType, entityType, resultType);
                sb.AppendLine($"    public static {returnType}<{entityType}, {resultType}> {methodName}(");
                sb.AppendLine($"        this {receiverType} builder,");
                sb.AppendLine($"        Func<{entityType}, {keyType}> {funcParamName})");
            }
            else
            {
                var isAccessor = InterceptorCodeGenerator.IsEntityAccessorType(thisType);
                var genericReceiver = isAccessor ? $"{thisType}<T>" : $"{thisType}<T, TResult>";
                sb.AppendLine($"    public static {returnType}<T, TResult> {methodName}<T, TResult, TKey>(");
                sb.AppendLine($"        this {genericReceiver} builder,");
                sb.AppendLine($"        Func<T, TKey> {funcParamName}) where T : class");
            }
        }
        else
        {
            if (keyType != null)
            {
                sb.AppendLine($"    public static {returnType}<{entityType}> {methodName}(");
                sb.AppendLine($"        this {thisType}<{entityType}> builder,");
                sb.AppendLine($"        Func<{entityType}, {keyType}> {funcParamName})");
            }
            else
            {
                sb.AppendLine($"    public static {returnType}<T> {methodName}<T, TKey>(");
                sb.AppendLine($"        this {thisType}<T> builder,");
                sb.AppendLine($"        Func<T, TKey> {funcParamName}) where T : class");
            }
        }

        sb.AppendLine($"    {{");

        if (clauseInfo == null || !clauseInfo.IsSuccess)
        {
            // Fallback: return the builder unchanged with closing brace
            sb.AppendLine($"        return Unsafe.As<{returnType}<{entityType}>>(builder);");
            sb.AppendLine($"    }}");
            return;
        }

        // Carrier-optimized path
        if (carrier != null && prebuiltChain != null)
        {
            if (keyType != null)
            {
                var concreteBuilder = carrier.ClassName;
                var retInterface = site.ResultTypeName != null
                    ? $"IQueryBuilder<{entityType}, {InterceptorCodeGenerator.GetShortTypeName(site.ResultTypeName)}>"
                    : $"IQueryBuilder<{entityType}>";
                CarrierEmitter.EmitCarrierClauseBody(sb, carrier, prebuiltChain, site, clauseBit, isFirstInChain,
                    concreteBuilder, retInterface, hasResolvableCapturedParams, new List<InterceptorCodeGenerator.CachedExtractorField>());
            }
            else
            {
                // Generic key type — funnel through EmitCarrierClauseBody so captured-variable
                // extraction and P-field assignment happen exactly as on the keyType-known path.
                var castTarget = site.ResultTypeName != null ? $"{returnType}<T, TResult>" : $"{returnType}<T>";
                CarrierEmitter.EmitCarrierClauseBody(sb, carrier, prebuiltChain, site, clauseBit, isFirstInChain,
                    carrier.ClassName, castTarget, hasResolvableCapturedParams, new List<InterceptorCodeGenerator.CachedExtractorField>());
            }
            sb.AppendLine($"    }}");
            return;
        }
    }

    /// <summary>
    /// Emits a Having clause interceptor body.
    /// </summary>
    public static void EmitHaving(
        StringBuilder sb,
        TranslatedCallSite site,
        string methodName,
        int? clauseBit,
        AssembledPlan? prebuiltChain,
        bool isFirstInChain,
        CarrierPlan? carrier)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);
        var clauseInfo = site.Clause;

        var hasResolvableCapturedParams = clauseInfo?.Parameters.Any(p => p.IsCaptured && p.CanGenerateDirectPath) == true;
        var funcParamName = hasResolvableCapturedParams ? "func" : "_";

        var thisType = site.BuilderTypeName;
        var returnType = InterceptorCodeGenerator.ToReturnTypeName(thisType);
        var concreteType = InterceptorCodeGenerator.ToConcreteTypeName(returnType);

        if (site.ResultTypeName != null)
        {
            var resultType = InterceptorCodeGenerator.GetShortTypeName(site.ResultTypeName);
            var receiverType = InterceptorCodeGenerator.BuildReceiverType(thisType, entityType, resultType);
            sb.AppendLine($"    public static {returnType}<{entityType}, {resultType}> {methodName}(");
            sb.AppendLine($"        this {receiverType} builder,");
            sb.AppendLine($"        Func<{entityType}, bool> {funcParamName})");
        }
        else
        {
            sb.AppendLine($"    public static {returnType}<{entityType}> {methodName}(");
            sb.AppendLine($"        this {thisType}<{entityType}> builder,");
            sb.AppendLine($"        Func<{entityType}, bool> {funcParamName})");
        }

        sb.AppendLine($"    {{");

        if (clauseInfo == null || !clauseInfo.IsSuccess)
        {
            // Fallback: return the builder unchanged with closing brace
            sb.AppendLine($"        return Unsafe.As<{returnType}<{entityType}>>(builder);");
            sb.AppendLine($"    }}");
            return;
        }

        // Carrier-optimized path
        if (carrier != null && prebuiltChain != null)
        {
            var concreteBuilder = carrier.ClassName;
            var retInterface = site.ResultTypeName != null
                ? $"IQueryBuilder<{entityType}, {InterceptorCodeGenerator.GetShortTypeName(site.ResultTypeName)}>"
                : $"IQueryBuilder<{entityType}>";
            CarrierEmitter.EmitCarrierClauseBody(sb, carrier, prebuiltChain, site, clauseBit, isFirstInChain,
                concreteBuilder, retInterface, false, new List<InterceptorCodeGenerator.CachedExtractorField>());
            sb.AppendLine($"    }}");
            return;
        }
    }

    /// <summary>
    /// Emits a Set clause interceptor body (for IQueryBuilder Set).
    /// </summary>
    public static void EmitSet(
        StringBuilder sb,
        TranslatedCallSite site,
        string methodName,
        int? clauseBit,
        AssembledPlan? prebuiltChain,
        bool isFirstInChain,
        CarrierPlan? carrier)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);

        var thisType = site.BuilderTypeName;
        var returnType = InterceptorCodeGenerator.ToReturnTypeName(thisType);
        var concreteType = InterceptorCodeGenerator.ToConcreteTypeName(returnType);

        // Carrier-optimized path
        var resolvedValueType = site.ValueTypeName;
        if (carrier != null && prebuiltChain != null && resolvedValueType != null)
        {
            sb.AppendLine($"    public static {returnType}<{entityType}> {methodName}(");
            sb.AppendLine($"        this {returnType}<{entityType}> builder,");
            sb.AppendLine($"        Func<{entityType}, {resolvedValueType}> _,");
            sb.AppendLine($"        {resolvedValueType} value)");
            sb.AppendLine($"    {{");

            var concreteBuilder = carrier.ClassName;
            var returnInterface = $"{returnType}<{entityType}>";
            CarrierEmitter.EmitCarrierClauseBody(sb, carrier, prebuiltChain, site, clauseBit, isFirstInChain,
                concreteBuilder, returnInterface, false, new List<InterceptorCodeGenerator.CachedExtractorField>());
            sb.AppendLine($"    }}");
            return;
        }
    }

    /// <summary>
    /// Emits a modification Where interceptor (DeleteWhere/UpdateWhere).
    /// </summary>
    public static void EmitModificationWhere(
        StringBuilder sb,
        TranslatedCallSite site,
        string methodName,
        List<InterceptorCodeGenerator.CachedExtractorField>? methodFields,
        bool isDelete,
        int? clauseBit,
        AssembledPlan? prebuiltChain,
        bool isFirstInChain,
        CarrierPlan? carrier)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);
        var clauseInfo = site.Clause;
        var modKind = isDelete ? "Delete" : "Update";

        var hasResolvableCapturedParams = clauseInfo?.Parameters.Any(p => p.IsCaptured && p.CanGenerateDirectPath) == true;
        var funcParamName = hasResolvableCapturedParams ? "func" : "_";

        methodFields ??= new List<InterceptorCodeGenerator.CachedExtractorField>();
        if (methodFields.Count > 0)
        {
            sb.AppendLine($"    [UnconditionalSuppressMessage(\"Trimming\", \"IL2075\",");
            sb.AppendLine($"        Justification = \"Closure field access via UnsafeAccessor is AOT-safe.\")]");
        }

        var thisType = site.BuilderTypeName;
        var returnType = InterceptorCodeGenerator.ToReturnTypeName(thisType);
        var concreteType = InterceptorCodeGenerator.ToConcreteTypeName(returnType);
        var receiverType = $"{thisType}<{entityType}>";

        sb.AppendLine($"    public static IExecutable{modKind}Builder<{entityType}> {methodName}(");
        sb.AppendLine($"        this {receiverType} builder,");
        sb.AppendLine($"        Func<{entityType}, bool> {funcParamName})");
        sb.AppendLine($"    {{");

        if (clauseInfo == null || !clauseInfo.IsSuccess)
        {
            // Fallback: return the builder unchanged with closing brace
            sb.AppendLine($"        return Unsafe.As<{returnType}<{entityType}>>(builder);");
            sb.AppendLine($"    }}");
            return;
        }

        // Carrier-optimized path
        if (carrier != null && prebuiltChain != null)
        {
            var concreteBuilder = carrier.ClassName;
            var returnInterface = $"IExecutable{modKind}Builder<{entityType}>";
            CarrierEmitter.EmitCarrierClauseBody(sb, carrier, prebuiltChain, site, clauseBit, isFirstInChain,
                concreteBuilder, returnInterface, hasResolvableCapturedParams, methodFields);
            sb.AppendLine($"    }}");
            return;
        }
    }

    /// <summary>
    /// Emits an UpdateSet interceptor for UpdateBuilder or ExecutableUpdateBuilder.
    /// </summary>
    public static void EmitUpdateSet(
        StringBuilder sb,
        TranslatedCallSite site,
        string methodName,
        int? clauseBit,
        AssembledPlan? prebuiltChain,
        bool isFirstInChain,
        CarrierPlan? carrier)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);

        var isExecutable = site.BuilderKind is BuilderKind.ExecutableUpdate;
        var concreteBaseName = isExecutable ? "ExecutableUpdateBuilder" : "UpdateBuilder";
        var returnInterfaceBaseName = "I" + concreteBaseName;

        // Carrier-optimized path
        var resolvedValueType = site.ValueTypeName;
        if (carrier != null && prebuiltChain != null && resolvedValueType != null)
        {
            sb.AppendLine($"    public static {returnInterfaceBaseName}<{entityType}> {methodName}(");
            sb.AppendLine($"        this {returnInterfaceBaseName}<{entityType}> builder,");
            sb.AppendLine($"        Func<{entityType}, {resolvedValueType}> _,");
            sb.AppendLine($"        {resolvedValueType} value)");
            sb.AppendLine($"    {{");

            var concreteBuilder = carrier.ClassName;
            var returnInterface = $"{returnInterfaceBaseName}<{entityType}>";
            CarrierEmitter.EmitCarrierClauseBody(sb, carrier, prebuiltChain, site, clauseBit, isFirstInChain,
                concreteBuilder, returnInterface, false, new List<InterceptorCodeGenerator.CachedExtractorField>());
            sb.AppendLine($"    }}");
            return;
        }
    }

    /// <summary>
    /// Emits an UpdateSetAction interceptor (Set with Action&lt;T&gt; lambda).
    /// </summary>
    public static void EmitUpdateSetAction(
        StringBuilder sb,
        TranslatedCallSite site,
        string methodName,
        int? clauseBit,
        AssembledPlan? prebuiltChain,
        bool isFirstInChain,
        CarrierPlan? carrier)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);

        var isExecutable = site.BuilderKind is BuilderKind.ExecutableUpdate;
        var concreteBaseName = isExecutable ? "ExecutableUpdateBuilder" : "UpdateBuilder";
        var returnInterfaceBaseName = "I" + concreteBaseName;

        // Determine captured params from SetActionParameters (canonical location for UpdateSetAction)
        var setActionParams = site.Bound.Raw.SetActionParameters;
        var hasCapturedParams = setActionParams?.Any(p => p.IsCaptured) == true;
        var actionParamName = hasCapturedParams ? "action" : "_";

        // Carrier-optimized path
        if (carrier != null && prebuiltChain != null)
        {
            sb.AppendLine($"    public static {returnInterfaceBaseName}<{entityType}> {methodName}(");
            sb.AppendLine($"        this {returnInterfaceBaseName}<{entityType}> builder,");
            sb.AppendLine($"        Action<{entityType}> {actionParamName})");
            sb.AppendLine($"    {{");

            var concreteBuilder = carrier.ClassName;
            var returnInterface = $"{returnInterfaceBaseName}<{entityType}>";
            CarrierEmitter.EmitCarrierClauseBody(sb, carrier, prebuiltChain, site, clauseBit, isFirstInChain,
                concreteBuilder, returnInterface, false, new List<InterceptorCodeGenerator.CachedExtractorField>(),
                delegateParamName: actionParamName);

            sb.AppendLine($"    }}");
            return;
        }
    }

    /// <summary>
    /// Emits an UpdateSetPoco interceptor (Set with entity POCO).
    /// </summary>
    public static void EmitUpdateSetPoco(
        StringBuilder sb,
        TranslatedCallSite site,
        string methodName,
        int? clauseBit,
        AssembledPlan? prebuiltChain,
        bool isFirstInChain,
        CarrierPlan? carrier)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);
        var updateInfo = site.UpdateInfo;

        var isExecutable = site.BuilderKind is BuilderKind.ExecutableUpdate;
        var concreteBaseName = isExecutable ? "ExecutableUpdateBuilder" : "UpdateBuilder";
        var returnInterfaceBaseName = "I" + concreteBaseName;

        // Carrier-optimized path
        if (carrier != null && prebuiltChain != null && updateInfo != null && updateInfo.Columns.Count > 0)
        {
            sb.AppendLine($"    public static {returnInterfaceBaseName}<{entityType}> {methodName}(");
            sb.AppendLine($"        this {returnInterfaceBaseName}<{entityType}> builder,");
            sb.AppendLine($"        {entityType} entity)");
            sb.AppendLine($"    {{");

            // In the carrier-only architecture, the builder is already the carrier
            sb.AppendLine($"        var __c = Unsafe.As<{carrier.ClassName}>(builder);");

            sb.AppendLine($"        __c.Entity = entity;");

            if (clauseBit.HasValue)
                sb.AppendLine($"        __c.Mask |= unchecked(({CarrierEmitter.GetMaskType(prebuiltChain)})(1 << {clauseBit.Value}));");

            var returnInterface = $"{returnInterfaceBaseName}<{entityType}>";
            sb.AppendLine($"        return Unsafe.As<{returnInterface}>(builder);");

            sb.AppendLine($"    }}");
            return;
        }
    }
}
