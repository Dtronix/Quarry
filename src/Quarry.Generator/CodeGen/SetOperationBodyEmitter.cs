using System.Text;
using Quarry.Generators.Generation;
using Quarry.Generators.IR;
using Quarry.Generators.Models;

namespace Quarry.Generators.CodeGen;

/// <summary>
/// Generates interceptor method bodies for set operation calls (Union, UnionAll, Intersect, etc.).
/// The set operation interceptor is a pass-through: the SQL is prebuilt with the UNION/INTERSECT/EXCEPT
/// clause included. The interceptor accepts the operand builder argument and returns the carrier.
/// </summary>
internal static class SetOperationBodyEmitter
{
    /// <summary>
    /// Emits a carrier set operation interceptor method.
    /// The method signature matches the IQueryBuilder set operation method (e.g., Union(IQueryBuilder&lt;T&gt; other)).
    /// At runtime, this is a no-op — the prebuilt SQL already includes the set operation clause.
    /// The operand builder reference is accepted but not used (its SQL is compiled in).
    /// </summary>
    public static void EmitSetOperation(
        StringBuilder sb, TranslatedCallSite site, string methodName,
        CarrierPlan carrier, AssembledPlan chain)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);
        var receiverType = CarrierEmitter.ResolveCarrierReceiverType(site, entityType, chain);

        // The argument type is the same as the receiver type — both sides of a set operation
        // must have compatible projections. Use the receiver type for the argument.
        var argType = receiverType;

        var returnTypeName = site.BuilderTypeName is "IEntityAccessor" or "EntityAccessor"
            ? $"IQueryBuilder<{entityType}>" : receiverType;

        sb.AppendLine($"    public static {returnTypeName} {methodName}(");
        sb.AppendLine($"        this {receiverType} builder,");
        sb.AppendLine($"        {argType} other)");
        sb.AppendLine($"    {{");

        // Copy operand carrier parameters into main carrier parameter fields
        if (chain.Plan.SetOperations.Count > 0)
        {
            sb.AppendLine($"        // Set operation: operand SQL is prebuilt into this carrier's SQL.");
            sb.AppendLine($"        // Copy operand carrier parameters to main carrier fields.");
            sb.AppendLine($"        var __c = Unsafe.As<{carrier.ClassName}>(builder);");
            sb.AppendLine($"        var __op = Unsafe.As<{carrier.ClassName}>(other);");

            // Find the set operation plan that matches this site by looking at parameter offsets
            foreach (var setOp in chain.Plan.SetOperations)
            {
                for (int i = 0; i < setOp.Operand.Parameters.Count; i++)
                {
                    var targetIdx = setOp.ParameterOffset + i;
                    sb.AppendLine($"        __c.P{targetIdx} = __op.P{i};");
                }
            }
        }

        if (returnTypeName != receiverType)
            sb.AppendLine($"        return Unsafe.As<{returnTypeName}>(builder);");
        else
            sb.AppendLine($"        return builder;");
        sb.AppendLine($"    }}");
    }
}
