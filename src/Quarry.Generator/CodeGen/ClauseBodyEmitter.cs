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
        var exprParamName = hasResolvableCapturedParams ? "expr" : "_";

        methodFields ??= new List<InterceptorCodeGenerator.CachedExtractorField>();
        if (methodFields.Count > 0)
        {
            sb.AppendLine($"    [UnconditionalSuppressMessage(\"Trimming\", \"IL2075\",");
            sb.AppendLine($"        Justification = \"Closure fields are preserved by the expression tree that references them.\")]");
        }

        var thisType = site.BuilderTypeName;
        var returnType = InterceptorCodeGenerator.ToReturnTypeName(thisType);
        var concreteType = InterceptorCodeGenerator.ToConcreteTypeName(returnType);

        if (site.ResultTypeName != null)
        {
            var resultType = InterceptorCodeGenerator.GetShortTypeName(site.ResultTypeName);
            var receiverType = InterceptorCodeGenerator.BuildReceiverType(thisType, entityType, resultType);
            sb.AppendLine($"    public static {returnType}<{entityType}, {resultType}> {methodName}(");
            sb.AppendLine($"        this {receiverType} builder,");
            sb.AppendLine($"        Expression<Func<{entityType}, bool>> {exprParamName})");
        }
        else
        {
            sb.AppendLine($"    public static {returnType}<{entityType}> {methodName}(");
            sb.AppendLine($"        this {thisType}<{entityType}> builder,");
            sb.AppendLine($"        Expression<Func<{entityType}, bool>> {exprParamName})");
        }

        sb.AppendLine($"    {{");

        if (clauseInfo == null || !clauseInfo.IsSuccess)
        {
            // Fallback: return the builder unchanged with closing brace
            sb.AppendLine($"        return Unsafe.As<{concreteType}<{entityType}>>(builder);");
            sb.AppendLine($"    }}");
            return;
        }

        // Carrier-optimized path
        if (carrier != null && prebuiltChain != null)
        {
            var concreteBuilder = $"QueryBuilder<{entityType}>";
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

        if (site.ResultTypeName != null)
        {
            var resultType = InterceptorCodeGenerator.GetShortTypeName(site.ResultTypeName);
            var isBrokenTuple = resultType.Contains("object") && resultType.StartsWith("(");
            if (isBrokenTuple)
                keyType = null;

            if (keyType != null)
            {
                var receiverType = InterceptorCodeGenerator.BuildReceiverType(thisType, entityType, resultType);
                sb.AppendLine($"    public static {returnType}<{entityType}, {resultType}> {methodName}(");
                sb.AppendLine($"        this {receiverType} builder,");
                sb.AppendLine($"        Expression<Func<{entityType}, {keyType}>> _,");
                sb.AppendLine($"        Direction direction = Direction.Ascending)");
            }
            else
            {
                var isAccessor = InterceptorCodeGenerator.IsEntityAccessorType(thisType);
                var genericReceiver = isAccessor ? $"{thisType}<T>" : $"{thisType}<T, TResult>";
                var genericReturn = $"{returnType}<T, TResult>";
                sb.AppendLine($"    public static {genericReturn} {methodName}<T, TResult, TKey>(");
                sb.AppendLine($"        this {genericReceiver} builder,");
                sb.AppendLine($"        Expression<Func<T, TKey>> _,");
                sb.AppendLine($"        Direction direction = Direction.Ascending) where T : class");
            }
        }
        else
        {
            if (keyType != null)
            {
                sb.AppendLine($"    public static {returnType}<{entityType}> {methodName}(");
                sb.AppendLine($"        this {thisType}<{entityType}> builder,");
                sb.AppendLine($"        Expression<Func<{entityType}, {keyType}>> _,");
                sb.AppendLine($"        Direction direction = Direction.Ascending)");
            }
            else
            {
                sb.AppendLine($"    public static {returnType}<T> {methodName}<T, TKey>(");
                sb.AppendLine($"        this {thisType}<T> builder,");
                sb.AppendLine($"        Expression<Func<T, TKey>> _,");
                sb.AppendLine($"        Direction direction = Direction.Ascending) where T : class");
            }
        }

        sb.AppendLine($"    {{");

        // Carrier-optimized path
        if (carrier != null && prebuiltChain != null)
        {
            if (keyType != null)
            {
                var concreteBuilder = $"QueryBuilder<{entityType}>";
                var retInterface = site.ResultTypeName != null
                    ? $"IQueryBuilder<{entityType}, {InterceptorCodeGenerator.GetShortTypeName(site.ResultTypeName)}>"
                    : $"IQueryBuilder<{entityType}>";
                CarrierEmitter.EmitCarrierClauseBody(sb, carrier, prebuiltChain, site, clauseBit, isFirstInChain,
                    concreteBuilder, retInterface, false, new List<InterceptorCodeGenerator.CachedExtractorField>());
            }
            else
            {
                // Generic key type — emit passthrough with clause mask bit set
                sb.AppendLine($"        var __c = Unsafe.As<{carrier.ClassName}>(builder);");
                if (clauseBit.HasValue)
                    sb.AppendLine($"        __c.Mask |= unchecked(({carrier.MaskType})(1 << {clauseBit.Value}));");
                var castTarget = site.ResultTypeName != null ? $"{returnType}<T, TResult>" : $"{returnType}<T>";
                sb.AppendLine($"        return Unsafe.As<{castTarget}>(builder);");
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
            sb.AppendLine($"    public static {returnType}<{entityType}, {resultType}> {methodName}(");
            sb.AppendLine($"        this {thisType}<{entityType}> builder,");
            sb.AppendLine($"        Func<{entityType}, {resultType}> _)");
            sb.AppendLine($"    {{");

            if (carrier != null)
            {
                var targetInterface = $"IQueryBuilder<{entityType}, {resultType}>";
                if (isFirstInChain)
                {
                    var siteParams = new List<QueryParameter>();
                    var globalParamOffset = 0;
                    foreach (var clause in prebuiltChain.GetClauseEntries())
                    {
                        if (clause.Site.UniqueId == site.UniqueId)
                        {
                            if (clause.Site.Clause != null)
                                for (int i = 0; i < clause.Site.Clause.Parameters.Count && globalParamOffset + i < prebuiltChain.ChainParameters.Count; i++)
                                    siteParams.Add(prebuiltChain.ChainParameters[globalParamOffset + i]);
                            break;
                        }
                        if (clause.Site.Clause != null)
                            globalParamOffset += clause.Site.Clause.Parameters.Count;
                    }
                    int? clauseBit = null;
                    CarrierEmitter.EmitCarrierChainEntry(sb, carrier, prebuiltChain, site, $"QueryBuilder<{entityType}>", targetInterface, clauseBit, siteParams, globalParamOffset);
                }
                else
                {
                    CarrierEmitter.EmitCarrierSelect(sb, targetInterface);
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

        if (site.ResultTypeName != null)
        {
            var resultType = InterceptorCodeGenerator.GetShortTypeName(site.ResultTypeName);
            var isBrokenTuple = resultType.Contains("object") && resultType.StartsWith("(");
            if (isBrokenTuple)
                keyType = null;

            if (keyType != null)
            {
                var receiverType = InterceptorCodeGenerator.BuildReceiverType(thisType, entityType, resultType);
                sb.AppendLine($"    public static {returnType}<{entityType}, {resultType}> {methodName}(");
                sb.AppendLine($"        this {receiverType} builder,");
                sb.AppendLine($"        Expression<Func<{entityType}, {keyType}>> _)");
            }
            else
            {
                var isAccessor = InterceptorCodeGenerator.IsEntityAccessorType(thisType);
                var genericReceiver = isAccessor ? $"{thisType}<T>" : $"{thisType}<T, TResult>";
                sb.AppendLine($"    public static {returnType}<T, TResult> {methodName}<T, TResult, TKey>(");
                sb.AppendLine($"        this {genericReceiver} builder,");
                sb.AppendLine($"        Expression<Func<T, TKey>> _) where T : class");
            }
        }
        else
        {
            if (keyType != null)
            {
                sb.AppendLine($"    public static {returnType}<{entityType}> {methodName}(");
                sb.AppendLine($"        this {thisType}<{entityType}> builder,");
                sb.AppendLine($"        Expression<Func<{entityType}, {keyType}>> _)");
            }
            else
            {
                sb.AppendLine($"    public static {returnType}<T> {methodName}<T, TKey>(");
                sb.AppendLine($"        this {thisType}<T> builder,");
                sb.AppendLine($"        Expression<Func<T, TKey>> _) where T : class");
            }
        }

        sb.AppendLine($"    {{");

        if (clauseInfo == null || !clauseInfo.IsSuccess)
        {
            // Fallback: return the builder unchanged with closing brace
            sb.AppendLine($"        return Unsafe.As<{concreteType}<{entityType}>>(builder);");
            sb.AppendLine($"    }}");
            return;
        }

        // Carrier-optimized path
        if (carrier != null && prebuiltChain != null)
        {
            if (keyType != null)
            {
                var concreteBuilder = $"QueryBuilder<{entityType}>";
                var retInterface = site.ResultTypeName != null
                    ? $"IQueryBuilder<{entityType}, {InterceptorCodeGenerator.GetShortTypeName(site.ResultTypeName)}>"
                    : $"IQueryBuilder<{entityType}>";
                CarrierEmitter.EmitCarrierClauseBody(sb, carrier, prebuiltChain, site, clauseBit, isFirstInChain,
                    concreteBuilder, retInterface, false, new List<InterceptorCodeGenerator.CachedExtractorField>());
            }
            else
            {
                // Generic key type — emit passthrough with clause mask bit set
                sb.AppendLine($"        var __c = Unsafe.As<{carrier.ClassName}>(builder);");
                if (clauseBit.HasValue)
                    sb.AppendLine($"        __c.Mask |= unchecked(({carrier.MaskType})(1 << {clauseBit.Value}));");
                var castTarget = site.ResultTypeName != null ? $"{returnType}<T, TResult>" : $"{returnType}<T>";
                sb.AppendLine($"        return Unsafe.As<{castTarget}>(builder);");
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

        var thisType = site.BuilderTypeName;
        var returnType = InterceptorCodeGenerator.ToReturnTypeName(thisType);
        var concreteType = InterceptorCodeGenerator.ToConcreteTypeName(returnType);

        if (site.ResultTypeName != null)
        {
            var resultType = InterceptorCodeGenerator.GetShortTypeName(site.ResultTypeName);
            var receiverType = InterceptorCodeGenerator.BuildReceiverType(thisType, entityType, resultType);
            sb.AppendLine($"    public static {returnType}<{entityType}, {resultType}> {methodName}(");
            sb.AppendLine($"        this {receiverType} builder,");
            sb.AppendLine($"        Expression<Func<{entityType}, bool>> _)");
        }
        else
        {
            sb.AppendLine($"    public static {returnType}<{entityType}> {methodName}(");
            sb.AppendLine($"        this {thisType}<{entityType}> builder,");
            sb.AppendLine($"        Expression<Func<{entityType}, bool>> _)");
        }

        sb.AppendLine($"    {{");

        if (clauseInfo == null || !clauseInfo.IsSuccess)
        {
            // Fallback: return the builder unchanged with closing brace
            sb.AppendLine($"        return Unsafe.As<{concreteType}<{entityType}>>(builder);");
            sb.AppendLine($"    }}");
            return;
        }

        // Carrier-optimized path
        if (carrier != null && prebuiltChain != null)
        {
            var concreteBuilder = $"QueryBuilder<{entityType}>";
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
            sb.AppendLine($"        Expression<Func<{entityType}, {resolvedValueType}>> _,");
            sb.AppendLine($"        {resolvedValueType} value)");
            sb.AppendLine($"    {{");

            var concreteBuilder = $"{concreteType}<{entityType}>";
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
        var exprParamName = hasResolvableCapturedParams ? "expr" : "_";

        methodFields ??= new List<InterceptorCodeGenerator.CachedExtractorField>();
        if (methodFields.Count > 0)
        {
            sb.AppendLine($"    [UnconditionalSuppressMessage(\"Trimming\", \"IL2075\",");
            sb.AppendLine($"        Justification = \"Closure fields are preserved by the expression tree that references them.\")]");
        }

        var thisType = site.BuilderTypeName;
        var returnType = InterceptorCodeGenerator.ToReturnTypeName(thisType);
        var concreteType = InterceptorCodeGenerator.ToConcreteTypeName(returnType);
        var receiverType = $"{thisType}<{entityType}>";

        sb.AppendLine($"    public static IExecutable{modKind}Builder<{entityType}> {methodName}(");
        sb.AppendLine($"        this {receiverType} builder,");
        sb.AppendLine($"        Expression<Func<{entityType}, bool>> {exprParamName})");
        sb.AppendLine($"    {{");

        if (clauseInfo == null || !clauseInfo.IsSuccess)
        {
            // Fallback: return the builder unchanged with closing brace
            sb.AppendLine($"        return Unsafe.As<{concreteType}<{entityType}>>(builder);");
            sb.AppendLine($"    }}");
            return;
        }

        // Carrier-optimized path
        if (carrier != null && prebuiltChain != null)
        {
            var concreteBuilder = $"{concreteType}<{entityType}>";
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
            sb.AppendLine($"        Expression<Func<{entityType}, {resolvedValueType}>> _,");
            sb.AppendLine($"        {resolvedValueType} value)");
            sb.AppendLine($"    {{");

            var concreteBuilder = $"{concreteBaseName}<{entityType}>";
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
        var clauseInfo = site.Clause;

        var isExecutable = site.BuilderKind is BuilderKind.ExecutableUpdate;
        var concreteBaseName = isExecutable ? "ExecutableUpdateBuilder" : "UpdateBuilder";
        var returnInterfaceBaseName = "I" + concreteBaseName;

        if (clauseInfo is not { SetAssignments: not null, IsSuccess: true })
            return;

        var hasCapturedParams = clauseInfo.Parameters.Any(p => p.IsCaptured);
        var actionParamName = hasCapturedParams ? "action" : "_";

        // Carrier-optimized path
        if (carrier != null && prebuiltChain != null)
        {
            sb.AppendLine($"    public static {returnInterfaceBaseName}<{entityType}> {methodName}(");
            sb.AppendLine($"        this {returnInterfaceBaseName}<{entityType}> builder,");
            sb.AppendLine($"        Action<{entityType}> {actionParamName})");
            sb.AppendLine($"    {{");

            var globalParamOffset = 0;
            foreach (var clause in prebuiltChain.GetClauseEntries())
            {
                if (clause.Site.UniqueId == site.UniqueId)
                    break;
                if (clause.Site.Kind == InterceptorKind.UpdateSetPoco && clause.Site.UpdateInfo != null)
                    globalParamOffset += clause.Site.UpdateInfo.Columns.Count;
                else if (clause.Site.Clause != null)
                    globalParamOffset += clause.Site.Clause.Parameters.Count;
            }

            if (isFirstInChain)
            {
                var concreteBuilder = $"{concreteBaseName}<{entityType}>";
                sb.AppendLine($"        var __b = Unsafe.As<{concreteBuilder}>(builder);");
                sb.AppendLine($"        var __c = new {carrier.ClassName} {{ Ctx = __b.State.ExecutionContext }};");
            }
            else
            {
                sb.AppendLine($"        var __c = Unsafe.As<{carrier.ClassName}>(builder);");
            }

            for (int i = 0; i < clauseInfo.Parameters.Count; i++)
            {
                var p = clauseInfo.Parameters[i];
                var globalIdx = globalParamOffset + i;
                if (globalIdx >= prebuiltChain.ChainParameters.Count) continue;
                var carrierParam = prebuiltChain.ChainParameters[globalIdx];

                var castType = carrierParam.ClrType == "?" || carrierParam.ClrType == "object"
                    ? "object?"
                    : carrierParam.ClrType;
                if (p.IsCaptured)
                {
                    sb.AppendLine($"        {carrier.ClassName}.F{globalIdx} ??= action.Target!.GetType().GetField(\"{p.ValueExpression}\")!;");
                    sb.AppendLine($"        __c.P{globalIdx} = ({castType}){carrier.ClassName}.F{globalIdx}.GetValue(action.Target)!;");
                }
                else
                {
                    sb.AppendLine($"        __c.P{globalIdx} = ({castType}){p.ValueExpression}!;");
                }
            }

            if (clauseBit.HasValue)
                sb.AppendLine($"        __c.Mask |= unchecked(({CarrierEmitter.GetMaskType(prebuiltChain)})(1 << {clauseBit.Value}));");

            var returnInterface = $"{returnInterfaceBaseName}<{entityType}>";
            if (isFirstInChain)
                sb.AppendLine($"        return Unsafe.As<{returnInterface}>(__c);");
            else
                sb.AppendLine($"        return Unsafe.As<{returnInterface}>(builder);");

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

            if (isFirstInChain)
            {
                var concreteBuilder = $"{concreteBaseName}<{entityType}>";
                sb.AppendLine($"        var __b = Unsafe.As<{concreteBuilder}>(builder);");
                sb.AppendLine($"        var __c = new {carrier.ClassName} {{ Ctx = __b.State.ExecutionContext }};");
            }
            else
            {
                sb.AppendLine($"        var __c = Unsafe.As<{carrier.ClassName}>(builder);");
            }

            sb.AppendLine($"        __c.Entity = entity;");

            if (clauseBit.HasValue)
                sb.AppendLine($"        __c.Mask |= unchecked(({CarrierEmitter.GetMaskType(prebuiltChain)})(1 << {clauseBit.Value}));");

            var returnInterface = $"{returnInterfaceBaseName}<{entityType}>";
            if (isFirstInChain)
                sb.AppendLine($"        return Unsafe.As<{returnInterface}>(__c);");
            else
                sb.AppendLine($"        return Unsafe.As<{returnInterface}>(builder);");

            sb.AppendLine($"    }}");
            return;
        }
    }
}
