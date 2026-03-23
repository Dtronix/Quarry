using System;
using System.Collections.Generic;
using System.Linq;
using Quarry.Generators.Generation;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;

namespace Quarry.Generators.CodeGen;

/// <summary>
/// Analyzes a pre-built chain to determine carrier optimization eligibility
/// and compute the carrier strategy (base class, fields, parameters).
/// Consolidates eligibility checks previously spread across QuarryGenerator,
/// CarrierClassBuilder, and InterceptorCodeGenerator.Carrier.cs.
/// </summary>
internal static class CarrierAnalyzer
{
    // Old pipeline methods (Analyze, CheckTerminalEligibility, BuildEligibleStrategy) removed.
    // Use AnalyzeNew(AssembledPlan) for the new pipeline.

    /// <summary>
    /// Normalizes a parameter type for carrier field emission.
    /// </summary>
    private static string NormalizeFieldType(string typeName)
    {
        // Normalize Nullable<T> → T?
        if (typeName.StartsWith("System.Nullable<") || typeName.StartsWith("Nullable<"))
        {
            var inner = typeName.Substring(typeName.IndexOf('<') + 1).TrimEnd('>');
            return inner + "?";
        }

        if (typeName.EndsWith("?"))
            return typeName;

        if (ValueTypes.Contains(typeName))
            return typeName;

        if (typeName.Contains('<') || typeName.Contains('[') || typeName.Contains('.'))
            return typeName;

        // Reference types — append ? for nullable context
        return typeName + "?";
    }

    private static readonly HashSet<string> ValueTypes = new(StringComparer.Ordinal)
    {
        "int", "long", "short", "byte", "sbyte", "uint", "ulong", "ushort",
        "float", "double", "decimal", "bool", "char",
        "DateTime", "DateTimeOffset", "TimeSpan", "Guid", "DateOnly", "TimeOnly",
        "Int32", "Int64", "Int16", "Byte", "SByte", "UInt32", "UInt64", "UInt16",
        "Single", "Double", "Decimal", "Boolean", "Char"
    };

    #region New pipeline (AssembledPlan → CarrierPlan)

