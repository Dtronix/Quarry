using System;
using System.Collections.Generic;
using System.Linq;
using Quarry.Generators.Generation;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;
using Quarry.Generators.Translation;
using Quarry.Generators.Utilities;

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

        // Known value types keep their type name as-is
        if (TypeClassification.IsValueType(typeName))
            return typeName;

        // Everything remaining is a reference type: arrays (byte[]),
        // generics (IReadOnlyList<T>), qualified names (System.String),
        // and simple names (string) — append ? for nullable context.
        return typeName + "?";
    }

    /// <summary>
    /// Determines whether a CLR type name represents a reference type.
    /// </summary>
    internal static bool IsReferenceTypeName(string typeName)
        => TypeClassification.IsReferenceType(typeName);

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
            // Determine the clause's parameters: regular clauses use cs.Clause.Parameters,
            // UpdateSetAction uses raw.SetActionParameters (Action<T> can't be parsed to SqlExpr)
            IReadOnlyList<ParameterInfo>? clauseLocalParams = null;
            if (cs.Clause != null)
                clauseLocalParams = cs.Clause.Parameters;
            else if (cs.Kind == InterceptorKind.UpdateSetAction)
                clauseLocalParams = cs.Bound.Raw.SetActionParameters;

            if (clauseLocalParams == null) continue;

            foreach (var p in clauseLocalParams)
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
                string fieldType;
                if (param.IsEnumerableCollection)
                    fieldType = $"System.Collections.Generic.IEnumerable<{elementType}>";
                else
                    fieldType = $"System.Collections.Generic.IReadOnlyList<{elementType}>";
                fields.Add(new Models.CarrierField($"P{param.GlobalIndex}", fieldType, Models.FieldRole.Parameter, isReferenceType: true));
                parameters.Add(new CarrierParameter(
                    globalIndex: param.GlobalIndex,
                    fieldName: $"P{param.GlobalIndex}",
                    fieldType: fieldType,
                    extractionCode: param.ValueExpression,
                    bindingCode: null,
                    isCollection: true,
                    isEnumerableCollection: param.IsEnumerableCollection));
            }
            else
            {
                // When param.ClrType is unresolved ("?" or "object"), try to use
                // the resolved type from CapturedVariableTypes if available.
                // Only apply the hint for simple captures where the variable IS the
                // value (ValueExpression == CapturedFieldName). For property chains
                // like source.UserName, the variable type (User) differs from the
                // expression type (string), so the hint would be wrong.
                var effectiveClrType = param.ClrType;
                if ((effectiveClrType == "?" || effectiveClrType == "object")
                    && param.CapturedFieldName != null
                    && param.ValueExpression == param.CapturedFieldName
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
        }

        // Build per-clause extraction plans for captured variable extraction
        var extractionPlans = BuildExtractionPlans(assembled);

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
            parameters: parameters,
            maskType: maskType,
            maskBitCount: maskBitCount,
            extractionPlans: extractionPlans);
    }

    /// <summary>
    /// Builds per-clause extraction plans for captured variable extraction via [UnsafeAccessor].
    /// Each plan covers a single clause and contains per-variable extractors.
    /// </summary>
    private static List<Models.ClauseExtractionPlan> BuildExtractionPlans(
        AssembledPlan assembled)
    {
        var plans = new List<Models.ClauseExtractionPlan>();
        var clauseIndex = 0;

        var setOpIndex = 0;

        foreach (var cs in assembled.ClauseSites)
        {
            // Lambda-form CTE definition: extract inner chain params via display class
            if (cs.Kind == InterceptorKind.CteDefinition
                && cs.Bound.Raw.LambdaInnerSpanStart.HasValue
                && cs.DisplayClassName != null)
            {
                BuildLambdaInnerExtractionPlan(plans, cs, assembled.Plan.CteDefinitions,
                    "innerBuilder", ref clauseIndex);
                continue;
            }

            // Lambda-form set operation: extract operand params via display class
            if (Parsing.ChainAnalyzer.IsSetOperationKind(cs.Kind))
            {
                if (cs.Bound.Raw.LambdaInnerSpanStart.HasValue
                    && cs.DisplayClassName != null
                    && setOpIndex < assembled.Plan.SetOperations.Count)
                {
                    var setOp = assembled.Plan.SetOperations[setOpIndex];
                    BuildLambdaInnerExtractionPlanFromParams(plans, cs, setOp.Operand.Parameters,
                        "other", ref clauseIndex);
                }
                setOpIndex++;
                continue;
            }

            // Determine which parameters belong to this clause
            IReadOnlyList<Translation.ParameterInfo>? clauseParams = null;
            string delegateParamName;

            if (cs.Kind == InterceptorKind.UpdateSetAction)
            {
                clauseParams = cs.Clause?.Parameters ?? cs.Bound.Raw.SetActionParameters;
                var hasCaptured = clauseParams?.Any(p => p.IsCaptured) == true;
                delegateParamName = hasCaptured ? "action" : "_";
            }
            else if (cs.Clause?.Parameters.Count > 0)
            {
                clauseParams = cs.Clause.Parameters;
                delegateParamName = "func";
            }
            else
            {
                continue;
            }

            if (clauseParams == null || clauseParams.Count == 0)
                continue;

            // Collect unique captured variables for this clause
            var seenVariables = new Dictionary<string, Models.CapturedVariableExtractor>();

            foreach (var p in clauseParams)
            {
                if (!p.IsCaptured || p.CapturedFieldName == null)
                    continue;

                if (seenVariables.ContainsKey(p.CapturedFieldName))
                    continue;

                // Resolve display class info from the clause site
                var captureKind = cs.CaptureKind;
                var displayClassName = cs.DisplayClassName;

                if (captureKind == CaptureKind.None || displayClassName == null)
                    continue;

                // Resolve the variable type from CapturedVariableTypes or fallback to param metadata
                string variableType;
                if (cs.CapturedVariableTypes != null
                    && cs.CapturedVariableTypes.TryGetValue(p.CapturedFieldName, out var resolvedType)
                    && resolvedType != "object" && resolvedType != "?")
                {
                    variableType = resolvedType;
                }
                else
                {
                    variableType = p.CapturedFieldType ?? p.ClrType;
                    if (variableType == "?" || string.IsNullOrWhiteSpace(variableType))
                        variableType = "object";
                }

                // For FieldCapture, strip the display class suffix to get the containing type
                var effectiveDisplayClass = displayClassName;
                var isStaticField = p.IsStaticCapture;
                if (captureKind == CaptureKind.FieldCapture)
                {
                    var marker = displayClassName.IndexOf("+<>c__DisplayClass");
                    if (marker > 0)
                        effectiveDisplayClass = displayClassName.Substring(0, marker);
                }

                var methodName = $"__ExtractVar_{p.CapturedFieldName}_{clauseIndex}";
                seenVariables[p.CapturedFieldName] = new Models.CapturedVariableExtractor(
                    methodName,
                    p.CapturedFieldName,
                    variableType,
                    effectiveDisplayClass,
                    captureKind,
                    isStaticField);
            }

            // For UpdateSetAction: also create extractors from clause-level captured identifiers
            // that aren't covered by any parameter's CapturedFieldName (computed expressions like a + b)
            if (cs.Kind == InterceptorKind.UpdateSetAction
                && cs.SetActionAllCapturedIdentifiers != null)
            {
                foreach (var kvp in cs.SetActionAllCapturedIdentifiers)
                {
                    var varName = kvp.Key;
                    var (varType, isStatic, containingClass) = kvp.Value;

                    if (seenVariables.ContainsKey(varName))
                        continue;

                    var captureKind = cs.CaptureKind;
                    var displayClassName = cs.DisplayClassName;

                    if (displayClassName == null)
                        continue;

                    // For field captures, use the containing class from Phase 1
                    string effectiveDisplayClass;
                    CaptureKind effectiveCaptureKind;
                    if (isStatic || containingClass != null)
                    {
                        effectiveDisplayClass = containingClass ?? displayClassName;
                        effectiveCaptureKind = CaptureKind.FieldCapture;
                    }
                    else
                    {
                        // Closure-captured local — use the display class
                        effectiveDisplayClass = displayClassName;
                        effectiveCaptureKind = captureKind != CaptureKind.None ? captureKind : CaptureKind.ClosureCapture;
                    }

                    var methodName = $"__ExtractVar_{varName}_{clauseIndex}";
                    seenVariables[varName] = new Models.CapturedVariableExtractor(
                        methodName,
                        varName,
                        varType,
                        effectiveDisplayClass,
                        effectiveCaptureKind,
                        isStatic);
                }
            }

            if (seenVariables.Count > 0)
            {
                plans.Add(new Models.ClauseExtractionPlan(
                    cs.UniqueId,
                    delegateParamName,
                    new List<Models.CapturedVariableExtractor>(seenVariables.Values)));
            }

            clauseIndex++;
        }

        return plans;
    }

    /// <summary>
    /// Builds an extraction plan for a lambda-form CTE definition site.
    /// Matches the site to its CteDef by name and creates extractors for captured inner parameters.
    /// </summary>
    private static void BuildLambdaInnerExtractionPlan(
        List<Models.ClauseExtractionPlan> plans,
        TranslatedCallSite cs,
        IReadOnlyList<IR.CteDef> cteDefinitions,
        string delegateParamName,
        ref int clauseIndex)
    {
        var cteName = IR.CteNameHelpers.ExtractShortName(
            cs.Bound.Raw.CteEntityTypeName ?? cs.EntityTypeName);
        IR.CteDef? matchingCteDef = null;
        foreach (var cte in cteDefinitions)
        {
            if (cte.Name == cteName) { matchingCteDef = cte; break; }
        }

        if (matchingCteDef != null && matchingCteDef.InnerParameters.Count > 0)
        {
            BuildLambdaInnerExtractionPlanFromParams(plans, cs,
                matchingCteDef.InnerParameters, delegateParamName, ref clauseIndex);
        }
        else
        {
            clauseIndex++;
        }
    }

    /// <summary>
    /// Builds an extraction plan for a lambda-form inner chain (CTE or set-op).
    /// Creates extractors for captured parameters using the site's display class info.
    /// </summary>
    private static void BuildLambdaInnerExtractionPlanFromParams(
        List<Models.ClauseExtractionPlan> plans,
        TranslatedCallSite cs,
        IReadOnlyList<IR.QueryParameter> innerParams,
        string delegateParamName,
        ref int clauseIndex)
    {
        var seenVariables = new Dictionary<string, Models.CapturedVariableExtractor>();

        foreach (var p in innerParams)
        {
            if (!p.IsCaptured || p.CapturedFieldName == null)
                continue;
            if (seenVariables.ContainsKey(p.CapturedFieldName))
                continue;

            var captureKind = cs.CaptureKind;
            var displayClassName = cs.DisplayClassName!;

            // Resolve variable type from site's CapturedVariableTypes or fallback to param metadata
            string variableType;
            if (cs.CapturedVariableTypes != null
                && cs.CapturedVariableTypes.TryGetValue(p.CapturedFieldName, out var resolvedType)
                && resolvedType != "object" && resolvedType != "?")
            {
                variableType = resolvedType;
            }
            else
            {
                variableType = p.CapturedFieldType ?? p.ClrType;
                if (variableType == "?" || string.IsNullOrWhiteSpace(variableType))
                    variableType = "object";
            }

            // For FieldCapture, strip the display class suffix to get the containing type
            var effectiveDisplayClass = displayClassName;
            var isStaticField = p.IsStaticCapture;
            if (captureKind == CaptureKind.FieldCapture)
            {
                var marker = displayClassName.IndexOf("+<>c__DisplayClass");
                if (marker > 0)
                    effectiveDisplayClass = displayClassName.Substring(0, marker);
            }

            var methodName = $"__ExtractVar_{p.CapturedFieldName}_{clauseIndex}";
            seenVariables[p.CapturedFieldName] = new Models.CapturedVariableExtractor(
                methodName, p.CapturedFieldName, variableType,
                effectiveDisplayClass, captureKind, isStaticField);
        }

        if (seenVariables.Count > 0)
        {
            plans.Add(new Models.ClauseExtractionPlan(
                cs.UniqueId, delegateParamName,
                new List<Models.CapturedVariableExtractor>(seenVariables.Values)));
        }

        clauseIndex++;
    }

    /// <summary>
    /// Maps an InterceptorKind to the delegate parameter name used in the interceptor method signature.
    /// </summary>
    internal static string GetDelegateParamName(InterceptorKind kind)
    {
        return kind == InterceptorKind.UpdateSetAction ? "action" : "func";
    }

    #endregion
}
