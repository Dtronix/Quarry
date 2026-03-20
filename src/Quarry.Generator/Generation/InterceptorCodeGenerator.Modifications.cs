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
    /// Generates a Where() interceptor for Delete or Update builders.
    /// Handles both DeleteBuilder/ExecutableDeleteBuilder and UpdateBuilder/ExecutableUpdateBuilder.
    /// The return type is always IExecutableDeleteBuilder&lt;T&gt; or IExecutableUpdateBuilder&lt;T&gt;
    /// since Where() transitions the builder to the executable variant.
    /// </summary>
    private static void GenerateModificationWhereInterceptor(
        StringBuilder sb, UsageSiteInfo site, string methodName,
        List<CachedExtractorField>? methodFields, bool isDelete,
        int? clauseBit = null, PrebuiltChainInfo? prebuiltChain = null,
        bool isFirstInChain = false, CarrierClassInfo? carrier = null)
    {
        var entityType = GetShortTypeName(site.EntityTypeName);
        var clauseInfo = site.ClauseInfo;
        var modKind = isDelete ? "Delete" : "Update";

        // Check if there are captured parameters that need runtime extraction
        var hasAnyParams = clauseInfo?.Parameters.Count > 0;
        var hasResolvableCapturedParams = clauseInfo?.Parameters.Any(p => p.IsCaptured && p.CanGenerateDirectPath) == true;
        var exprParamName = hasResolvableCapturedParams ? "expr" : "_";

        // Emit trim suppression if we'll use FieldInfo.GetValue inline
        methodFields ??= new List<CachedExtractorField>();
        if (methodFields.Count > 0)
        {
            sb.AppendLine($"    [UnconditionalSuppressMessage(\"Trimming\", \"IL2075\",");
            sb.AppendLine($"        Justification = \"Closure fields are preserved by the expression tree that references them.\")]");
        }

        var thisType = site.BuilderTypeName;
        var returnType = ToReturnTypeName(thisType);
        var isExecutable = site.BuilderKind is BuilderKind.ExecutableDelete or BuilderKind.ExecutableUpdate;
        var concreteType = ToConcreteTypeName(returnType);
        var receiverType = $"{thisType}<{entityType}>";

        sb.AppendLine($"    public static IExecutable{modKind}Builder<{entityType}> {methodName}(");
        sb.AppendLine($"        this {receiverType} builder,");
        sb.AppendLine($"        Expression<Func<{entityType}, bool>> {exprParamName})");
        sb.AppendLine($"    {{");

        if (clauseInfo == null || !clauseInfo.IsSuccess)
        {
            // Non-translatable clause — skip interceptor entirely so the original method runs
            return;
        }

        // Carrier-optimized path
        if (carrier != null && prebuiltChain != null)
        {
            var concreteBuilder = $"{concreteType}<{entityType}>";
            var returnInterface = $"IExecutable{modKind}Builder<{entityType}>";
            EmitCarrierClauseBody(sb, carrier, prebuiltChain, site, clauseBit, isFirstInChain,
                concreteBuilder, returnInterface, hasResolvableCapturedParams, methodFields);
            sb.AppendLine($"    }}");
            return;
        }

        // Cast to concrete type (receiver is always an interface)
        sb.AppendLine($"        var __b = Unsafe.As<{concreteType}<{entityType}>>(builder);");
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
                    sb.AppendLine($"        {builderVar}.BindParam({WrapWithToDb(expr, p)});");
                }
            }

            // Non-executable receiver must transition to executable via AsExecutable()
            var returnExpr = isExecutable ? builderVar : $"{builderVar}.AsExecutable()";
            if (clauseBit.HasValue)
                sb.AppendLine($"        return {returnExpr}.SetClauseBit({clauseBit.Value});");
            else
                sb.AppendLine($"        return {returnExpr};");
            sb.AppendLine($"    }}");
            return;
        }

        {
            // Standalone path: pre-computed SQL with AddParameter index capture
            var escapedSql = EscapeStringLiteral(clauseInfo.SqlFragment);
            var hasUnresolvableCaptured = clauseInfo.Parameters.Any(p => p.IsCaptured && !p.CanGenerateDirectPath);
            var bitSuffix = ClauseBitSuffix(clauseBit);

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
                        sb.AppendLine($"        var _pi{p.Index} = {builderVar}.AddParameter({WrapWithToDb(p.ValueExpression, p)});");
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
                    GenerateCachedExtraction(sb, methodFields);

                var allParams = clauseInfo.Parameters.OrderBy(p => p.Index).ToList();
                var transformedSql = escapedSql;
                for (int i = allParams.Count - 1; i >= 0; i--)
                {
                    var p = allParams[i];
                    var valueExpr = p.IsCaptured ? $"p{p.Index}" : p.ValueExpression;
                    sb.AppendLine($"        var _pi{p.Index} = {builderVar}.AddParameter({WrapWithToDb(valueExpr, p)});");
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
    /// Generates a Set() interceptor with SQL fragment.
    /// </summary>
    private static void GenerateSetInterceptor(StringBuilder sb, UsageSiteInfo site, string methodName, int? clauseBit = null,
        PrebuiltChainInfo? prebuiltChain = null, bool isFirstInChain = false, CarrierClassInfo? carrier = null)
    {
        var entityType = GetShortTypeName(site.EntityTypeName);
        var clauseInfo = site.ClauseInfo;

        var thisType = site.BuilderTypeName;
        var returnType = ToReturnTypeName(thisType);
        var concreteType = ToConcreteTypeName(returnType);

        // Carrier-optimized path: emit concrete-typed signature (no generics)
        var resolvedValueType = site.ValueTypeName
            ?? (site.ClauseInfo is SetClauseInfo setClauseA ? setClauseA.ValueTypeName : null);
        if (carrier != null && prebuiltChain != null && resolvedValueType != null)
        {
            sb.AppendLine($"    public static {returnType}<{entityType}> {methodName}(");
            sb.AppendLine($"        this {returnType}<{entityType}> builder,");
            sb.AppendLine($"        Expression<Func<{entityType}, {resolvedValueType}>> _,");
            sb.AppendLine($"        {resolvedValueType} value)");
            sb.AppendLine($"    {{");

            var concreteBuilder = $"{concreteType}<{entityType}>";
            var returnInterface = $"{returnType}<{entityType}>";
            EmitCarrierClauseBody(sb, carrier, prebuiltChain, site, clauseBit, isFirstInChain,
                concreteBuilder, returnInterface, false, new List<CachedExtractorField>());
            sb.AppendLine($"    }}");
            return;
        }

        sb.AppendLine($"    public static {returnType}<{entityType}> {methodName}<TValue>(");
        sb.AppendLine($"        this {thisType}<{entityType}> builder,");
        sb.AppendLine($"        Expression<Func<{entityType}, TValue>> _,");
        sb.AppendLine($"        TValue value)");
        sb.AppendLine($"    {{");

        // Cast to concrete type (receiver is always an interface)
        sb.AppendLine($"        var __b = Unsafe.As<{concreteType}<{entityType}>>(builder);");
        var builderVar = "__b";

        // Simplified prebuilt chain path: BindParam for value
        if (prebuiltChain != null)
        {
            if (isFirstInChain && prebuiltChain.MaxParameterCount > 0)
                sb.AppendLine($"        {builderVar}.AllocatePrebuiltParams({prebuiltChain.MaxParameterCount});");
            var setValueArg = (clauseInfo is SetClauseInfo prebuiltSetInfo && prebuiltSetInfo.CustomTypeMappingClass != null)
                ? $"{GetMappingFieldName(prebuiltSetInfo.CustomTypeMappingClass)}.ToDb(value)"
                : "value";
            sb.AppendLine($"        {builderVar}.BindParam({setValueArg});");
            if (clauseBit.HasValue)
                sb.AppendLine($"        return {builderVar}.SetClauseBit({clauseBit.Value});");
            else
                sb.AppendLine($"        return {builderVar};");
            sb.AppendLine($"    }}");
            return;
        }

        if (clauseInfo is SetClauseInfo setInfo && setInfo.IsSuccess)
        {
            // Generate optimized interceptor with pre-computed column
            var escapedColumnSql = EscapeStringLiteral(setInfo.ColumnSql);
            var valueArg = setInfo.CustomTypeMappingClass != null
                ? $"{GetMappingFieldName(setInfo.CustomTypeMappingClass)}.ToDb(value)"
                : "value";
            var bitSuffix = ClauseBitSuffix(clauseBit);
            sb.AppendLine($"        return {builderVar}.AddSetClause(@\"{escapedColumnSql}\", {valueArg}, {setInfo.ParameterIndex}){bitSuffix};");
        }
        else if (clauseInfo != null && clauseInfo.IsSuccess)
        {
            // Non-SetClauseInfo but still successful
            var escapedSql = EscapeStringLiteral(clauseInfo.SqlFragment);
            var bitSuffix = ClauseBitSuffix(clauseBit);
            sb.AppendLine($"        return {builderVar}.AddSetClauseRaw(@\"{escapedSql}\", value){bitSuffix};");
        }
        else
        {
            // Non-translatable clause — skip interceptor entirely so the original method runs
            return;
        }

        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Generates a Set() interceptor for UpdateBuilder or ExecutableUpdateBuilder.
    /// The return type uses the interface variant: IUpdateBuilder&lt;T&gt; or IExecutableUpdateBuilder&lt;T&gt;.
    /// </summary>
    private static void GenerateUpdateSetInterceptor(StringBuilder sb, UsageSiteInfo site, string methodName, int? clauseBit = null,
        PrebuiltChainInfo? prebuiltChain = null, bool isFirstInChain = false, CarrierClassInfo? carrier = null)
    {
        var entityType = GetShortTypeName(site.EntityTypeName);
        var clauseInfo = site.ClauseInfo;

        // Determine the receiver type: UpdateBuilder<T> or ExecutableUpdateBuilder<T> (or interface variants)
        var thisType = site.BuilderTypeName;
        var returnType = ToReturnTypeName(thisType);
        var isExecutable = site.BuilderKind is BuilderKind.ExecutableUpdate;
        var concreteBaseName = isExecutable ? "ExecutableUpdateBuilder" : "UpdateBuilder";
        var returnInterfaceBaseName = "I" + concreteBaseName;

        // Carrier-optimized path: emit concrete-typed signature (no generics)
        var resolvedValueType = site.ValueTypeName
            ?? (site.ClauseInfo is SetClauseInfo setClauseB ? setClauseB.ValueTypeName : null);
        if (carrier != null && prebuiltChain != null && resolvedValueType != null)
        {
            sb.AppendLine($"    public static {returnInterfaceBaseName}<{entityType}> {methodName}(");
            sb.AppendLine($"        this {returnInterfaceBaseName}<{entityType}> builder,");
            sb.AppendLine($"        Expression<Func<{entityType}, {resolvedValueType}>> _,");
            sb.AppendLine($"        {resolvedValueType} value)");
            sb.AppendLine($"    {{");

            var concreteBuilder = $"{concreteBaseName}<{entityType}>";
            var returnInterface = $"{returnInterfaceBaseName}<{entityType}>";
            EmitCarrierClauseBody(sb, carrier, prebuiltChain, site, clauseBit, isFirstInChain,
                concreteBuilder, returnInterface, false, new List<CachedExtractorField>());
            sb.AppendLine($"    }}");
            return;
        }

        // Interceptors for generic methods on generic types need arity = type params + method params.
        // UpdateBuilder<T>.Set<TValue>() has total arity 2 (T + TValue).
        // We emit the interceptor with <T, TValue> and where T : class constraint.
        sb.AppendLine($"    public static {returnInterfaceBaseName}<T> {methodName}<T, TValue>(");
        sb.AppendLine($"        this {thisType}<T> builder,");
        sb.AppendLine($"        Expression<Func<T, TValue>> _,");
        sb.AppendLine($"        TValue value) where T : class");
        sb.AppendLine($"    {{");

        // Cast to concrete type (receiver is always an interface)
        sb.AppendLine($"        var __b = Unsafe.As<{concreteBaseName}<T>>(builder);");
        var builderVar = "__b";

        // Simplified prebuilt chain path: BindParam for value
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

        var bitSuffix = ClauseBitSuffix(clauseBit);
        if (clauseInfo is SetClauseInfo setInfo && setInfo.IsSuccess)
        {
            // Generate optimized interceptor with pre-computed column (from semantic analysis)
            var escapedColumnSql = EscapeStringLiteral(setInfo.ColumnSql);
            sb.AppendLine($"        return {builderVar}.AddSetClause(@\"{escapedColumnSql}\", value){bitSuffix};");
        }
        else if (clauseInfo != null && clauseInfo.IsSuccess)
        {
            // Plain ClauseInfo from syntactic translation — SqlFragment is the column SQL
            var escapedColumnSql = EscapeStringLiteral(clauseInfo.SqlFragment);
            sb.AppendLine($"        return {builderVar}.AddSetClause(@\"{escapedColumnSql}\", value){bitSuffix};");
        }
        else
        {
            // Non-translatable clause — skip interceptor entirely so the original method runs
            return;
        }

        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Generates a Set(Action&lt;T&gt;) interceptor for UpdateBuilder or ExecutableUpdateBuilder.
    /// Extracts assignment expressions from the lambda body and generates carrier-optimized code
    /// that stores values directly in typed carrier fields.
    /// </summary>
    private static void GenerateUpdateSetActionInterceptor(StringBuilder sb, UsageSiteInfo site, string methodName,
        int? clauseBit = null, PrebuiltChainInfo? prebuiltChain = null, bool isFirstInChain = false, CarrierClassInfo? carrier = null)
    {
        var entityType = GetShortTypeName(site.EntityTypeName);
        var clauseInfo = site.ClauseInfo;

        var thisType = site.BuilderTypeName;
        var isExecutable = site.BuilderKind is BuilderKind.ExecutableUpdate;
        var concreteBaseName = isExecutable ? "ExecutableUpdateBuilder" : "UpdateBuilder";
        var returnInterfaceBaseName = "I" + concreteBaseName;

        if (clauseInfo is not SetActionClauseInfo actionInfo || !actionInfo.IsSuccess)
            return;

        // Check if any assignments have captured variables that need runtime extraction
        var hasCapturedParams = actionInfo.Parameters.Any(p => p.IsCaptured);
        var actionParamName = hasCapturedParams ? "action" : "_";

        // Carrier-optimized path
        if (carrier != null && prebuiltChain != null)
        {
            sb.AppendLine($"    public static {returnInterfaceBaseName}<{entityType}> {methodName}(");
            sb.AppendLine($"        this {returnInterfaceBaseName}<{entityType}> builder,");
            sb.AppendLine($"        global::System.Action<{entityType}> {actionParamName})");
            sb.AppendLine($"    {{");

            // Compute global parameter offset
            var globalParamOffset = 0;
            foreach (var clause in prebuiltChain.Analysis.Clauses)
            {
                if (clause.Site.UniqueId == site.UniqueId)
                    break;
                if (clause.Site.Kind == InterceptorKind.UpdateSetPoco && clause.Site.UpdateInfo != null)
                    globalParamOffset += clause.Site.UpdateInfo.Columns.Count;
                else if (clause.Site.ClauseInfo != null)
                    globalParamOffset += clause.Site.ClauseInfo.Parameters.Count;
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

            // Extract values from each assignment
            for (int i = 0; i < actionInfo.Parameters.Count; i++)
            {
                var p = actionInfo.Parameters[i];
                var globalIdx = globalParamOffset + i;
                if (globalIdx >= prebuiltChain.ChainParameters.Count) continue;
                var carrierParam = prebuiltChain.ChainParameters[globalIdx];

                if (p.IsCaptured)
                {
                    // Captured variable: extract from delegate.Target using cached FieldInfo.
                    // The field name is the value expression (the identifier name from the syntax tree).
                    sb.AppendLine($"        {carrier.ClassName}.F{globalIdx} ??= action.Target!.GetType().GetField(\"{p.ValueExpression}\")!;");
                    sb.AppendLine($"        __c.P{globalIdx} = ({carrierParam.TypeName}){carrier.ClassName}.F{globalIdx}.GetValue(action.Target)!;");
                }
                else
                {
                    // Literal or constant: inline the value
                    sb.AppendLine($"        __c.P{globalIdx} = ({carrierParam.TypeName}){p.ValueExpression}!;");
                }
            }

            if (clauseBit.HasValue)
                sb.AppendLine($"        __c.Mask |= unchecked(({GetMaskType(prebuiltChain)})(1 << {clauseBit.Value}));");

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
            sb.AppendLine($"        global::System.Action<T> {actionParamName}) where T : class");
            sb.AppendLine($"    {{");

            sb.AppendLine($"        var __b = Unsafe.As<{concreteBaseName}<T>>(builder);");

            if (isFirstInChain && prebuiltChain.MaxParameterCount > 0)
                sb.AppendLine($"        __b.AllocatePrebuiltParams({prebuiltChain.MaxParameterCount});");

            for (int i = 0; i < actionInfo.Parameters.Count; i++)
            {
                var p = actionInfo.Parameters[i];
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
        sb.AppendLine($"        global::System.Action<T> {actionParamName}) where T : class");
        sb.AppendLine($"    {{");

        sb.AppendLine($"        var __b = Unsafe.As<{concreteBaseName}<T>>(builder);");
        var bitSuffix = ClauseBitSuffix(clauseBit);

        for (int i = 0; i < actionInfo.Assignments.Count; i++)
        {
            var assignment = actionInfo.Assignments[i];
            var p = actionInfo.Parameters[i];
            var escapedColumnSql = EscapeStringLiteral(assignment.ColumnSql);

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
                valueExpr = $"{GetMappingFieldName(assignment.CustomTypeMappingClass)}.ToDb({valueExpr})";

            sb.AppendLine($"        __b.AddSetClauseBoxed(@\"{escapedColumnSql}\", {valueExpr});");
        }

        sb.AppendLine($"        return __b{bitSuffix};");
        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Generates a Set(T entity) POCO interceptor for UpdateBuilder or ExecutableUpdateBuilder.
    /// Extracts property values from the entity and calls AddSetClause for each initialized column.
    /// </summary>
    private static void GenerateUpdateSetPocoInterceptor(StringBuilder sb, UsageSiteInfo site, string methodName,
        int? clauseBit = null, PrebuiltChainInfo? prebuiltChain = null, bool isFirstInChain = false, CarrierClassInfo? carrier = null)
    {
        var entityType = GetShortTypeName(site.EntityTypeName);
        var updateInfo = site.UpdateInfo;

        // Determine the receiver type: UpdateBuilder<T> or ExecutableUpdateBuilder<T> (or interface variants)
        var thisType = site.BuilderTypeName;
        var isExecutable = site.BuilderKind is BuilderKind.ExecutableUpdate;
        var concreteBaseName = isExecutable ? "ExecutableUpdateBuilder" : "UpdateBuilder";
        var returnInterfaceBaseName = "I" + concreteBaseName;

        // Carrier-optimized path: concrete-typed, stores entity reference on carrier
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
                sb.AppendLine($"        __c.Mask |= unchecked(({GetMaskType(prebuiltChain)})(1 << {clauseBit.Value}));");

            var returnInterface = $"{returnInterfaceBaseName}<{entityType}>";
            if (isFirstInChain)
                sb.AppendLine($"        return Unsafe.As<{returnInterface}>(__c);");
            else
                sb.AppendLine($"        return Unsafe.As<{returnInterface}>(builder);");

            sb.AppendLine($"    }}");
            return;
        }

        // Set(T entity) has no method-level type params, only class-level T.
        // Interceptor arity = 1 (T from the class).
        sb.AppendLine($"    public static {returnInterfaceBaseName}<T> {methodName}<T>(");
        sb.AppendLine($"        this {thisType}<T> builder,");
        sb.AppendLine($"        T entity) where T : class");
        sb.AppendLine($"    {{");

        if (updateInfo != null && updateInfo.Columns.Count > 0)
        {
            // Cast to concrete type (receiver is always an interface)
            sb.AppendLine($"        var __b = Unsafe.As<{concreteBaseName}<T>>(builder);");
            var builderVar = "__b";

            // Simplified prebuilt chain path: BindParam for each column value
            if (prebuiltChain != null)
            {
                if (isFirstInChain && prebuiltChain.MaxParameterCount > 0)
                    sb.AppendLine($"        {builderVar}.AllocatePrebuiltParams({prebuiltChain.MaxParameterCount});");

                sb.AppendLine($"        var e = Unsafe.As<{entityType}>(entity);");

                foreach (var column in updateInfo.Columns)
                {
                    var valueExpr = GetColumnValueExpression("e", column.PropertyName, column.IsForeignKey, column.CustomTypeMappingClass);
                    sb.AppendLine($"        {builderVar}.BindParam({valueExpr});");
                }

                if (clauseBit.HasValue)
                    sb.AppendLine($"        return {builderVar}.SetClauseBit({clauseBit.Value});");
                else
                    sb.AppendLine($"        return {builderVar};");
                sb.AppendLine($"    }}");
                return;
            }

            // Cast to concrete entity type for property access
            sb.AppendLine($"        var e = Unsafe.As<{entityType}>(entity);");

            foreach (var column in updateInfo.Columns)
            {
                var escapedColumnSql = EscapeStringLiteral(column.QuotedColumnName);
                var valueExpr = GetColumnValueExpression("e", column.PropertyName, column.IsForeignKey, column.CustomTypeMappingClass);
                var sensitiveArg = column.IsSensitive ? ", isSensitive: true" : "";
                sb.AppendLine($"        {builderVar}.AddSetClause(@\"{escapedColumnSql}\", {valueExpr}{sensitiveArg});");
            }

            sb.AppendLine($"        return {builderVar};");
        }
        else
        {
            // Non-translatable clause — skip interceptor entirely so the original method runs
            return;
        }

        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Generates an InsertBuilder ExecuteNonQueryAsync() interceptor.
    /// </summary>
    private static void GenerateInsertExecuteNonQueryInterceptor(StringBuilder sb, UsageSiteInfo site, string methodName,
        PrebuiltChainInfo? prebuiltChain = null, CarrierClassInfo? carrier = null)
    {
        var entityType = GetShortTypeName(site.EntityTypeName);
        var insertInfo = site.InsertInfo;

        sb.AppendLine($"    public static Task<int> {methodName}(");
        sb.AppendLine($"        this IInsertBuilder<{entityType}> builder,");
        sb.AppendLine($"        CancellationToken cancellationToken = default)");
        sb.AppendLine($"    {{");

        if (insertInfo != null && insertInfo.Columns.Count > 0)
        {
            // Carrier-optimized path
            if (carrier != null && prebuiltChain != null)
            {
                EmitCarrierInsertTerminal(sb, carrier, prebuiltChain,
                    "ExecuteCarrierNonQueryWithCommandAsync");
                sb.AppendLine($"    }}");
                return;
            }

            sb.AppendLine($"        var __b = Unsafe.As<InsertBuilder<{entityType}>>(builder);");

            EmitInsertColumnSetup(sb, insertInfo);

            // Extract values from each entity
            sb.AppendLine($"        foreach (var entity in __b.Entities)");
            sb.AppendLine($"        {{");
            EmitInsertEntityBindings(sb, insertInfo, "entity", "__b", "            ");
            sb.AppendLine($"        }}");
            sb.AppendLine();

            sb.AppendLine($"        return Quarry.Internal.ModificationExecutor.ExecuteInsertNonQueryAsync(__b.State, __b.Entities, cancellationToken);");
        }
        else
        {
            // Non-translatable — skip interceptor entirely so the original method runs
            return;
        }

        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Generates an InsertBuilder ExecuteScalarAsync() interceptor for identity return.
    /// </summary>
    private static void GenerateInsertExecuteScalarInterceptor(StringBuilder sb, UsageSiteInfo site, string methodName,
        PrebuiltChainInfo? prebuiltChain = null, CarrierClassInfo? carrier = null)
    {
        var entityType = GetShortTypeName(site.EntityTypeName);
        var insertInfo = site.InsertInfo;

        // ExecuteScalarAsync<TKey> is a generic method on generic class IInsertBuilder<T>.
        // Interceptors must match the combined arity: <T, TKey> (CS9177).
        // T is constrained to class to match IInsertBuilder<T> where T : class.
        sb.AppendLine($"    public static Task<TKey> {methodName}<T, TKey>(");
        sb.AppendLine($"        this IInsertBuilder<T> builder,");
        sb.AppendLine($"        CancellationToken cancellationToken = default) where T : class");
        sb.AppendLine($"    {{");

        if (insertInfo != null && insertInfo.Columns.Count > 0)
        {
            // Carrier-optimized path
            if (carrier != null && prebuiltChain != null)
            {
                EmitCarrierInsertTerminal(sb, carrier, prebuiltChain,
                    "ExecuteCarrierScalarWithCommandAsync<TKey>", isScalar: true);
                sb.AppendLine($"    }}");
                return;
            }

            sb.AppendLine($"        var __b = Unsafe.As<InsertBuilder<T>>(builder);");

            EmitInsertColumnSetup(sb, insertInfo);

            // Set identity column if present
            if (!string.IsNullOrEmpty(insertInfo.IdentityColumnName))
            {
                sb.AppendLine($"        __b.SetIdentityColumn(@\"{EscapeStringLiteral(insertInfo.IdentityColumnName!)}\");");
                sb.AppendLine();
            }

            // Validate single entity insert
            sb.AppendLine($"        if (__b.Entities.Count != 1)");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            throw new InvalidOperationException(");
            sb.AppendLine($"                \"ExecuteScalarAsync can only be used for single entity inserts. \" +");
            sb.AppendLine($"                \"For batch inserts, use ExecuteNonQueryAsync() instead.\");");
            sb.AppendLine($"        }}");
            sb.AppendLine();

            // Cast entity to concrete type for property access.
            // At the call site T is always the concrete entity type (e.g. User),
            // so this cast is safe.
            sb.AppendLine($"        var entity = Unsafe.As<{entityType}>(__b.Entities[0]);");
            EmitInsertEntityBindings(sb, insertInfo, "entity", "__b", "        ");
            sb.AppendLine();

            sb.AppendLine($"        return Quarry.Internal.ModificationExecutor.ExecuteInsertScalarAsync<T, TKey>(__b.State, __b.Entities[0], cancellationToken);");
        }
        else
        {
            // Non-translatable — skip interceptor entirely so the original method runs
            return;
        }

        sb.AppendLine($"    }}");
    }

    #region Modification Helpers

    /// <summary>
    /// Gets the value expression for an entity column property, handling FK navigation and type mapping.
    /// Used by Insert, Update POCO, and other entity property extraction code.
    /// </summary>
    private static string GetColumnValueExpression(string entityVar, string propertyName, bool isForeignKey, string? customTypeMappingClass)
    {
        var valueExpr = isForeignKey
            ? $"{entityVar}.{propertyName}.Id"
            : $"{entityVar}.{propertyName}";
        if (customTypeMappingClass != null)
            valueExpr = $"{GetMappingFieldName(customTypeMappingClass)}.ToDb({valueExpr})";
        return valueExpr;
    }

    /// <summary>
    /// Emits the column setup code shared by all insert interceptors.
    /// </summary>
    private static void EmitInsertColumnSetup(StringBuilder sb, InsertInfo insertInfo)
    {
        var columnNames = string.Join(", ", insertInfo.Columns.Select(c => $"@\"{EscapeStringLiteral(c.QuotedColumnName)}\""));
        sb.AppendLine($"        __b.SetColumns(new[] {{ {columnNames} }});");
        sb.AppendLine();
    }

    /// <summary>
    /// Emits entity property extraction and parameter binding for insert operations.
    /// </summary>
    private static void EmitInsertEntityBindings(StringBuilder sb, InsertInfo insertInfo, string entityVar, string builderVar, string indent)
    {
        sb.AppendLine($"{indent}var paramIndices = new List<int>({insertInfo.Columns.Count});");

        foreach (var column in insertInfo.Columns)
        {
            var valueExpr = GetColumnValueExpression(entityVar, column.PropertyName, column.IsForeignKey, column.CustomTypeMappingClass);
            var sensitiveArg = column.IsSensitive ? ", isSensitive: true" : "";
            sb.AppendLine($"{indent}paramIndices.Add({builderVar}.AddParameter({valueExpr}{sensitiveArg}));");
        }

        sb.AppendLine($"{indent}{builderVar}.AddRow(paramIndices);");
    }

    #endregion
}
