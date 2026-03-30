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
        sb.AppendLine($"/// <remarks>Chain: PrebuiltDispatch (1 allocation: carrier)</remarks>");
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

        // [UnsafeAccessor] extern methods for captured parameter extraction
        foreach (var sf in strategy.StaticFields)
        {
            // Old pipeline: StaticFields may not have UnsafeAccessor metadata; emit legacy format as fallback
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
            $"IJoinedQueryBuilder<{joinedTypes[0]}, {joinedTypes[1]}>"
        };

        if (joinCount >= 2)
            interfaces.Add($"IJoinedQueryBuilder3<{joinedTypes[0]}, {joinedTypes[1]}, {joinedTypes[2]}>");
        if (joinCount >= 3)
            interfaces.Add($"IJoinedQueryBuilder4<{joinedStr}>");

        if (resultType != null)
        {
            var lastJoinInterface = joinCount switch
            {
                1 => $"IJoinedQueryBuilder<{joinedStr}, {resultType}>",
                2 => $"IJoinedQueryBuilder3<{joinedStr}, {resultType}>",
                3 => $"IJoinedQueryBuilder4<{joinedStr}, {resultType}>",
                _ => $"IJoinedQueryBuilder<{joinedStr}, {resultType}>"
            };
            interfaces.Add(lastJoinInterface);
        }

        return interfaces.ToArray();
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
        bool hasResolvableCapturedParams, List<InterceptorCodeGenerator.CachedExtractorField> methodFields,
        string delegateParamName = "func")
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
            else if (clause.Site.Kind == InterceptorKind.UpdateSetAction && clause.Site.Bound.Raw.SetActionParameters != null)
                globalParamOffset += clause.Site.Bound.Raw.SetActionParameters.Count;
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

        // Determine which parameters to bind: clause params or SetAction params
        IReadOnlyList<Translation.ParameterInfo>? clauseParams = null;
        if (site.Kind == InterceptorKind.UpdateSetAction)
            clauseParams = site.Bound.Raw.SetActionParameters;
        else if (site.Clause != null)
            clauseParams = site.Clause.Parameters;

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
                    EmitCollectionContainsExtraction(sb, globalIdx, carrierParam, carrier, delegateParamName);
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
                        // Per-variable extraction locals are already typed by the [UnsafeAccessor]
                        // return type, so ValueExpression is type-safe C# — no cast needed.
                        // For compound expressions (a + b), parenthesize so ! applies to the full result.
                        // For simple identifiers/member-access, bare ! is correct and avoids C# cast ambiguity.
                        var ve = p.ValueExpression;
                        var wrap = ve.Contains(' ') || ve.Contains('(');
                        sb.AppendLine(wrap
                            ? $"        __c.P{globalIdx} = ({ve})!;"
                            : $"        __c.P{globalIdx} = {ve}!;");
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
        // Cast incoming builder to concrete type to extract ExecutionContext
        sb.AppendLine($"        var __b = Unsafe.As<{builderTypeName}>(builder);");
        sb.Append($"        var __c = new {carrier.ClassName} {{ ");

        var isReadOnly = chain.ExecutionSite.Kind is InterceptorKind.ToDiagnostics;
        if (!isReadOnly)
        {
            sb.Append("Ctx = __b.State.ExecutionContext");
        }

        sb.AppendLine(" };");

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
    private static void EmitCarrierCommandBinding(
        StringBuilder sb, AssembledPlan chain, CarrierPlan carrier,
        string timeoutExpr)
    {
        sb.AppendLine("        var __cmd = __ctx.Connection.CreateCommand();");
        sb.AppendLine("        __cmd.CommandText = sql;");
        sb.AppendLine($"        __cmd.CommandTimeout = (int)({timeoutExpr}).TotalSeconds;");

        var dialectLiteral = GetDialectLiteral(chain.Dialect);
        var paramCount = chain.ChainParameters.Count;
        var hasLimitField = HasCarrierField(carrier, FieldRole.Limit);
        var hasOffsetField = HasCarrierField(carrier, FieldRole.Offset);

        var condMap = TerminalEmitHelpers.BuildParamConditionalMap(chain);
        var hasConditional = chain.ConditionalTerms.Count > 0;
        var maskType = hasConditional ? GetMaskType(chain) : null;

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
                // EmitCollectionExpansion (called in preamble) already declared:
                //   __col{i} = __c.P{i}        (IReadOnlyList<T>)
                //   __col{i}Len = count
                //   __col{i}Parts = string[]    (parameter name per element)
                sb.AppendLine($"{indent}for (int __bi = 0; __bi < __col{i}Len; __bi++)");
                sb.AppendLine($"{indent}{{");
                sb.AppendLine($"{indent}    var __pc = __cmd.CreateParameter();");
                sb.AppendLine($"{indent}    __pc.ParameterName = __col{i}Parts[__bi];");
                sb.AppendLine($"{indent}    __pc.Value = (object?)__col{i}[__bi] ?? DBNull.Value;");
                sb.AppendLine($"{indent}    __cmd.Parameters.Add(__pc);");
                sb.AppendLine($"{indent}}}");
            }
            else
            {
                var valueExpr = TerminalEmitHelpers.GetParameterValueExpression(param, i);

                sb.AppendLine($"{indent}var __p{i} = __cmd.CreateParameter();");
                sb.AppendLine($"{indent}__p{i}.ParameterName = \"@p{i}\";");
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

        // Pagination parameters — always unconditional
        var nextIdx = paramCount;
        if (hasLimitField)
        {
            sb.AppendLine($"        var __pL = __cmd.CreateParameter();");
            sb.AppendLine($"        __pL.ParameterName = \"@p{nextIdx}\";");
            sb.AppendLine($"        __pL.Value = (object)__c.Limit;");
            sb.AppendLine($"        __cmd.Parameters.Add(__pL);");
            nextIdx++;
        }
        if (hasOffsetField)
        {
            sb.AppendLine($"        var __pO = __cmd.CreateParameter();");
            sb.AppendLine($"        __pO.ParameterName = \"@p{nextIdx}\";");
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

        // Command binding
        var timeoutExpr = HasCarrierField(carrier, FieldRole.Timeout)
            ? "__c.Timeout ?? __ctx.DefaultTimeout"
            : "__ctx.DefaultTimeout";
        EmitCarrierCommandBinding(sb, chain, carrier, timeoutExpr);

        // Executor call
        var readerArg = readerExpression != null ? $", {readerExpression}" : "";
        sb.AppendLine($"        return QueryExecutor.{executorMethod}(__opId, __ctx, __cmd{readerArg}, cancellationToken);");
    }

    /// <summary>
    /// Emits a carrier non-query execution terminal (DELETE/UPDATE) with inline binding.
    /// </summary>
    internal static void EmitCarrierNonQueryTerminal(
        StringBuilder sb, CarrierPlan carrier, AssembledPlan chain)
    {
        EmitCarrierPreamble(sb, carrier, chain);

        sb.AppendLine("        if (__logger?.IsEnabled(LogLevel.Debug, QueryLog.CategoryName) == true)");
        sb.AppendLine("            QueryLog.SqlGenerated(__opId, sql);");

        EmitInlineParameterLogging(sb, chain, carrier);

        var timeoutExpr = HasCarrierField(carrier, FieldRole.Timeout)
            ? "__c.Timeout ?? __ctx.DefaultTimeout"
            : "__ctx.DefaultTimeout";
        EmitCarrierCommandBinding(sb, chain, carrier, timeoutExpr);

        sb.AppendLine("        return QueryExecutor.ExecuteCarrierNonQueryWithCommandAsync(__opId, __ctx, __cmd, cancellationToken);");
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

        // Command creation + inline parameter binding from entity properties
        var timeoutExpr = HasCarrierField(carrier, FieldRole.Timeout)
            ? "__c.Timeout ?? __ctx.DefaultTimeout"
            : "__ctx.DefaultTimeout";
        sb.AppendLine("        var __cmd = __ctx.Connection.CreateCommand();");
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
                var valueExpr = InterceptorCodeGenerator.GetColumnValueExpression("__c.Entity!", col.PropertyName, col.IsForeignKey, col.CustomTypeMappingClass, col.IsBoolean, col.IsEnum, col.IsNullable, convertBool);
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
        CarrierPlan carrier, string delegateParamName = "func")
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
                // Per-variable extraction locals are already typed — no cast needed.
                // For compound expressions, parenthesize so ! applies to the full result.
                var ve = param.ValueExpression;
                var wrap = ve.Contains(' ') || ve.Contains('(');
                sb.AppendLine(wrap
                    ? $"        __c.P{globalIdx} = ({ve})!;"
                    : $"        __c.P{globalIdx} = {ve}!;");
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

}
