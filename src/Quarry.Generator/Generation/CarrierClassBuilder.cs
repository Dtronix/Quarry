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

        // Fields: typed parameters P0, P1, ... (scalar or collection)
        // Entity-sourced parameters (SetPoco) don't get their own P{n} fields — they
        // are read from the Entity field at terminal time.
        foreach (var param in chain.ChainParameters)
        {
            if (param.EntityPropertyExpression != null)
                continue;

            if (param.IsCollection && param.ElementTypeName != null)
            {
                // Collection parameters use IReadOnlyList<T> carrier fields
                var elementType = NormalizeFieldType(param.ElementTypeName);
                fields.Add(new CarrierField($"P{param.Index}", $"System.Collections.Generic.IReadOnlyList<{elementType}>", FieldRole.Collection));
            }
            else
            {
                fields.Add(new CarrierField($"P{param.Index}", NormalizeFieldType(param.TypeName), FieldRole.Parameter));
            }
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

        // Field: Entity (for insert chains — stores the entity passed to .Insert(),
        // or for update chains with SetPoco — stores the entity passed to .Set(entity))
        if (chain.QueryKind == QueryKind.Insert
            || (chain.QueryKind == QueryKind.Update
                && chain.Analysis.Clauses.Any(c => c.Site.Kind == InterceptorKind.UpdateSetPoco)))
        {
            var entityType = InterceptorCodeGenerator.GetShortTypeName(chain.EntityTypeName);
            fields.Add(new CarrierField("Entity", entityType + "?", FieldRole.Entity));
        }

        // Static FieldInfo cache fields — only for captured params needing expression tree extraction
        var staticFields = new List<CarrierStaticField>();
        foreach (var param in chain.ChainParameters)
        {
            if (param.NeedsFieldInfoCache)
                staticFields.Add(new CarrierStaticField($"F{param.Index}", "FieldInfo?", param.Index));
        }

        // Determine base class from chain shape (caller may provide pre-resolved base)
        var baseClassName = resolvedBaseClass ?? SelectBaseClass(chain);

        var className = $"Chain_{chainIndex}";

        return new CarrierClassInfo(
            className: className,
            implementedInterfaces: new[] { baseClassName },
            fields: fields,
            deadMethods: System.Array.Empty<CarrierInterfaceStub>(),
            staticFields: staticFields);
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

    /// <summary>
    /// Checks if the given CLR type name is a non-nullable value type.
    /// Used by the logging emitter to decide between .ToString() and ?.ToString() ?? "null".
    /// </summary>
    internal static bool IsNonNullableValueType(string typeName)
    {
        if (typeName.EndsWith("?"))
            return false;
        if (ValueTypes.Contains(typeName))
            return true;
        // Enum types (PascalCase, no dots/generics) are value types
        if (!typeName.Contains('<') && !typeName.Contains('[') && !typeName.Contains('.'))
            return false; // Could be a class name — treat conservatively as reference
        return false;
    }

    /// <summary>
    /// Known value types that don't need nullable annotation.
    /// </summary>
    private static readonly HashSet<string> ValueTypes = new(System.StringComparer.Ordinal)
    {
        "int", "long", "short", "byte", "sbyte", "uint", "ulong", "ushort",
        "float", "double", "decimal", "bool", "char",
        "DateTime", "DateTimeOffset", "TimeSpan", "Guid", "DateOnly", "TimeOnly",
        "Int32", "Int64", "Int16", "Byte", "SByte", "UInt32", "UInt64", "UInt16",
        "Single", "Double", "Decimal", "Boolean", "Char"
    };

    /// <summary>
    /// Normalizes a parameter type for carrier field emission.
    /// - Normalizes <c>Nullable&lt;T&gt;</c> to <c>T?</c>
    /// - Appends <c>?</c> to reference types (non-value-types without existing <c>?</c>)
    ///   to suppress nullable warnings in <c>#nullable enable</c> context
    /// </summary>
    private static string NormalizeFieldType(string typeName)
    {
        // Normalize Nullable<T> → T?
        if (typeName.StartsWith("System.Nullable<") || typeName.StartsWith("Nullable<"))
        {
            var inner = typeName.Substring(typeName.IndexOf('<') + 1).TrimEnd('>');
            return inner + "?";
        }

        // Already nullable — pass through
        if (typeName.EndsWith("?"))
            return typeName;

        // Value types don't need ?
        if (ValueTypes.Contains(typeName))
            return typeName;

        // Enum types (usually PascalCase without dots) — assume value type, pass through
        // Generic types, array types — pass through (complex to analyze)
        if (typeName.Contains('<') || typeName.Contains('[') || typeName.Contains('.'))
            return typeName;

        // Reference types (string, class names) — append ? for nullable context
        return typeName + "?";
    }
}
