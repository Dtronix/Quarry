using System.Collections.Generic;
using System.Linq;
using System.Text;
using Quarry.Generators.Generation;
using Quarry.Generators.Models;

namespace Quarry.Generators.CodeGen;

/// <summary>
/// Emits carrier class definitions and carrier-path interceptor method bodies.
/// Works from <see cref="CarrierStrategy"/> (produced by <see cref="CarrierAnalyzer"/>)
/// rather than computing eligibility inline.
/// </summary>
/// <remarks>
/// Currently delegates to InterceptorCodeGenerator.Carrier.cs methods for emission.
/// Phase 6A will port emission logic here, eliminating the dependency on
/// InterceptorCodeGenerator partial methods.
/// </remarks>
internal static class CarrierEmitter
{
    /// <summary>
    /// Emits the carrier class declaration (fields, static caches, base class).
    /// </summary>
    public static void EmitClassDeclaration(StringBuilder sb, CarrierStrategy strategy, string className)
    {
        sb.AppendLine($"/// <remarks>Chain: Carrier-Optimized PrebuiltDispatch (1 allocation: carrier)</remarks>");
        sb.Append($"file sealed class {className}");

        if (!string.IsNullOrEmpty(strategy.BaseClassName))
        {
            sb.Append($" : {strategy.BaseClassName}");
        }
        sb.AppendLine();
        sb.AppendLine("{");

        // Instance fields
        foreach (var field in strategy.Fields)
        {
            sb.AppendLine($"    public {field.Type} {field.Name};");
        }

        // Static FieldInfo cache fields
        foreach (var sf in strategy.StaticFields)
        {
            sb.Append($"    private static {sf.Type} {sf.Name}");
            if (sf.Initializer != null)
                sb.Append($" = {sf.Initializer}");
            sb.AppendLine(";");
        }

        sb.AppendLine("}");
    }

    /// <summary>
    /// Emits a carrier ChainRoot interceptor body (db.Users() → new Carrier { Ctx = ctx }).
    /// </summary>
    public static void EmitChainRootBody(
        StringBuilder sb, string className, string entityType, string contextClass)
    {
        sb.AppendLine($"    public static IEntityAccessor<{entityType}> {{0}}(");
        sb.AppendLine($"        this {contextClass} @this)");
        sb.AppendLine($"    {{");
        sb.AppendLine($"        return new {className} {{ Ctx = (IQueryExecutionContext)@this }};");
        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Emits a noop transition body (carrier implements both interfaces).
    /// </summary>
    public static void EmitNoopTransitionBody(
        StringBuilder sb, string receiverType, string returnType)
    {
        sb.AppendLine($"        return Unsafe.As<{returnType}>(builder);");
    }

    /// <summary>
    /// Emits a carrier clause body: cast to carrier, extract params, set mask bit, return.
    /// </summary>
    public static void EmitClauseBody(
        StringBuilder sb,
        string className,
        CarrierStrategy strategy,
        IReadOnlyList<CarrierParameter> clauseParameters,
        int globalParamOffset,
        int? clauseBit,
        bool isFirstInChain,
        string concreteBuilderType,
        string returnInterface,
        string maskType)
    {
        if (isFirstInChain)
        {
            sb.AppendLine($"        var __b = Unsafe.As<{concreteBuilderType}>(builder);");
            sb.AppendLine($"        var __c = new {className} {{ Ctx = __b.State.ExecutionContext }};");
        }
        else
        {
            sb.AppendLine($"        var __c = Unsafe.As<{className}>(builder);");
        }

        // Bind parameters to carrier fields
        foreach (var param in clauseParameters)
        {
            if (param.IsEntitySourced)
                continue;
            if (param.ExtractionCode != null)
            {
                sb.AppendLine($"        __c.{param.FieldName} = ({param.FieldType}){param.ExtractionCode}!;");
            }
        }

        // Set conditional clause bit
        if (clauseBit.HasValue)
            sb.AppendLine($"        __c.Mask |= unchecked(({maskType})(1 << {clauseBit.Value}));");

        // Return
        if (isFirstInChain)
            sb.AppendLine($"        return Unsafe.As<{returnInterface}>(__c);");
        else
            sb.AppendLine($"        return Unsafe.As<{returnInterface}>(builder);");
    }

    /// <summary>
    /// Emits carrier terminal preamble: cast carrier, resolve OpId, dispatch SQL.
    /// </summary>
    public static void EmitTerminalPreamble(
        StringBuilder sb, string className, string maskType, int maskBitCount)
    {
        sb.AppendLine($"        var __c = Unsafe.As<{className}>(builder);");
        if (maskBitCount > 0)
        {
            sb.AppendLine($"        var __opId = (ulong)__c.Mask;");
        }
        else
        {
            sb.AppendLine($"        const ulong __opId = 0;");
        }
    }

    /// <summary>
    /// Emits carrier parameter locals (__pVal0, __pVal1, etc.) from carrier fields.
    /// </summary>
    public static void EmitParameterLocals(
        StringBuilder sb, IReadOnlyList<CarrierParameter> parameters)
    {
        foreach (var param in parameters)
        {
            if (param.IsEntitySourced || param.IsCollection)
                continue;
            sb.AppendLine($"        var __pVal{param.GlobalIndex} = __c.{param.FieldName};");
        }
    }
}
