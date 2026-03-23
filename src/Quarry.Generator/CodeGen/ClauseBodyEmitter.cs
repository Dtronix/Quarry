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
/// Emits interceptor method bodies for clause sites (Where, OrderBy, Select,
/// GroupBy, Having, Set, Distinct, Limit, Offset, WithTimeout).
/// Handles both carrier-path (field mutation + cast) and non-carrier-path
/// (QueryBuilder delegation) emission.
/// </summary>
internal static class ClauseBodyEmitter
{
    /// <summary>
    /// Emits a Where clause interceptor body.
    /// Non-carrier: appends SQL fragment to QueryBuilder.
    /// Carrier: extracts params to carrier fields, sets mask bit.
    /// </summary>
    public static void EmitWhere(
        StringBuilder sb,
        TranslatedCallSite site,
        string methodName,
        List<InterceptorCodeGenerator.CachedExtractorField>? methodFields,
        int? clauseBit,
        AssembledPlan? prebuiltChain,
        bool isFirstInChain,
        CarrierPlan? carrier)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);
        var clauseInfo = site.Clause;

        var hasAnyParams = clauseInfo?.Parameters.Count > 0;
        var hasResolvableCapturedParams = clauseInfo?.Parameters.Any(p => p.IsCaptured && p.CanGenerateDirectPath) == true;
        var exprParamName = hasResolvableCapturedParams ? "expr" : "_";

        methodFields ??= new List<InterceptorCodeGenerator.CachedExtractorField>();
        if (methodFields.Count > 0)
        {
            sb.AppendLine($"    [UnconditionalSuppressMessage(\"Trimming\", \"IL2075\",");
            sb.AppendLine($"        Justification = \"Closure fields are preserved by the expression tree that references them.\")]");
        }

        var thisType = site.BuilderTypeName;
        var returnType = InterceptorCodeGenerator.ToReturnTypeName(thisType);
        var concreteType = InterceptorCodeGenerator.ToConcreteTypeName(returnType);

        if (site.ResultTypeName != null)
        {
            var resultType = InterceptorCodeGenerator.SanitizeTupleResultType(InterceptorCodeGenerator.GetShortTypeName(site.ResultTypeName));
            sb.AppendLine($"    public static {returnType}<{entityType}, {resultType}> {methodName}(");
            sb.AppendLine($"        this {thisType}<{entityType}, {resultType}> builder,");
            sb.AppendLine($"        Expression<Func<{entityType}, bool>> {exprParamName})");
        }
        else
        {
            sb.AppendLine($"    public static {returnType}<{entityType}> {methodName}(");
            sb.AppendLine($"        this {thisType}<{entityType}> builder,");
            sb.AppendLine($"        Expression<Func<{entityType}, bool>> {exprParamName})");
        }

        sb.AppendLine($"    {{");

        if (clauseInfo == null || !clauseInfo.IsSuccess)
        {
            // Fallback: return the builder unchanged with closing brace
            sb.AppendLine($"        return Unsafe.As<{concreteType}<{entityType}>>(builder);");
            sb.AppendLine($"    }}");
            return;
        }

        // Carrier-optimized path
        if (carrier != null && prebuiltChain != null)
        {
            var concreteBuilder = $"QueryBuilder<{entityType}>";
            var retInterface = site.ResultTypeName != null
                ? $"IQueryBuilder<{entityType}, {InterceptorCodeGenerator.SanitizeTupleResultType(InterceptorCodeGenerator.GetShortTypeName(site.ResultTypeName))}>"
                : $"IQueryBuilder<{entityType}>";
            CarrierEmitter.EmitCarrierClauseBody(sb, carrier, prebuiltChain, site, clauseBit, isFirstInChain,
                concreteBuilder, retInterface, hasResolvableCapturedParams, methodFields);
            sb.AppendLine($"    }}");
            return;
        }

        // Cast to concrete type
        var whereBuilderExpr = InterceptorCodeGenerator.IsEntityAccessorType(thisType) ? InterceptorCodeGenerator.EntityAccessorToQueryBuilder(entityType) : "builder";
        if (site.ResultTypeName != null)
        {
            var resultType = InterceptorCodeGenerator.SanitizeTupleResultType(InterceptorCodeGenerator.GetShortTypeName(site.ResultTypeName));
            sb.AppendLine($"        var __b = Unsafe.As<{concreteType}<{entityType}, {resultType}>>({whereBuilderExpr});");
        }
        else
        {
            sb.AppendLine($"        var __b = Unsafe.As<{concreteType}<{entityType}>>({whereBuilderExpr});");
        }
        var builderVar = "__b";

        // Simplified prebuilt chain path
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
                    sb.AppendLine($"        {builderVar}.BindParam({InterceptorCodeGenerator.WrapWithToDb(expr, p)});");
                }
            }

            if (clauseBit.HasValue)
                sb.AppendLine($"        return {builderVar}.SetClauseBit({clauseBit.Value});");
            else
                sb.AppendLine($"        return {builderVar};");
            sb.AppendLine($"    }}");
            return;
        }

        {
            var escapedSql = InterceptorCodeGenerator.EscapeStringLiteral(clauseInfo.SqlFragment);

            if (InterceptorCodeGenerator.IsConstantTrueClause(clauseInfo.SqlFragment) && clauseInfo.Parameters.Count == 0)
            {
                var bitSuffix0 = InterceptorCodeGenerator.ClauseBitSuffix(clauseBit);
                if (InterceptorCodeGenerator.IsEntityAccessorType(thisType))
                {
                    sb.AppendLine($"        return Unsafe.As<{concreteType}<{entityType}>>({InterceptorCodeGenerator.EntityAccessorToQueryBuilder(entityType)}){bitSuffix0};");
                }
                else
                {
                    sb.AppendLine($"        return builder{bitSuffix0};");
                }
                sb.AppendLine($"    }}");
                return;
            }

            var hasUnresolvableCaptured = clauseInfo.Parameters.Any(p => p.IsCaptured && !p.CanGenerateDirectPath);

            var bitSuffix = InterceptorCodeGenerator.ClauseBitSuffix(clauseBit);
            if (hasUnresolvableCaptured)
            {
                var resolvableParams = clauseInfo.Parameters
                    .Where(p => !p.IsCaptured)
                    .OrderBy(p => p.Index)
                    .ToList();
                if (resolvableParams.Count > 0)
                {
                    var paramArgs = string.Join(", ", resolvableParams.Select(p => InterceptorCodeGenerator.WrapWithToDb(p.ValueExpression, p)));
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
                {
                    var expr = p.IsCaptured ? $"p{p.Index}" : p.ValueExpression;
                    return InterceptorCodeGenerator.WrapWithToDb(expr, p);
                }));
                sb.AppendLine($"        return {builderVar}.AddWhereClause(@\"{escapedSql}\", {paramArgs}){bitSuffix};");
            }
            else
            {
                sb.AppendLine($"        return {builderVar}.AddWhereClause(@\"{escapedSql}\"){bitSuffix};");
            }
        }

        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Emits an OrderBy/ThenBy clause interceptor body.
    /// </summary>
    public static void EmitOrderBy(
        StringBuilder sb,
        TranslatedCallSite site,
        string methodName,
        int? clauseBit,
        AssembledPlan? prebuiltChain,
        bool isFirstInChain,
        CarrierPlan? carrier)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);
        var clauseInfo = site.Clause;
        var isOrderBy = site.Kind == InterceptorKind.OrderBy;

        var keyType = site.KeyTypeName != null ? InterceptorCodeGenerator.GetShortTypeName(site.KeyTypeName) : null;

        var thisType = site.BuilderTypeName;
        var returnType = InterceptorCodeGenerator.ToReturnTypeName(thisType);
        var concreteType = InterceptorCodeGenerator.ToConcreteTypeName(returnType);

        if (site.ResultTypeName != null)
        {
            var resultType = InterceptorCodeGenerator.SanitizeTupleResultType(InterceptorCodeGenerator.GetShortTypeName(site.ResultTypeName));
            var isBrokenTuple = resultType.Contains("object") && resultType.StartsWith("(");
            if (isBrokenTuple)
                keyType = null;

            if (keyType != null)
            {
                sb.AppendLine($"    public static {returnType}<{entityType}, {resultType}> {methodName}(");
                sb.AppendLine($"        this {thisType}<{entityType}, {resultType}> builder,");
                sb.AppendLine($"        Expression<Func<{entityType}, {keyType}>> _,");
                sb.AppendLine($"        Direction direction = Direction.Ascending)");
            }
            else
            {
                sb.AppendLine($"    public static {returnType}<T, TResult> {methodName}<T, TResult, TKey>(");
                sb.AppendLine($"        this {thisType}<T, TResult> builder,");
                sb.AppendLine($"        Expression<Func<T, TKey>> _,");
                sb.AppendLine($"        Direction direction = Direction.Ascending) where T : class");
            }
        }
        else
        {
            if (keyType != null)
            {
                sb.AppendLine($"    public static {returnType}<{entityType}> {methodName}(");
                sb.AppendLine($"        this {thisType}<{entityType}> builder,");
                sb.AppendLine($"        Expression<Func<{entityType}, {keyType}>> _,");
                sb.AppendLine($"        Direction direction = Direction.Ascending)");
            }
            else
            {
                sb.AppendLine($"    public static {returnType}<T> {methodName}<T, TKey>(");
                sb.AppendLine($"        this {thisType}<T> builder,");
                sb.AppendLine($"        Expression<Func<T, TKey>> _,");
                sb.AppendLine($"        Direction direction = Direction.Ascending) where T : class");
            }
        }

        sb.AppendLine($"    {{");

        // Carrier-optimized path (only with concrete key type)
        if (carrier != null && prebuiltChain != null && keyType != null)
        {
            var concreteBuilder = $"QueryBuilder<{entityType}>";
            var retInterface = site.ResultTypeName != null
                ? $"IQueryBuilder<{entityType}, {InterceptorCodeGenerator.SanitizeTupleResultType(InterceptorCodeGenerator.GetShortTypeName(site.ResultTypeName))}>"
                : $"IQueryBuilder<{entityType}>";
            CarrierEmitter.EmitCarrierClauseBody(sb, carrier, prebuiltChain, site, clauseBit, isFirstInChain,
                concreteBuilder, retInterface, false, new List<InterceptorCodeGenerator.CachedExtractorField>());
            sb.AppendLine($"    }}");
            return;
        }

        // Cast to concrete type
        if (site.ResultTypeName != null)
        {
            var resultType = InterceptorCodeGenerator.SanitizeTupleResultType(InterceptorCodeGenerator.GetShortTypeName(site.ResultTypeName));
            sb.AppendLine($"        var __b = Unsafe.As<{concreteType}<{entityType}, {resultType}>>(builder);");
        }
        else if (keyType != null)
        {
            sb.AppendLine($"        var __b = Unsafe.As<{concreteType}<{entityType}>>(builder);");
        }
        else
        {
            sb.AppendLine($"        var __b = Unsafe.As<{concreteType}<T>>(builder);");
        }
        var builderVar = "__b";

        // Simplified prebuilt chain path
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

        var needsReturnCast = keyType == null;
        string returnCastOpen;
        string returnCastClose;
        if (needsReturnCast)
        {
            var castTarget = site.ResultTypeName != null ? $"{returnType}<T, TResult>" : $"{returnType}<T>";
            returnCastOpen = $"Unsafe.As<{castTarget}>(";
            returnCastClose = ")";
        }
        else
        {
            returnCastOpen = "";
            returnCastClose = "";
        }

        var bitSuffix = InterceptorCodeGenerator.ClauseBitSuffix(clauseBit);
        if (clauseInfo is { Kind: ClauseKind.OrderBy, IsSuccess: true })
        {
            var escapedSql = InterceptorCodeGenerator.EscapeStringLiteral(clauseInfo.ColumnSql);
            var builderMethod = isOrderBy ? "AddOrderByClause" : "AddThenByClause";
            sb.AppendLine($"        return {returnCastOpen}{builderVar}.{builderMethod}(@\"{escapedSql}\", direction){bitSuffix}{returnCastClose};");
        }
        else if (clauseInfo != null && clauseInfo.IsSuccess)
        {
            var escapedSql = InterceptorCodeGenerator.EscapeStringLiteral(clauseInfo.SqlFragment);
            var builderMethod = isOrderBy ? "AddOrderByClause" : "AddThenByClause";
            sb.AppendLine($"        return {returnCastOpen}{builderVar}.{builderMethod}(@\"{escapedSql}\", direction){bitSuffix}{returnCastClose};");
        }
        else
        {
            return;
        }

        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Emits a Select clause interceptor body.
    /// </summary>
    public static void EmitSelect(
        StringBuilder sb,
        TranslatedCallSite site,
        string methodName,
        AssembledPlan? prebuiltChain,
        bool isFirstInChain,
        CarrierPlan? carrier)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);
        // Prefer chain's enriched ProjectionInfo over site's discovery-time projection
        // (discovery-time ResultTypeName may be '?' when entity types are generator-produced)
        var projection = (prebuiltChain?.ProjectionInfo != null && prebuiltChain.ProjectionInfo.Columns.Count > 0)
            ? prebuiltChain.ProjectionInfo
            : site.ProjectionInfo;

        // Simplified prebuilt chain path: AsProjected instead of SelectWithReader
        if (prebuiltChain != null && projection != null && projection.IsOptimalPath && projection.Columns.Count > 0 && projection.ResultTypeName != "?")
        {
            var resultType = InterceptorCodeGenerator.GetShortTypeName(projection.ResultTypeName);
            var thisType = site.BuilderTypeName;
            var returnType = InterceptorCodeGenerator.ToReturnTypeName(thisType);
            var concreteType = InterceptorCodeGenerator.ToConcreteTypeName(returnType);
            sb.AppendLine($"    public static {returnType}<{entityType}, {resultType}> {methodName}(");
            sb.AppendLine($"        this {thisType}<{entityType}> builder,");
            sb.AppendLine($"        Func<{entityType}, {resultType}> _)");
            sb.AppendLine($"    {{");

            if (carrier != null)
            {
                var targetInterface = $"IQueryBuilder<{entityType}, {resultType}>";
                if (isFirstInChain)
                {
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
                    int? clauseBit = null;
                    CarrierEmitter.EmitCarrierChainEntry(sb, carrier, prebuiltChain, site, $"QueryBuilder<{entityType}>", targetInterface, clauseBit, siteParams, globalParamOffset);
                }
                else
                {
                    CarrierEmitter.EmitCarrierSelect(sb, targetInterface);
                }
                sb.AppendLine($"    }}");
                return;
            }

            var selectBuilderExpr = InterceptorCodeGenerator.IsEntityAccessorType(site.BuilderTypeName) ? InterceptorCodeGenerator.EntityAccessorToQueryBuilder(entityType) : "builder";
            sb.AppendLine($"        var __b = Unsafe.As<{concreteType}<{entityType}>>({selectBuilderExpr});");
            if (isFirstInChain && prebuiltChain.MaxParameterCount > 0)
                sb.AppendLine($"        __b.AllocatePrebuiltParams({prebuiltChain.MaxParameterCount});");
            sb.AppendLine($"        return __b.AsProjected<{resultType}>();");
            sb.AppendLine($"    }}");
            return;
        }

        // Non-prebuilt paths
        if (projection != null &&
            projection.IsOptimalPath &&
            projection.Columns.Count > 0 &&
            projection.ResultTypeName != "?")
        {
            EmitOptimalSelect(sb, site, methodName, entityType, projection);
        }
        else
        {
            EmitFallbackSelect(sb, site, methodName, entityType);
        }
    }

    private static void EmitOptimalSelect(
        StringBuilder sb, TranslatedCallSite site, string methodName,
        string entityType, ProjectionInfo projection)
    {
        var columnList = ReaderCodeGenerator.GenerateColumnList(projection, site.Dialect);
        var columnNames = ReaderCodeGenerator.GenerateColumnNamesArray(projection, site.Dialect);
        var readerDelegate = ReaderCodeGenerator.GenerateReaderDelegate(projection, entityType);

        {
            var resultType = InterceptorCodeGenerator.GetShortTypeName(projection.ResultTypeName);
            var thisType = site.BuilderTypeName;
            var returnType = InterceptorCodeGenerator.ToReturnTypeName(thisType);
            var concreteType = InterceptorCodeGenerator.ToConcreteTypeName(returnType);
            sb.AppendLine($"    public static {returnType}<{entityType}, {resultType}> {methodName}(");
            sb.AppendLine($"        this {thisType}<{entityType}> builder,");
            sb.AppendLine($"        Func<{entityType}, {resultType}> _)");
            sb.AppendLine($"    {{");
            sb.AppendLine($"        // Generated column list: {columnList}");
            var builderExpr = InterceptorCodeGenerator.IsEntityAccessorType(thisType) ? InterceptorCodeGenerator.EntityAccessorToQueryBuilder(entityType) : "builder";
            sb.AppendLine($"        var __b = Unsafe.As<{concreteType}<{entityType}>>({builderExpr});");
            sb.AppendLine($"        return __b.SelectWithReader(");
            sb.AppendLine($"            {columnNames},");
            sb.AppendLine($"            {readerDelegate});");
            sb.AppendLine($"    }}");
        }
    }

    private static void EmitFallbackSelect(
        StringBuilder sb, TranslatedCallSite site, string methodName, string entityType)
    {
        var thisType = site.BuilderTypeName;
        var returnType = InterceptorCodeGenerator.ToReturnTypeName(thisType);
        var concreteType = InterceptorCodeGenerator.ToConcreteTypeName(returnType);
        sb.AppendLine($"    public static {returnType}<T, TResult> {methodName}<T, TResult>(");
        sb.AppendLine($"        this {thisType}<T> builder,");
        sb.AppendLine($"        Func<T, TResult> selector) where T : class");
        sb.AppendLine($"    {{");
        sb.AppendLine($"        // Fallback path - projection not fully analyzed at compile time");
        var fallbackBuilderExpr = InterceptorCodeGenerator.IsEntityAccessorType(thisType) ? "((EntityAccessor<T>)(object)builder).CreateQueryBuilder()" : "builder";
        sb.AppendLine($"        var __b = Unsafe.As<{concreteType}<T>>({fallbackBuilderExpr});");
        sb.AppendLine($"        return __b.Select(selector);");
        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Emits a GroupBy clause interceptor body.
    /// </summary>
    public static void EmitGroupBy(
        StringBuilder sb,
        TranslatedCallSite site,
        string methodName,
        int? clauseBit,
        AssembledPlan? prebuiltChain,
        bool isFirstInChain,
        CarrierPlan? carrier)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);
        var clauseInfo = site.Clause;

        var keyType = site.KeyTypeName != null ? InterceptorCodeGenerator.GetShortTypeName(site.KeyTypeName) : null;

        var thisType = site.BuilderTypeName;
        var returnType = InterceptorCodeGenerator.ToReturnTypeName(thisType);
        var concreteType = InterceptorCodeGenerator.ToConcreteTypeName(returnType);

        if (site.ResultTypeName != null)
        {
            var resultType = InterceptorCodeGenerator.SanitizeTupleResultType(InterceptorCodeGenerator.GetShortTypeName(site.ResultTypeName));
            var isBrokenTuple = resultType.Contains("object") && resultType.StartsWith("(");
            if (isBrokenTuple)
                keyType = null;

            if (keyType != null)
            {
                sb.AppendLine($"    public static {returnType}<{entityType}, {resultType}> {methodName}(");
                sb.AppendLine($"        this {thisType}<{entityType}, {resultType}> builder,");
                sb.AppendLine($"        Expression<Func<{entityType}, {keyType}>> _)");
            }
            else
            {
                sb.AppendLine($"    public static {returnType}<T, TResult> {methodName}<T, TResult, TKey>(");
                sb.AppendLine($"        this {thisType}<T, TResult> builder,");
                sb.AppendLine($"        Expression<Func<T, TKey>> _) where T : class");
            }
        }
        else
        {
            if (keyType != null)
            {
                sb.AppendLine($"    public static {returnType}<{entityType}> {methodName}(");
                sb.AppendLine($"        this {thisType}<{entityType}> builder,");
                sb.AppendLine($"        Expression<Func<{entityType}, {keyType}>> _)");
            }
            else
            {
                sb.AppendLine($"    public static {returnType}<T> {methodName}<T, TKey>(");
                sb.AppendLine($"        this {thisType}<T> builder,");
                sb.AppendLine($"        Expression<Func<T, TKey>> _) where T : class");
            }
        }

        sb.AppendLine($"    {{");

        if (clauseInfo == null || !clauseInfo.IsSuccess)
        {
            // Fallback: return the builder unchanged with closing brace
            sb.AppendLine($"        return Unsafe.As<{concreteType}<{entityType}>>(builder);");
            sb.AppendLine($"    }}");
            return;
        }

        // Carrier-optimized path (only with concrete key type)
        if (carrier != null && prebuiltChain != null && keyType != null)
        {
            var concreteBuilder = $"QueryBuilder<{entityType}>";
            var retInterface = site.ResultTypeName != null
                ? $"IQueryBuilder<{entityType}, {InterceptorCodeGenerator.SanitizeTupleResultType(InterceptorCodeGenerator.GetShortTypeName(site.ResultTypeName))}>"
                : $"IQueryBuilder<{entityType}>";
            CarrierEmitter.EmitCarrierClauseBody(sb, carrier, prebuiltChain, site, clauseBit, isFirstInChain,
                concreteBuilder, retInterface, false, new List<InterceptorCodeGenerator.CachedExtractorField>());
            sb.AppendLine($"    }}");
            return;
        }

        // Cast to concrete type
        if (site.ResultTypeName != null)
        {
            var resultType = InterceptorCodeGenerator.SanitizeTupleResultType(InterceptorCodeGenerator.GetShortTypeName(site.ResultTypeName));
            sb.AppendLine($"        var __b = Unsafe.As<{concreteType}<{entityType}, {resultType}>>(builder);");
        }
        else if (keyType != null)
        {
            sb.AppendLine($"        var __b = Unsafe.As<{concreteType}<{entityType}>>(builder);");
        }
        else
        {
            sb.AppendLine($"        var __b = Unsafe.As<{concreteType}<T>>(builder);");
        }
        var builderVar = "__b";

        // Simplified prebuilt chain path
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

        {
            var escapedSql = InterceptorCodeGenerator.EscapeStringLiteral(clauseInfo.SqlFragment);
            var bitSuffix = InterceptorCodeGenerator.ClauseBitSuffix(clauseBit);

            var needsReturnCast = keyType == null;
            string returnCastOpen = "", returnCastClose = "";
            if (needsReturnCast)
            {
                var castTarget = site.ResultTypeName != null ? $"{returnType}<T, TResult>" : $"{returnType}<T>";
                returnCastOpen = $"Unsafe.As<{castTarget}>(";
                returnCastClose = ")";
            }

            sb.AppendLine($"        return {returnCastOpen}{builderVar}.AddGroupByClause(@\"{escapedSql}\"){bitSuffix}{returnCastClose};");
        }

        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Emits a Having clause interceptor body.
    /// </summary>
    public static void EmitHaving(
        StringBuilder sb,
        TranslatedCallSite site,
        string methodName,
        int? clauseBit,
        AssembledPlan? prebuiltChain,
        bool isFirstInChain,
        CarrierPlan? carrier)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);
        var clauseInfo = site.Clause;

        var thisType = site.BuilderTypeName;
        var returnType = InterceptorCodeGenerator.ToReturnTypeName(thisType);
        var concreteType = InterceptorCodeGenerator.ToConcreteTypeName(returnType);

        if (site.ResultTypeName != null)
        {
            var resultType = InterceptorCodeGenerator.SanitizeTupleResultType(InterceptorCodeGenerator.GetShortTypeName(site.ResultTypeName));
            sb.AppendLine($"    public static {returnType}<{entityType}, {resultType}> {methodName}(");
            sb.AppendLine($"        this {thisType}<{entityType}, {resultType}> builder,");
            sb.AppendLine($"        Expression<Func<{entityType}, bool>> _)");
        }
        else
        {
            sb.AppendLine($"    public static {returnType}<{entityType}> {methodName}(");
            sb.AppendLine($"        this {thisType}<{entityType}> builder,");
            sb.AppendLine($"        Expression<Func<{entityType}, bool>> _)");
        }

        sb.AppendLine($"    {{");

        if (clauseInfo == null || !clauseInfo.IsSuccess)
        {
            // Fallback: return the builder unchanged with closing brace
            sb.AppendLine($"        return Unsafe.As<{concreteType}<{entityType}>>(builder);");
            sb.AppendLine($"    }}");
            return;
        }

        // Carrier-optimized path
        if (carrier != null && prebuiltChain != null)
        {
            var concreteBuilder = $"QueryBuilder<{entityType}>";
            var retInterface = site.ResultTypeName != null
                ? $"IQueryBuilder<{entityType}, {InterceptorCodeGenerator.SanitizeTupleResultType(InterceptorCodeGenerator.GetShortTypeName(site.ResultTypeName))}>"
                : $"IQueryBuilder<{entityType}>";
            CarrierEmitter.EmitCarrierClauseBody(sb, carrier, prebuiltChain, site, clauseBit, isFirstInChain,
                concreteBuilder, retInterface, false, new List<InterceptorCodeGenerator.CachedExtractorField>());
            sb.AppendLine($"    }}");
            return;
        }

        // Cast to concrete type
        if (site.ResultTypeName != null)
        {
            var resultType = InterceptorCodeGenerator.SanitizeTupleResultType(InterceptorCodeGenerator.GetShortTypeName(site.ResultTypeName));
            sb.AppendLine($"        var __b = Unsafe.As<{concreteType}<{entityType}, {resultType}>>(builder);");
        }
        else
        {
            sb.AppendLine($"        var __b = Unsafe.As<{concreteType}<{entityType}>>(builder);");
        }
        var builderVar = "__b";

        // Simplified prebuilt chain path
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

        {
            var escapedSql = InterceptorCodeGenerator.EscapeStringLiteral(clauseInfo.SqlFragment);
            var bitSuffix = InterceptorCodeGenerator.ClauseBitSuffix(clauseBit);
            sb.AppendLine($"        return {builderVar}.AddHavingClause(@\"{escapedSql}\"){bitSuffix};");
        }

        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Emits a Set clause interceptor body (for IQueryBuilder Set).
    /// </summary>
    public static void EmitSet(
        StringBuilder sb,
        TranslatedCallSite site,
        string methodName,
        int? clauseBit,
        AssembledPlan? prebuiltChain,
        bool isFirstInChain,
        CarrierPlan? carrier)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);
        var clauseInfo = site.Clause;

        var thisType = site.BuilderTypeName;
        var returnType = InterceptorCodeGenerator.ToReturnTypeName(thisType);
        var concreteType = InterceptorCodeGenerator.ToConcreteTypeName(returnType);

        // Carrier-optimized path
        var resolvedValueType = site.ValueTypeName;
        if (carrier != null && prebuiltChain != null && resolvedValueType != null)
        {
            sb.AppendLine($"    public static {returnType}<{entityType}> {methodName}(");
            sb.AppendLine($"        this {returnType}<{entityType}> builder,");
            sb.AppendLine($"        Expression<Func<{entityType}, {resolvedValueType}>> _,");
            sb.AppendLine($"        {resolvedValueType} value)");
            sb.AppendLine($"    {{");

            var concreteBuilder = $"{concreteType}<{entityType}>";
            var returnInterface = $"{returnType}<{entityType}>";
            CarrierEmitter.EmitCarrierClauseBody(sb, carrier, prebuiltChain, site, clauseBit, isFirstInChain,
                concreteBuilder, returnInterface, false, new List<InterceptorCodeGenerator.CachedExtractorField>());
            sb.AppendLine($"    }}");
            return;
        }

        sb.AppendLine($"    public static {returnType}<{entityType}> {methodName}<TValue>(");
        sb.AppendLine($"        this {thisType}<{entityType}> builder,");
        sb.AppendLine($"        Expression<Func<{entityType}, TValue>> _,");
        sb.AppendLine($"        TValue value)");
        sb.AppendLine($"    {{");

        sb.AppendLine($"        var __b = Unsafe.As<{concreteType}<{entityType}>>(builder);");
        var builderVar = "__b";

        // Simplified prebuilt chain path
        if (prebuiltChain != null)
        {
            if (isFirstInChain && prebuiltChain.MaxParameterCount > 0)
                sb.AppendLine($"        {builderVar}.AllocatePrebuiltParams({prebuiltChain.MaxParameterCount});");
            var setValueArg = (clauseInfo?.CustomTypeMappingClass != null)
                ? $"{InterceptorCodeGenerator.GetMappingFieldName(clauseInfo.CustomTypeMappingClass)}.ToDb(value)"
                : "value";
            sb.AppendLine($"        {builderVar}.BindParam({setValueArg});");
            if (clauseBit.HasValue)
                sb.AppendLine($"        return {builderVar}.SetClauseBit({clauseBit.Value});");
            else
                sb.AppendLine($"        return {builderVar};");
            sb.AppendLine($"    }}");
            return;
        }

        if (clauseInfo is { Kind: ClauseKind.Set, IsSuccess: true } && clauseInfo.Parameters.Count > 0)
        {
            var escapedColumnSql = InterceptorCodeGenerator.EscapeStringLiteral(clauseInfo.ColumnSql);
            var valueArg = clauseInfo.CustomTypeMappingClass != null
                ? $"{InterceptorCodeGenerator.GetMappingFieldName(clauseInfo.CustomTypeMappingClass)}.ToDb(value)"
                : "value";
            var bitSuffix = InterceptorCodeGenerator.ClauseBitSuffix(clauseBit);
            sb.AppendLine($"        return {builderVar}.AddSetClause(@\"{escapedColumnSql}\", {valueArg}, {clauseInfo.Parameters[0].Index}){bitSuffix};");
        }
        else if (clauseInfo != null && clauseInfo.IsSuccess)
        {
            var escapedSql = InterceptorCodeGenerator.EscapeStringLiteral(clauseInfo.SqlFragment);
            var bitSuffix = InterceptorCodeGenerator.ClauseBitSuffix(clauseBit);
            sb.AppendLine($"        return {builderVar}.AddSetClauseRaw(@\"{escapedSql}\", value){bitSuffix};");
        }
        else
        {
            return;
        }

        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Emits a modification Where interceptor (DeleteWhere/UpdateWhere).
    /// </summary>
    public static void EmitModificationWhere(
        StringBuilder sb,
        TranslatedCallSite site,
        string methodName,
        List<InterceptorCodeGenerator.CachedExtractorField>? methodFields,
        bool isDelete,
        int? clauseBit,
        AssembledPlan? prebuiltChain,
        bool isFirstInChain,
        CarrierPlan? carrier)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);
        var clauseInfo = site.Clause;
        var modKind = isDelete ? "Delete" : "Update";

        var hasAnyParams = clauseInfo?.Parameters.Count > 0;
        var hasResolvableCapturedParams = clauseInfo?.Parameters.Any(p => p.IsCaptured && p.CanGenerateDirectPath) == true;
        var exprParamName = hasResolvableCapturedParams ? "expr" : "_";

        methodFields ??= new List<InterceptorCodeGenerator.CachedExtractorField>();
        if (methodFields.Count > 0)
        {
            sb.AppendLine($"    [UnconditionalSuppressMessage(\"Trimming\", \"IL2075\",");
            sb.AppendLine($"        Justification = \"Closure fields are preserved by the expression tree that references them.\")]");
        }

        var thisType = site.BuilderTypeName;
        var returnType = InterceptorCodeGenerator.ToReturnTypeName(thisType);
        var isExecutable = site.BuilderKind is BuilderKind.ExecutableDelete or BuilderKind.ExecutableUpdate;
        var concreteType = InterceptorCodeGenerator.ToConcreteTypeName(returnType);
        var receiverType = $"{thisType}<{entityType}>";

        sb.AppendLine($"    public static IExecutable{modKind}Builder<{entityType}> {methodName}(");
        sb.AppendLine($"        this {receiverType} builder,");
        sb.AppendLine($"        Expression<Func<{entityType}, bool>> {exprParamName})");
        sb.AppendLine($"    {{");

        if (clauseInfo == null || !clauseInfo.IsSuccess)
        {
            // Fallback: return the builder unchanged with closing brace
            sb.AppendLine($"        return Unsafe.As<{concreteType}<{entityType}>>(builder);");
            sb.AppendLine($"    }}");
            return;
        }

        // Carrier-optimized path
        if (carrier != null && prebuiltChain != null)
        {
            var concreteBuilder = $"{concreteType}<{entityType}>";
            var returnInterface = $"IExecutable{modKind}Builder<{entityType}>";
            CarrierEmitter.EmitCarrierClauseBody(sb, carrier, prebuiltChain, site, clauseBit, isFirstInChain,
                concreteBuilder, returnInterface, hasResolvableCapturedParams, methodFields);
            sb.AppendLine($"    }}");
            return;
        }

        sb.AppendLine($"        var __b = Unsafe.As<{concreteType}<{entityType}>>(builder);");
        var builderVar = "__b";

        // Simplified prebuilt chain path
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
                    sb.AppendLine($"        {builderVar}.BindParam({InterceptorCodeGenerator.WrapWithToDb(expr, p)});");
                }
            }

            var returnExpr = isExecutable ? builderVar : $"{builderVar}.AsExecutable()";
            if (clauseBit.HasValue)
                sb.AppendLine($"        return {returnExpr}.SetClauseBit({clauseBit.Value});");
            else
                sb.AppendLine($"        return {returnExpr};");
            sb.AppendLine($"    }}");
            return;
        }

        {
            var escapedSql = InterceptorCodeGenerator.EscapeStringLiteral(clauseInfo.SqlFragment);
            var hasUnresolvableCaptured = clauseInfo.Parameters.Any(p => p.IsCaptured && !p.CanGenerateDirectPath);
            var bitSuffix = InterceptorCodeGenerator.ClauseBitSuffix(clauseBit);

            if (hasUnresolvableCaptured)
            {
                var resolvableParams = clauseInfo.Parameters
                    .Where(p => !p.IsCaptured)
                    .OrderBy(p => p.Index)
                    .ToList();
                if (resolvableParams.Count > 0)
                {
                    var transformedSql = escapedSql;
                    for (int i = resolvableParams.Count - 1; i >= 0; i--)
                    {
                        var p = resolvableParams[i];
                        sb.AppendLine($"        var _pi{p.Index} = {builderVar}.AddParameter({InterceptorCodeGenerator.WrapWithToDb(p.ValueExpression, p)});");
                        transformedSql = transformedSql.Replace($"@p{p.Index}", $"@p{{_pi{p.Index}}}");
                    }
                    sb.AppendLine($"        return {builderVar}.AddWhereClause($@\"{transformedSql}\"){bitSuffix};");
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
                var transformedSql = escapedSql;
                for (int i = allParams.Count - 1; i >= 0; i--)
                {
                    var p = allParams[i];
                    var valueExpr = p.IsCaptured ? $"p{p.Index}" : p.ValueExpression;
                    sb.AppendLine($"        var _pi{p.Index} = {builderVar}.AddParameter({InterceptorCodeGenerator.WrapWithToDb(valueExpr, p)});");
                    transformedSql = transformedSql.Replace($"@p{p.Index}", $"@p{{_pi{p.Index}}}");
                }
                sb.AppendLine($"        return {builderVar}.AddWhereClause($@\"{transformedSql}\"){bitSuffix};");
            }
            else
            {
                sb.AppendLine($"        return {builderVar}.AddWhereClause(@\"{escapedSql}\"){bitSuffix};");
            }
        }

        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Emits an UpdateSet interceptor for UpdateBuilder or ExecutableUpdateBuilder.
    /// </summary>
    public static void EmitUpdateSet(
        StringBuilder sb,
        TranslatedCallSite site,
        string methodName,
        int? clauseBit,
        AssembledPlan? prebuiltChain,
        bool isFirstInChain,
        CarrierPlan? carrier)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);
        var clauseInfo = site.Clause;

        var thisType = site.BuilderTypeName;
        var returnType = InterceptorCodeGenerator.ToReturnTypeName(thisType);
        var isExecutable = site.BuilderKind is BuilderKind.ExecutableUpdate;
        var concreteBaseName = isExecutable ? "ExecutableUpdateBuilder" : "UpdateBuilder";
        var returnInterfaceBaseName = "I" + concreteBaseName;

        // Carrier-optimized path
        var resolvedValueType = site.ValueTypeName;
        if (carrier != null && prebuiltChain != null && resolvedValueType != null)
        {
            sb.AppendLine($"    public static {returnInterfaceBaseName}<{entityType}> {methodName}(");
            sb.AppendLine($"        this {returnInterfaceBaseName}<{entityType}> builder,");
            sb.AppendLine($"        Expression<Func<{entityType}, {resolvedValueType}>> _,");
            sb.AppendLine($"        {resolvedValueType} value)");
            sb.AppendLine($"    {{");

            var concreteBuilder = $"{concreteBaseName}<{entityType}>";
            var returnInterface = $"{returnInterfaceBaseName}<{entityType}>";
            CarrierEmitter.EmitCarrierClauseBody(sb, carrier, prebuiltChain, site, clauseBit, isFirstInChain,
                concreteBuilder, returnInterface, false, new List<InterceptorCodeGenerator.CachedExtractorField>());
            sb.AppendLine($"    }}");
            return;
        }

        sb.AppendLine($"    public static {returnInterfaceBaseName}<T> {methodName}<T, TValue>(");
        sb.AppendLine($"        this {thisType}<T> builder,");
        sb.AppendLine($"        Expression<Func<T, TValue>> _,");
        sb.AppendLine($"        TValue value) where T : class");
        sb.AppendLine($"    {{");

        sb.AppendLine($"        var __b = Unsafe.As<{concreteBaseName}<T>>(builder);");
        var builderVar = "__b";

        // Simplified prebuilt chain path
        if (prebuiltChain != null)
        {
            if (isFirstInChain && prebuiltChain.MaxParameterCount > 0)
                sb.AppendLine($"        {builderVar}.AllocatePrebuiltParams({prebuiltChain.MaxParameterCount});");
            sb.AppendLine($"        {builderVar}.BindParam(value);");
            if (clauseBit.HasValue)
                sb.AppendLine($"        return {builderVar}.SetClauseBit({clauseBit.Value});");
            else
                sb.AppendLine($"        return {builderVar};");
            sb.AppendLine($"    }}");
            return;
        }

        var bitSuffix = InterceptorCodeGenerator.ClauseBitSuffix(clauseBit);
        if (clauseInfo is { Kind: ClauseKind.Set, IsSuccess: true })
        {
            var escapedColumnSql = InterceptorCodeGenerator.EscapeStringLiteral(clauseInfo.ColumnSql);
            sb.AppendLine($"        return {builderVar}.AddSetClause(@\"{escapedColumnSql}\", value){bitSuffix};");
        }
        else if (clauseInfo != null && clauseInfo.IsSuccess)
        {
            var escapedColumnSql = InterceptorCodeGenerator.EscapeStringLiteral(clauseInfo.SqlFragment);
            sb.AppendLine($"        return {builderVar}.AddSetClause(@\"{escapedColumnSql}\", value){bitSuffix};");
        }
        else
        {
            return;
        }

        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Emits an UpdateSetAction interceptor (Set with Action&lt;T&gt; lambda).
    /// </summary>
    public static void EmitUpdateSetAction(
        StringBuilder sb,
        TranslatedCallSite site,
        string methodName,
        int? clauseBit,
        AssembledPlan? prebuiltChain,
        bool isFirstInChain,
        CarrierPlan? carrier)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);
        var clauseInfo = site.Clause;

        var thisType = site.BuilderTypeName;
        var isExecutable = site.BuilderKind is BuilderKind.ExecutableUpdate;
        var concreteBaseName = isExecutable ? "ExecutableUpdateBuilder" : "UpdateBuilder";
        var returnInterfaceBaseName = "I" + concreteBaseName;

        if (clauseInfo is not { SetAssignments: not null, IsSuccess: true })
            return;

        var hasCapturedParams = clauseInfo.Parameters.Any(p => p.IsCaptured);
        var actionParamName = hasCapturedParams ? "action" : "_";

        // Carrier-optimized path
        if (carrier != null && prebuiltChain != null)
        {
            sb.AppendLine($"    public static {returnInterfaceBaseName}<{entityType}> {methodName}(");
            sb.AppendLine($"        this {returnInterfaceBaseName}<{entityType}> builder,");
            sb.AppendLine($"        Action<{entityType}> {actionParamName})");
            sb.AppendLine($"    {{");

            var globalParamOffset = 0;
            foreach (var clause in prebuiltChain.GetClauseEntries())
            {
                if (clause.Site.UniqueId == site.UniqueId)
                    break;
                if (clause.Site.Kind == InterceptorKind.UpdateSetPoco && clause.Site.UpdateInfo != null)
                    globalParamOffset += clause.Site.UpdateInfo.Columns.Count;
                else if (clause.Site.Clause != null)
                    globalParamOffset += clause.Site.Clause.Parameters.Count;
            }

            if (isFirstInChain)
            {
                var concreteBuilder = $"{concreteBaseName}<{entityType}>";
                sb.AppendLine($"        var __b = Unsafe.As<{concreteBuilder}>(builder);");
                sb.AppendLine($"        var __c = new {carrier.ClassName} {{ Ctx = __b.State.ExecutionContext }};");
            }
            else
            {
                sb.AppendLine($"        var __c = Unsafe.As<{carrier.ClassName}>(builder);");
            }

            for (int i = 0; i < clauseInfo.Parameters.Count; i++)
            {
                var p = clauseInfo.Parameters[i];
                var globalIdx = globalParamOffset + i;
                if (globalIdx >= prebuiltChain.ChainParameters.Count) continue;
                var carrierParam = prebuiltChain.ChainParameters[globalIdx];

                if (p.IsCaptured)
                {
                    sb.AppendLine($"        {carrier.ClassName}.F{globalIdx} ??= action.Target!.GetType().GetField(\"{p.ValueExpression}\")!;");
                    sb.AppendLine($"        __c.P{globalIdx} = ({carrierParam.ClrType}){carrier.ClassName}.F{globalIdx}.GetValue(action.Target)!;");
                }
                else
                {
                    sb.AppendLine($"        __c.P{globalIdx} = ({carrierParam.ClrType}){p.ValueExpression}!;");
                }
            }

            if (clauseBit.HasValue)
                sb.AppendLine($"        __c.Mask |= unchecked(({CarrierEmitter.GetMaskType(prebuiltChain)})(1 << {clauseBit.Value}));");

            var returnInterface = $"{returnInterfaceBaseName}<{entityType}>";
            if (isFirstInChain)
                sb.AppendLine($"        return Unsafe.As<{returnInterface}>(__c);");
            else
                sb.AppendLine($"        return Unsafe.As<{returnInterface}>(builder);");

            sb.AppendLine($"    }}");
            return;
        }

        // Non-carrier prebuilt chain path
        if (prebuiltChain != null)
        {
            sb.AppendLine($"    public static {returnInterfaceBaseName}<T> {methodName}<T>(");
            sb.AppendLine($"        this {thisType}<T> builder,");
            sb.AppendLine($"        Action<T> {actionParamName}) where T : class");
            sb.AppendLine($"    {{");

            sb.AppendLine($"        var __b = Unsafe.As<{concreteBaseName}<T>>(builder);");

            if (isFirstInChain && prebuiltChain.MaxParameterCount > 0)
                sb.AppendLine($"        __b.AllocatePrebuiltParams({prebuiltChain.MaxParameterCount});");

            for (int i = 0; i < clauseInfo.Parameters.Count; i++)
            {
                var p = clauseInfo.Parameters[i];
                if (p.IsCaptured)
                {
                    sb.AppendLine($"        __b.BindParam(action.Target!.GetType().GetField(\"{p.ValueExpression}\")!.GetValue(action.Target));");
                }
                else
                {
                    sb.AppendLine($"        __b.BindParam({p.ValueExpression});");
                }
            }

            if (clauseBit.HasValue)
                sb.AppendLine($"        return __b.SetClauseBit({clauseBit.Value});");
            else
                sb.AppendLine($"        return __b;");
            sb.AppendLine($"    }}");
            return;
        }

        // Standalone path
        sb.AppendLine($"    public static {returnInterfaceBaseName}<T> {methodName}<T>(");
        sb.AppendLine($"        this {thisType}<T> builder,");
        sb.AppendLine($"        Action<T> {actionParamName}) where T : class");
        sb.AppendLine($"    {{");

        sb.AppendLine($"        var __b = Unsafe.As<{concreteBaseName}<T>>(builder);");
        var bitSuffix2 = InterceptorCodeGenerator.ClauseBitSuffix(clauseBit);

        // Build column name lookup for quoting
        var columnLookup = new Dictionary<string, string>(StringComparer.Ordinal);
        if (site.Bound.Entity != null)
        {
            foreach (var col in site.Bound.Entity.Columns)
            {
                columnLookup[col.PropertyName] = col.ColumnName;
                columnLookup[col.ColumnName] = col.ColumnName;
            }
        }

        var paramIdx = 0;
        for (int i = 0; i < clauseInfo.SetAssignments.Count; i++)
        {
            var assignment = clauseInfo.SetAssignments[i];
            // Resolve and quote column name: discovery stores unquoted property name
            var propertyName = assignment.ColumnSql.Trim('"', '[', ']', '`');
            var resolvedName = columnLookup.TryGetValue(propertyName, out var colName) ? colName : propertyName;
            var quotedColumn = Sql.SqlFormatting.QuoteIdentifier(site.Bound.Dialect, resolvedName);
            var escapedColumnSql = InterceptorCodeGenerator.EscapeStringLiteral(quotedColumn);

            if (assignment.IsInlined)
            {
                sb.AppendLine($"        __b.AddSetClauseBoxed(@\"{escapedColumnSql}\", {assignment.InlinedCSharpExpression});");
                continue;
            }

            var p = clauseInfo.Parameters[paramIdx++];
            string valueExpr;
            if (p.IsCaptured)
            {
                valueExpr = $"action.Target!.GetType().GetField(\"{p.ValueExpression}\")!.GetValue(action.Target)";
            }
            else
            {
                valueExpr = p.ValueExpression;
            }

            if (assignment.CustomTypeMappingClass != null)
                valueExpr = $"{InterceptorCodeGenerator.GetMappingFieldName(assignment.CustomTypeMappingClass)}.ToDb({valueExpr})";

            sb.AppendLine($"        __b.AddSetClauseBoxed(@\"{escapedColumnSql}\", {valueExpr});");
        }

        sb.AppendLine($"        return __b{bitSuffix2};");
        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Emits an UpdateSetPoco interceptor (Set with entity POCO).
    /// </summary>
    public static void EmitUpdateSetPoco(
        StringBuilder sb,
        TranslatedCallSite site,
        string methodName,
        int? clauseBit,
        AssembledPlan? prebuiltChain,
        bool isFirstInChain,
        CarrierPlan? carrier)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(site.EntityTypeName);
        var updateInfo = site.UpdateInfo;

        var thisType = site.BuilderTypeName;
        var isExecutable = site.BuilderKind is BuilderKind.ExecutableUpdate;
        var concreteBaseName = isExecutable ? "ExecutableUpdateBuilder" : "UpdateBuilder";
        var returnInterfaceBaseName = "I" + concreteBaseName;

        // Carrier-optimized path
        if (carrier != null && prebuiltChain != null && updateInfo != null && updateInfo.Columns.Count > 0)
        {
            sb.AppendLine($"    public static {returnInterfaceBaseName}<{entityType}> {methodName}(");
            sb.AppendLine($"        this {returnInterfaceBaseName}<{entityType}> builder,");
            sb.AppendLine($"        {entityType} entity)");
            sb.AppendLine($"    {{");

            if (isFirstInChain)
            {
                var concreteBuilder = $"{concreteBaseName}<{entityType}>";
                sb.AppendLine($"        var __b = Unsafe.As<{concreteBuilder}>(builder);");
                sb.AppendLine($"        var __c = new {carrier.ClassName} {{ Ctx = __b.State.ExecutionContext }};");
            }
            else
            {
                sb.AppendLine($"        var __c = Unsafe.As<{carrier.ClassName}>(builder);");
            }

            sb.AppendLine($"        __c.Entity = entity;");

            if (clauseBit.HasValue)
                sb.AppendLine($"        __c.Mask |= unchecked(({CarrierEmitter.GetMaskType(prebuiltChain)})(1 << {clauseBit.Value}));");

            var returnInterface = $"{returnInterfaceBaseName}<{entityType}>";
            if (isFirstInChain)
                sb.AppendLine($"        return Unsafe.As<{returnInterface}>(__c);");
            else
                sb.AppendLine($"        return Unsafe.As<{returnInterface}>(builder);");

            sb.AppendLine($"    }}");
            return;
        }

        sb.AppendLine($"    public static {returnInterfaceBaseName}<T> {methodName}<T>(");
        sb.AppendLine($"        this {thisType}<T> builder,");
        sb.AppendLine($"        T entity) where T : class");
        sb.AppendLine($"    {{");

        if (updateInfo != null && updateInfo.Columns.Count > 0)
        {
            sb.AppendLine($"        var __b = Unsafe.As<{concreteBaseName}<T>>(builder);");
            var builderVar = "__b";

            // Simplified prebuilt chain path
            if (prebuiltChain != null)
            {
                if (isFirstInChain && prebuiltChain.MaxParameterCount > 0)
                    sb.AppendLine($"        {builderVar}.AllocatePrebuiltParams({prebuiltChain.MaxParameterCount});");

                sb.AppendLine($"        var e = Unsafe.As<{entityType}>(entity);");

                foreach (var column in updateInfo.Columns)
                {
                    var valueExpr = InterceptorCodeGenerator.GetColumnValueExpression("e", column.PropertyName, column.IsForeignKey, column.CustomTypeMappingClass);
                    sb.AppendLine($"        {builderVar}.BindParam({valueExpr});");
                }

                if (clauseBit.HasValue)
                    sb.AppendLine($"        return {builderVar}.SetClauseBit({clauseBit.Value});");
                else
                    sb.AppendLine($"        return {builderVar};");
                sb.AppendLine($"    }}");
                return;
            }

            sb.AppendLine($"        var e = Unsafe.As<{entityType}>(entity);");

            foreach (var column in updateInfo.Columns)
            {
                var escapedColumnSql = InterceptorCodeGenerator.EscapeStringLiteral(column.QuotedColumnName);
                var valueExpr = InterceptorCodeGenerator.GetColumnValueExpression("e", column.PropertyName, column.IsForeignKey, column.CustomTypeMappingClass);
                var sensitiveArg = column.IsSensitive ? ", isSensitive: true" : "";
                sb.AppendLine($"        {builderVar}.AddSetClause(@\"{escapedColumnSql}\", {valueExpr}{sensitiveArg});");
            }

            sb.AppendLine($"        return {builderVar};");
        }
        else
        {
            return;
        }

        sb.AppendLine($"    }}");
    }
}
