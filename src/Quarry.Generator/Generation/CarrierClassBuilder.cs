using System.Collections.Generic;
using System.Linq;
using Quarry.Generators.Models;

namespace Quarry.Generators.Generation;

/// <summary>
/// Builds <see cref="CarrierClassInfo"/> from a <see cref="PrebuiltChainInfo"/>.
/// The carrier class is a lightweight <c>file sealed class</c> that inherits from
/// a carrier base class and declares only chain-specific fields.
/// </summary>
internal static class CarrierClassBuilder
{
    /// <summary>
    /// Builds a CarrierClassInfo for a carrier-eligible PrebuiltDispatch chain.
    /// Returns null if the chain cannot be carrier-optimized.
    /// </summary>
    public static CarrierClassInfo? Build(PrebuiltChainInfo chain, int chainIndex, string? resolvedBaseClass = null)
    {
        if (!chain.IsCarrierEligible)
            return null;

        var fields = new List<CarrierField>();

        // Fields: typed parameters P0, P1, ...
        foreach (var param in chain.ChainParameters)
        {
            fields.Add(new CarrierField($"P{param.Index}", param.TypeName, FieldRole.Parameter));
        }

        // Field: Mask (if chain has conditional clauses)
        if (chain.Analysis.ConditionalClauses.Count > 0)
        {
            var bitCount = chain.Analysis.ConditionalClauses.Count;
            var maskType = bitCount <= 8 ? "byte" : bitCount <= 16 ? "ushort" : "uint";
            fields.Add(new CarrierField("Mask", maskType, FieldRole.ClauseMask));
        }

        // Fields: Limit/Offset (if chain has runtime pagination values)
        foreach (var clause in chain.Analysis.Clauses)
        {
            if (clause.Role == ClauseRole.Limit)
                fields.Add(new CarrierField("Limit", "int", FieldRole.Limit));
            if (clause.Role == ClauseRole.Offset)
                fields.Add(new CarrierField("Offset", "int", FieldRole.Offset));
        }

        // Field: Timeout (if chain contains WithTimeout)
        if (chain.Analysis.Clauses.Any(c => c.Role == ClauseRole.WithTimeout))
            fields.Add(new CarrierField("Timeout", "TimeSpan?", FieldRole.Timeout));

        // Determine base class from chain shape (caller may provide pre-resolved base)
        var baseClassName = resolvedBaseClass ?? SelectBaseClass(chain);

        var className = $"Chain_{chainIndex}";

        return new CarrierClassInfo(
            className: className,
            implementedInterfaces: new[] { baseClassName },
            fields: fields,
            deadMethods: System.Array.Empty<CarrierInterfaceStub>());
    }

    /// <summary>
    /// Selects the appropriate carrier base class based on the chain's shape
    /// (join count, whether it has a Select projection).
    /// </summary>
    private static string SelectBaseClass(PrebuiltChainInfo chain)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(chain.EntityTypeName);
        var hasSelect = chain.Analysis.Clauses.Any(c => c.Role == ClauseRole.Select);
        var joinCount = chain.IsJoinChain ? (chain.JoinedEntityTypeNames?.Count ?? 1) - 1 : 0;

        if (joinCount == 0)
        {
            if (hasSelect && chain.ResultTypeName != null)
            {
                var resultType = InterceptorCodeGenerator.GetShortTypeName(chain.ResultTypeName);
                return $"CarrierBase<{entityType}, {resultType}>";
            }
            return $"CarrierBase<{entityType}>";
        }

        var joinedTypes = chain.JoinedEntityTypeNames!.Select(InterceptorCodeGenerator.GetShortTypeName).ToArray();
        var joinedTypesStr = string.Join(", ", joinedTypes);

        if (hasSelect && chain.ResultTypeName != null)
        {
            var resultType = InterceptorCodeGenerator.GetShortTypeName(chain.ResultTypeName);
            return joinCount switch
            {
                1 => $"JoinedCarrierBase<{joinedTypesStr}, {resultType}>",
                2 => $"JoinedCarrierBase3<{joinedTypesStr}, {resultType}>",
                3 => $"JoinedCarrierBase4<{joinedTypesStr}, {resultType}>",
                _ => $"JoinedCarrierBase<{joinedTypesStr}, {resultType}>"
            };
        }

        return joinCount switch
        {
            1 => $"JoinedCarrierBase<{joinedTypesStr}>",
            2 => $"JoinedCarrierBase3<{joinedTypesStr}>",
            3 => $"JoinedCarrierBase4<{joinedTypesStr}>",
            _ => $"JoinedCarrierBase<{joinedTypesStr}>"
        };
    }
}
