using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;
using Quarry;
using Quarry.Generators.Projection;
using Quarry.Generators.Translation;

namespace Quarry.Generators.Generation;

internal static partial class InterceptorCodeGenerator
{
    /// <summary>
    /// Generates a Join interceptor with SQL fragment generation.
    /// Handles both initial joins (QueryBuilder→JoinedQueryBuilder) and
    /// chained joins (JoinedQueryBuilder→JoinedQueryBuilder3, etc.)
    /// </summary>
    private static void GenerateJoinInterceptor(StringBuilder sb, UsageSiteInfo site, string methodName,
        PrebuiltChainInfo? prebuiltChain = null, bool isFirstInChain = false, CarrierClassInfo? carrier = null)
    {
        var entityType = GetShortTypeName(site.EntityTypeName);
        var clauseInfo = site.ClauseInfo as JoinClauseInfo;
        var joinedEntityTypeNames = site.JoinedEntityTypeNames;

        var joinKind = site.Kind switch
        {
            InterceptorKind.LeftJoin => "JoinKind.Left",
            InterceptorKind.RightJoin => "JoinKind.Right",
            _ => "JoinKind.Inner"
        };

        var thisType = site.BuilderTypeName;
        var returnType = ToReturnTypeName(thisType);
        var concreteThisType = ToConcreteTypeName(returnType);
        // When receiver is IEntityAccessor, use CreateQueryBuilder() to get a real QueryBuilder
        var joinBuilderExpr = IsEntityAccessorType(thisType) ? EntityAccessorToQueryBuilder(entityType) : "builder";

        // Determine if this is a chained join (from JoinedQueryBuilder/3)
        var isChainedJoin = joinedEntityTypeNames != null && joinedEntityTypeNames.Count >= 2;

        if (isChainedJoin && site.JoinedEntityTypeName != null)
        {
            var joinedType = GetShortTypeName(site.JoinedEntityTypeName);
            var priorTypes = joinedEntityTypeNames!.Select(GetShortTypeName).ToArray();
            var allTypes = priorTypes.Concat(new[] { joinedType }).ToArray();

            // Determine receiver and return builder type names
            var receiverBuilderName = GetJoinedBuilderTypeName(priorTypes.Length);
            var concreteReceiverBuilderName = ToConcreteTypeName(receiverBuilderName);
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
                    var siteParams = new List<ChainParameterInfo>();
                    var globalParamOffset = 0;
                    foreach (var clause in prebuiltChain.Analysis.Clauses)
                    {
                        if (clause.Site.UniqueId == site.UniqueId)
                        {
                            if (clause.Site.ClauseInfo != null)
                                for (int i = 0; i < clause.Site.ClauseInfo.Parameters.Count && globalParamOffset + i < prebuiltChain.ChainParameters.Count; i++)
                                    siteParams.Add(prebuiltChain.ChainParameters[globalParamOffset + i]);
                            break;
                        }
                        if (clause.Site.ClauseInfo != null)
                            globalParamOffset += clause.Site.ClauseInfo.Parameters.Count;
                    }

                    var joinReturnType = $"{returnBuilderName}<{returnTypeArgs}>";
                    if (isFirstInChain)
                    {
                        // For chained join first-in-chain, the incoming builder is the pre-join type
                        var preJoinBuilderType = GetJoinedConcreteBuilderTypeName(priorTypes.Length, priorTypes);
                        EmitCarrierChainEntry(sb, carrier, prebuiltChain, site, preJoinBuilderType, joinReturnType, null, siteParams, globalParamOffset);
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
                var escapedSql = EscapeStringLiteral(clauseInfo.OnConditionSql);
                sb.AppendLine($"    public static {returnBuilderName}<{returnTypeArgs}> {methodName}(");
                sb.AppendLine($"        this {receiverBuilderName}<{receiverTypeArgs}> builder,");
                sb.AppendLine($"        Expression<Func<{funcTypeArgs}>> _)");
                sb.AppendLine($"    {{");
                sb.AppendLine($"        var __b = Unsafe.As<{concreteReceiverBuilderName}<{receiverTypeArgs}>>(builder);");
                var escapedTableName = EscapeStringLiteral(clauseInfo.JoinedTableName);
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
            var joinedEntityName = clauseInfo.JoinedEntityName;

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
                var siteParams = new List<ChainParameterInfo>();
                var globalParamOffset = 0;
                foreach (var clause in prebuiltChain.Analysis.Clauses)
                {
                    if (clause.Site.UniqueId == site.UniqueId)
                    {
                        if (clause.Site.ClauseInfo != null)
                            for (int i = 0; i < clause.Site.ClauseInfo.Parameters.Count && globalParamOffset + i < prebuiltChain.ChainParameters.Count; i++)
                                siteParams.Add(prebuiltChain.ChainParameters[globalParamOffset + i]);
                        break;
                    }
                    if (clause.Site.ClauseInfo != null)
                        globalParamOffset += clause.Site.ClauseInfo.Parameters.Count;
                }

                var joinReturnType = $"IJoinedQueryBuilder<{entityType}, {joinedEntityName}>";
                if (isFirstInChain)
                {
                    // For first join, the incoming builder is QueryBuilder<T>
                    EmitCarrierChainEntry(sb, carrier, prebuiltChain, site, $"QueryBuilder<{entityType}>", joinReturnType, null, siteParams, globalParamOffset);
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
            var joinedEntityName = clauseInfo.JoinedEntityName;
            var escapedSql = EscapeStringLiteral(clauseInfo.OnConditionSql);

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
            var escapedTableName = EscapeStringLiteral(clauseInfo.JoinedTableName);
            sb.AppendLine($"        return __b.AddJoinClause<{joinedEntityName}>({joinKind}, @\"{escapedTableName}\", @\"{escapedSql}\");");
            sb.AppendLine($"    }}");
        }
        else if (site.JoinedEntityTypeName != null)
        {
            // Fallback with concrete joined type - delegates to original method
            var joinedType = GetShortTypeName(site.JoinedEntityTypeName);

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
    /// Gets the builder type name for a given entity count in joins.
    /// </summary>
    private static string GetJoinedBuilderTypeName(int entityCount)
    {
        return entityCount switch
        {
            2 => "IJoinedQueryBuilder",
            3 => "IJoinedQueryBuilder3",
            4 => "IJoinedQueryBuilder4",
            _ => throw new ArgumentOutOfRangeException(nameof(entityCount), $"Unsupported entity count: {entityCount}")
        };
    }

    /// <summary>
    /// Generates a Where() interceptor for joined query builders.
    /// </summary>
    private static void GenerateJoinedWhereInterceptor(StringBuilder sb, UsageSiteInfo site, string methodName, List<CachedExtractorField>? methodFields, int? clauseBit = null,
        PrebuiltChainInfo? prebuiltChain = null, bool isFirstInChain = false, CarrierClassInfo? carrier = null)
    {
        var entityTypes = site.JoinedEntityTypeNames!.Select(GetShortTypeName).ToArray();
        var builderName = GetJoinedBuilderTypeName(entityTypes.Length);
        var concreteBuilderName = ToConcreteTypeName(builderName);
        var thisType = site.BuilderTypeName;
        var returnType = ToReturnTypeName(thisType);
        var thisBuilderName = builderName;
        var typeArgs = string.Join(", ", entityTypes);
        var clauseInfo = site.ClauseInfo;
        var hasAnyParams = clauseInfo?.Parameters.Count > 0;
        var hasResolvableCapturedParams = clauseInfo?.Parameters.Any(p => p.IsCaptured && p.CanGenerateDirectPath) == true;
        var exprParamName = hasResolvableCapturedParams ? "expr" : "_";
        var bitSuffix = ClauseBitSuffix(clauseBit);

        methodFields ??= new List<CachedExtractorField>();
        if (methodFields.Count > 0)
        {
            sb.AppendLine($"    [UnconditionalSuppressMessage(\"Trimming\", \"IL2075\",");
            sb.AppendLine($"        Justification = \"Closure fields are preserved by the expression tree that references them.\")]");
        }

        // Check if this is on a projected builder (has TResult)
        if (site.ResultTypeName != null)
        {
            var resultType = SanitizeTupleResultType(GetShortTypeName(site.ResultTypeName));
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
            var siteParams = new List<ChainParameterInfo>();
            var globalParamOffset = 0;
            foreach (var clause in prebuiltChain.Analysis.Clauses)
            {
                if (clause.Site.UniqueId == site.UniqueId)
                {
                    if (clause.Site.ClauseInfo != null)
                        for (int i = 0; i < clause.Site.ClauseInfo.Parameters.Count && globalParamOffset + i < prebuiltChain.ChainParameters.Count; i++)
                            siteParams.Add(prebuiltChain.ChainParameters[globalParamOffset + i]);
                    break;
                }
                if (clause.Site.ClauseInfo != null)
                    globalParamOffset += clause.Site.ClauseInfo.Parameters.Count;
            }

            var joinedBuilderTypeName = GetJoinedConcreteBuilderTypeName(entityTypes.Length, entityTypes);
            var returnInterface = site.ResultTypeName != null
                ? $"{builderName}<{typeArgs}, {SanitizeTupleResultType(GetShortTypeName(site.ResultTypeName))}>"
                : $"{builderName}<{typeArgs}>";

            if (isFirstInChain)
            {
                EmitCarrierChainEntry(sb, carrier, prebuiltChain, site, joinedBuilderTypeName, returnInterface, clauseBit, siteParams, globalParamOffset);
            }
            else if (siteParams.Count > 0)
            {
                EmitCarrierParamBind(sb, carrier, prebuiltChain, clauseBit, siteParams, globalParamOffset);
            }
            else
            {
                EmitCarrierNoop(sb, carrier, prebuiltChain, clauseBit);
            }
            sb.AppendLine($"    }}");
            return;
        }

        // Cast to concrete type (receiver is always an interface)
        if (site.ResultTypeName != null)
        {
            var resultType = SanitizeTupleResultType(GetShortTypeName(site.ResultTypeName));
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
                    GenerateCachedExtraction(sb, methodFields);

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
            var escapedSql = EscapeStringLiteral(clauseInfo.SqlFragment);

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
                    GenerateCachedExtraction(sb, methodFields);

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
    /// Generates an OrderBy/ThenBy interceptor for joined query builders.
    /// </summary>
    private static void GenerateJoinedOrderByInterceptor(StringBuilder sb, UsageSiteInfo site, string methodName, int? clauseBit = null,
        PrebuiltChainInfo? prebuiltChain = null, bool isFirstInChain = false, CarrierClassInfo? carrier = null)
    {
        var entityTypes = site.JoinedEntityTypeNames!.Select(GetShortTypeName).ToArray();
        var builderName = GetJoinedBuilderTypeName(entityTypes.Length);
        var concreteBuilderName = ToConcreteTypeName(builderName);
        var thisType = site.BuilderTypeName;
        var returnType = ToReturnTypeName(thisType);
        var thisBuilderName = builderName;
        var typeArgs = string.Join(", ", entityTypes);
        var clauseInfo = site.ClauseInfo;
        var isOrderBy = site.Kind == InterceptorKind.OrderBy;
        var bitSuffix = ClauseBitSuffix(clauseBit);

        // Use concrete key type (arity 0) when available
        var keyType = site.KeyTypeName != null ? GetShortTypeName(site.KeyTypeName) : null;

        if (site.ResultTypeName != null)
        {
            var resultType = SanitizeTupleResultType(GetShortTypeName(site.ResultTypeName));
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
            var siteParams = new List<ChainParameterInfo>();
            var globalParamOffset = 0;
            foreach (var clause in prebuiltChain.Analysis.Clauses)
            {
                if (clause.Site.UniqueId == site.UniqueId)
                {
                    if (clause.Site.ClauseInfo != null)
                        for (int i = 0; i < clause.Site.ClauseInfo.Parameters.Count && globalParamOffset + i < prebuiltChain.ChainParameters.Count; i++)
                            siteParams.Add(prebuiltChain.ChainParameters[globalParamOffset + i]);
                    break;
                }
                if (clause.Site.ClauseInfo != null)
                    globalParamOffset += clause.Site.ClauseInfo.Parameters.Count;
            }

            var joinedBuilderTypeName = GetJoinedConcreteBuilderTypeName(entityTypes.Length, entityTypes);
            var returnInterface = site.ResultTypeName != null
                ? $"{builderName}<{typeArgs}, {SanitizeTupleResultType(GetShortTypeName(site.ResultTypeName))}>"
                : $"{builderName}<{typeArgs}>";

            if (isFirstInChain)
            {
                EmitCarrierChainEntry(sb, carrier, prebuiltChain, site, joinedBuilderTypeName, returnInterface, clauseBit, siteParams, globalParamOffset);
            }
            else if (siteParams.Count > 0)
            {
                EmitCarrierParamBind(sb, carrier, prebuiltChain, clauseBit, siteParams, globalParamOffset);
            }
            else
            {
                EmitCarrierNoop(sb, carrier, prebuiltChain, clauseBit);
            }
            sb.AppendLine($"    }}");
            return;
        }

        // Cast to concrete type (receiver is always an interface)
        if (site.ResultTypeName != null && keyType != null)
        {
            var resultType = SanitizeTupleResultType(GetShortTypeName(site.ResultTypeName));
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

        if (clauseInfo is OrderByClauseInfo orderByInfo && orderByInfo.IsSuccess)
        {
            var escapedSql = EscapeStringLiteral(orderByInfo.ColumnSql);
            var builderMethod = isOrderBy ? "AddOrderByClause" : "AddThenByClause";
            sb.AppendLine($"        return {builderVar}.{builderMethod}(@\"{escapedSql}\", direction){bitSuffix};");
        }
        else if (clauseInfo != null && clauseInfo.IsSuccess)
        {
            var escapedSql = EscapeStringLiteral(clauseInfo.SqlFragment);
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
    /// Generates a Select() interceptor for joined query builders.
    /// </summary>
    private static void GenerateJoinedSelectInterceptor(StringBuilder sb, UsageSiteInfo site, string methodName,
        PrebuiltChainInfo? prebuiltChain = null, bool isFirstInChain = false, CarrierClassInfo? carrier = null)
    {
        var entityTypes = site.JoinedEntityTypeNames!.Select(GetShortTypeName).ToArray();
        var builderName = GetJoinedBuilderTypeName(entityTypes.Length);
        var concreteBuilderName = ToConcreteTypeName(builderName);
        var thisType = site.BuilderTypeName;
        var returnType = ToReturnTypeName(thisType);
        var thisBuilderName = builderName;
        var typeArgs = string.Join(", ", entityTypes);
        var projection = site.ProjectionInfo;

        // Simplified prebuilt chain path: AsProjected instead of AddSelectClause
        if (prebuiltChain != null && projection != null && projection.IsOptimalPath && projection.Columns.Count > 0)
        {
            var resultType = GetShortTypeName(projection.ResultTypeName);
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
                    var siteParams = new List<ChainParameterInfo>();
                    var globalParamOffset = 0;
                    foreach (var clause in prebuiltChain.Analysis.Clauses)
                    {
                        if (clause.Site.UniqueId == site.UniqueId)
                        {
                            if (clause.Site.ClauseInfo != null)
                                for (int i = 0; i < clause.Site.ClauseInfo.Parameters.Count && globalParamOffset + i < prebuiltChain.ChainParameters.Count; i++)
                                    siteParams.Add(prebuiltChain.ChainParameters[globalParamOffset + i]);
                            break;
                        }
                        if (clause.Site.ClauseInfo != null)
                            globalParamOffset += clause.Site.ClauseInfo.Parameters.Count;
                    }
                    var joinedBuilderType = GetJoinedConcreteBuilderTypeName(entityTypes.Length, entityTypes);
                    int? clauseBit = null;
                    EmitCarrierChainEntry(sb, carrier, prebuiltChain, site, joinedBuilderType, targetInterface, clauseBit, siteParams, globalParamOffset);
                }
                else
                {
                    EmitCarrierSelect(sb, targetInterface);
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
            var resultType = GetShortTypeName(projection.ResultTypeName);
            var columnNames = ReaderCodeGenerator.GenerateColumnNamesArray(projection);
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
