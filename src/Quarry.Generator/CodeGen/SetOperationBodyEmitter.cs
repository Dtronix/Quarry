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
    /// <paramref name="setOpIndex"/> identifies which SetOperationPlan this site corresponds to
    /// (0 for the first Union/Intersect/Except, 1 for the second, etc.).
    /// </summary>
    public static void EmitSetOperation(
        StringBuilder sb, TranslatedCallSite site, string methodName,
        CarrierPlan carrier, AssembledPlan chain, int setOpIndex,
        Dictionary<QueryPlan, string>? operandCarrierNames = null)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);
        var receiverType = CarrierEmitter.ResolveCarrierReceiverType(site, entityType, chain);

        // For cross-entity set operations, the argument type uses the operand's entity type
        string argType;
        if (setOpIndex < chain.Plan.SetOperations.Count
            && chain.Plan.SetOperations[setOpIndex].OperandEntityTypeName != null)
        {
            var operandEntityType = InterceptorCodeGenerator.GetShortTypeName(
                chain.Plan.SetOperations[setOpIndex].OperandEntityTypeName!);
            // Resolve the shared result type (both sides project to the same TResult)
            var resolved = InterceptorCodeGenerator.ResolveExecutionResultType(
                site.ResultTypeName, chain.ResultTypeName, chain.ProjectionInfo);
            var resultType = !string.IsNullOrEmpty(resolved)
                ? InterceptorCodeGenerator.GetShortTypeName(resolved!)
                : null;
            argType = resultType != null
                ? $"IQueryBuilder<{operandEntityType}, {resultType}>"
                : $"IQueryBuilder<{operandEntityType}>";
        }
        else
        {
            argType = receiverType;
        }

        var returnTypeName = site.BuilderTypeName is "IEntityAccessor" or "EntityAccessor"
            ? $"IQueryBuilder<{entityType}>" : receiverType;

        sb.AppendLine($"    public static {returnTypeName} {methodName}(");
        sb.AppendLine($"        this {receiverType} builder,");
        sb.AppendLine($"        {argType} other)");
        sb.AppendLine($"    {{");

        // Copy operand carrier parameters into main carrier parameter fields
        if (setOpIndex < chain.Plan.SetOperations.Count)
        {
            var setOp = chain.Plan.SetOperations[setOpIndex];

            // Resolve the operand carrier class name
            string? opCarrierName = null;
            operandCarrierNames?.TryGetValue(setOp.Operand, out opCarrierName);

            if (opCarrierName != null && setOp.Operand.Parameters.Count > 0)
            {
                sb.AppendLine($"        var __c = Unsafe.As<{carrier.ClassName}>(builder);");
                sb.AppendLine($"        var __op = Unsafe.As<{opCarrierName}>(other);");
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
