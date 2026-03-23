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
        sb.AppendLine($"        return new {carrier.ClassName} {{ Ctx = (IQueryExecutionContext)@this }};");
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
