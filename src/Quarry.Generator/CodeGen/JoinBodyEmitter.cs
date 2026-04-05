using System.Collections.Generic;
using System.Linq;
using System.Text;
using Quarry.Generators.Generation;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Generators.Projection;
using Quarry.Generators.Sql;

namespace Quarry.Generators.CodeGen;

/// <summary>
/// Emits interceptor method bodies for join sites (Join, LeftJoin, RightJoin)
/// and joined builder methods (joined Where, OrderBy, Select).
/// </summary>
/// <remarks>
/// Ported from InterceptorCodeGenerator.Joins.cs.
/// </remarks>
internal static class JoinBodyEmitter
{
    /// <summary>
    /// Emits a Join/LeftJoin/RightJoin interceptor body.
    /// Appends ON clause and returns joined builder type.
    /// </summary>
    public static void EmitJoin(
        StringBuilder sb,
        TranslatedCallSite site,
        string methodName,
        AssembledPlan? prebuiltChain = null,
        bool isFirstInChain = false,
        CarrierPlan? carrier = null)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);
        var clauseInfo = site.Clause;
        var joinedEntityTypeNames = site.JoinedEntityTypeNames;

        var thisType = site.BuilderTypeName;

        // Determine if this is a chained join (from JoinedQueryBuilder/3)
        var isChainedJoin = joinedEntityTypeNames != null && joinedEntityTypeNames.Count >= 2;

        var isCrossJoin = site.Kind == InterceptorKind.CrossJoin;

        if (isChainedJoin && site.JoinedEntityTypeName != null)
        {
            var joinedType = InterceptorCodeGenerator.GetShortTypeName(site.JoinedEntityTypeName);
            var priorTypes = joinedEntityTypeNames!.Select(InterceptorCodeGenerator.GetShortTypeName).ToArray();
            var allTypes = priorTypes.Concat(new[] { joinedType }).ToArray();

            // Determine receiver and return builder type names
            var receiverBuilderName = InterceptorCodeGenerator.GetJoinedBuilderTypeName(priorTypes.Length);
            var returnBuilderName = InterceptorCodeGenerator.GetJoinedBuilderTypeName(allTypes.Length);
            var receiverTypeArgs = string.Join(", ", priorTypes);
            var returnTypeArgs = string.Join(", ", allTypes);
            var funcTypeArgs = string.Join(", ", allTypes) + ", bool";

            if (prebuiltChain != null && clauseInfo != null && clauseInfo.IsSuccess)
            {
                // Prebuilt path: AsJoined<T>() — type conversion only, no state mutation
                sb.AppendLine($"    public static {returnBuilderName}<{returnTypeArgs}> {methodName}(");
                sb.AppendLine($"        this {receiverBuilderName}<{receiverTypeArgs}> builder{(isCrossJoin ? ")" : ",")}");
                if (!isCrossJoin)
                    sb.AppendLine($"        Func<{funcTypeArgs}> _)");
                sb.AppendLine($"    {{");

                // Compute carrier site params
                var (siteParams, globalParamOffset) = TerminalEmitHelpers.ResolveSiteParams(prebuiltChain, site.UniqueId);

                var joinReturnType = $"{returnBuilderName}<{returnTypeArgs}>";
                if (isFirstInChain)
                {
                    // For chained join first-in-chain, the incoming builder is the pre-join type
                    var preJoinBuilderType = CarrierEmitter.GetJoinedConcreteBuilderTypeName(priorTypes.Length, priorTypes);
                    CarrierEmitter.EmitCarrierChainEntry(sb, carrier!, prebuiltChain, site, preJoinBuilderType, joinReturnType, null, siteParams, globalParamOffset);
                }
                else
                {
                    // Join noops need Unsafe.As since the return type differs from receiver
                    sb.AppendLine($"        return Unsafe.As<{joinReturnType}>(builder);");
                }
                sb.AppendLine($"    }}");
            }
        }
        else if (prebuiltChain != null && clauseInfo != null && clauseInfo.IsSuccess)
        {
            // Prebuilt path: AsJoined<T>() — type conversion only, no state mutation
            var joinedEntityName = InterceptorCodeGenerator.GetShortTypeName(site.JoinedEntityTypeName ?? site.EntityTypeName);

            if (isCrossJoin)
            {
                sb.AppendLine($"    public static IJoinedQueryBuilder<{entityType}, {joinedEntityName}> {methodName}(");
                sb.AppendLine($"        this {thisType}<{entityType}> builder)");
            }
            else if (site.IsNavigationJoin)
            {
                sb.AppendLine($"    public static IJoinedQueryBuilder<{entityType}, {joinedEntityName}> {methodName}(");
                sb.AppendLine($"        this {thisType}<{entityType}> builder,");
                sb.AppendLine($"        Func<{entityType}, NavigationList<{joinedEntityName}>> _)");
            }
            else
            {
                sb.AppendLine($"    public static IJoinedQueryBuilder<{entityType}, {joinedEntityName}> {methodName}(");
                sb.AppendLine($"        this {thisType}<{entityType}> builder,");
                sb.AppendLine($"        Func<{entityType}, {joinedEntityName}, bool> _)");
            }
            sb.AppendLine($"    {{");

            // Compute carrier site params
            var (siteParams, globalParamOffset) = TerminalEmitHelpers.ResolveSiteParams(prebuiltChain, site.UniqueId);

            var joinReturnType = $"IJoinedQueryBuilder<{entityType}, {joinedEntityName}>";
            if (isFirstInChain)
            {
                // For first join, the incoming builder is the carrier (from ChainRoot)
                CarrierEmitter.EmitCarrierChainEntry(sb, carrier!, prebuiltChain, site, carrier!.ClassName, joinReturnType, null, siteParams, globalParamOffset);
            }
            else
            {
                // Join noops need Unsafe.As since the return type differs from receiver
                sb.AppendLine($"        return Unsafe.As<{joinReturnType}>(builder);");
            }
            sb.AppendLine($"    }}");
        }
    }

    /// <summary>
    /// Emits a joined Where interceptor (multi-entity lambda resolution).
    /// </summary>
    public static void EmitJoinedWhere(
        StringBuilder sb,
        TranslatedCallSite site,
        string methodName,
        List<InterceptorCodeGenerator.CachedExtractorField>? methodFields,
        int? clauseBit = null,
        AssembledPlan? prebuiltChain = null,
        bool isFirstInChain = false,
        CarrierPlan? carrier = null)
    {
        var entityTypes = site.JoinedEntityTypeNames!.Select(InterceptorCodeGenerator.GetShortTypeName).ToArray();
        var builderName = InterceptorCodeGenerator.GetJoinedBuilderTypeName(entityTypes.Length);
        var thisBuilderName = builderName;
        var typeArgs = string.Join(", ", entityTypes);
        var clauseInfo = site.Clause;
        var hasResolvableCapturedParams = clauseInfo?.Parameters.Any(p => p.IsCaptured && p.CanGenerateDirectPath) == true;
        var funcParamName = hasResolvableCapturedParams ? "func" : "_";

        methodFields ??= new List<InterceptorCodeGenerator.CachedExtractorField>();
        if (methodFields.Count > 0)
        {
            sb.AppendLine($"    [UnconditionalSuppressMessage(\"Trimming\", \"IL2075\",");
            sb.AppendLine($"        Justification = \"Closure field access via UnsafeAccessor is AOT-safe.\")]");
        }

        // Check if this is on a projected builder (has TResult)
        if (site.ResultTypeName != null)
        {
            var resultType = InterceptorCodeGenerator.SanitizeTupleResultType(InterceptorCodeGenerator.GetShortTypeName(site.ResultTypeName));
            var isBrokenTuple = InterceptorCodeGenerator.IsBrokenTupleType(resultType);

            if (isBrokenTuple)
            {
                // Tuple element types could not be resolved by the semantic model (generated entity types).
                // Use arity-matching generic parameters so the compiler infers the concrete TResult.
                var allTypeParams = string.Join(", ", Enumerable.Range(1, entityTypes.Length).Select(i => $"T{i}"));
                var constraints = string.Join(" ", Enumerable.Range(1, entityTypes.Length).Select(i => $"where T{i} : class"));
                sb.AppendLine($"    public static {builderName}<{allTypeParams}, TResult> {methodName}<{allTypeParams}, TResult>(");
                sb.AppendLine($"        this {thisBuilderName}<{allTypeParams}, TResult> builder,");
                sb.AppendLine($"        Func<{allTypeParams}, bool> {funcParamName}) {constraints}");
            }
            else
            {
                sb.AppendLine($"    public static {builderName}<{typeArgs}, {resultType}> {methodName}(");
                sb.AppendLine($"        this {thisBuilderName}<{typeArgs}, {resultType}> builder,");
                sb.AppendLine($"        Func<{typeArgs}, bool> {funcParamName})");
            }
        }
        else
        {
            sb.AppendLine($"    public static {builderName}<{typeArgs}> {methodName}(");
            sb.AppendLine($"        this {thisBuilderName}<{typeArgs}> builder,");
            sb.AppendLine($"        Func<{typeArgs}, bool> {funcParamName})");
        }

        sb.AppendLine($"    {{");

        // Carrier-optimized path: bypass concrete type cast entirely
        if (carrier != null && prebuiltChain != null)
        {
            // Compute carrier site params
            var (siteParams, globalParamOffset) = TerminalEmitHelpers.ResolveSiteParams(prebuiltChain, site.UniqueId);

            var joinedBuilderTypeName = CarrierEmitter.GetJoinedConcreteBuilderTypeName(entityTypes.Length, entityTypes);
            var returnInterface = site.ResultTypeName != null
                ? $"{builderName}<{typeArgs}, {InterceptorCodeGenerator.SanitizeTupleResultType(InterceptorCodeGenerator.GetShortTypeName(site.ResultTypeName))}>"
                : $"{builderName}<{typeArgs}>";

            if (isFirstInChain)
            {
                CarrierEmitter.EmitCarrierChainEntry(sb, carrier, prebuiltChain, site, joinedBuilderTypeName, returnInterface, clauseBit, siteParams, globalParamOffset);
            }
            else if (siteParams.Count > 0)
            {
                CarrierEmitter.EmitCarrierParamBind(sb, carrier, prebuiltChain, clauseBit, siteParams, globalParamOffset, site: site);
            }
            else
            {
                CarrierEmitter.EmitCarrierNoop(sb, carrier, prebuiltChain, clauseBit);
            }
            sb.AppendLine($"    }}");
        }
    }

    /// <summary>
    /// Emits a joined OrderBy interceptor.
    /// </summary>
    public static void EmitJoinedOrderBy(
        StringBuilder sb,
        TranslatedCallSite site,
        string methodName,
        int? clauseBit = null,
        AssembledPlan? prebuiltChain = null,
        bool isFirstInChain = false,
        CarrierPlan? carrier = null)
    {
        var entityTypes = site.JoinedEntityTypeNames!.Select(InterceptorCodeGenerator.GetShortTypeName).ToArray();
        var builderName = InterceptorCodeGenerator.GetJoinedBuilderTypeName(entityTypes.Length);
        var thisBuilderName = builderName;
        var typeArgs = string.Join(", ", entityTypes);

        // Use concrete key type (arity 0) when available
        var keyType = site.KeyTypeName != null ? InterceptorCodeGenerator.GetShortTypeName(site.KeyTypeName) : null;

        if (site.ResultTypeName != null)
        {
            var resultType = InterceptorCodeGenerator.SanitizeTupleResultType(InterceptorCodeGenerator.GetShortTypeName(site.ResultTypeName));
            var isBrokenTuple = InterceptorCodeGenerator.IsBrokenTupleType(resultType);

            // Broken tuple result types cannot use concrete arity-0 signatures;
            // fall back to full arity-matching with TKey to preserve interceptor arity.
            if (isBrokenTuple)
                keyType = null;

            if (keyType != null)
            {
                sb.AppendLine($"    public static {builderName}<{typeArgs}, {resultType}> {methodName}(");
                sb.AppendLine($"        this {thisBuilderName}<{typeArgs}, {resultType}> builder,");
                sb.AppendLine($"        Func<{typeArgs}, {keyType}> _,");
                sb.AppendLine($"        Direction direction = Direction.Ascending)");
            }
            else
            {
                // Arity-matching: include all type params with class constraints.
                // Also used when tuple result type has unresolved element types (broken tuple).
                var allTypeParams = string.Join(", ", Enumerable.Range(1, entityTypes.Length).Select(i => $"T{i}"));
                var constraints = string.Join(" ", Enumerable.Range(1, entityTypes.Length).Select(i => $"where T{i} : class"));
                sb.AppendLine($"    public static {builderName}<{allTypeParams}, TResult> {methodName}<{allTypeParams}, TResult, TKey>(");
                sb.AppendLine($"        this {thisBuilderName}<{allTypeParams}, TResult> builder,");
                sb.AppendLine($"        Func<{allTypeParams}, TKey> _,");
                sb.AppendLine($"        Direction direction = Direction.Ascending) {constraints}");
            }
        }
        else
        {
            if (keyType != null)
            {
                sb.AppendLine($"    public static {builderName}<{typeArgs}> {methodName}(");
                sb.AppendLine($"        this {thisBuilderName}<{typeArgs}> builder,");
                sb.AppendLine($"        Func<{typeArgs}, {keyType}> _,");
                sb.AppendLine($"        Direction direction = Direction.Ascending)");
            }
            else
            {
                var allTypeParams = string.Join(", ", Enumerable.Range(1, entityTypes.Length).Select(i => $"T{i}"));
                var constraints = string.Join(" ", Enumerable.Range(1, entityTypes.Length).Select(i => $"where T{i} : class"));
                sb.AppendLine($"    public static {builderName}<{allTypeParams}> {methodName}<{allTypeParams}, TKey>(");
                sb.AppendLine($"        this {thisBuilderName}<{allTypeParams}> builder,");
                sb.AppendLine($"        Func<{allTypeParams}, TKey> _,");
                sb.AppendLine($"        Direction direction = Direction.Ascending) {constraints}");
            }
        }

        sb.AppendLine($"    {{");

        // Carrier-optimized path: bypass concrete type cast entirely
        if (carrier != null && prebuiltChain != null && keyType == null)
        {
            // Generic key type — emit passthrough with clause mask bit set
            sb.AppendLine($"        var __c = Unsafe.As<{carrier.ClassName}>(builder);");
            if (clauseBit.HasValue)
                sb.AppendLine($"        __c.Mask |= unchecked(({carrier.MaskType})(1 << {clauseBit.Value}));");
            var allTypeP = string.Join(", ", Enumerable.Range(1, entityTypes.Length).Select(i => $"T{i}"));
            var castTarget = site.ResultTypeName != null ? $"{builderName}<{allTypeP}, TResult>" : $"{builderName}<{allTypeP}>";
            sb.AppendLine($"        return Unsafe.As<{castTarget}>(builder);");
            sb.AppendLine($"    }}");
            return;
        }
        if (carrier != null && prebuiltChain != null && keyType != null)
        {
            // Compute carrier site params
            var (siteParams, globalParamOffset) = TerminalEmitHelpers.ResolveSiteParams(prebuiltChain, site.UniqueId);

            var joinedBuilderTypeName = CarrierEmitter.GetJoinedConcreteBuilderTypeName(entityTypes.Length, entityTypes);
            var returnInterface = site.ResultTypeName != null
                ? $"{builderName}<{typeArgs}, {InterceptorCodeGenerator.SanitizeTupleResultType(InterceptorCodeGenerator.GetShortTypeName(site.ResultTypeName))}>"
                : $"{builderName}<{typeArgs}>";

            if (isFirstInChain)
            {
                CarrierEmitter.EmitCarrierChainEntry(sb, carrier, prebuiltChain, site, joinedBuilderTypeName, returnInterface, clauseBit, siteParams, globalParamOffset);
            }
            else if (siteParams.Count > 0)
            {
                CarrierEmitter.EmitCarrierParamBind(sb, carrier, prebuiltChain, clauseBit, siteParams, globalParamOffset, site: site);
            }
            else
            {
                CarrierEmitter.EmitCarrierNoop(sb, carrier, prebuiltChain, clauseBit);
            }
            sb.AppendLine($"    }}");
        }
    }

    /// <summary>
    /// Emits a joined Select interceptor (multi-entity projection).
    /// </summary>
    public static void EmitJoinedSelect(
        StringBuilder sb,
        TranslatedCallSite site,
        string methodName,
        AssembledPlan? prebuiltChain = null,
        bool isFirstInChain = false,
        CarrierPlan? carrier = null)
    {
        var entityTypes = site.JoinedEntityTypeNames!.Select(InterceptorCodeGenerator.GetShortTypeName).ToArray();
        var builderName = InterceptorCodeGenerator.GetJoinedBuilderTypeName(entityTypes.Length);
        var thisBuilderName = builderName;
        var typeArgs = string.Join(", ", entityTypes);
        // Prefer chain's enriched ProjectionInfo over site's discovery-time projection
        var projection = (prebuiltChain?.ProjectionInfo != null && prebuiltChain.ProjectionInfo.Columns.Count > 0)
            ? prebuiltChain.ProjectionInfo
            : site.ProjectionInfo;

        // Simplified prebuilt chain path: AsProjected instead of AddSelectClause
        if (prebuiltChain != null && projection != null && projection.IsOptimalPath && projection.Columns.Count > 0 && projection.ResultTypeName != "?")
        {
            var resultType = InterceptorCodeGenerator.GetShortTypeName(projection.ResultTypeName);
            sb.AppendLine($"    public static {builderName}<{typeArgs}, {resultType}> {methodName}(");
            sb.AppendLine($"        this {thisBuilderName}<{typeArgs}> builder,");
            sb.AppendLine($"        Func<{typeArgs}, {resultType}> _)");
            sb.AppendLine($"    {{");

            if (carrier != null)
            {
                var targetInterface = $"{builderName}<{typeArgs}, {resultType}>";
                if (isFirstInChain)
                {
                    var (siteParams, globalParamOffset) = TerminalEmitHelpers.ResolveSiteParams(prebuiltChain, site.UniqueId);
                    var joinedBuilderType = CarrierEmitter.GetJoinedConcreteBuilderTypeName(entityTypes.Length, entityTypes);
                    int? clauseBit = null;
                    CarrierEmitter.EmitCarrierChainEntry(sb, carrier, prebuiltChain, site, joinedBuilderType, targetInterface, clauseBit, siteParams, globalParamOffset);
                }
                else
                {
                    CarrierEmitter.EmitCarrierSelect(sb, targetInterface);
                }
                sb.AppendLine($"    }}");
            }
        }
    }
}