    /// <summary>
    /// Analyzes an AssembledPlan to produce a CarrierPlan for the new pipeline.
    /// </summary>
    public static CarrierPlan AnalyzeNew(AssembledPlan assembled)
    {
        var plan = assembled.Plan;

        // Gate: RuntimeBuild is never eligible
        if (plan.Tier == OptimizationTier.RuntimeBuild)
            return CarrierPlan.Ineligible(plan.NotAnalyzableReason ?? "runtime build tier");

        // Gate: Unmatched methods
        if (plan.UnmatchedMethodNames != null && plan.UnmatchedMethodNames.Count > 0)
            return CarrierPlan.Ineligible("chain has unmatched methods");

        // Gate: Trivial ToDiagnostics chains with no parameters or conditions
        // (e.g., db.Users().ToDiagnostics()) — carrier overhead is wasteful
        if (assembled.ExecutionSite.Bound.Raw.Kind == InterceptorKind.ToDiagnostics
            && plan.Parameters.Count == 0
            && plan.ConditionalTerms.Count == 0
            && plan.WhereTerms.Count == 0
            && plan.SetTerms.Count == 0)
            return CarrierPlan.Ineligible("trivial ToDiagnostics chain");

        // Gate: Empty SQL variants
        if (assembled.SqlVariants.Count == 0)
            return CarrierPlan.Ineligible("no SQL variants");

        // Gate: Malformed SQL
        foreach (var kvp in assembled.SqlVariants)
        {
            if (string.IsNullOrWhiteSpace(kvp.Value.Sql))
                return CarrierPlan.Ineligible("empty SQL variant");
        }

        // Build fields and parameters from QueryPlan.Parameters
        var fields = new List<Models.CarrierField>();
        var staticFields = new List<Models.CarrierStaticField>();
        var parameters = new List<CarrierParameter>();

        foreach (var param in plan.Parameters)
        {
            // Entity-sourced parameters (SetPoco) don't get carrier fields
            if (param.EntityPropertyExpression != null)
            {
                parameters.Add(new CarrierParameter(
                    globalIndex: param.GlobalIndex,
                    fieldName: $"P{param.GlobalIndex}",
                    fieldType: param.ClrType,
                    extractionCode: null,
                    bindingCode: null,
                    typeMappingClass: param.TypeMappingClass,
                    isEntitySourced: true));
                continue;
            }

            if (param.IsCollection && param.ElementTypeName != null)
            {
                var elementType = NormalizeFieldType(param.ElementTypeName);
                var fieldType = $"System.Collections.Generic.IReadOnlyList<{elementType}>";
                fields.Add(new Models.CarrierField($"P{param.GlobalIndex}", fieldType, Models.FieldRole.Parameter));
                parameters.Add(new CarrierParameter(
                    globalIndex: param.GlobalIndex,
                    fieldName: $"P{param.GlobalIndex}",
                    fieldType: fieldType,
                    extractionCode: param.ValueExpression,
                    bindingCode: null,
                    isCollection: true));
            }
            else
            {
                var fieldType = NormalizeFieldType(param.ClrType);
                fields.Add(new Models.CarrierField($"P{param.GlobalIndex}", fieldType, Models.FieldRole.Parameter));
                parameters.Add(new CarrierParameter(
                    globalIndex: param.GlobalIndex,
                    fieldName: $"P{param.GlobalIndex}",
                    fieldType: fieldType,
                    extractionCode: param.ValueExpression,
                    bindingCode: null,
                    typeMappingClass: param.TypeMappingClass,
                    isSensitive: param.IsSensitive));
            }

            if (param.NeedsFieldInfoCache)
                staticFields.Add(new Models.CarrierStaticField($"F{param.GlobalIndex}", "FieldInfo?", param.GlobalIndex));
        }

        // Mask field for conditional clauses
        string? maskType = null;
        var maskBitCount = plan.ConditionalTerms.Count;
        if (maskBitCount > 0)
        {
            maskType = maskBitCount <= 8 ? "byte" : maskBitCount <= 16 ? "ushort" : "uint";
            fields.Add(new Models.CarrierField("Mask", maskType, Models.FieldRole.ClauseMask));
        }

        // Pagination fields — only add if the chain actually has the clause and it's not inlined
        if (plan.Pagination != null)
        {
            var hasLimit = plan.Pagination.LimitParamIndex != null || plan.Pagination.LiteralLimit != null;
            var hasOffset = plan.Pagination.OffsetParamIndex != null || plan.Pagination.LiteralOffset != null;
            if (hasLimit && plan.Pagination.LimitParamIndex != null)
                fields.Add(new Models.CarrierField("Limit", "int", Models.FieldRole.Limit));
            if (hasOffset && plan.Pagination.OffsetParamIndex != null)
                fields.Add(new Models.CarrierField("Offset", "int", Models.FieldRole.Offset));
        }

        // Timeout field (if chain contains WithTimeout clause)
        if (assembled.ClauseSites.Any(cs => cs.Bound.Raw.Kind == InterceptorKind.WithTimeout))
            fields.Add(new Models.CarrierField("Timeout", "TimeSpan?", Models.FieldRole.Timeout));

        // Entity field for insert/setPoco chains (NOT needed for SetAction — values are inlined in SQL)
        var hasSetPoco = assembled.ClauseSites.Any(cs =>
            cs.Bound.Raw.Kind == InterceptorKind.UpdateSetPoco);
        if (plan.Kind == QueryKind.Insert || hasSetPoco)
        {
            var entityType = InterceptorCodeGenerator.GetShortTypeName(assembled.EntityTypeName);
            fields.Add(new Models.CarrierField("Entity", entityType + "?", Models.FieldRole.Entity));
        }

        // BatchEntities field for batch insert chains
        if (plan.Kind == QueryKind.BatchInsert)
        {
            var entityType = InterceptorCodeGenerator.GetShortTypeName(assembled.EntityTypeName);
            fields.Add(new Models.CarrierField("BatchEntities", $"System.Collections.Generic.IEnumerable<{entityType}>?", Models.FieldRole.Entity));
        }

        return new CarrierPlan(
            isEligible: true,
            className: null, // Assigned during file grouping
            baseClassName: "", // Resolved by emitter
            fields: fields,
            staticFields: staticFields,
            parameters: parameters,
            maskType: maskType,
            maskBitCount: maskBitCount);
    }

    #endregion
}
