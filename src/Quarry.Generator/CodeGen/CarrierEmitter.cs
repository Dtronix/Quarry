using System.Collections.Generic;
using System.Linq;
using System.Text;
using Quarry.Generators.Generation;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;
using Quarry.Generators.Translation;

namespace Quarry.Generators.CodeGen;

/// <summary>
/// Emits carrier class definitions and carrier-path interceptor method bodies.
/// Works from <see cref="CarrierStrategy"/> (produced by <see cref="CarrierAnalyzer"/>)
/// rather than computing eligibility inline.
/// </summary>
internal static class CarrierEmitter
{
    /// <summary>
    /// Emits the carrier class declaration (fields, static caches, base class).
    /// </summary>
    public static void EmitClassDeclaration(StringBuilder sb, CarrierStrategy strategy, string className)
    {
        sb.AppendLine($"/// <remarks>Chain: Carrier-Optimized PrebuiltDispatch (1 allocation: carrier)</remarks>");
        sb.Append($"file sealed class {className}");

        if (!string.IsNullOrEmpty(strategy.BaseClassName))
        {
            sb.Append($" : {strategy.BaseClassName}");
        }
        sb.AppendLine();
        sb.AppendLine("{");

        // Instance fields
        foreach (var field in strategy.Fields)
        {
            sb.AppendLine($"    public {field.Type} {field.Name};");
        }

        // Static FieldInfo cache fields
        foreach (var sf in strategy.StaticFields)
        {
            sb.Append($"    private static {sf.Type} {sf.Name}");
            if (sf.Initializer != null)
                sb.Append($" = {sf.Initializer}");
            sb.AppendLine(";");
        }

        sb.AppendLine("}");
    }

    /// <summary>
    /// Emits a carrier ChainRoot interceptor body (db.Users() → new Carrier { Ctx = ctx }).
    /// </summary>
    public static void EmitChainRootBody(
        StringBuilder sb, string className, string entityType, string contextClass)
    {
        sb.AppendLine($"    public static IEntityAccessor<{entityType}> {{0}}(");
        sb.AppendLine($"        this {contextClass} @this)");
        sb.AppendLine($"    {{");
        sb.AppendLine($"        return new {className} {{ Ctx = (IQueryExecutionContext)@this }};");
        sb.AppendLine($"    }}");
    }

    /// <summary>
    /// Emits a noop transition body (carrier implements both interfaces).
    /// </summary>
    public static void EmitNoopTransitionBody(
        StringBuilder sb, string receiverType, string returnType)
    {
        sb.AppendLine($"        return Unsafe.As<{returnType}>(builder);");
    }

    /// <summary>
    /// Emits a carrier clause body: cast to carrier, extract params, set mask bit, return.
    /// </summary>
    public static void EmitClauseBody(
        StringBuilder sb,
        string className,
        CarrierStrategy strategy,
        IReadOnlyList<CarrierParameter> clauseParameters,
        int globalParamOffset,
        int? clauseBit,
        bool isFirstInChain,
        string concreteBuilderType,
        string returnInterface,
        string maskType)
    {
        if (isFirstInChain)
        {
            sb.AppendLine($"        var __b = Unsafe.As<{concreteBuilderType}>(builder);");
            sb.AppendLine($"        var __c = new {className} {{ Ctx = __b.State.ExecutionContext }};");
        }
        else
        {
            sb.AppendLine($"        var __c = Unsafe.As<{className}>(builder);");
        }

        // Bind parameters to carrier fields
        foreach (var param in clauseParameters)
        {
            if (param.IsEntitySourced)
                continue;
            if (param.ExtractionCode != null)
            {
                sb.AppendLine($"        __c.{param.FieldName} = ({param.FieldType}){param.ExtractionCode}!;");
            }
        }

        // Set conditional clause bit
        if (clauseBit.HasValue)
            sb.AppendLine($"        __c.Mask |= unchecked(({maskType})(1 << {clauseBit.Value}));");

        // Return
        if (isFirstInChain)
            sb.AppendLine($"        return Unsafe.As<{returnInterface}>(__c);");
        else
            sb.AppendLine($"        return Unsafe.As<{returnInterface}>(builder);");
    }

    /// <summary>
    /// Emits carrier terminal preamble: cast carrier, resolve OpId, dispatch SQL.
    /// </summary>
    public static void EmitTerminalPreamble(
        StringBuilder sb, string className, string maskType, int maskBitCount)
    {
        sb.AppendLine($"        var __c = Unsafe.As<{className}>(builder);");
        if (maskBitCount > 0)
        {
            sb.AppendLine($"        var __opId = (ulong)__c.Mask;");
        }
        else
        {
            sb.AppendLine($"        const ulong __opId = 0;");
        }
    }

    /// <summary>
    /// Emits carrier parameter locals (__pVal0, __pVal1, etc.) from carrier fields.
    /// </summary>
    public static void EmitParameterLocals(
        StringBuilder sb, IReadOnlyList<CarrierParameter> parameters)
    {
        foreach (var param in parameters)
        {
            if (param.IsEntitySourced || param.IsCollection)
                continue;
            sb.AppendLine($"        var __pVal{param.GlobalIndex} = __c.{param.FieldName};");
        }
    }

