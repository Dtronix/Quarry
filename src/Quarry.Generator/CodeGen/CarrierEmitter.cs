using System.Collections.Generic;
using System.Linq;
using System.Text;
using Quarry.Generators.Generation;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;
using Quarry.Generators.Translation;
using Quarry.Generators.Utilities;

namespace Quarry.Generators.CodeGen;

/// <summary>
/// Emits carrier class definitions and carrier-path interceptor method bodies.
/// Works from <see cref="CarrierPlan"/> (produced by <see cref="CarrierAnalyzer"/>)
/// rather than computing eligibility inline.
/// </summary>
internal static class CarrierEmitter
{
    /// <summary>
    /// Emits a carrier ChainRoot interceptor body (db.Users() → new Carrier { Ctx = ctx }).
    /// </summary>
    public static void EmitChainRootBody(
        StringBuilder sb, string className, string entityType, string contextClass)
    {
        sb.AppendLine($"    public static IEntityAccessor<{entityType}> {{0}}(");
        sb.AppendLine($"        this {contextClass} @this)");
        sb.AppendLine($"    {{");
        sb.AppendLine($"        return new {className} {{ Ctx = @this }};");
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
                or InterceptorKind.ExecuteFetchSingleOrDefault or InterceptorKind.ToAsyncEnumerable
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
    /// Resolves the interface list for a carrier class, replacing the former base class hierarchy.
    /// Returns a list of fully-closed interface names the carrier must implement.
    /// </summary>
    internal static string[] ResolveCarrierInterfaceList(AssembledPlan chain)
    {
        var entityType = InterceptorCodeGenerator.GetShortTypeName(chain.EntityTypeName);

        // Modification chains
        if (chain.QueryKind == QueryKind.Delete)
            return [$"IEntityAccessor<{entityType}>", $"IDeleteBuilder<{entityType}>", $"IExecutableDeleteBuilder<{entityType}>"];
        if (chain.QueryKind == QueryKind.Update)
            return [$"IEntityAccessor<{entityType}>", $"IUpdateBuilder<{entityType}>", $"IExecutableUpdateBuilder<{entityType}>"];
        if (chain.QueryKind == QueryKind.Insert)
            return [$"IEntityAccessor<{entityType}>", $"IInsertBuilder<{entityType}>"];
        if (chain.QueryKind == QueryKind.BatchInsert)
            return [$"IEntityAccessor<{entityType}>", $"IBatchInsertBuilder<{entityType}>", $"IExecutableBatchInsert<{entityType}>"];

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
                ? [$"IEntityAccessor<{entityType}>", $"IQueryBuilder<{entityType}>", $"IQueryBuilder<{entityType}, {resultType}>"]
                : [$"IEntityAccessor<{entityType}>", $"IQueryBuilder<{entityType}>"];
        }

        var joinedTypes = chain.JoinedEntityTypeNames!.Select(InterceptorCodeGenerator.GetShortTypeName).ToArray();
        var joinedStr = string.Join(", ", joinedTypes);
        var interfaces = new List<string>
        {
            $"IEntityAccessor<{joinedTypes[0]}>",
            $"IQueryBuilder<{joinedTypes[0]}>",
        };

        // Add progressive join interfaces from arity 2 up to the current join depth
        for (int level = 2; level <= joinCount + 1; level++)
        {
            var levelTypes = string.Join(", ", joinedTypes.Take(level));
            interfaces.Add($"{JoinArityHelpers.GetInterfaceName(level)}<{levelTypes}>");
        }

        if (resultType != null)
        {
            interfaces.Add(JoinArityHelpers.GetGenericInterfaceWithResult(joinedTypes, resultType));
        }

        return interfaces.ToArray();
    }

