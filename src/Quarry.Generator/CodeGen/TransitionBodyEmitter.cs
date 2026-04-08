using System.Collections.Generic;
using System.Text;
using Quarry.Generators.Generation;
using Quarry.Generators.IR;
using Quarry.Generators.Models;

namespace Quarry.Generators.CodeGen;

/// <summary>
/// Emits interceptor method bodies for transition sites (DeleteTransition,
/// UpdateTransition, InsertTransition, AllTransition), chain roots,
/// and carrier-only clause interceptors (Pagination, Distinct, WithTimeout).
/// </summary>
internal static class TransitionBodyEmitter
{
    /// <summary>
    /// Emits a Delete/Update transition interceptor.
    /// Carrier path only — noop cast since carrier implements both interfaces.
    /// </summary>
    public static void EmitDeleteUpdateTransition(
        StringBuilder sb, TranslatedCallSite site, string methodName)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);
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
    /// Emits an Insert transition interceptor.
    /// Carrier path only — stores entity on carrier, returns as IInsertBuilder.
    /// </summary>
    public static void EmitInsertTransition(
        StringBuilder sb, TranslatedCallSite site, string methodName, CarrierPlan carrier)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);

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
    /// Emits a batch insert column selector interceptor.
    /// Carrier path only — returns carrier as IBatchInsertBuilder.
    /// The column selector expression is ignored at runtime (compile-time only).
    /// </summary>
    public static void EmitBatchInsertColumnSelector(
        StringBuilder sb, TranslatedCallSite site, string methodName, CarrierPlan carrier)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);

        sb.AppendLine($"    public static IBatchInsertBuilder<T> {methodName}<T, TColumns>(");
        sb.AppendLine($"        this IEntityAccessor<T> builder,");
        sb.AppendLine($"        Func<T, TColumns> columnSelector) where T : class");
        sb.AppendLine($"    {{");
        sb.AppendLine($"        var __c = Unsafe.As<{carrier.ClassName}>(builder);");
        sb.AppendLine($"        return Unsafe.As<IBatchInsertBuilder<T>>(__c);");
        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Emits a batch insert Values() interceptor.
    /// Carrier path only — stores entities on carrier, returns as IExecutableBatchInsert.
    /// </summary>
    public static void EmitBatchInsertValues(
        StringBuilder sb, TranslatedCallSite site, string methodName, CarrierPlan carrier)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);

        sb.AppendLine($"    public static IExecutableBatchInsert<{entityType}> {methodName}(");
        sb.AppendLine($"        this IBatchInsertBuilder<{entityType}> builder,");
        sb.AppendLine($"        IEnumerable<{entityType}> entities)");
        sb.AppendLine($"    {{");
        sb.AppendLine($"        var __c = Unsafe.As<{carrier.ClassName}>(builder);");
        sb.AppendLine($"        __c.BatchEntities = entities;");
        sb.AppendLine($"        return Unsafe.As<IExecutableBatchInsert<{entityType}>>(__c);");
        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Emits a CteDefinition interceptor (e.g., db.With&lt;TDto&gt;(inner)).
    /// For the FIRST CTE site in the chain, allocates a new outer carrier from the real
    /// context. For each SUBSEQUENT CTE site in the same chain, the receiver is the
    /// outer carrier produced by the previous With() call (typed as the context class via
    /// <see cref="System.Runtime.CompilerServices.Unsafe.As{T}(object)"/>) — the body
    /// reinterpret-casts it back to the carrier so that prior CTE state is preserved.
    /// In both cases, the inner chain's captured parameter values are copied from the
    /// passed-in <c>innerQuery</c> carrier into the outer carrier's matching parameter
    /// slots, then the outer carrier is returned typed as the context class for the next
    /// chained call. The inner SQL itself is embedded into the outer query's WITH clause
    /// at compile time by <see cref="IR.SqlAssembler"/>.
    /// </summary>
    public static void EmitCteDefinition(
        StringBuilder sb, TranslatedCallSite site, string methodName, CarrierPlan carrier,
        AssembledPlan chain,
        Dictionary<QueryPlan, string>? operandCarrierNames)
    {
        var contextClass = site.ContextClassName ?? "QuarryContext";
        var raw = site.Bound.Raw;
        // The method has a generic parameter TDto and takes IQueryBuilder<TDto> (or IQueryBuilder<TEntity,TDto>)
        // Interceptors for generic methods must match the concrete type arguments from the call site.
        var dtoType = InterceptorCodeGenerator.GetShortTypeName(raw.CteEntityTypeName ?? site.EntityTypeName);

        // Use the stored parameter type from discovery (handles both 1-arg and 2-arg overloads,
        // and lambda form Func<IEntityAccessor<TDto>, IQueryBuilder<TDto>>)
        var paramType = raw.BuilderTypeName ?? $"IQueryBuilder<{dtoType}>";
        var isLambdaForm = raw.LambdaInnerSpanStart.HasValue;
        var paramName = isLambdaForm ? "innerBuilder" : "innerQuery";

        // Determine whether this is the FIRST CteDefinition site in the chain (in source
        // order) or a subsequent one. The first site allocates a new carrier from the real
        // context; subsequent sites recover the carrier produced by the previous With()
        // call, which the call chain has reinterpret-cast as the context class via
        // Unsafe.As. Detection is purely positional — count CteDefinition-kind sites in
        // chain.ClauseSites that precede this site by UniqueId. This is O(N) per site but
        // N is the chain length and chains are short.
        bool isFirstCteSite = true;
        bool sawCurrentSite = false;
        bool sawPriorCte = false;
        for (int i = 0; i < chain.ClauseSites.Count; i++)
        {
            var s = chain.ClauseSites[i];
            if (s.UniqueId == site.UniqueId) { sawCurrentSite = true; break; }
            if (s.Bound.Raw.Kind == InterceptorKind.CteDefinition)
            {
                sawPriorCte = true;
                isFirstCteSite = false;
                break;
            }
        }
        // Defensive note: if the loop walks the entire ClauseSites list without hitting
        // either the current site or a prior CteDefinition, the current site is not
        // present in its own chain's ClauseSites — a discovery-pipeline invariant
        // violation that is not reachable by any current test scenario. The defaulted
        // behavior (isFirstCteSite = true) is still correct for the only scenario this
        // could reach: the current site being the only CTE in the chain. Emit a code
        // comment so that any future bug report can quickly locate the violation.
        if (!sawCurrentSite && !sawPriorCte)
        {
            sb.AppendLine($"    // NOTE: EmitCteDefinition site {site.UniqueId} not found in chain.ClauseSites; defaulted to first-CTE behavior.");
        }

        sb.AppendLine($"    public static {contextClass} {methodName}(");
        sb.AppendLine($"        this {contextClass} @this,");
        sb.AppendLine($"        {paramType} {paramName})");
        sb.AppendLine($"    {{");
        if (isFirstCteSite)
        {
            sb.AppendLine($"        var __c = new {carrier.ClassName} {{ Ctx = @this }};");
        }
        else
        {
            // Subsequent With() in a multi-CTE chain: @this is actually the carrier from
            // the previous With() call, reinterpret-cast as the context class. Recover it
            // so this call extends the same carrier instance instead of allocating a new
            // one (which would discard prior CTE parameter state and corrupt Ctx).
            sb.AppendLine($"        var __c = Unsafe.As<{carrier.ClassName}>(@this);");
        }

        var siteCteName = CteNameHelpers.ExtractShortName(raw.CteEntityTypeName ?? site.EntityTypeName) ?? "CTE";

        if (isLambdaForm)
        {
            // Lambda form: extract captured variables from the lambda delegate's display class
            // via [UnsafeAccessor] and bind them to carrier P-fields at ParameterOffset.
            // No inner carrier — the inner chain is purely compile-time.
            for (int i = 0; i < chain.Plan.CteDefinitions.Count; i++)
            {
                var cteDef = chain.Plan.CteDefinitions[i];
                if (cteDef.Name != siteCteName) continue;
                if (cteDef.InnerParameters.Count > 0)
                    CarrierEmitter.EmitLambdaInnerChainCapture(sb, carrier, site, cteDef.InnerParameters, cteDef.ParameterOffset);
                break;
            }
        }
        else
        {
            // Direct form: copy P-fields from the inner carrier into the outer carrier.
            // Locate the CteDef whose CTE name matches this site's DTO and whose InnerPlan
            // has a registered carrier.
            //
            // Both this site's CTE name and cteDef.Name are produced by the SAME helper
            // (CteNameHelpers.ExtractShortName) — using divergent helpers here previously
            // caused the captured-param copy to silently no-op for global-namespace DTOs.
            //
            // NOTE: matches the FIRST cteDef whose name equals this site's DTO short name.
            // Multiple With<T>(...) calls referencing the same DTO type in one chain (e.g.,
            // db.With<X>(a).With<X>(b)...) would produce duplicate CTE aliases in the WITH
            // clause and silently route the second call to the first cteDef. The duplicate-
            // name case is rejected at compile time by diagnostic QRY082.
            for (int i = 0; i < chain.Plan.CteDefinitions.Count; i++)
            {
                var cteDef = chain.Plan.CteDefinitions[i];
                if (cteDef.Name != siteCteName) continue;
                if (cteDef.InnerPlan == null || cteDef.InnerParameters.Count == 0) break;
                string? innerCarrierName = null;
                operandCarrierNames?.TryGetValue(cteDef.InnerPlan, out innerCarrierName);
                if (innerCarrierName == null) break;

                sb.AppendLine($"        var __inner = Unsafe.As<{innerCarrierName}>({paramName});");
                for (int p = 0; p < cteDef.InnerParameters.Count; p++)
                {
                    var targetIdx = cteDef.ParameterOffset + p;
                    sb.AppendLine($"        __c.P{targetIdx} = __inner.P{p};");
                }
                break;
            }
        }

        sb.AppendLine($"        return Unsafe.As<{contextClass}>(__c);");
        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Emits a FromCte interceptor (e.g., db.FromCte&lt;TDto&gt;()).
    /// Noop type transition — carrier already exists from CteDefinition.
    /// </summary>
    public static void EmitFromCte(
        StringBuilder sb, TranslatedCallSite site, string methodName, CarrierPlan carrier)
    {
        var contextClass = site.ContextClassName ?? "QuarryContext";
        var dtoType = InterceptorCodeGenerator.GetShortTypeName(site.Bound.Raw.CteEntityTypeName ?? site.EntityTypeName);

        sb.AppendLine($"    public static IEntityAccessor<{dtoType}> {methodName}(");
        sb.AppendLine($"        this {contextClass} @this)");
        sb.AppendLine($"    {{");
        sb.AppendLine($"        return Unsafe.As<IEntityAccessor<{dtoType}>>(@this);");
        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Emits a carrier ChainRoot interceptor (e.g., db.Users()).
    /// Creates the carrier directly from the context — zero QueryBuilder allocation.
    /// </summary>
    public static void EmitChainRoot(
        StringBuilder sb, TranslatedCallSite site, string methodName, CarrierPlan carrier)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);
        var contextClass = site.ContextClassName ?? "QuarryContext";

        sb.AppendLine($"    public static IEntityAccessor<{entityType}> {methodName}(");
        sb.AppendLine($"        this {contextClass} @this)");
        sb.AppendLine($"    {{");
        sb.AppendLine($"        return new {carrier.ClassName} {{ Ctx = @this }};");
        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Emits a ChainRoot interceptor that follows a CTE definition in the same chain
    /// (e.g., <c>db.With&lt;A&gt;(inner).Users()</c>).
    /// The carrier was already created by <see cref="EmitCteDefinition"/>; this is a
    /// noop type transition — same pattern as <see cref="EmitFromCte"/>.
    /// </summary>
    public static void EmitChainRootAfterCte(
        StringBuilder sb, TranslatedCallSite site, string methodName)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);
        var contextClass = site.ContextClassName ?? "QuarryContext";

        sb.AppendLine($"    public static IEntityAccessor<{entityType}> {methodName}(");
        sb.AppendLine($"        this {contextClass} @this)");
        sb.AppendLine($"    {{");
        sb.AppendLine($"        return Unsafe.As<IEntityAccessor<{entityType}>>(@this);");
        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Emits an All() transition interceptor.
    /// Carrier path: noop cast. Non-carrier path: delegates to real All() method.
    /// </summary>
    public static void EmitAllTransition(
        StringBuilder sb, TranslatedCallSite site, string methodName, CarrierPlan? carrier)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);
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
            sb.AppendLine($"        return Unsafe.As<{returnType}>(builder);");
        }
        else
        {
            sb.AppendLine($"        return Unsafe.As<{concreteType}>(builder).All();");
        }

        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Emits a carrier Limit/Offset interceptor method.
    /// Stores the runtime value on the carrier field if present; always returns builder.
    /// </summary>
    public static void EmitPagination(
        StringBuilder sb, TranslatedCallSite site, string methodName,
        CarrierPlan carrier, AssembledPlan chain)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);
        var receiverType = CarrierEmitter.ResolveCarrierReceiverType(site, entityType, chain);

        sb.AppendLine($"    public static {receiverType} {methodName}(");
        sb.AppendLine($"        this {receiverType} builder, int count)");
        sb.AppendLine($"    {{");

        var fieldName = site.Kind == InterceptorKind.Limit ? "Limit" : "Offset";

        if (CarrierEmitter.HasCarrierField(carrier, site.Kind == InterceptorKind.Limit ? FieldRole.Limit : FieldRole.Offset))
        {
            sb.AppendLine($"        Unsafe.As<{carrier.ClassName}>(builder).{fieldName} = count;");
        }

        sb.AppendLine($"        return builder;");
        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Emits a carrier Distinct interceptor method (always noop — Distinct is baked into SQL).
    /// </summary>
    public static void EmitDistinct(
        StringBuilder sb, TranslatedCallSite site, string methodName,
        CarrierPlan carrier, AssembledPlan? chain = null)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);
        var receiverType = CarrierEmitter.ResolveCarrierReceiverType(site, entityType, chain);
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
    /// Emits a carrier WithTimeout interceptor method.
    /// Stores timeout on the carrier field if present; always returns builder.
    /// </summary>
    public static void EmitWithTimeout(
        StringBuilder sb, TranslatedCallSite site, string methodName,
        CarrierPlan carrier, AssembledPlan? chain = null)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);
        var receiverType = CarrierEmitter.ResolveCarrierReceiverType(site, entityType, chain);

        sb.AppendLine($"    public static {receiverType} {methodName}(");
        sb.AppendLine($"        this {receiverType} builder, TimeSpan timeout)");
        sb.AppendLine($"    {{");

        if (CarrierEmitter.HasCarrierField(carrier, FieldRole.Timeout))
        {
            sb.AppendLine($"        Unsafe.As<{carrier.ClassName}>(builder).Timeout = timeout;");
        }

        sb.AppendLine($"        return builder;");
        sb.AppendLine($"    }}");
    }
}