    // ───────────────────────────────────────────────────────────────
    // Methods ported from InterceptorCodeGenerator.Carrier.cs
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Checks whether a chain's execution terminal would pass the generation checks
    /// in the terminal generator methods. If not, the chain must not be carrier-eligible
    /// because clause interceptors would create carriers with no terminal to consume them.
    /// </summary>
    internal static bool WouldExecutionTerminalBeEmitted(AssembledPlan chain)
    {
        if (chain.UnmatchedMethodNames != null)
            return false;

        var site = chain.ExecutionSite;
        return site.Kind switch
        {
            InterceptorKind.ExecuteFetchAll or InterceptorKind.ExecuteFetchFirst
                or InterceptorKind.ExecuteFetchFirstOrDefault or InterceptorKind.ExecuteFetchSingle
                or InterceptorKind.ToAsyncEnumerable
                => CanEmitReaderTerminal(chain),
            InterceptorKind.ExecuteScalar
                => CanEmitScalarTerminal(chain),
            InterceptorKind.ExecuteNonQuery
                => CanEmitNonQueryTerminal(chain),
            InterceptorKind.InsertExecuteNonQuery or InterceptorKind.InsertExecuteScalar
                or InterceptorKind.InsertToDiagnostics
                => CanEmitInsertTerminal(chain),
            InterceptorKind.BatchInsertExecuteNonQuery or InterceptorKind.BatchInsertExecuteScalar
                or InterceptorKind.BatchInsertToDiagnostics
                => true, // Batch insert terminals are always emittable
            _ => true
        };
    }

    /// <summary>
    /// Returns true if a reader-based terminal (FetchAll, FetchFirst, etc.) can be emitted.
    /// </summary>
    internal static bool CanEmitReaderTerminal(AssembledPlan chain)
    {
        var rawResult = InterceptorCodeGenerator.ResolveExecutionResultType(
            chain.ExecutionSite.ResultTypeName, chain.ResultTypeName, chain.ProjectionInfo);
        if (string.IsNullOrEmpty(rawResult))
            return false;
        if (chain.ReaderDelegateCode == null)
            return false;
        if (chain.ProjectionInfo != null && chain.ProjectionInfo.Columns.Any(c =>
            c.SqlExpression != null && !string.IsNullOrEmpty(c.ColumnName)))
            return false;
        return true;
    }

    /// <summary>
    /// Returns true if a scalar terminal (ExecuteScalar) can be emitted.
    /// </summary>
    internal static bool CanEmitScalarTerminal(AssembledPlan chain)
    {
        var rawResult = InterceptorCodeGenerator.ResolveExecutionResultType(
            chain.ExecutionSite.ResultTypeName, chain.ResultTypeName, chain.ProjectionInfo);
        return !string.IsNullOrEmpty(rawResult);
    }

    /// <summary>
    /// Returns true if a non-query terminal (DELETE/UPDATE ExecuteNonQuery) can be emitted.
    /// </summary>
    internal static bool CanEmitNonQueryTerminal(AssembledPlan chain)
    {
        return !chain.SqlVariants.Values.Any(v => string.IsNullOrWhiteSpace(v.Sql)
            || (chain.QueryKind == QueryKind.Update && v.Sql.Contains("SET  ")));
    }

    /// <summary>
    /// Returns true if an insert terminal can be emitted.
    /// </summary>
    private static bool CanEmitInsertTerminal(AssembledPlan chain)
    {
        return !chain.SqlVariants.Values.Any(v => string.IsNullOrWhiteSpace(v.Sql));
    }

    /// <summary>
    /// Resolves the carrier base class name for a chain, handling tuple result types correctly.
    /// </summary>
    internal static string ResolveCarrierBaseClass(AssembledPlan chain)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(chain.EntityTypeName);

        // Modification chains use modification-specific base classes
        if (chain.QueryKind == QueryKind.Delete)
            return $"DeleteCarrierBase<{entityType}>";
        if (chain.QueryKind == QueryKind.Update)
            return $"UpdateCarrierBase<{entityType}>";
        if (chain.QueryKind == QueryKind.Insert)
            return $"InsertCarrierBase<{entityType}>";
        if (chain.QueryKind == QueryKind.BatchInsert)
            return $"BatchInsertCarrierBase<{entityType}>";

        var hasSelect = chain.GetClauseEntries().Any(c => c.Role == ClauseRole.Select);
        var joinCount = chain.IsJoinChain ? (chain.JoinedEntityTypeNames?.Count ?? 1) - 1 : 0;

        string? resultType = null;
        if (hasSelect && chain.ResultTypeName != null)
        {
            // Use the same resolution pipeline as execution terminals for tuple safety
            var resolved = InterceptorCodeGenerator.ResolveExecutionResultType(
                chain.ExecutionSite.ResultTypeName,
                chain.ResultTypeName,
                chain.ProjectionInfo);
            if (!string.IsNullOrEmpty(resolved))
                resultType = InterceptorCodeGenerator.SanitizeTupleResultType(InterceptorCodeGenerator.GetShortTypeName(resolved!));
        }

        if (joinCount == 0)
        {
            return resultType != null
                ? $"CarrierBase<{entityType}, {resultType}>"
                : $"CarrierBase<{entityType}>";
        }

        var joinedTypes = chain.JoinedEntityTypeNames!.Select(InterceptorCodeGenerator.GetShortTypeName).ToArray();
        var joinedStr = string.Join(", ", joinedTypes);

        if (resultType != null)
        {
            return joinCount switch
            {
                1 => $"JoinedCarrierBase<{joinedStr}, {resultType}>",
                2 => $"JoinedCarrierBase3<{joinedStr}, {resultType}>",
                3 => $"JoinedCarrierBase4<{joinedStr}, {resultType}>",
                _ => $"JoinedCarrierBase<{joinedStr}, {resultType}>"
            };
        }