    internal static string GetJoinedConcreteBuilderTypeName(int entityCount, string[] entityTypes)
        => JoinArityHelpers.GetGenericInterface(entityTypes).Replace("IJoinedQueryBuilder", "JoinedQueryBuilder");

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
                return JoinArityHelpers.GetGenericInterfaceWithResult(joinedTypes, resultType);
            return JoinArityHelpers.GetGenericInterface(joinedTypes);
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
        bool hasResolvableCapturedParams, List<InterceptorCodeGenerator.CachedExtractorField> methodFields,
        string delegateParamName = "func")
    {
        // Compute global parameter offset for this clause's params
        var (_, globalParamOffset) = TerminalEmitHelpers.ResolveSiteParams(chain, site.UniqueId);

        // Determine which parameters to bind: prefer translated clause params (handles
        // column expression assignments), fall back to raw SetAction params.
        IReadOnlyList<Translation.ParameterInfo>? clauseParams = null;
        if (site.Kind == InterceptorKind.UpdateSetAction)
            clauseParams = site.Clause?.Parameters ?? site.Bound.Raw.SetActionParameters;
        else if (site.Clause != null)
            clauseParams = site.Clause.Parameters;

        // Only emit the carrier cast when the body actually uses it
        // (parameter binding or mask bit setting).
        bool needsCarrierRef = (clauseParams != null && clauseParams.Count > 0) || clauseBit.HasValue;
        if (needsCarrierRef)
            sb.AppendLine($"        var __c = Unsafe.As<{carrier.ClassName}>(builder);");

        if (clauseParams != null && clauseParams.Count > 0)
        {
            // Emit per-variable extraction locals from the extraction plan
            var extractionPlan = carrier.GetExtractionPlan(site.UniqueId);
            if (extractionPlan != null && extractionPlan.Extractors.Count > 0)
            {
                sb.AppendLine($"        var __target = {delegateParamName}.Target!;");
                foreach (var extractor in extractionPlan.Extractors)
                {
                    var targetExpr = extractor.IsStaticField ? "null!" : "__target";
                    sb.AppendLine($"        var {extractor.VariableName} = {carrier.ClassName}.{extractor.MethodName}({targetExpr});");
                }
            }

            // Bind parameters using ValueExpression — captured variables are now in scope as locals
            var hasExtraction = extractionPlan != null && extractionPlan.Extractors.Count > 0;
            var allParams = clauseParams.OrderBy(p => p.Index).ToList();
            for (int i = 0; i < allParams.Count; i++)
            {
                var p = allParams[i];
                var globalIdx = globalParamOffset + i;
                if (globalIdx >= chain.ChainParameters.Count) continue;
                var carrierParam = chain.ChainParameters[globalIdx];

                if (p.ExpressionPath == "__CONTAINS_COLLECTION__")
                {
                    EmitCollectionContainsExtraction(sb, globalIdx, carrierParam, carrier);
                }
                else
                {
                    var isSetClause = site.Kind == InterceptorKind.Set || site.Kind == InterceptorKind.UpdateSet;
                    if (isSetClause)
                    {
                        var effectiveCastType = GetEffectiveCastType(globalIdx, carrierParam, carrier);
                        sb.AppendLine($"        __c.P{globalIdx} = ({effectiveCastType})value!;");
                    }
                    else if (hasExtraction && p.IsCaptured)
                    {
                        sb.AppendLine(FormatCarrierFieldAssignment(globalIdx, p.ValueExpression));
                    }
                    else
                    {
                        var effectiveCastType = GetEffectiveCastType(globalIdx, carrierParam, carrier);
                        sb.AppendLine($"        __c.P{globalIdx} = ({effectiveCastType}){p.ValueExpression}!;");
                    }
                }
            }
        }

        if (clauseBit.HasValue)
            sb.AppendLine($"        __c.Mask |= unchecked(({GetMaskType(chain)})(1 << {clauseBit.Value}));");

        // Always use Unsafe.As for the return — handles interface crossings
        sb.AppendLine($"        return Unsafe.As<{returnInterface}>(builder);");
    }

    /// <summary>
    /// Emits a carrier class at namespace scope (outside the static interceptor class).
    /// </summary>
    internal static void EmitCarrierClass(StringBuilder sb, CarrierPlan info, AssembledPlan chain, string contextTypeName)
    {
        sb.AppendLine($"/// <remarks>Chain: PrebuiltDispatch (1 allocation: carrier)</remarks>");
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

        // Emit collection SQL cache field if needed
        if (chain.ChainParameters.Any(p => p.IsCollection))
            EmitCollectionSqlCacheField(sb, chain);

        // Emit static reader delegate field to avoid duplicating the lambda at each terminal.
        // Only extract when the reader is self-contained (no references to interceptor-class
        // fields like _entityReader_* or _mapper_*).
        if (chain.ReaderDelegateCode != null && IsReaderSelfContained(chain))
        {
            var rawResult = InterceptorCodeGenerator.ResolveExecutionResultType(
                chain.ExecutionSite.ResultTypeName, chain.ResultTypeName, chain.ProjectionInfo);
            if (!string.IsNullOrEmpty(rawResult))
            {
                var readerResultType = InterceptorCodeGenerator.GetShortTypeName(rawResult!);
                sb.AppendLine($"    internal static readonly System.Func<System.Data.Common.DbDataReader, {readerResultType}> _reader = {chain.ReaderDelegateCode};");
            }
        }

        // Emit the execution context field using the concrete context type for devirtualization
        sb.AppendLine($"    internal {contextTypeName}? Ctx;");

        // Emit instance fields (typed params, mask, limit, offset, timeout)
        foreach (var field in info.Fields)
        {
            // Non-nullable reference type fields need null! to suppress CS8618
            var initializer = (field.IsReferenceType && !field.TypeName.EndsWith("?")) ? " = null!" : "";
            sb.AppendLine($"    internal {field.TypeName} {field.Name}{initializer};");
        }

        // Emit [UnsafeAccessor] extern methods from per-clause extraction plans
        foreach (var extractionPlan in info.ExtractionPlans)
        {
            foreach (var extractor in extractionPlan.Extractors)
            {
                var accessorKind = extractor.IsStaticField ? "UnsafeAccessorKind.StaticField" : "UnsafeAccessorKind.Field";
                sb.AppendLine($"    [UnsafeAccessor({accessorKind}, Name = \"{extractor.VariableName}\")]");
                sb.AppendLine($"    internal extern static ref {extractor.VariableType} {extractor.MethodName}(");
                sb.AppendLine($"        [UnsafeAccessorType(\"{extractor.DisplayClassName}\")] object target);");
            }
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
    /// Emits the carrier chain entry interceptor (first clause in chain).
    /// </summary>
    internal static void EmitCarrierChainEntry(
        StringBuilder sb, CarrierPlan carrier, AssembledPlan chain,
        TranslatedCallSite site, string builderTypeName, string returnInterface,
        int? bitIndex, IReadOnlyList<QueryParameter> siteParams, int globalParamOffset)
    {
        // In the carrier-only architecture, the builder is already the carrier
        // (created by the ChainRoot interceptor). Just cast to it.
        sb.AppendLine($"        var __c = Unsafe.As<{carrier.ClassName}>(builder);");

        // Emit per-variable extraction locals and bind parameters
        EmitExtractionLocalsAndBindParams(sb, carrier, site, siteParams, globalParamOffset);

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
        int? bitIndex, IReadOnlyList<QueryParameter> siteParams, int globalParamOffset,
        TranslatedCallSite? site = null)
    {
        sb.AppendLine($"        var __c = Unsafe.As<{carrier.ClassName}>(builder);");

        if (site != null)
            EmitExtractionLocalsAndBindParams(sb, carrier, site, siteParams, globalParamOffset);
        else
            EmitCarrierParamBindings(sb, carrier, siteParams, globalParamOffset);

        if (bitIndex.HasValue)
        {
            sb.AppendLine($"        __c.Mask |= unchecked(({GetMaskType(chain)})(1 << {bitIndex.Value}));");
        }

        sb.AppendLine("        return builder;");
    }

    /// <summary>
    /// Emits per-variable extraction locals from the extraction plan, then binds parameters.
    /// </summary>
    private static void EmitExtractionLocalsAndBindParams(
        StringBuilder sb, CarrierPlan carrier, TranslatedCallSite site,
        IReadOnlyList<QueryParameter> siteParams, int globalParamOffset)
    {
        var extractionPlan = carrier.GetExtractionPlan(site.UniqueId);
        if (extractionPlan != null && extractionPlan.Extractors.Count > 0)
        {
            sb.AppendLine($"        var __target = {extractionPlan.DelegateParamName}.Target!;");
            foreach (var extractor in extractionPlan.Extractors)
            {
                var targetExpr = extractor.IsStaticField ? "null!" : "__target";
                sb.AppendLine($"        var {extractor.VariableName} = {carrier.ClassName}.{extractor.MethodName}({targetExpr});");
            }
        }

        var hasExtraction = extractionPlan != null && extractionPlan.Extractors.Count > 0;
        EmitCarrierParamBindings(sb, carrier, siteParams, globalParamOffset, hasExtraction);
    }

    /// <summary>
    /// Emits extraction + parameter binding for a lambda inner chain (CTE or set-op).
    /// The lambda delegate's Target holds the display class with captured variables.
    /// Extracted values are bound to carrier P-fields at the given parameter offset.
    /// </summary>
    internal static void EmitLambdaInnerChainCapture(
        StringBuilder sb, CarrierPlan carrier, TranslatedCallSite site,
        IReadOnlyList<QueryParameter> innerParams, int parameterOffset)
    {
        var extractionPlan = carrier.GetExtractionPlan(site.UniqueId);
        if (extractionPlan != null && extractionPlan.Extractors.Count > 0)
        {
            sb.AppendLine($"        var __target = {extractionPlan.DelegateParamName}.Target!;");
            foreach (var extractor in extractionPlan.Extractors)
            {
                var targetExpr = extractor.IsStaticField ? "null!" : "__target";
                sb.AppendLine($"        var {extractor.VariableName} = {carrier.ClassName}.{extractor.MethodName}({targetExpr});");
            }
        }

        var hasExtraction = extractionPlan != null && extractionPlan.Extractors.Count > 0;
        EmitCarrierParamBindings(sb, carrier, innerParams, parameterOffset, hasExtraction);
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
    /// When projection parameters exist, extracts captured variables and binds them.
    /// </summary>
    internal static void EmitCarrierSelect(
        StringBuilder sb, string targetInterface,
        CarrierPlan? carrier = null, AssembledPlan? chain = null,
        TranslatedCallSite? site = null)
    {
        if (carrier != null && chain != null && site != null)
        {
            var (siteParams, globalParamOffset) = TerminalEmitHelpers.ResolveSiteParams(chain, site.UniqueId);
            if (siteParams.Count > 0)
            {
                sb.AppendLine($"        var __c = Unsafe.As<{carrier.ClassName}>(builder);");
                EmitExtractionLocalsAndBindParams(sb, carrier, site, siteParams, globalParamOffset);
                sb.AppendLine($"        return Unsafe.As<{targetInterface}>(__c);");
                return;
            }
        }
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
        bool emitOpId = true, bool emitCtxLocal = true)
    {
        sb.AppendLine($"        var __c = Unsafe.As<{carrier.ClassName}>(builder);");
        if (emitCtxLocal)
            sb.AppendLine("        var __ctx = __c.Ctx!;");
        if (emitOpId)
        {
            sb.AppendLine("        var __logger = LogsmithOutput.Logger;");
            sb.AppendLine("        var __opId = __logger != null ? OpId.Next() : 0;");
        }
        EmitCarrierSqlDispatch(sb, carrier, chain);
    }

    /// <summary>
    /// Emits DbCommand creation and binds parameters with mask-gated conditional support.
    /// </summary>
    /// <remarks>
    /// The CommandTimeout assignment is guarded by a compare-and-set that skips the property
    /// setter when the value already matches the driver's default — saving a setter call
    /// (and any provider-side validation) on the hot path.
    /// </remarks>
    private static void EmitCarrierCommandBinding(
        StringBuilder sb, AssembledPlan chain, CarrierPlan carrier)
    {
        var timeoutExpr = HasCarrierField(carrier, FieldRole.Timeout)
            ? "__c.Timeout ?? __ctx.DefaultTimeout"
            : "__ctx.DefaultTimeout";

        sb.AppendLine("        var __cmd = __ctx.Connection.CreateCommand();");
        sb.AppendLine("        __cmd.CommandText = sql;");
        sb.AppendLine($"        var __timeoutSec = (int)({timeoutExpr}).TotalSeconds;");
        sb.AppendLine("        if (__cmd.CommandTimeout != __timeoutSec) __cmd.CommandTimeout = __timeoutSec;");

        var dialectLiteral = GetDialectLiteral(chain.Dialect);
        var paramCount = chain.ChainParameters.Count;
        var hasCollections = chain.ChainParameters.Any(p => p.IsCollection);
        var hasLimitField = HasCarrierField(carrier, FieldRole.Limit);
        var hasOffsetField = HasCarrierField(carrier, FieldRole.Offset);

        var condMap = TerminalEmitHelpers.BuildParamConditionalMap(chain);
        var hasConditional = chain.ConditionalTerms.Count > 0;
        var maskType = hasConditional ? GetMaskType(chain) : null;

        // Shift variable design (three variables serve different purposes):
        //   __colShift  — set in the preamble's inline SQL builder, accumulates left-to-right
        //                 as SQL text is built. Used for SQL construction + cache entry.
        //   __bindShift — set here in command binding, accumulates in GlobalIndex order as
        //                 DbParameters are created. Used for scalar/pagination ParameterName.
        //   __diagShift — set in diagnostic emission, same accumulation as __bindShift.
        // Invariant: after all chain params, __bindShift == __colShift (same collections,
        // same order, same accumulation formula). Pagination uses __bindShift.
        if (hasCollections)
            sb.AppendLine("        var __bindShift = 0;");

        int? currentBitIndex = null;
        bool inConditionalBlock = false;

        for (int i = 0; i < paramCount; i++)
        {
            var param = chain.ChainParameters[i];
            condMap.TryGetValue(i, out var ci);

            if (ci.IsConditional)
            {
                if (!inConditionalBlock || ci.BitIndex != currentBitIndex)
                {
                    // Close previous block if open
                    if (inConditionalBlock)
                        sb.AppendLine("        }");

                    sb.AppendLine($"        if ((__c.Mask & unchecked(({maskType})(1 << {ci.BitIndex!.Value}))) != 0)");
                    sb.AppendLine("        {");
                    inConditionalBlock = true;
                    currentBitIndex = ci.BitIndex;
                }
            }
            else
            {
                // Close any open conditional block
                if (inConditionalBlock)
                {
                    sb.AppendLine("        }");
                    inConditionalBlock = false;
                    currentBitIndex = null;
                }
            }

            var indent = inConditionalBlock ? "            " : "        ";

            if (param.IsCollection)
            {
                // Collection parameters are expanded into N individual DbParameters.
                // Preamble already declared: __col{i}, __col{i}Len, __col{i}Parts
                sb.AppendLine($"{indent}for (int __bi = 0; __bi < __col{i}Len; __bi++)");
                sb.AppendLine($"{indent}{{");
                sb.AppendLine($"{indent}    var __pc = __cmd.CreateParameter();");
                sb.AppendLine($"{indent}    __pc.ParameterName = __col{i}Parts[__bi];");
                sb.AppendLine($"{indent}    __pc.Value = (object?)__col{i}[__bi] ?? DBNull.Value;");
                sb.AppendLine($"{indent}    __cmd.Parameters.Add(__pc);");
                sb.AppendLine($"{indent}}}");
                // Accumulate shift after this collection's parameters are bound
                sb.AppendLine($"{indent}__bindShift += __col{i}Len - 1;");
            }
            else
            {
                var valueExpr = TerminalEmitHelpers.GetParameterValueExpression(param, i);

                sb.AppendLine($"{indent}var __p{i} = __cmd.CreateParameter();");
                if (hasCollections)
                    sb.AppendLine($"{indent}__p{i}.ParameterName = {EmitParamNameExpr(chain.Dialect, i, "__bindShift")};");
                else
                    sb.AppendLine($"{indent}__p{i}.ParameterName = \"{FormatParamName(chain.Dialect, i)}\";");
                sb.AppendLine($"{indent}__p{i}.Value = {valueExpr};");

                if (param.TypeMappingClass != null)
                {
                    var mappingField = InterceptorCodeGenerator.GetMappingFieldName(param.TypeMappingClass);
                    sb.AppendLine($"{indent}({mappingField} as IDialectAwareTypeMapping)?.ConfigureParameter({dialectLiteral}, __p{i});");
                }

                sb.AppendLine($"{indent}__cmd.Parameters.Add(__p{i});");
            }
        }

        // Close any trailing conditional block
        if (inConditionalBlock)
            sb.AppendLine("        }");

        // Pagination parameters — always unconditional.
        // __bindShift == __colShift here: both accumulate (colLen-1) per collection
        // in GlobalIndex order. Using __bindShift keeps the binding self-contained.
        var nextIdx = paramCount;
        if (hasLimitField)
        {
            sb.AppendLine($"        var __pL = __cmd.CreateParameter();");
            if (hasCollections)
                sb.AppendLine($"        __pL.ParameterName = {EmitParamNameExpr(chain.Dialect, nextIdx, "__bindShift")};");
            else
                sb.AppendLine($"        __pL.ParameterName = \"{FormatParamName(chain.Dialect, nextIdx)}\";");
            sb.AppendLine($"        __pL.Value = (object)__c.Limit;");
            sb.AppendLine($"        __cmd.Parameters.Add(__pL);");
            nextIdx++;
        }
        if (hasOffsetField)
        {
            sb.AppendLine($"        var __pO = __cmd.CreateParameter();");
            if (hasCollections)
                sb.AppendLine($"        __pO.ParameterName = {EmitParamNameExpr(chain.Dialect, nextIdx, "__bindShift")};");
            else
                sb.AppendLine($"        __pO.ParameterName = \"{FormatParamName(chain.Dialect, nextIdx)}\";");
            sb.AppendLine($"        __pO.Value = (object)__c.Offset;");
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
        sb.AppendLine("        if (__logger?.IsEnabled(LogLevel.Debug, QueryLog.CategoryName) == true)");
        sb.AppendLine("            QueryLog.SqlGenerated(__opId, sql);");

        // Parameter logging
        EmitInlineParameterLogging(sb, chain, carrier);

        // Command binding (CommandTimeout is set inside the executor)
        EmitCarrierCommandBinding(sb, chain, carrier);

        // Executor call. Reader-shaped terminals receive a per-query CommandBehavior literal
        // chosen by CommandBehaviorSelector — non-row terminals (NonQuery / Scalar) do not.
        var readerArg = readerExpression != null ? $", {readerExpression}" : "";
        if (readerExpression != null)
        {
            var behaviorExpr = CommandBehaviorSelector.Select(chain.Dialect, chain.ProjectionInfo?.Columns);
            sb.AppendLine($"        return QueryExecutor.{executorMethod}(__opId, __ctx, __cmd{readerArg}, {behaviorExpr}, cancellationToken);");
        }
        else
        {
            sb.AppendLine($"        return QueryExecutor.{executorMethod}(__opId, __ctx, __cmd, cancellationToken);");
        }
    }

    /// <summary>
    /// Emits a carrier non-query execution terminal (DELETE/UPDATE) with inline binding.
    /// </summary>
    internal static void EmitCarrierNonQueryTerminal(
        StringBuilder sb, CarrierPlan carrier, AssembledPlan chain)
    {
        EmitCarrierExecutionTerminal(sb, carrier, chain,
            readerExpression: null,
            executorMethod: "ExecuteCarrierNonQueryWithCommandAsync");
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

        var condMap = TerminalEmitHelpers.BuildParamConditionalMap(chain);
        var hasConditional = chain.ConditionalTerms.Count > 0;
        var maskType = hasConditional ? GetMaskType(chain) : null;

        sb.AppendLine("        if (__logger?.IsEnabled(LogLevel.Trace, ParameterLog.CategoryName) == true)");
        sb.AppendLine("        {");

        int? currentBitIndex = null;
        bool inConditionalBlock = false;

        for (int i = 0; i < paramCount; i++)
        {
            var param = chain.ChainParameters[i];
            condMap.TryGetValue(i, out var ci);

            if (ci.IsConditional)
            {
                if (!inConditionalBlock || ci.BitIndex != currentBitIndex)
                {
                    if (inConditionalBlock)
                        sb.AppendLine("            }");

                    sb.AppendLine($"            if ((__c.Mask & unchecked(({maskType})(1 << {ci.BitIndex!.Value}))) != 0)");
                    sb.AppendLine("            {");
                    inConditionalBlock = true;
                    currentBitIndex = ci.BitIndex;
                }
            }
            else
            {
                if (inConditionalBlock)
                {
                    sb.AppendLine("            }");
                    inConditionalBlock = false;
                    currentBitIndex = null;
                }
            }

            var indent = inConditionalBlock ? "                " : "            ";

            if (param.IsCollection)
            {
                // Log each collection element individually
                if (param.IsSensitive)
                {
                    sb.AppendLine($"{indent}for (int __li = 0; __li < __col{i}Len; __li++)");
                    sb.AppendLine($"{indent}    ParameterLog.BoundSensitive(__opId, {i});");
                }
                else
                {
                    sb.AppendLine($"{indent}for (int __li = 0; __li < __col{i}Len; __li++)");
                    if (param.ElementTypeName != null && TypeClassification.IsNonNullableValueType(param.ElementTypeName))
                        sb.AppendLine($"{indent}    ParameterLog.Bound(__opId, {i}, __col{i}[__li].ToString());");
                    else
                        sb.AppendLine($"{indent}    ParameterLog.Bound(__opId, {i}, __col{i}[__li]?.ToString() ?? \"null\");");
                }
            }
            else if (param.IsSensitive)
            {
                sb.AppendLine($"{indent}ParameterLog.BoundSensitive(__opId, {i});");
            }
            else
            {
                if (param.EntityPropertyExpression != null)
                    sb.AppendLine($"{indent}ParameterLog.Bound(__opId, {i}, ((object?){param.EntityPropertyExpression})?.ToString() ?? \"null\");");
                else if (TypeClassification.IsNonNullableValueType(GetEffectiveCastType(i, param, carrier)))
                    sb.AppendLine($"{indent}ParameterLog.Bound(__opId, {i}, __c.P{i}.ToString());");
                else
                    sb.AppendLine($"{indent}ParameterLog.Bound(__opId, {i}, __c.P{i}?.ToString() ?? \"null\");");
            }
        }

        // Close any trailing conditional block
        if (inConditionalBlock)
            sb.AppendLine("            }");

        // Pagination logging — always unconditional
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
        sb.AppendLine("        var __ctx = __c.Ctx!;");
        sb.AppendLine("        var __logger = LogsmithOutput.Logger;");

        sb.AppendLine("        var __opId = OpId.Next();");
        EmitCarrierSqlDispatch(sb, carrier, chain);

        sb.AppendLine("        if (__logger?.IsEnabled(LogLevel.Debug, QueryLog.CategoryName) == true)");
        sb.AppendLine("            QueryLog.SqlGenerated(__opId, sql);");

        // Command creation + inline parameter binding from entity properties.
        // CommandTimeout uses a guarded compare-and-set to skip the property setter when
        // the value already matches.
        var timeoutExpr = HasCarrierField(carrier, FieldRole.Timeout)
            ? "__c.Timeout ?? __ctx.DefaultTimeout"
            : "__ctx.DefaultTimeout";
        sb.AppendLine("        var __cmd = __ctx.Connection.CreateCommand();");
        sb.AppendLine("        __cmd.CommandText = sql;");
        sb.AppendLine($"        var __timeoutSec = (int)({timeoutExpr}).TotalSeconds;");
        sb.AppendLine("        if (__cmd.CommandTimeout != __timeoutSec) __cmd.CommandTimeout = __timeoutSec;");

        // Bind entity properties as parameters using InsertInfo
        var insertInfo = chain.InsertInfo;
        if (insertInfo != null)
        {
            var entityType = InterceptorCodeGenerator.GetShortTypeName(chain.EntityTypeName);
            var convertBool = InterceptorCodeGenerator.RequiresBoolToIntConversion(chain.Dialect);
            for (int i = 0; i < insertInfo.Columns.Count; i++)
            {
                var (valueExpr, needsIntType) = TerminalEmitHelpers.GetInsertColumnBinding(insertInfo.Columns[i], "__c.Entity!", convertBool);
                sb.AppendLine($"        var __p{i} = __cmd.CreateParameter();");
                sb.AppendLine($"        __p{i}.ParameterName = \"{FormatParamName(chain.Dialect, i)}\";");
                sb.AppendLine($"        __p{i}.Value = (object?){valueExpr} ?? DBNull.Value;");
                if (needsIntType)
                    sb.AppendLine($"        __p{i}.DbType = System.Data.DbType.Int32;");
                sb.AppendLine($"        __cmd.Parameters.Add(__p{i});");
            }

            // Parameter logging
            if (insertInfo.Columns.Count > 0)
            {
                sb.AppendLine("        if (__logger?.IsEnabled(LogLevel.Trace, ParameterLog.CategoryName) == true)");
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

        sb.AppendLine($"        return QueryExecutor.{executorMethod}(__opId, __ctx, __cmd, cancellationToken);");
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
        EmitCarrierPreamble(sb, carrier, chain, emitOpId: false, emitCtxLocal: false);

        var insertInfo = chain.InsertInfo;
        if (insertInfo != null && insertInfo.Columns.Count > 0)
        {
            var convertBool = InterceptorCodeGenerator.RequiresBoolToIntConversion(chain.Dialect);
            sb.AppendLine("        var __params = new DiagnosticParameter[]");
            sb.AppendLine("        {");
            for (int i = 0; i < insertInfo.Columns.Count; i++)
            {
                var col = insertInfo.Columns[i];
                var valueExpr = InterceptorCodeGenerator.GetColumnValueExpression("__c.Entity!", col.PropertyName, col.IsForeignKey, col.CustomTypeMappingClass, col.IsBoolean, col.IsEnum, col.IsNullable, convertBool, col.EnumUnderlyingType ?? "int");
                sb.AppendLine($"            new(\"@p{i}\", (object?){valueExpr} ?? DBNull.Value),");
            }
            sb.AppendLine("        };");
        }
        else
        {
            sb.AppendLine("        var __params = Array.Empty<DiagnosticParameter>();");
        }

        sb.AppendLine($"        return new QueryDiagnostics(sql, __params, DiagnosticQueryKind.Insert, SqlDialect.{chain.Dialect}, \"{InterceptorCodeGenerator.EscapeStringLiteral(chain.TableName)}\");");
    }

    /// <summary>
    /// Emits a carrier ToDiagnostics terminal with full parameter and clause diagnostic output.
    /// </summary>
    internal static void EmitCarrierToDiagnosticsTerminal(
        StringBuilder sb, CarrierPlan carrier, AssembledPlan chain,
        string diagnosticKind)
    {
        EmitCarrierPreamble(sb, carrier, chain, emitOpId: false, emitCtxLocal: false);
        TerminalEmitHelpers.EmitParameterLocals(sb, chain, carrier);
        TerminalEmitHelpers.EmitDiagnosticClauseArray(sb, chain, carrier);
        TerminalEmitHelpers.EmitDiagnosticParameterArray(sb, chain, carrier);
        TerminalEmitHelpers.EmitDiagnosticsConstruction(sb, chain, carrier, diagnosticKind);
    }

    // ───────────────────────────────────────────────────────────────
    // Carrier helpers
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Emits code to extract a collection parameter from a Contains() call.
    /// Uses direct access when available, otherwise [UnsafeAccessor] extraction via func.Target.
    /// </summary>
    private static void EmitCollectionContainsExtraction(
        StringBuilder sb, int globalIdx, QueryParameter carrierParam,
        CarrierPlan carrier)
    {
        string fieldType;
        if (carrierParam.ElementTypeName != null)
        {
            fieldType = carrierParam.IsEnumerableCollection
                ? $"System.Collections.Generic.IEnumerable<{carrierParam.ElementTypeName}>"
                : $"System.Collections.Generic.IReadOnlyList<{carrierParam.ElementTypeName}>";
        }
        else
        {
            fieldType = carrierParam.ClrType;
        }

        if (carrierParam.IsDirectAccessible && carrierParam.CollectionAccessExpression != null)
        {
            sb.AppendLine($"        __c.P{globalIdx} = ({fieldType}){carrierParam.CollectionAccessExpression};");
        }
        else if (carrierParam.ValueExpression != null)
        {
            // Per-variable extraction locals bring captured variables into scope,
            // so ValueExpression is valid C# in the generated context
            sb.AppendLine($"        __c.P{globalIdx} = ({fieldType}){carrierParam.ValueExpression}!;");
        }
    }

    private static void EmitCarrierParamBindings(
        StringBuilder sb, CarrierPlan carrier, IReadOnlyList<QueryParameter> siteParams, int globalParamOffset,
        bool hasExtraction = false)
    {
        // Bind parameters to carrier fields using ValueExpression — per-variable locals are in scope
        for (int i = 0; i < siteParams.Count; i++)
        {
            var param = siteParams[i];
            var globalIdx = globalParamOffset + i;
            if (param.IsCollection)
            {
                EmitCollectionContainsExtraction(sb, globalIdx, param, carrier);
            }
            else if (hasExtraction && param.IsCaptured)
            {
                sb.AppendLine(FormatCarrierFieldAssignment(globalIdx, param.ValueExpression));
            }
            else
            {
                var castType = GetEffectiveCastType(globalIdx, param, carrier);
                sb.AppendLine($"        __c.P{globalIdx} = ({castType}){param.ValueExpression}!;");
            }
        }
    }


    /// <summary>
    /// Emits SQL read from the carrier's static _sql field.
    /// When no collections: direct read. When collections exist: cache check + inline StringBuilder builder.
    /// </summary>
    private static void EmitCarrierSqlDispatch(StringBuilder sb, CarrierPlan carrier, AssembledPlan chain)
    {
        var hasCollections = chain.ChainParameters.Any(p => p.IsCollection);

        if (!hasCollections)
        {
            if (chain.SqlVariants.Count == 1)
                sb.AppendLine($"        var sql = {carrier.ClassName}._sql;");
            else
                sb.AppendLine($"        var sql = {carrier.ClassName}._sql[__c.Mask];");
            return;
        }

        // Build ordered list of collection parameters with their ordinals
        var collections = new List<(int GlobalIndex, int CollectionOrdinal)>();
        int ord = 0;
        foreach (var p in chain.ChainParameters)
            if (p.IsCollection) collections.Add((p.GlobalIndex, ord++));

        var condMap = TerminalEmitHelpers.BuildParamConditionalMap(chain);

        // Step 1: Materialize collections (mask-gated if conditional)
        foreach (var p in chain.ChainParameters.Where(p => p.IsCollection))
        {
            condMap.TryGetValue(p.GlobalIndex, out var ci);
            var maskType = chain.ConditionalTerms.Count > 0 ? GetMaskType(chain) : null;

            if (ci.IsConditional && ci.BitIndex.HasValue)
            {
                sb.AppendLine($"        System.Collections.Generic.IReadOnlyList<{p.ElementTypeName}>? __col{p.GlobalIndex} = null;");
                sb.AppendLine($"        int __col{p.GlobalIndex}Len = 0;");
                sb.AppendLine($"        if ((__c.Mask & unchecked(({maskType})(1 << {ci.BitIndex.Value}))) != 0)");
                sb.AppendLine($"        {{");
                if (p.IsEnumerableCollection)
                    sb.AppendLine($"            __col{p.GlobalIndex} = Quarry.Internal.CollectionHelper.Materialize(__c.P{p.GlobalIndex});");
                else
                    sb.AppendLine($"            __col{p.GlobalIndex} = __c.P{p.GlobalIndex};");
                sb.AppendLine($"            __col{p.GlobalIndex}Len = __col{p.GlobalIndex}.Count;");
                sb.AppendLine($"        }}");
            }
            else
            {
                if (p.IsEnumerableCollection)
                    sb.AppendLine($"        var __col{p.GlobalIndex} = Quarry.Internal.CollectionHelper.Materialize(__c.P{p.GlobalIndex});");
                else
                    sb.AppendLine($"        var __col{p.GlobalIndex} = __c.P{p.GlobalIndex};");
                sb.AppendLine($"        var __col{p.GlobalIndex}Len = __col{p.GlobalIndex}.Count;");
            }
        }

        // Step 2: Hash of collection sizes
        sb.Append("        var __colHash = ");
        if (collections.Count == 1)
        {
            sb.AppendLine($"__col{collections[0].GlobalIndex}Len * 16777619;");
        }
        else
        {
            var primes = new[] { 16777619, 486187739, 1073741827, 2013265921, 40343, 999979, 15485863, 32452843 };
            var parts = new List<string>();
            for (int i = 0; i < collections.Count; i++)
            {
                var prime = primes[i % primes.Length];
                parts.Add($"(__col{collections[i].GlobalIndex}Len * {prime})");
            }
            sb.AppendLine($"{string.Join(" ^ ", parts)};");
        }

        // Step 3: Cache check
        var maskExpr = chain.SqlVariants.Count == 1 ? "0" : "__c.Mask";
        sb.AppendLine($"        var __cached = {carrier.ClassName}._sqlCache[{maskExpr}];");

        // Declare vars
        foreach (var c in collections)
            sb.AppendLine($"        string[] __col{c.GlobalIndex}Parts;");
        sb.AppendLine("        int __colShift;");
        sb.AppendLine("        string sql;");

        // Cache hit
        sb.AppendLine("        if (__cached != null && __cached.Hash == __colHash)");
        sb.AppendLine("        {");
        sb.AppendLine("            sql = __cached.Sql;");
        sb.AppendLine("            __colShift = __cached.ColShift;");
        for (int i = 0; i < collections.Count; i++)
            sb.AppendLine($"            __col{collections[i].GlobalIndex}Parts = __cached.ColParts[{i}];");
        sb.AppendLine("        }");
        sb.AppendLine("        else");
        sb.AppendLine("        {");

        // Cache miss: populate parts arrays
        sb.AppendLine("            __colShift = 0;");
        foreach (var c in collections)
        {
            TerminalEmitHelpers.EmitCollectionPartsPopulation(sb, "            ", c.GlobalIndex, chain.Dialect);
            sb.AppendLine($"            __colShift += __col{c.GlobalIndex}Len - 1;");
        }

        // Compute max template length for StringBuilder capacity
        var maxTemplateLen = chain.SqlVariants.Values.Max(v => v.Sql.Length);
        sb.AppendLine($"            var __sb = new System.Text.StringBuilder({maxTemplateLen + 64});");

        // Reset __colShift before builder (parts already computed with correct offsets)
        sb.AppendLine("            __colShift = 0;");

        if (chain.SqlVariants.Count == 1)
        {
            // Single variant — inline builder directly
            var variant = chain.SqlVariants.Values.First();
            var segments = TerminalEmitHelpers.ParseSqlSegments(variant.Sql, chain.Dialect);
            TerminalEmitHelpers.EmitInlineSqlBuilder(sb, "            ", segments, chain.Dialect, collections);
        }
        else
        {
            // Multi-variant — switch on mask
            sb.AppendLine("            switch (__c.Mask)");
            sb.AppendLine("            {");
            foreach (var kvp in chain.SqlVariants.OrderBy(kv => kv.Key))
            {
                sb.AppendLine($"            case {kvp.Key}:");
                var segments = TerminalEmitHelpers.ParseSqlSegments(kvp.Value.Sql, chain.Dialect);
                TerminalEmitHelpers.EmitInlineSqlBuilder(sb, "                ", segments, chain.Dialect, collections);
                // Reset __colShift between cases would be wrong — each case is mutually exclusive
                sb.AppendLine("                break;");
            }
            sb.AppendLine("            default: break;");
            sb.AppendLine("            }");
        }

        sb.AppendLine("            sql = __sb.ToString();");
        // Store to cache
        sb.Append($"            {carrier.ClassName}._sqlCache[{maskExpr}] = new Quarry.Internal.CollectionSqlCache(__colHash, sql, __colShift, new string[][] {{ ");
        sb.Append(string.Join(", ", collections.Select(c => $"__col{c.GlobalIndex}Parts")));
        sb.AppendLine(" });");

        sb.AppendLine("        }");
    }

    /// <summary>
    /// Emits the static _sqlCache field for carriers with collection parameters.
    /// </summary>
    private static void EmitCollectionSqlCacheField(StringBuilder sb, AssembledPlan chain)
    {
        var maxMask = chain.SqlVariants.Count == 1 ? 0 : chain.SqlVariants.Keys.Max();
        var arraySize = maxMask + 1;
        sb.AppendLine($"    internal static readonly Quarry.Internal.CollectionSqlCache?[] _sqlCache = new Quarry.Internal.CollectionSqlCache?[{arraySize}];");
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
    /// Formats a compile-time constant parameter name string (no shift).
    /// PostgreSQL returns the empty string so Npgsql stays on its native
    /// positional-binding path against the `$N` placeholders — see
    /// <see cref="Quarry.Shared.Sql.SqlFormatting.GetParameterName"/> for the
    /// full rationale (this is the generator-side counterpart).
    /// </summary>
    internal static string FormatParamName(SqlDialect dialect, int index)
    {
        return dialect switch
        {
            SqlDialect.PostgreSQL => string.Empty,
            SqlDialect.MySQL => "?",
            _ => $"@p{index}"
        };
    }

    /// <summary>
    /// Returns a C# expression that evaluates to the parameter name string at
    /// runtime. For PostgreSQL this is always the empty-string literal (native
    /// positional binding, see <see cref="FormatParamName"/>); SQLite/SqlServer
    /// use <c>ParameterNames.AtP</c> for a zero-allocation lookup; MySQL uses
    /// the literal `?` placeholder string.
    /// </summary>
    internal static string EmitParamNameExpr(SqlDialect dialect, int originalIndex, string shiftVar)
    {
        return dialect switch
        {
            SqlDialect.PostgreSQL => "\"\"",
            SqlDialect.MySQL => "\"?\"",
            _ => $"Quarry.Internal.ParameterNames.AtP({originalIndex} + {shiftVar})"
        };
    }

    /// <summary>
    /// Formats a carrier field assignment for a captured parameter with extraction.
    /// Parenthesizes compound expressions so the null-forgiving ! applies to the full result.
    /// </summary>
    private static string FormatCarrierFieldAssignment(int globalIndex, string valueExpression)
    {
        var wrap = valueExpression.Contains(' ') || valueExpression.Contains('(');
        return wrap
            ? $"        __c.P{globalIndex} = ({valueExpression})!;"
            : $"        __c.P{globalIndex} = {valueExpression}!;";
    }

    /// <summary>
    /// Gets the effective CLR type for a parameter, using the carrier parameter's
    /// resolved field type when the QueryParameter's type is unresolved ("?" or "object").
    /// Returns the non-nullable base type suitable for casts.
    /// </summary>
    internal static string GetEffectiveCastType(int globalIndex, QueryParameter queryParam, CarrierPlan carrier)
    {
        if (queryParam.ClrType != "?" && queryParam.ClrType != "object")
            return queryParam.ClrType;

        foreach (var cp in carrier.Parameters)
        {
            if (cp.GlobalIndex == globalIndex)
            {
                var ft = cp.FieldType;
                if (ft.EndsWith("?"))
                    ft = ft.Substring(0, ft.Length - 1);
                if (ft != "object")
                    return ft;
                break;
            }
        }

        return queryParam.ClrType;
    }

    /// <summary>
    /// Returns true when the reader delegate is self-contained (only references DbDataReader),
    /// false when it references interceptor-class fields (_entityReader_*, _mapper_*).
    /// </summary>
    internal static bool IsReaderSelfContained(AssembledPlan chain)
    {
        var proj = chain.ProjectionInfo;
        if (proj == null) return false;
        if (proj.CustomEntityReaderClass != null) return false;
        foreach (var col in proj.Columns)
        {
            if (col.CustomTypeMapping != null) return false;
        }
        return true;
    }

}
