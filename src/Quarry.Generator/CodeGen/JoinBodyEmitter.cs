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
    /// Gets the builder type name for a given entity count in joins.
    /// </summary>
    private static string GetJoinedBuilderTypeName(int entityCount)
    {
        return entityCount switch
        {
            2 => "IJoinedQueryBuilder",
            3 => "IJoinedQueryBuilder3",
            4 => "IJoinedQueryBuilder4",
            _ => throw new System.ArgumentOutOfRangeException(nameof(entityCount), $"Unsupported entity count: {entityCount}")
        };
    }

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

        var joinKind = site.Kind switch
        {
            InterceptorKind.LeftJoin => "JoinKind.Left",
            InterceptorKind.RightJoin => "JoinKind.Right",
            _ => "JoinKind.Inner"
        };

        var thisType = site.BuilderTypeName;
        var returnType = InterceptorCodeGenerator.ToReturnTypeName(thisType);
        var concreteThisType = InterceptorCodeGenerator.ToConcreteTypeName(returnType);
        // When receiver is IEntityAccessor, use CreateQueryBuilder() to get a real QueryBuilder
        var joinBuilderExpr = InterceptorCodeGenerator.IsEntityAccessorType(thisType) ? InterceptorCodeGenerator.EntityAccessorToQueryBuilder(entityType) : "builder";

        // Determine if this is a chained join (from JoinedQueryBuilder/3)
        var isChainedJoin = joinedEntityTypeNames != null && joinedEntityTypeNames.Count >= 2;

        if (isChainedJoin && site.JoinedEntityTypeName != null)
        {
            var joinedType = InterceptorCodeGenerator.GetShortTypeName(site.JoinedEntityTypeName);
            var priorTypes = joinedEntityTypeNames!.Select(InterceptorCodeGenerator.GetShortTypeName).ToArray();
            var allTypes = priorTypes.Concat(new[] { joinedType }).ToArray();

            // Determine receiver and return builder type names
            var receiverBuilderName = GetJoinedBuilderTypeName(priorTypes.Length);
            var concreteReceiverBuilderName = InterceptorCodeGenerator.ToConcreteTypeName(receiverBuilderName);
            var returnBuilderName = GetJoinedBuilderTypeName(allTypes.Length);
            var receiverTypeArgs = string.Join(", ", priorTypes);
            var returnTypeArgs = string.Join(", ", allTypes);
            var funcTypeArgs = string.Join(", ", allTypes) + ", bool";

            if (prebuiltChain != null && clauseInfo != null && clauseInfo.IsSuccess)
            {
                // Prebuilt path: AsJoined<T>() — type conversion only, no state mutation
                sb.AppendLine($"    public static {returnBuilderName}<{returnTypeArgs}> {methodName}(");
                sb.AppendLine($"        this {receiverBuilderName}<{receiverTypeArgs}> builder,");
                sb.AppendLine($"        Expression<Func<{funcTypeArgs}>> _)");
                sb.AppendLine($"    {{");

                if (carrier != null)
                {
                    // Compute carrier site params
                    var siteParams = new List<QueryParameter>();
                    var globalParamOffset = 0;
                    foreach (var clause in prebuiltChain.GetClauseEntries())
                    {
                        if (clause.Site.UniqueId == site.UniqueId)
                        {
                            if (clause.Site.Clause != null)
                                for (int i = 0; i < clause.Site.Clause.Parameters.Count && globalParamOffset + i < prebuiltChain.ChainParameters.Count; i++)
                                    siteParams.Add(prebuiltChain.ChainParameters[globalParamOffset + i]);
                            break;
                        }
                        if (clause.Site.Clause != null)
                            globalParamOffset += clause.Site.Clause.Parameters.Count;
                    }

                    var joinReturnType = $"{returnBuilderName}<{returnTypeArgs}>";
                    if (isFirstInChain)
                    {
                        // For chained join first-in-chain, the incoming builder is the pre-join type
                        var preJoinBuilderType = CarrierEmitter.GetJoinedConcreteBuilderTypeName(priorTypes.Length, priorTypes);
                        CarrierEmitter.EmitCarrierChainEntry(sb, carrier, prebuiltChain, site, preJoinBuilderType, joinReturnType, null, siteParams, globalParamOffset);
                    }
                    else
                    {
                        // Join noops need Unsafe.As since the return type differs from receiver
                        sb.AppendLine($"        return Unsafe.As<{joinReturnType}>(builder);");
                    }
                    sb.AppendLine($"    }}");
                }
                else
                {
                sb.AppendLine($"        var __b = Unsafe.As<{concreteReceiverBuilderName}<{receiverTypeArgs}>>(builder);");
                if (isFirstInChain && prebuiltChain.MaxParameterCount > 0)
                    sb.AppendLine($"        __b.AllocatePrebuiltParams({prebuiltChain.MaxParameterCount});");
                sb.AppendLine($"        return __b.AsJoined<{joinedType}>();");
                sb.AppendLine($"    }}");
                }
            }
            else if (clauseInfo != null && clauseInfo.IsSuccess)
            {
                var escapedSql = InterceptorCodeGenerator.EscapeStringLiteral(clauseInfo.SqlFragment);
                sb.AppendLine($"    public static {returnBuilderName}<{returnTypeArgs}> {methodName}(");
                sb.AppendLine($"        this {receiverBuilderName}<{receiverTypeArgs}> builder,");
                sb.AppendLine($"        Expression<Func<{funcTypeArgs}>> _)");
                sb.AppendLine($"    {{");
                sb.AppendLine($"        var __b = Unsafe.As<{concreteReceiverBuilderName}<{receiverTypeArgs}>>(builder);");
                var escapedTableName = InterceptorCodeGenerator.EscapeStringLiteral(clauseInfo.JoinedTableName);
                sb.AppendLine($"        return __b.AddJoinClause<{joinedType}>({joinKind}, @\"{escapedTableName}\", @\"{escapedSql}\");");
                sb.AppendLine($"    }}");
            }
            else
            {
                var methodCall = site.Kind switch
                {
                    InterceptorKind.LeftJoin => "LeftJoin",
                    InterceptorKind.RightJoin => "RightJoin",
                    _ => "Join"
                };

                sb.AppendLine($"    public static {returnBuilderName}<{returnTypeArgs}> {methodName}(");
                sb.AppendLine($"        this {receiverBuilderName}<{receiverTypeArgs}> builder,");
                sb.AppendLine($"        Expression<Func<{funcTypeArgs}>> condition)");
                sb.AppendLine($"    {{");
                sb.AppendLine($"        var __b = Unsafe.As<{concreteReceiverBuilderName}<{receiverTypeArgs}>>(builder);");
                sb.AppendLine($"        // Fallback - join condition not fully analyzed at compile time");
                sb.AppendLine($"        return __b.{methodCall}(condition);");
                sb.AppendLine($"    }}");
            }
        }
        else if (prebuiltChain != null && clauseInfo != null && clauseInfo.IsSuccess)
        {
            // Prebuilt path: AsJoined<T>() — type conversion only, no state mutation
            var joinedEntityName = InterceptorCodeGenerator.GetShortTypeName(site.JoinedEntityTypeName ?? site.EntityTypeName);

            if (site.IsNavigationJoin)
            {
                sb.AppendLine($"    public static IJoinedQueryBuilder<{entityType}, {joinedEntityName}> {methodName}(");
                sb.AppendLine($"        this {thisType}<{entityType}> builder,");
                sb.AppendLine($"        Expression<Func<{entityType}, NavigationList<{joinedEntityName}>>> _)");
            }
            else
            {
                sb.AppendLine($"    public static IJoinedQueryBuilder<{entityType}, {joinedEntityName}> {methodName}(");
                sb.AppendLine($"        this {thisType}<{entityType}> builder,");
                sb.AppendLine($"        Expression<Func<{entityType}, {joinedEntityName}, bool>> _)");
            }
            sb.AppendLine($"    {{");

            if (carrier != null)
            {
                // Compute carrier site params
                var siteParams = new List<QueryParameter>();
                var globalParamOffset = 0;
                foreach (var clause in prebuiltChain.GetClauseEntries())
                {
                    if (clause.Site.UniqueId == site.UniqueId)
                    {
                        if (clause.Site.Clause != null)
                            for (int i = 0; i < clause.Site.Clause.Parameters.Count && globalParamOffset + i < prebuiltChain.ChainParameters.Count; i++)
                                siteParams.Add(prebuiltChain.ChainParameters[globalParamOffset + i]);
                        break;
                    }
                    if (clause.Site.Clause != null)
                        globalParamOffset += clause.Site.Clause.Parameters.Count;
                }

                var joinReturnType = $"IJoinedQueryBuilder<{entityType}, {joinedEntityName}>";
                if (isFirstInChain)
                {
                    // For first join, the incoming builder is QueryBuilder<T>
                    CarrierEmitter.EmitCarrierChainEntry(sb, carrier, prebuiltChain, site, $"QueryBuilder<{entityType}>", joinReturnType, null, siteParams, globalParamOffset);
                }
                else
                {
                    // Join noops need Unsafe.As since the return type differs from receiver
                    sb.AppendLine($"        return Unsafe.As<{joinReturnType}>(builder);");
                }
                sb.AppendLine($"    }}");
            }
            else
            {
            sb.AppendLine($"        var __b = Unsafe.As<{concreteThisType}<{entityType}>>({joinBuilderExpr});");
            if (isFirstInChain && prebuiltChain.MaxParameterCount > 0)
                sb.AppendLine($"        __b.AllocatePrebuiltParams({prebuiltChain.MaxParameterCount});");
            sb.AppendLine($"        return __b.AsJoined<{joinedEntityName}>();");
            sb.AppendLine($"    }}");
            }
        }
        else if (clauseInfo != null && clauseInfo.IsSuccess)
        {
            var joinedEntityName = InterceptorCodeGenerator.GetShortTypeName(site.JoinedEntityTypeName ?? site.EntityTypeName);
            var escapedSql = InterceptorCodeGenerator.EscapeStringLiteral(clauseInfo.SqlFragment);

            if (site.IsNavigationJoin)
            {
                // Navigation overload: Expression<Func<T, NavigationList<TJoined>>>
                sb.AppendLine($"    public static IJoinedQueryBuilder<{entityType}, {joinedEntityName}> {methodName}(");
                sb.AppendLine($"        this {thisType}<{entityType}> builder,");
                sb.AppendLine($"        Expression<Func<{entityType}, NavigationList<{joinedEntityName}>>> _)");
            }
            else
            {
                // Explicit-lambda overload: Expression<Func<T, TJoined, bool>>
                sb.AppendLine($"    public static IJoinedQueryBuilder<{entityType}, {joinedEntityName}> {methodName}(");
                sb.AppendLine($"        this {thisType}<{entityType}> builder,");
                sb.AppendLine($"        Expression<Func<{entityType}, {joinedEntityName}, bool>> _)");
            }
            sb.AppendLine($"    {{");
            sb.AppendLine($"        var __b = Unsafe.As<{concreteThisType}<{entityType}>>({joinBuilderExpr});");
            var escapedTableName = InterceptorCodeGenerator.EscapeStringLiteral(clauseInfo.JoinedTableName);
            sb.AppendLine($"        return __b.AddJoinClause<{joinedEntityName}>({joinKind}, @\"{escapedTableName}\", @\"{escapedSql}\");");
            sb.AppendLine($"    }}");
        }
        else if (site.JoinedEntityTypeName != null)
        {
            // Fallback with concrete joined type - delegates to original method
            var joinedType = InterceptorCodeGenerator.GetShortTypeName(site.JoinedEntityTypeName);

            var methodCall = site.Kind switch
            {
                InterceptorKind.LeftJoin => "LeftJoin",
                InterceptorKind.RightJoin => "RightJoin",
                _ => "Join"
            };

            if (site.IsNavigationJoin)
            {
                // Navigation overload fallback
                sb.AppendLine($"    public static IJoinedQueryBuilder<{entityType}, {joinedType}> {methodName}(");
                sb.AppendLine($"        this {thisType}<{entityType}> builder,");
                sb.AppendLine($"        Expression<Func<{entityType}, NavigationList<{joinedType}>>> navigation)");
                sb.AppendLine($"    {{");
                sb.AppendLine($"        var __b = Unsafe.As<{concreteThisType}<{entityType}>>({joinBuilderExpr});");
                sb.AppendLine($"        // Fallback - navigation join not fully analyzed at compile time");
                sb.AppendLine($"        return __b.{methodCall}(navigation);");
                sb.AppendLine($"    }}");
            }
            else
            {
                // Explicit-lambda overload fallback
                sb.AppendLine($"    public static IJoinedQueryBuilder<{entityType}, {joinedType}> {methodName}(");
                sb.AppendLine($"        this {thisType}<{entityType}> builder,");
                sb.AppendLine($"        Expression<Func<{entityType}, {joinedType}, bool>> condition)");
                sb.AppendLine($"    {{");
                sb.AppendLine($"        var __b = Unsafe.As<{concreteThisType}<{entityType}>>({joinBuilderExpr});");
                sb.AppendLine($"        // Fallback - join condition not fully analyzed at compile time");
                sb.AppendLine($"        return __b.{methodCall}(condition);");
                sb.AppendLine($"    }}");
            }
        }
        else
        {
            // No type info available - skip this interceptor
            sb.AppendLine($"    // WARNING: Could not determine joined entity type for {site.MethodName}");
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
        var builderName = GetJoinedBuilderTypeName(entityTypes.Length);
        var concreteBuilderName = InterceptorCodeGenerator.ToConcreteTypeName(builderName);
        var thisType = site.BuilderTypeName;
        var returnType = InterceptorCodeGenerator.ToReturnTypeName(thisType);
        var thisBuilderName = builderName;
        var typeArgs = string.Join(", ", entityTypes);
        var clauseInfo = site.Clause;
        var hasAnyParams = clauseInfo?.Parameters.Count > 0;
        var hasResolvableCapturedParams = clauseInfo?.Parameters.Any(p => p.IsCaptured && p.CanGenerateDirectPath) == true;
        var exprParamName = hasResolvableCapturedParams ? "expr" : "_";
        var bitSuffix = InterceptorCodeGenerator.ClauseBitSuffix(clauseBit);

        methodFields ??= new List<InterceptorCodeGenerator.CachedExtractorField>();
        if (methodFields.Count > 0)
        {
            sb.AppendLine($"    [UnconditionalSuppressMessage(\"Trimming\", \"IL2075\",");
            sb.AppendLine($"        Justification = \"Closure fields are preserved by the expression tree that references them.\")]");
        }

        // Check if this is on a projected builder (has TResult)
        if (site.ResultTypeName != null)
        {
            var resultType = InterceptorCodeGenerator.SanitizeTupleResultType(InterceptorCodeGenerator.GetShortTypeName(site.ResultTypeName));
            var isBrokenTuple = resultType.Contains("object") && resultType.StartsWith("(");

            if (isBrokenTuple)
            {
                // Tuple element types could not be resolved by the semantic model (generated entity types).
                // Use arity-matching generic parameters so the compiler infers the concrete TResult.
                var allTypeParams = string.Join(", ", Enumerable.Range(1, entityTypes.Length).Select(i => $"T{i}"));
                var constraints = string.Join(" ", Enumerable.Range(1, entityTypes.Length).Select(i => $"where T{i} : class"));
                sb.AppendLine($"    public static {builderName}<{allTypeParams}, TResult> {methodName}<{allTypeParams}, TResult>(");
                sb.AppendLine($"        this {thisBuilderName}<{allTypeParams}, TResult> builder,");
                sb.AppendLine($"        Expression<Func<{allTypeParams}, bool>> {exprParamName}) {constraints}");
            }
            else
            {
                sb.AppendLine($"    public static {builderName}<{typeArgs}, {resultType}> {methodName}(");
                sb.AppendLine($"        this {thisBuilderName}<{typeArgs}, {resultType}> builder,");
                sb.AppendLine($"        Expression<Func<{typeArgs}, bool>> {exprParamName})");
            }
        }
        else
        {
            sb.AppendLine($"    public static {builderName}<{typeArgs}> {methodName}(");
            sb.AppendLine($"        this {thisBuilderName}<{typeArgs}> builder,");
            sb.AppendLine($"        Expression<Func<{typeArgs}, bool>> {exprParamName})");
        }

        sb.AppendLine($"    {{");

        // Carrier-optimized path: bypass concrete type cast entirely
        if (carrier != null && prebuiltChain != null)
        {
            // Compute carrier site params
            var siteParams = new List<QueryParameter>();
            var globalParamOffset = 0;
            foreach (var clause in prebuiltChain.GetClauseEntries())
            {
                if (clause.Site.UniqueId == site.UniqueId)
                {
                    if (clause.Site.Clause != null)
                        for (int i = 0; i < clause.Site.Clause.Parameters.Count && globalParamOffset + i < prebuiltChain.ChainParameters.Count; i++)
                            siteParams.Add(prebuiltChain.ChainParameters[globalParamOffset + i]);
                    break;
                }
                if (clause.Site.Clause != null)
                    globalParamOffset += clause.Site.Clause.Parameters.Count;
            }

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
                CarrierEmitter.EmitCarrierParamBind(sb, carrier, prebuiltChain, clauseBit, siteParams, globalParamOffset);
            }
            else
            {
                CarrierEmitter.EmitCarrierNoop(sb, carrier, prebuiltChain, clauseBit);
            }
            sb.AppendLine($"    }}");
            return;
        }

        // Cast to concrete type (receiver is always an interface)
        if (site.ResultTypeName != null)
        {
            var resultType = InterceptorCodeGenerator.SanitizeTupleResultType(InterceptorCodeGenerator.GetShortTypeName(site.ResultTypeName));
            var isBrokenTuple = resultType.Contains("object") && resultType.StartsWith("(");
            if (isBrokenTuple)
            {
                var allTypeParams = string.Join(", ", Enumerable.Range(1, entityTypes.Length).Select(i => $"T{i}"));
                sb.AppendLine($"        var __b = Unsafe.As<{concreteBuilderName}<{allTypeParams}, TResult>>(builder);");
            }
            else
            {
                sb.AppendLine($"        var __b = Unsafe.As<{concreteBuilderName}<{typeArgs}, {resultType}>>(builder);");
            }
        }
        else
        {
            sb.AppendLine($"        var __b = Unsafe.As<{concreteBuilderName}<{typeArgs}>>(builder);");
        }
        var builderVar = "__b";

        // Simplified prebuilt chain path: BindParam instead of AddWhereClause
        if (prebuiltChain != null)
        {
            if (isFirstInChain && prebuiltChain.MaxParameterCount > 0)
                sb.AppendLine($"        {builderVar}.AllocatePrebuiltParams({prebuiltChain.MaxParameterCount});");

            if (hasAnyParams == true)
            {
                if (hasResolvableCapturedParams)
                    InterceptorCodeGenerator.GenerateCachedExtraction(sb, methodFields);

                var allParams = clauseInfo!.Parameters.OrderBy(p => p.Index).ToList();
                foreach (var p in allParams)
                {
                    var expr = p.IsCaptured ? $"p{p.Index}" : p.ValueExpression;
                    sb.AppendLine($"        {builderVar}.BindParam({expr});");
                }
            }

            if (clauseBit.HasValue)
            {
                sb.AppendLine($"        {builderVar}.SetClauseBit({clauseBit.Value});");
                sb.AppendLine($"        return builder;");
            }
            else
            {
                sb.AppendLine($"        return builder;");
            }
            sb.AppendLine($"    }}");
            return;
        }

        if (clauseInfo != null && clauseInfo.IsSuccess)
        {
            var escapedSql = InterceptorCodeGenerator.EscapeStringLiteral(clauseInfo.SqlFragment);

            // Check if any captured parameters lack extraction paths (e.g., captured variables in subqueries)
            var hasUnresolvableCaptured = clauseInfo.Parameters.Any(p => p.IsCaptured && !p.CanGenerateDirectPath);

            if (hasUnresolvableCaptured)
            {
                // Emit SQL-only clause (parameters cannot be extracted at compile time)
                var resolvableParams = clauseInfo.Parameters
                    .Where(p => !p.IsCaptured)
                    .OrderBy(p => p.Index)
                    .ToList();
                if (resolvableParams.Count > 0)
                {
                    var paramArgs = string.Join(", ", resolvableParams.Select(p => p.ValueExpression));
                    sb.AppendLine($"        return {builderVar}.AddWhereClause(@\"{escapedSql}\", {paramArgs}){bitSuffix};");
                }
                else
                {
                    sb.AppendLine($"        return {builderVar}.AddWhereClause(@\"{escapedSql}\"){bitSuffix};");
                }
            }
            else if (hasAnyParams)
            {
                if (hasResolvableCapturedParams)
                    InterceptorCodeGenerator.GenerateCachedExtraction(sb, methodFields);

                var allParams = clauseInfo.Parameters.OrderBy(p => p.Index).ToList();
                var paramArgs = string.Join(", ", allParams.Select(p =>
                    p.IsCaptured ? $"p{p.Index}" : p.ValueExpression));
                sb.AppendLine($"        return {builderVar}.AddWhereClause(@\"{escapedSql}\", {paramArgs}){bitSuffix};");
            }
            else
            {
                sb.AppendLine($"        return {builderVar}.AddWhereClause(@\"{escapedSql}\"){bitSuffix};");
            }
        }
        else
        {
            sb.AppendLine($"        // Fallback - expression not fully analyzed at compile time");
            sb.AppendLine($"        return {builderVar}.Where({exprParamName}){bitSuffix};");
        }

        sb.AppendLine($"    }}");
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
        var builderName = GetJoinedBuilderTypeName(entityTypes.Length);
        var concreteBuilderName = InterceptorCodeGenerator.ToConcreteTypeName(builderName);
        var thisType = site.BuilderTypeName;
        var returnType = InterceptorCodeGenerator.ToReturnTypeName(thisType);
        var thisBuilderName = builderName;
        var typeArgs = string.Join(", ", entityTypes);
        var clauseInfo = site.Clause;
        var isOrderBy = site.Kind == InterceptorKind.OrderBy;
        var bitSuffix = InterceptorCodeGenerator.ClauseBitSuffix(clauseBit);

        // Use concrete key type (arity 0) when available
        var keyType = site.KeyTypeName != null ? InterceptorCodeGenerator.GetShortTypeName(site.KeyTypeName) : null;

        if (site.ResultTypeName != null)
        {
            var resultType = InterceptorCodeGenerator.SanitizeTupleResultType(InterceptorCodeGenerator.GetShortTypeName(site.ResultTypeName));
            var isBrokenTuple = resultType.Contains("object") && resultType.StartsWith("(");

            // Broken tuple result types cannot use concrete arity-0 signatures;
            // fall back to full arity-matching with TKey to preserve interceptor arity.
            if (isBrokenTuple)
                keyType = null;

            if (keyType != null)
            {
                sb.AppendLine($"    public static {builderName}<{typeArgs}, {resultType}> {methodName}(");
                sb.AppendLine($"        this {thisBuilderName}<{typeArgs}, {resultType}> builder,");
                sb.AppendLine($"        Expression<Func<{typeArgs}, {keyType}>> _,");
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
                sb.AppendLine($"        Expression<Func<{allTypeParams}, TKey>> _,");
                sb.AppendLine($"        Direction direction = Direction.Ascending) {constraints}");
            }
        }
        else
        {
            if (keyType != null)
            {
                sb.AppendLine($"    public static {builderName}<{typeArgs}> {methodName}(");
                sb.AppendLine($"        this {thisBuilderName}<{typeArgs}> builder,");
                sb.AppendLine($"        Expression<Func<{typeArgs}, {keyType}>> _,");
                sb.AppendLine($"        Direction direction = Direction.Ascending)");
            }
            else
            {
                var allTypeParams = string.Join(", ", Enumerable.Range(1, entityTypes.Length).Select(i => $"T{i}"));
                var constraints = string.Join(" ", Enumerable.Range(1, entityTypes.Length).Select(i => $"where T{i} : class"));
                sb.AppendLine($"    public static {builderName}<{allTypeParams}> {methodName}<{allTypeParams}, TKey>(");
                sb.AppendLine($"        this {thisBuilderName}<{allTypeParams}> builder,");
                sb.AppendLine($"        Expression<Func<{allTypeParams}, TKey>> _,");
                sb.AppendLine($"        Direction direction = Direction.Ascending) {constraints}");
            }
        }

        sb.AppendLine($"    {{");

        // Carrier-optimized path: bypass concrete type cast entirely
        // Only when concrete key type is available (open-generic fallback can't use carrier types)
        if (carrier != null && prebuiltChain != null && keyType != null)
        {
            // Compute carrier site params
            var siteParams = new List<QueryParameter>();
            var globalParamOffset = 0;
            foreach (var clause in prebuiltChain.GetClauseEntries())
            {
                if (clause.Site.UniqueId == site.UniqueId)
                {
                    if (clause.Site.Clause != null)
                        for (int i = 0; i < clause.Site.Clause.Parameters.Count && globalParamOffset + i < prebuiltChain.ChainParameters.Count; i++)
                            siteParams.Add(prebuiltChain.ChainParameters[globalParamOffset + i]);
                    break;
                }
                if (clause.Site.Clause != null)
                    globalParamOffset += clause.Site.Clause.Parameters.Count;
            }

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
                CarrierEmitter.EmitCarrierParamBind(sb, carrier, prebuiltChain, clauseBit, siteParams, globalParamOffset);
            }
            else
            {
                CarrierEmitter.EmitCarrierNoop(sb, carrier, prebuiltChain, clauseBit);
            }
            sb.AppendLine($"    }}");
            return;
        }

        // Cast to concrete type (receiver is always an interface)
        if (site.ResultTypeName != null && keyType != null)
        {
            var resultType = InterceptorCodeGenerator.SanitizeTupleResultType(InterceptorCodeGenerator.GetShortTypeName(site.ResultTypeName));
            sb.AppendLine($"        var __b = Unsafe.As<{concreteBuilderName}<{typeArgs}, {resultType}>>(builder);");
        }
        else if (site.ResultTypeName != null)
        {
            var allTypeParams = string.Join(", ", Enumerable.Range(1, entityTypes.Length).Select(i => $"T{i}"));
            sb.AppendLine($"        var __b = Unsafe.As<{concreteBuilderName}<{allTypeParams}, TResult>>(builder);");
        }
        else if (keyType != null)
        {
            sb.AppendLine($"        var __b = Unsafe.As<{concreteBuilderName}<{typeArgs}>>(builder);");
        }
        else
        {
            var allTypeParams = string.Join(", ", Enumerable.Range(1, entityTypes.Length).Select(i => $"T{i}"));
            sb.AppendLine($"        var __b = Unsafe.As<{concreteBuilderName}<{allTypeParams}>>(builder);");
        }
        var builderVar = "__b";

        // Simplified prebuilt chain path: passthrough (no SQL generation)
        if (prebuiltChain != null)
        {
            if (isFirstInChain && prebuiltChain.MaxParameterCount > 0)
                sb.AppendLine($"        {builderVar}.AllocatePrebuiltParams({prebuiltChain.MaxParameterCount});");
            if (clauseBit.HasValue)
            {
                sb.AppendLine($"        {builderVar}.SetClauseBit({clauseBit.Value});");
                sb.AppendLine($"        return builder;");
            }
            else
            {
                sb.AppendLine($"        return builder;");
            }
            sb.AppendLine($"    }}");
            return;
        }

        if (clauseInfo != null && clauseInfo.Kind == ClauseKind.OrderBy && clauseInfo.IsSuccess)
        {
            var escapedSql = InterceptorCodeGenerator.EscapeStringLiteral(clauseInfo.ColumnSql);
            var builderMethod = isOrderBy ? "AddOrderByClause" : "AddThenByClause";
            sb.AppendLine($"        return {builderVar}.{builderMethod}(@\"{escapedSql}\", direction){bitSuffix};");
        }
        else if (clauseInfo != null && clauseInfo.IsSuccess)
        {
            var escapedSql = InterceptorCodeGenerator.EscapeStringLiteral(clauseInfo.SqlFragment);
            var builderMethod = isOrderBy ? "AddOrderByClause" : "AddThenByClause";
            sb.AppendLine($"        return {builderVar}.{builderMethod}(@\"{escapedSql}\", direction){bitSuffix};");
        }
        else
        {
            // Non-translatable clause — skip interceptor entirely so the original method runs
            return;
        }

        sb.AppendLine($"    }}");
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
        var builderName = GetJoinedBuilderTypeName(entityTypes.Length);
        var concreteBuilderName = InterceptorCodeGenerator.ToConcreteTypeName(builderName);
        var thisType = site.BuilderTypeName;
        var returnType = InterceptorCodeGenerator.ToReturnTypeName(thisType);
        var thisBuilderName = builderName;
        var typeArgs = string.Join(", ", entityTypes);
        var projection = site.ProjectionInfo;

        // Simplified prebuilt chain path: AsProjected instead of AddSelectClause
        if (prebuiltChain != null && projection != null && projection.IsOptimalPath && projection.Columns.Count > 0)
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
                    // Compute carrier site params
                    var siteParams = new List<QueryParameter>();
                    var globalParamOffset = 0;
                    foreach (var clause in prebuiltChain.GetClauseEntries())
                    {
                        if (clause.Site.UniqueId == site.UniqueId)
                        {
                            if (clause.Site.Clause != null)
                                for (int i = 0; i < clause.Site.Clause.Parameters.Count && globalParamOffset + i < prebuiltChain.ChainParameters.Count; i++)
                                    siteParams.Add(prebuiltChain.ChainParameters[globalParamOffset + i]);
                            break;
                        }
                        if (clause.Site.Clause != null)
                            globalParamOffset += clause.Site.Clause.Parameters.Count;
                    }
                    var joinedBuilderType = CarrierEmitter.GetJoinedConcreteBuilderTypeName(entityTypes.Length, entityTypes);
                    int? clauseBit = null;
                    CarrierEmitter.EmitCarrierChainEntry(sb, carrier, prebuiltChain, site, joinedBuilderType, targetInterface, clauseBit, siteParams, globalParamOffset);
                }
                else
                {
                    CarrierEmitter.EmitCarrierSelect(sb, targetInterface);
                }
                sb.AppendLine($"    }}");
                return;
            }

            sb.AppendLine($"        var __b = Unsafe.As<{concreteBuilderName}<{typeArgs}>>(builder);");
            if (isFirstInChain && prebuiltChain.MaxParameterCount > 0)
                sb.AppendLine($"        __b.AllocatePrebuiltParams({prebuiltChain.MaxParameterCount});");
            sb.AppendLine($"        return __b.AsProjected<{resultType}>();");
            sb.AppendLine($"    }}");
            return;
        }

        if (projection != null && projection.IsOptimalPath && projection.Columns.Count > 0)
        {
            var resultType = InterceptorCodeGenerator.GetShortTypeName(projection.ResultTypeName);
            var columnNames = ReaderCodeGenerator.GenerateColumnNamesArray(projection, site.Dialect);
            var readerDelegate = ReaderCodeGenerator.GenerateReaderDelegate(projection, entityTypes[0]);

            sb.AppendLine($"    public static {builderName}<{typeArgs}, {resultType}> {methodName}(");
            sb.AppendLine($"        this {thisBuilderName}<{typeArgs}> builder,");
            sb.AppendLine($"        Func<{typeArgs}, {resultType}> _)");
            sb.AppendLine($"    {{");
            sb.AppendLine($"        var __b = Unsafe.As<{concreteBuilderName}<{typeArgs}>>(builder);");
            sb.AppendLine($"        return __b.AddSelectClause<{resultType}>(");
            sb.AppendLine($"            {columnNames},");
            sb.AppendLine($"            {readerDelegate});");
            sb.AppendLine($"    }}");
        }
        else
        {
            // Fallback
            sb.AppendLine($"    public static {builderName}<{typeArgs}, TResult> {methodName}<TResult>(");
            sb.AppendLine($"        this {thisBuilderName}<{typeArgs}> builder,");
            sb.AppendLine($"        Func<{typeArgs}, TResult> selector)");
            sb.AppendLine($"    {{");
            sb.AppendLine($"        // Fallback - projection not fully analyzed at compile time");
            sb.AppendLine($"        var __b = Unsafe.As<{concreteBuilderName}<{typeArgs}>>(builder);");
            sb.AppendLine($"        return __b.Select(selector);");
            sb.AppendLine($"    }}");
        }
    }
}
