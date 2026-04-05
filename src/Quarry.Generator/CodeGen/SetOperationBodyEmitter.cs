using System.Collections.Generic;
using System.Text;
using Quarry.Generators.Generation;
using Quarry.Generators.IR;
using Quarry.Generators.Models;

namespace Quarry.Generators.CodeGen;

/// <summary>
/// Generates interceptor method bodies for set operation calls (Union, UnionAll, Intersect, etc.).
/// The set operation interceptor accepts the operand builder, copies its parameter values
/// into the main carrier's fields, and returns the main carrier.
/// </summary>
internal static class SetOperationBodyEmitter
{
    /// <summary>
    /// Emits a carrier set operation interceptor method.
    /// The method signature matches the IQueryBuilder set operation method (e.g., Union(IQueryBuilder&lt;T&gt; other)).
    /// At runtime, the operand carrier's parameter fields are copied into the main carrier's fields
    /// at the appropriate offsets.
    /// </summary>
    public static void EmitSetOperation(
        StringBuilder sb, TranslatedCallSite site, string methodName,
        CarrierPlan carrier, AssembledPlan chain,
        Dictionary<QueryPlan, string>? operandCarrierNames = null)
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
            sb.AppendLine($"        // Set operation: copy operand carrier parameters to main carrier fields.");
            sb.AppendLine($"        var __c = Unsafe.As<{carrier.ClassName}>(builder);");

            foreach (var setOp in chain.Plan.SetOperations)
            {
                // Resolve the operand carrier class name
                string? opCarrierName = null;
                operandCarrierNames?.TryGetValue(setOp.Operand, out opCarrierName);

                if (opCarrierName != null && setOp.Operand.Parameters.Count > 0)
                {
                    sb.AppendLine($"        var __op = Unsafe.As<{opCarrierName}>(other);");
                    for (int i = 0; i < setOp.Operand.Parameters.Count; i++)
                    {
                        var targetIdx = setOp.ParameterOffset + i;
                        sb.AppendLine($"        __c.P{targetIdx} = __op.P{i};");
                    }
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
