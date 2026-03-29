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
    internal static string NormalizeFieldType(string typeName)
    {
        // Guard: unresolved types from the semantic model (error types display as "?")
        if (typeName == "?" || typeName == "object")
            return "object?";

        // Normalize Nullable<T> → T?
        if (typeName.StartsWith("System.Nullable<") || typeName.StartsWith("Nullable<"))
        {
            var inner = typeName.Substring(typeName.IndexOf('<') + 1).TrimEnd('>');
            return inner + "?";
        }

        // Already nullable — nothing to do
        if (typeName.EndsWith("?"))
            return typeName;

        // Known value types by short name (int, DateTime, Guid, etc.)
        if (ValueTypes.Contains(typeName))
            return typeName;

        // Qualified value types (e.g. System.Int32, System.DateTime)
        if (typeName.Contains('.'))
        {
            var unqualified = typeName.Substring(typeName.LastIndexOf('.') + 1);
            if (ValueTypes.Contains(unqualified))
                return typeName;
        }

        // Everything remaining is a reference type: arrays (byte[]),
        // generics (IReadOnlyList<T>), qualified names (System.String),
        // and simple names (string) — append ? for nullable context.
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

    /// <summary>
    /// Determines whether a CLR type name represents a reference type.
    /// </summary>
    internal static bool IsReferenceTypeName(string typeName)
    {
        var baseName = typeName.EndsWith("?") ? typeName.Substring(0, typeName.Length - 1) : typeName;

        if (baseName.StartsWith("Nullable<") || baseName.StartsWith("System.Nullable<"))
            return false;

        if (baseName.EndsWith("[]"))
            return true;

        if (ValueTypes.Contains(baseName))
            return false;

        // Qualified value types (e.g. System.Int32)
        if (baseName.Contains('.'))
        {
            var unqualified = baseName.Substring(baseName.LastIndexOf('.') + 1);
            if (ValueTypes.Contains(unqualified))
                return false;
        }

        return true;
    }

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

        // Gate: Empty SQL variants
        if (assembled.SqlVariants.Count == 0)
            return CarrierPlan.Ineligible("no SQL variants");

        // Gate: Malformed SQL
        foreach (var kvp in assembled.SqlVariants)
        {
            if (string.IsNullOrWhiteSpace(kvp.Value.Sql))
                return CarrierPlan.Ineligible("empty SQL variant");
        }

        // Build a mapping from parameter GlobalIndex → (CaptureKind, DisplayClassName, CapturedVariableTypes)
        // by walking clause sites. Each clause site owns a contiguous range of global indices.
        var displayClassByParam = new Dictionary<int, (CaptureKind CaptureKind, string? DisplayClassName, System.Collections.Generic.IReadOnlyDictionary<string, string>? VarTypes)>();
        foreach (var cs in assembled.ClauseSites)
        {
            var clause = cs.Clause;
            if (clause == null) continue;
            foreach (var p in clause.Parameters)
            {
                if (p.IsCaptured && cs.DisplayClassName != null)
                {
                    // Find the global index for this clause-local parameter
                    // by matching via the plan.Parameters list
                    foreach (var gp in plan.Parameters)
                    {
                        if (gp.CapturedFieldName == p.CapturedFieldName
                            && gp.IsCaptured
                            && !displayClassByParam.ContainsKey(gp.GlobalIndex))
                        {
                            displayClassByParam[gp.GlobalIndex] = (cs.CaptureKind, cs.DisplayClassName, cs.CapturedVariableTypes);
                            break;
                        }
                    }
                }
            }
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
                fields.Add(new Models.CarrierField($"P{param.GlobalIndex}", fieldType, Models.FieldRole.Parameter, isReferenceType: true));
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
                // When param.ClrType is unresolved ("?" or "object"), try to use
                // the resolved type from CapturedVariableTypes if available.
                var effectiveClrType = param.ClrType;
                if ((effectiveClrType == "?" || effectiveClrType == "object")
                    && param.CapturedFieldName != null
                    && displayClassByParam.TryGetValue(param.GlobalIndex, out var dcTypeHint)
                    && dcTypeHint.VarTypes != null
                    && dcTypeHint.VarTypes.TryGetValue(param.CapturedFieldName, out var hintType)
                    && hintType != "object" && hintType != "?")
                {
                    effectiveClrType = hintType;
                }

                var fieldType = NormalizeFieldType(effectiveClrType);
                fields.Add(new Models.CarrierField($"P{param.GlobalIndex}", fieldType, Models.FieldRole.Parameter, isReferenceType: IsReferenceTypeName(effectiveClrType)));
                parameters.Add(new CarrierParameter(
                    globalIndex: param.GlobalIndex,
                    fieldName: $"P{param.GlobalIndex}",
                    fieldType: fieldType,
                    extractionCode: param.ValueExpression,
                    bindingCode: null,
                    typeMappingClass: param.TypeMappingClass,
                    isSensitive: param.IsSensitive));
            }

            if (param.NeedsUnsafeAccessor && param.CapturedFieldName != null
                && displayClassByParam.TryGetValue(param.GlobalIndex, out var dcInfo)
                && dcInfo.DisplayClassName != null)
            {
                switch (dcInfo.CaptureKind)
                {
                    case CaptureKind.ClosureCapture:
                    {
                        // Closure-captured local/parameter — use UnsafeAccessorKind.Field
                        // with the display class as the target type.
                        string capturedType;
                        if (dcInfo.VarTypes != null
                            && dcInfo.VarTypes.TryGetValue(param.CapturedFieldName, out var resolvedType))
                        {
                            capturedType = resolvedType;
                        }
                        else
                        {
                            capturedType = param.CapturedFieldType ?? param.ClrType;
                            if (capturedType == "?" || string.IsNullOrWhiteSpace(capturedType))
                                capturedType = "object";
                        }

                        staticFields.Add(new Models.CarrierStaticField(
                            $"__ExtractP{param.GlobalIndex}", capturedType, param.GlobalIndex,
                            displayClassName: dcInfo.DisplayClassName,
                            capturedFieldName: param.CapturedFieldName,
                            capturedFieldType: capturedType));
                        break;
                    }
                    case CaptureKind.FieldCapture:
                    {
                        // Static or instance field on the containing class — strip the
                        // display class suffix and use the per-parameter IsStaticCapture
                        // flag to select UnsafeAccessorKind.StaticField vs .Field.
                        var capturedType = param.CapturedFieldType ?? param.ClrType;
                        if (capturedType == "?" || string.IsNullOrWhiteSpace(capturedType))
                            capturedType = "object";

                        var containingType = dcInfo.DisplayClassName;
                        var displayClassMarker = containingType.IndexOf("+<>c__DisplayClass");
                        if (displayClassMarker > 0)
                            containingType = containingType.Substring(0, displayClassMarker);

                        staticFields.Add(new Models.CarrierStaticField(
                            $"__ExtractP{param.GlobalIndex}", capturedType, param.GlobalIndex,
                            displayClassName: containingType,
                            capturedFieldName: param.CapturedFieldName,
                            capturedFieldType: capturedType,
                            isStaticField: param.IsStaticCapture));
                        break;
                    }
                    // CaptureKind.None: no UnsafeAccessor needed
                }
            }
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
            fields.Add(new Models.CarrierField("Entity", entityType + "?", Models.FieldRole.Entity, isReferenceType: true));
        }

        // BatchEntities field for batch insert chains
        if (plan.Kind == QueryKind.BatchInsert)
        {
            var entityType = InterceptorCodeGenerator.GetShortTypeName(assembled.EntityTypeName);
            fields.Add(new Models.CarrierField("BatchEntities", $"System.Collections.Generic.IEnumerable<{entityType}>?", Models.FieldRole.Entity, isReferenceType: true));
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