        return joinCount switch
        {
            1 => $"JoinedCarrierBase<{joinedStr}>",
            2 => $"JoinedCarrierBase3<{joinedStr}>",
            3 => $"JoinedCarrierBase4<{joinedStr}>",
            _ => $"JoinedCarrierBase<{joinedStr}>"
        };
    }

    internal static string GetJoinedConcreteBuilderTypeName(int entityCount, string[] entityTypes)
    {
        return entityCount switch
        {
            2 => $"JoinedQueryBuilder<{entityTypes[0]}, {entityTypes[1]}>",
            3 => $"JoinedQueryBuilder3<{entityTypes[0]}, {entityTypes[1]}, {entityTypes[2]}>",
            4 => $"JoinedQueryBuilder4<{entityTypes[0]}, {entityTypes[1]}, {entityTypes[2]}, {entityTypes[3]}>",
            _ => $"JoinedQueryBuilder<{string.Join(", ", entityTypes)}>"
        };
    }

    /// <summary>
    /// Resolves the receiver interface type for a carrier interceptor method.
    /// </summary>
    internal static string ResolveCarrierReceiverType(TranslatedCallSite site, string entityType, AssembledPlan? chain = null)
    {
        // If the site's receiver is IEntityAccessor, use that as the receiver type
        if (site.BuilderTypeName is "IEntityAccessor" or "EntityAccessor")
            return $"IEntityAccessor<{entityType}>";

        // Resolve result type: use chain's enriched projection for broken tuples,
        // but preserve element names (no sanitization) since interceptors need exact type match.
        string? resultType = null;
        if (site.ResultTypeName != null)
        {
            var resolved = chain != null
                ? InterceptorCodeGenerator.ResolveExecutionResultType(site.ResultTypeName, chain.ResultTypeName, chain.ProjectionInfo)
                : site.ResultTypeName;
            if (!string.IsNullOrEmpty(resolved))
                resultType = InterceptorCodeGenerator.GetShortTypeName(resolved!);
        }

        if (site.JoinedEntityTypeNames != null && site.JoinedEntityTypeNames.Count >= 2)
        {
            var joinedTypes = site.JoinedEntityTypeNames.Select(InterceptorCodeGenerator.GetShortTypeName).ToArray();
            if (resultType != null)
            {
                return site.JoinedEntityTypeNames.Count switch
                {
                    2 => $"IJoinedQueryBuilder<{joinedTypes[0]}, {joinedTypes[1]}, {resultType}>",
                    3 => $"IJoinedQueryBuilder3<{joinedTypes[0]}, {joinedTypes[1]}, {joinedTypes[2]}, {resultType}>",
                    4 => $"IJoinedQueryBuilder4<{joinedTypes[0]}, {joinedTypes[1]}, {joinedTypes[2]}, {joinedTypes[3]}, {resultType}>",
                    _ => $"IJoinedQueryBuilder<{string.Join(", ", joinedTypes)}, {resultType}>"
                };
            }
            return site.JoinedEntityTypeNames.Count switch
            {
                2 => $"IJoinedQueryBuilder<{joinedTypes[0]}, {joinedTypes[1]}>",
                3 => $"IJoinedQueryBuilder3<{joinedTypes[0]}, {joinedTypes[1]}, {joinedTypes[2]}>",
                4 => $"IJoinedQueryBuilder4<{joinedTypes[0]}, {joinedTypes[1]}, {joinedTypes[2]}, {joinedTypes[3]}>",
                _ => $"IJoinedQueryBuilder<{string.Join(", ", joinedTypes)}>"
            };
        }

        if (resultType != null)
            return $"IQueryBuilder<{entityType}, {resultType}>";

        return $"IQueryBuilder<{entityType}>";
    }

    /// <summary>
    /// Emits a complete carrier clause body for Where/Having/OrderBy/GroupBy interceptors.
    /// </summary>
    internal static void EmitCarrierClauseBody(
        StringBuilder sb, CarrierPlan carrier, AssembledPlan chain,
        TranslatedCallSite site, int? clauseBit, bool isFirstInChain,
        string concreteBuilderType, string returnInterface,
        bool hasResolvableCapturedParams, List<InterceptorCodeGenerator.CachedExtractorField> methodFields)
    {
        // Compute global parameter offset for this clause's params
        var globalParamOffset = 0;
        foreach (var clause in chain.GetClauseEntries())
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
            sb.AppendLine($"        var __b = Unsafe.As<{concreteBuilderType}>(builder);");
            sb.AppendLine($"        var __c = new {carrier.ClassName} {{ Ctx = __b.State.ExecutionContext }};");
        }
        else
        {
            sb.AppendLine($"        var __c = Unsafe.As<{carrier.ClassName}>(builder);");
        }

        // Extract and bind parameters using FieldInfo extraction with carrier-owned statics
        var clauseInfo = site.Clause;
        if (clauseInfo != null && clauseInfo.Parameters.Count > 0)
        {
            // Single-pass partition: scalar vs collection params
            var scalarParams = new List<ParameterInfo>(clauseInfo.Parameters.Count);
            var collectionParams = new List<ParameterInfo>();
            foreach (var p in clauseInfo.Parameters)
            {
                if (p.ExpressionPath == "__CONTAINS_COLLECTION__")
                    collectionParams.Add(p);
                else
                    scalarParams.Add(p);
            }

            if (hasResolvableCapturedParams && scalarParams.Any(p => p.IsCaptured && p.CanGenerateDirectPath))
            {
                // Remap FieldInfo references from interceptor-class statics to carrier-class statics
                var carrierFields = new List<InterceptorCodeGenerator.CachedExtractorField>();
                foreach (var mf in methodFields)
                {
                    var globalIdx = globalParamOffset + mf.ParameterIndex;
                    var carrierFieldName = $"{carrier.ClassName}.F{globalIdx}";
                    carrierFields.Add(new InterceptorCodeGenerator.CachedExtractorField(
                        carrierFieldName, mf.MethodName, mf.ParameterIndex, mf.ExpressionPath));
                }
                InterceptorCodeGenerator.GenerateCachedExtraction(sb, carrierFields);
            }

            var allParams = clauseInfo.Parameters.OrderBy(p => p.Index).ToList();
            for (int i = 0; i < allParams.Count; i++)
            {
                var p = allParams[i];
                var globalIdx = globalParamOffset + i;
                if (globalIdx >= chain.ChainParameters.Count) continue;
                var carrierParam = chain.ChainParameters[globalIdx];

                if (p.ExpressionPath == "__CONTAINS_COLLECTION__")
                {
                    // Collection parameter: extract from MethodCallExpression in the expression tree.
                    EmitCollectionContainsExtraction(sb, globalIdx, carrierParam);
                }
                else
                {
                    // Set clauses: the value comes from the 'value' method parameter,
                    // not from an inlined literal or captured closure.
                    var isSetClause = site.Kind == InterceptorKind.Set || site.Kind == InterceptorKind.UpdateSet;
                    var extractExpr = isSetClause ? "value"
                        : (p.IsCaptured ? $"p{p.Index}" : p.ValueExpression);
                    sb.AppendLine($"        __c.P{globalIdx} = ({carrierParam.ClrType}){extractExpr}!;");
                }
            }
        }

        if (clauseBit.HasValue)
            sb.AppendLine($"        __c.Mask |= unchecked(({GetMaskType(chain)})(1 << {clauseBit.Value}));");

        // Always use Unsafe.As for the return — handles interface crossings
        if (isFirstInChain)
        {
            sb.AppendLine($"        return Unsafe.As<{returnInterface}>(__c);");
        }
        else
        {
            sb.AppendLine($"        return Unsafe.As<{returnInterface}>(builder);");
        }
    }

    /// <summary>
    /// Emits a carrier class at namespace scope (outside the static interceptor class).
    /// </summary>
    internal static void EmitCarrierClass(StringBuilder sb, CarrierPlan info, AssembledPlan chain)
    {
        sb.AppendLine($"/// <remarks>Chain: Carrier-Optimized PrebuiltDispatch (1 allocation: carrier)</remarks>");
        sb.Append($"file sealed class {info.ClassName}");

        if (info.ImplementedInterfaces.Count > 0)
        {
            sb.Append(" : ");
            for (int i = 0; i < info.ImplementedInterfaces.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(info.ImplementedInterfaces[i]);
            }
        }

        sb.AppendLine();
        sb.AppendLine("{");

        // Emit static SQL field: single string for single-variant, string[] for multi-variant
        EmitCarrierSqlField(sb, chain);

        // Emit instance fields (typed params, mask, limit, offset, timeout)
        foreach (var field in info.Fields)
        {
            // Collection fields are non-nullable reference types — use null! to suppress CS8618
            var initializer = field.TypeName.Contains("IReadOnlyList") ? " = null!" : "";
            sb.AppendLine($"    internal {field.TypeName} {field.Name}{initializer};");
        }

        // Emit static fields (FieldInfo caches for captured params)
        foreach (var staticField in info.StaticFields)
        {
            sb.AppendLine($"    internal static {staticField.TypeName} {staticField.Name};");
        }

        // Emit dead methods (explicit interface impls that throw)
        foreach (var stub in info.DeadMethods)
        {
            sb.AppendLine();
            EmitDeadInterfaceMethod(sb, stub);
        }

        sb.AppendLine("}");
        sb.AppendLine();
    }

    /// <summary>
    /// Emits the static _sql field on the carrier class.
    /// Single-variant chains get a static readonly string; multi-variant chains get a static readonly string[]
    /// indexed by mask value. Gaps (e.g., mutually-exclusive branches that skip mask 0) are filled with null! to surface routing bugs.
    /// </summary>
    private static void EmitCarrierSqlField(StringBuilder sb, AssembledPlan chain)
    {
        if (chain.SqlVariants.Count == 1)
        {
            foreach (var kvp in chain.SqlVariants)
            {
                sb.AppendLine($"    internal static readonly string _sql = @\"{InterceptorCodeGenerator.EscapeStringLiteral(kvp.Value.Sql)}\";");
            }
        }
        else
        {
            // Multi-variant: emit string[] indexed by mask value.
            // Array size = max mask value + 1. Gaps filled with null! (unreachable at runtime; NRE if hit).
            var maxMask = chain.SqlVariants.Keys.Max();
            var arraySize = maxMask + 1;
            sb.AppendLine("    internal static readonly string[] _sql =");
            sb.AppendLine("    [");
            for (int i = 0; i < arraySize; i++)
            {
                if (chain.SqlVariants.TryGetValue(i, out var variant))
                    sb.AppendLine($"        @\"{InterceptorCodeGenerator.EscapeStringLiteral(variant.Sql)}\",");
                else
                    sb.AppendLine("        null!,");
            }
            sb.AppendLine("    ];");
        }
    }

    /// <summary>
    /// Emits an explicit interface method implementation that throws InvalidOperationException.
    /// </summary>
    private static void EmitDeadInterfaceMethod(StringBuilder sb, CarrierInterfaceStub stub)
    {
        sb.AppendLine($"    {stub.FullSignature}");
        sb.AppendLine($"        => throw new InvalidOperationException(\"Method {stub.MethodName} is not used in this carrier-optimized chain.\");");
    }

    /// <summary>
    /// Emits the carrier chain entry interceptor (first clause in chain).
    /// </summary>
    internal static void EmitCarrierChainEntry(
        StringBuilder sb, CarrierPlan carrier, AssembledPlan chain,
        TranslatedCallSite site, string builderTypeName, string returnInterface,
        int? bitIndex, IReadOnlyList<QueryParameter> siteParams, int globalParamOffset)
    {
        // Cast incoming builder to concrete type to extract ExecutionContext
        sb.AppendLine($"        var __b = Unsafe.As<{builderTypeName}>(builder);");
        sb.Append($"        var __c = new {carrier.ClassName} {{ ");

        var isReadOnly = chain.ExecutionSite.Kind is InterceptorKind.ToDiagnostics;
        if (!isReadOnly)
        {
            sb.Append("Ctx = __b.State.ExecutionContext");
        }

        sb.AppendLine(" };");

        // Bind parameters if this clause has any
        EmitCarrierParamBindings(sb, siteParams, globalParamOffset);

        // Set clause bit if conditional
        if (bitIndex.HasValue)
        {
            sb.AppendLine($"        __c.Mask |= unchecked(({GetMaskType(chain)})(1 << {bitIndex.Value}));");
        }

        sb.AppendLine($"        return Unsafe.As<{returnInterface}>(__c);");
    }

    /// <summary>
    /// Emits a carrier parameter-binding interceptor (Where, Having with params).
    /// </summary>
    internal static void EmitCarrierParamBind(
        StringBuilder sb, CarrierPlan carrier, AssembledPlan chain,
        int? bitIndex, IReadOnlyList<QueryParameter> siteParams, int globalParamOffset)
    {
        sb.AppendLine($"        var __c = Unsafe.As<{carrier.ClassName}>(builder);");

        EmitCarrierParamBindings(sb, siteParams, globalParamOffset);

        if (bitIndex.HasValue)
        {
            sb.AppendLine($"        __c.Mask |= unchecked(({GetMaskType(chain)})(1 << {bitIndex.Value}));");
        }

        sb.AppendLine("        return builder;");
    }

    /// <summary>
    /// Emits a carrier noop interceptor (Join, unconditional OrderBy/ThenBy/GroupBy, Distinct).
    /// </summary>
    internal static void EmitCarrierNoop(
        StringBuilder sb, CarrierPlan carrier, AssembledPlan chain,
        int? bitIndex)
    {
        if (bitIndex.HasValue)
        {
            sb.AppendLine($"        Unsafe.As<{carrier.ClassName}>(builder).Mask |= unchecked(({GetMaskType(chain)})(1 << {bitIndex.Value}));");
        }

        sb.AppendLine("        return builder;");
    }

    /// <summary>
    /// Emits a carrier Select interceptor (interface crossing via Unsafe.As).
    /// </summary>
    internal static void EmitCarrierSelect(StringBuilder sb, string targetInterface)
    {
        sb.AppendLine($"        return Unsafe.As<{targetInterface}>(builder);");
    }

    /// <summary>
    /// Emits a carrier pagination bind (Limit/Offset with runtime value).
    /// </summary>
    private static void EmitCarrierPaginationBind(
        StringBuilder sb, CarrierPlan carrier, string fieldName, string valueExpression)
    {
        sb.AppendLine($"        Unsafe.As<{carrier.ClassName}>(builder).{fieldName} = {valueExpression};");
        sb.AppendLine("        return builder;");
    }

    /// <summary>
    /// Emits a carrier WithTimeout interceptor.
    /// </summary>
    private static void EmitCarrierWithTimeout(
        StringBuilder sb, CarrierPlan carrier, string timeoutExpression)
    {
        sb.AppendLine($"        Unsafe.As<{carrier.ClassName}>(builder).Timeout = {timeoutExpression};");
        sb.AppendLine("        return builder;");
    }

    /// <summary>
    /// Emits the common carrier terminal preamble: Unsafe.As cast, optional OpId, and SQL dispatch.
    /// </summary>
    private static void EmitCarrierPreamble(
        StringBuilder sb, CarrierPlan carrier, AssembledPlan chain,
        bool emitOpId = true)
    {
        sb.AppendLine($"        var __c = Unsafe.As<{carrier.ClassName}>(builder);");
        if (emitOpId)
            sb.AppendLine("        var __opId = OpId.Next();");
        EmitCarrierSqlDispatch(sb, carrier, chain);
    }

    /// <summary>
    /// Emits parameter value extraction into __pVal* local variables.
    /// </summary>
    private static void EmitCarrierParameterLocals(
        StringBuilder sb, AssembledPlan chain, CarrierPlan carrier)
        => TerminalEmitHelpers.EmitParameterLocals(sb, chain, carrier);

    /// <summary>
    /// Emits DbCommand creation and binds __pVal* locals to parameters.
    /// </summary>
    private static void EmitCarrierCommandBinding(
        StringBuilder sb, AssembledPlan chain, CarrierPlan carrier,
        string timeoutExpr)
    {
        sb.AppendLine("        var __cmd = __c.Ctx.Connection.CreateCommand();");
        sb.AppendLine("        __cmd.CommandText = sql;");
        sb.AppendLine($"        __cmd.CommandTimeout = (int)({timeoutExpr}).TotalSeconds;");

        var dialectLiteral = GetDialectLiteral(chain.Dialect);
        var paramCount = chain.ChainParameters.Count;
        var hasLimitField = HasCarrierField(carrier, FieldRole.Limit);
        var hasOffsetField = HasCarrierField(carrier, FieldRole.Offset);

        for (int i = 0; i < paramCount; i++)
        {
            var param = chain.ChainParameters[i];
            sb.AppendLine($"        var __p{i} = __cmd.CreateParameter();");
            sb.AppendLine($"        __p{i}.ParameterName = \"@p{i}\";");
            sb.AppendLine($"        __p{i}.Value = __pVal{i};");

            if (param.TypeMappingClass != null)
            {
                var mappingField = InterceptorCodeGenerator.GetMappingFieldName(param.TypeMappingClass);
                sb.AppendLine($"        ({mappingField} as IDialectAwareTypeMapping)?.ConfigureParameter({dialectLiteral}, __p{i});");
            }

            sb.AppendLine($"        __cmd.Parameters.Add(__p{i});");
        }

        var nextIdx = paramCount;
        if (hasLimitField)
        {
            sb.AppendLine($"        var __pL = __cmd.CreateParameter();");
            sb.AppendLine($"        __pL.ParameterName = \"@p{nextIdx}\";");
            sb.AppendLine($"        __pL.Value = __pValL;");
            sb.AppendLine($"        __cmd.Parameters.Add(__pL);");
            nextIdx++;
        }
        if (hasOffsetField)
        {
            sb.AppendLine($"        var __pO = __cmd.CreateParameter();");
            sb.AppendLine($"        __pO.ParameterName = \"@p{nextIdx}\";");
            sb.AppendLine($"        __pO.Value = __pValO;");
            sb.AppendLine($"        __cmd.Parameters.Add(__pO);");
        }
    }

    /// <summary>
    /// Emits a carrier execution terminal with inline per-parameter DbCommand binding.
    /// </summary>
    internal static void EmitCarrierExecutionTerminal(
        StringBuilder sb, CarrierPlan carrier, AssembledPlan chain,
        string? readerExpression, string executorMethod)
    {
        EmitCarrierPreamble(sb, carrier, chain);

        // SQL logging
        sb.AppendLine("        if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Debug, QueryLog.CategoryName) == true)");
        sb.AppendLine("            QueryLog.SqlGenerated(__opId, sql);");

        // Parameter logging
        EmitInlineParameterLogging(sb, chain, carrier);

        // Parameter value extraction + command binding
        EmitCarrierParameterLocals(sb, chain, carrier);
        var timeoutExpr = HasCarrierField(carrier, FieldRole.Timeout)
            ? "__c.Timeout ?? __c.Ctx!.DefaultTimeout"
            : "__c.Ctx!.DefaultTimeout";
        EmitCarrierCommandBinding(sb, chain, carrier, timeoutExpr);

        // Executor call
        var readerArg = readerExpression != null ? $", {readerExpression}" : "";
        sb.AppendLine($"        return QueryExecutor.{executorMethod}(__opId, __c.Ctx, __cmd{readerArg}, cancellationToken);");
    }

    /// <summary>
    /// Emits a carrier non-query execution terminal (DELETE/UPDATE) with inline binding.
    /// </summary>
    internal static void EmitCarrierNonQueryTerminal(
        StringBuilder sb, CarrierPlan carrier, AssembledPlan chain)
    {
        EmitCarrierPreamble(sb, carrier, chain);

        sb.AppendLine("        if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Debug, QueryLog.CategoryName) == true)");
        sb.AppendLine("            QueryLog.SqlGenerated(__opId, sql);");

        EmitInlineParameterLogging(sb, chain, carrier);

        EmitCarrierParameterLocals(sb, chain, carrier);
        var timeoutExpr = HasCarrierField(carrier, FieldRole.Timeout)
            ? "__c.Timeout ?? __c.Ctx!.DefaultTimeout"
            : "__c.Ctx!.DefaultTimeout";
        EmitCarrierCommandBinding(sb, chain, carrier, timeoutExpr);

        sb.AppendLine("        return QueryExecutor.ExecuteCarrierNonQueryWithCommandAsync(__opId, __c.Ctx, __cmd, cancellationToken);");
    }

    /// <summary>
    /// Emits inline per-parameter logging (sensitivity-aware).
    /// </summary>
    private static void EmitInlineParameterLogging(StringBuilder sb, AssembledPlan chain, CarrierPlan carrier)
    {
        var paramCount = chain.ChainParameters.Count;
        var hasLimitField = HasCarrierField(carrier, FieldRole.Limit);
        var hasOffsetField = HasCarrierField(carrier, FieldRole.Offset);
        var totalParams = paramCount + (hasLimitField ? 1 : 0) + (hasOffsetField ? 1 : 0);

        if (totalParams == 0)
            return;

        sb.AppendLine("        if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Trace, ParameterLog.CategoryName) == true)");
        sb.AppendLine("        {");
        for (int i = 0; i < paramCount; i++)
        {
            var param = chain.ChainParameters[i];
            if (param.IsSensitive)
            {
                sb.AppendLine($"            ParameterLog.BoundSensitive(__opId, {i});");
            }
            else
            {
                if (param.EntityPropertyExpression != null)
                    sb.AppendLine($"            ParameterLog.Bound(__opId, {i}, ((object?){param.EntityPropertyExpression})?.ToString() ?? \"null\");");
                else if (IsNonNullableValueType(param.ClrType) || param.IsEnum)
                    sb.AppendLine($"            ParameterLog.Bound(__opId, {i}, __c.P{i}.ToString());");
                else
                    sb.AppendLine($"            ParameterLog.Bound(__opId, {i}, __c.P{i}?.ToString() ?? \"null\");");
            }
        }
        var nextLogIdx = paramCount;
        if (hasLimitField)
        {
            sb.AppendLine($"            ParameterLog.Bound(__opId, {nextLogIdx}, __c.Limit.ToString());");
            nextLogIdx++;
        }
        if (hasOffsetField)
        {
            sb.AppendLine($"            ParameterLog.Bound(__opId, {nextLogIdx}, __c.Offset.ToString());");
        }
        sb.AppendLine("        }");
    }

    /// <summary>
    /// Gets the inline value expression for a parameter based on its type classification.
    /// </summary>
    private static string GetParameterValueExpression(QueryParameter param, int index)
        => TerminalEmitHelpers.GetParameterValueExpression(param, index);

    /// <summary>
    /// Emits a carrier insert execution terminal with inline per-parameter DbCommand binding.
    /// </summary>
    internal static void EmitCarrierInsertTerminal(
        StringBuilder sb, CarrierPlan carrier, AssembledPlan chain,
        string executorMethod, bool isScalar = false)
    {
        sb.AppendLine($"        var __c = Unsafe.As<{carrier.ClassName}>(builder);");

        sb.AppendLine("        var __opId = OpId.Next();");
        EmitCarrierSqlDispatch(sb, carrier, chain);

        sb.AppendLine("        if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Debug, QueryLog.CategoryName) == true)");
        sb.AppendLine("            QueryLog.SqlGenerated(__opId, sql);");

        // Command creation + inline parameter binding from entity properties
        var timeoutExpr = HasCarrierField(carrier, FieldRole.Timeout)
            ? "__c.Timeout ?? __c.Ctx!.DefaultTimeout"
            : "__c.Ctx!.DefaultTimeout";
        sb.AppendLine("        var __cmd = __c.Ctx.Connection.CreateCommand();");
        sb.AppendLine("        __cmd.CommandText = sql;");
        sb.AppendLine($"        __cmd.CommandTimeout = (int)({timeoutExpr}).TotalSeconds;");

        // Bind entity properties as parameters using InsertInfo
        var insertInfo = chain.InsertInfo;
        if (insertInfo != null)
        {
            var entityType = InterceptorCodeGenerator.GetShortTypeName(chain.EntityTypeName);
            var convertBool = InterceptorCodeGenerator.RequiresBoolToIntConversion(chain.Dialect);
            for (int i = 0; i < insertInfo.Columns.Count; i++)
            {
                var col = insertInfo.Columns[i];
                var needsIntType = col.IsEnum || (col.IsBoolean && convertBool);
                var valueExpr = InterceptorCodeGenerator.GetColumnValueExpression("__c.Entity!", col.PropertyName, col.IsForeignKey, col.CustomTypeMappingClass, col.IsBoolean, col.IsEnum, col.IsNullable, convertBool);
                sb.AppendLine($"        var __p{i} = __cmd.CreateParameter();");
                sb.AppendLine($"        __p{i}.ParameterName = \"@p{i}\";");
                sb.AppendLine($"        __p{i}.Value = (object?){valueExpr} ?? DBNull.Value;");
                if (needsIntType)
                    sb.AppendLine($"        __p{i}.DbType = System.Data.DbType.Int32;");
                sb.AppendLine($"        __cmd.Parameters.Add(__p{i});");
            }

            // Parameter logging
            if (insertInfo.Columns.Count > 0)
            {
                sb.AppendLine("        if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Trace, ParameterLog.CategoryName) == true)");
                sb.AppendLine("        {");
                for (int i = 0; i < insertInfo.Columns.Count; i++)
                {
                    var col = insertInfo.Columns[i];
                    if (col.IsSensitive)
                        sb.AppendLine($"            ParameterLog.BoundSensitive(__opId, {i});");
                    else
                        sb.AppendLine($"            ParameterLog.Bound(__opId, {i}, __p{i}.Value?.ToString() ?? \"null\");");
                }
                sb.AppendLine("        }");
            }
        }

        sb.AppendLine($"        return QueryExecutor.{executorMethod}(__opId, __c.Ctx, __cmd, cancellationToken);");
    }

    // ───────────────────────────────────────────────────────────────
    // Carrier diagnostic methods (from InterceptorCodeGenerator.Execution.cs)
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Emits a carrier insert ToDiagnostics terminal.
    /// </summary>
    internal static void EmitCarrierInsertToDiagnosticsTerminal(
        StringBuilder sb, CarrierPlan carrier, AssembledPlan chain)
    {
        EmitCarrierPreamble(sb, carrier, chain, emitOpId: false);

        var insertInfo = chain.InsertInfo;
        if (insertInfo != null && insertInfo.Columns.Count > 0)
        {
            var convertBool = InterceptorCodeGenerator.RequiresBoolToIntConversion(chain.Dialect);
            sb.AppendLine("        var __params = new DiagnosticParameter[]");
            sb.AppendLine("        {");
            for (int i = 0; i < insertInfo.Columns.Count; i++)
            {
                var col = insertInfo.Columns[i];
                var valueExpr = InterceptorCodeGenerator.GetColumnValueExpression("__c.Entity!", col.PropertyName, col.IsForeignKey, col.CustomTypeMappingClass, col.IsBoolean, col.IsEnum, col.IsNullable, convertBool);
                sb.AppendLine($"            new(\"@p{i}\", (object?){valueExpr} ?? DBNull.Value),");
            }
            sb.AppendLine("        };");
        }
        else
        {
            sb.AppendLine("        var __params = Array.Empty<DiagnosticParameter>();");
        }

        sb.AppendLine($"        return new QueryDiagnostics(sql, __params, DiagnosticQueryKind.Insert, SqlDialect.{chain.Dialect}, \"{InterceptorCodeGenerator.EscapeStringLiteral(chain.TableName)}\", DiagnosticOptimizationTier.PrebuiltDispatch, true);");
    }

    /// <summary>
    /// Emits a carrier ToDiagnostics terminal with full parameter and clause diagnostic output.
    /// </summary>
    internal static void EmitCarrierToDiagnosticsTerminal(
        StringBuilder sb, CarrierPlan carrier, AssembledPlan chain,
        string diagnosticKind, string isCarrierOptimized)
    {
        EmitCarrierPreamble(sb, carrier, chain, emitOpId: false);
        TerminalEmitHelpers.EmitParameterLocals(sb, chain, carrier);
        TerminalEmitHelpers.EmitDiagnosticClauseArray(sb, chain, carrier);
        TerminalEmitHelpers.EmitDiagnosticParameterArray(sb, chain, carrier);
        TerminalEmitHelpers.EmitDiagnosticsConstruction(sb, chain, carrier, diagnosticKind, isCarrierOptimized);
    }

    // ───────────────────────────────────────────────────────────────
    // Carrier helpers
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Emits code to extract a collection parameter from a Contains() call.
    /// </summary>
    private static void EmitCollectionContainsExtraction(
        StringBuilder sb, int globalIdx, QueryParameter carrierParam)
    {
        var fieldType = carrierParam.ElementTypeName != null
            ? $"System.Collections.Generic.IReadOnlyList<{carrierParam.ElementTypeName}>"
            : carrierParam.ClrType;

        if (carrierParam.IsDirectAccessible && carrierParam.CollectionAccessExpression != null)
        {
            sb.AppendLine($"        __c.P{globalIdx} = ({fieldType}){carrierParam.CollectionAccessExpression};");
        }
        else
        {
            sb.AppendLine($"        __c.P{globalIdx} = Quarry.Internal.ExpressionHelper.ExtractContainsCollection<{fieldType}>((System.Linq.Expressions.MethodCallExpression)expr.Body);");
        }
    }

    private static void EmitCarrierParamBindings(
        StringBuilder sb, IReadOnlyList<QueryParameter> siteParams, int globalParamOffset)
    {
        for (int i = 0; i < siteParams.Count; i++)
        {
            var param = siteParams[i];
            var globalIdx = globalParamOffset + i;
            if (param.IsCollection)
            {
                EmitCollectionContainsExtraction(sb, globalIdx, param);
            }
            else
            {
                sb.AppendLine($"        __c.P{globalIdx} = {param.ValueExpression};");
            }
        }
    }

    /// <summary>
    /// Emits SQL read from the carrier's static _sql field.
    /// Single-variant: var sql = ClassName._sql; Multi-variant: var sql = ClassName._sql[__c.Mask];
    /// Collection expansion tokens are still resolved at runtime in the terminal.
    /// </summary>
    private static void EmitCarrierSqlDispatch(StringBuilder sb, CarrierPlan carrier, AssembledPlan chain)
    {
        var hasCollections = chain.ChainParameters.Any(p => p.IsCollection);

        if (chain.SqlVariants.Count == 1)
        {
            sb.AppendLine($"        var sql = {carrier.ClassName}._sql;");
        }
        else
        {
            sb.AppendLine($"        var sql = {carrier.ClassName}._sql[__c.Mask];");
        }

        if (hasCollections)
        {
            TerminalEmitHelpers.EmitCollectionExpansion(sb, chain);
        }
    }

    internal static bool HasCarrierField(CarrierPlan carrier, FieldRole role)
    {
        var targetName = role switch
        {
            FieldRole.Limit => "Limit",
            FieldRole.Offset => "Offset",
            FieldRole.Timeout => "Timeout",
            FieldRole.ClauseMask => "Mask",
            FieldRole.ExecutionContext => "Ctx",
            FieldRole.Entity => "Entity",
            FieldRole.Collection => "Collection",
            _ => null
        };
        if (targetName == null) return false;
        foreach (var field in carrier.Fields)
        {
            if (field.Name == targetName)
                return true;
        }
        return false;
    }

    internal static string GetMaskType(AssembledPlan chain)
    {
        var bitCount = chain.ConditionalTerms.Count;
        return bitCount <= 8 ? "byte" : bitCount <= 16 ? "ushort" : "uint";
    }

    private static string GetDialectLiteral(SqlDialect dialect)
    {
        return dialect switch
        {
            SqlDialect.PostgreSQL => "SqlDialect.PostgreSQL",
            SqlDialect.SqlServer => "SqlDialect.SqlServer",
            SqlDialect.SQLite => "SqlDialect.SQLite",
            SqlDialect.MySQL => "SqlDialect.MySQL",
            _ => $"(SqlDialect){(int)dialect}"
        };
    }

    /// <summary>
    /// Checks if the given CLR type name is a non-nullable value type.
    /// Used by the logging emitter to decide between .ToString() and ?.ToString() ?? "null".
    /// </summary>
    private static bool IsNonNullableValueType(string typeName)
    {
        if (typeName.EndsWith("?"))
            return false;
        if (ValueTypes.Contains(typeName))
            return true;
        // Check unqualified name (e.g., "System.DateTime" → "DateTime")
        var dotIndex = typeName.LastIndexOf('.');
        if (dotIndex >= 0 && ValueTypes.Contains(typeName.Substring(dotIndex + 1)))
            return true;
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
}
