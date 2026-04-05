using System.Collections.Generic;
using System.IO;
using System.Text;
using Quarry.Generators.Generation;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;
using Quarry.Generators.Translation;

namespace Quarry.Generators.CodeGen;

/// <summary>
/// Shared helpers for emitting terminal method bodies.
/// Both execution terminals and diagnostic terminals call these methods,
/// ensuring SQL dispatch and parameter binding cannot drift between paths.
/// </summary>
internal static class TerminalEmitHelpers
{
    /// <summary>
    /// Resolves a clause site's parameters and their global offset within the chain.
    /// Handles all three parameter sources: UpdateSetPoco columns, standard clause
    /// parameters, and UpdateSetAction parameters.
    /// </summary>
    internal static (List<QueryParameter> SiteParams, int GlobalOffset) ResolveSiteParams(
        AssembledPlan chain,
        string siteUniqueId)
    {
        var globalParamOffset = 0;
        foreach (var clause in chain.GetClauseEntries())
        {
            if (clause.Site.UniqueId == siteUniqueId)
            {
                var siteParams = new List<QueryParameter>();
                if (clause.Site.Clause != null)
                    for (int i = 0; i < clause.Site.Clause.Parameters.Count && globalParamOffset + i < chain.ChainParameters.Count; i++)
                        siteParams.Add(chain.ChainParameters[globalParamOffset + i]);
                return (siteParams, globalParamOffset);
            }
            if (clause.Site.Kind == InterceptorKind.UpdateSetPoco && clause.Site.UpdateInfo != null)
                globalParamOffset += clause.Site.UpdateInfo.Columns.Count;
            else if (clause.Site.Clause != null)
                globalParamOffset += clause.Site.Clause.Parameters.Count;
            else if (clause.Site.Kind == InterceptorKind.UpdateSetAction && clause.Site.Bound.Raw.SetActionParameters != null)
                globalParamOffset += clause.Site.Bound.Raw.SetActionParameters.Count;
        }
        return (new List<QueryParameter>(), globalParamOffset);
    }

    /// <summary>
    /// Emits parameter value extraction into __pVal* local variables from carrier fields.
    /// </summary>
    internal static void EmitParameterLocals(
        StringBuilder sb, AssembledPlan chain, CarrierPlan carrier)
    {
        var paramCount = chain.ChainParameters.Count;
        var hasLimitField = CarrierEmitter.HasCarrierField(carrier, FieldRole.Limit);
        var hasOffsetField = CarrierEmitter.HasCarrierField(carrier, FieldRole.Offset);

        for (int i = 0; i < paramCount; i++)
        {
            var param = chain.ChainParameters[i];
            if (param.IsCollection) continue;
            sb.AppendLine($"        var __pVal{i} = {GetParameterValueExpression(param, i)};");
        }

        if (hasLimitField)
            sb.AppendLine("        var __pValL = (object)__c.Limit;");
        if (hasOffsetField)
            sb.AppendLine("        var __pValO = (object)__c.Offset;");
    }

    /// <summary>
    /// Computes the longest common directory prefix from a list of file paths.
    /// Used to make source locations project-relative.
    /// </summary>
    private static string ComputeCommonDirectory(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return "";
        var first = paths[0].Replace('\\', '/');
        var prefix = first.Substring(0, first.LastIndexOf('/') + 1);
        for (int i = 1; i < paths.Count; i++)
        {
            var normalized = paths[i].Replace('\\', '/');
            while (prefix.Length > 0 && !normalized.StartsWith(prefix))
            {
                var trimmed = prefix.TrimEnd('/');
                var lastSlash = trimmed.LastIndexOf('/');
                prefix = lastSlash >= 0 ? trimmed.Substring(0, lastSlash + 1) : "";
            }
        }
        return prefix;
    }

    /// <summary>
    /// Makes a file path relative to a common directory, using forward slashes.
    /// Falls back to the file name if the path doesn't start with the base.
    /// </summary>
    private static string MakeProjectRelativePath(string absolutePath, string commonDir)
    {
        if (string.IsNullOrEmpty(commonDir))
            return Path.GetFileName(absolutePath);
        var normalized = absolutePath.Replace('\\', '/');
        return normalized.StartsWith(commonDir)
            ? normalized.Substring(commonDir.Length)
            : Path.GetFileName(absolutePath);
    }

    /// <summary>
    /// Builds a mapping from global parameter index to conditional metadata (isConditional, bitIndex).
    /// </summary>
    internal static Dictionary<int, (bool IsConditional, int? BitIndex)> BuildParamConditionalMap(AssembledPlan chain)
    {
        var map = new Dictionary<int, (bool, int?)>();
        var globalOffset = 0;
        foreach (var clause in chain.GetClauseEntries())
        {
            var paramCount = GetClauseParamCount(clause);
            for (int i = 0; i < paramCount; i++)
                map[globalOffset + i] = (clause.IsConditional, clause.BitIndex);
            globalOffset += paramCount;
        }
        return map;
    }

    /// <summary>
    /// Gets the parameter count for a clause entry, handling all three parameter sources.
    /// </summary>
    private static int GetClauseParamCount(ChainClauseEntry clause)
    {
        if (clause.Site.Kind == InterceptorKind.UpdateSetPoco && clause.Site.UpdateInfo != null)
            return clause.Site.UpdateInfo.Columns.Count;
        if (clause.Site.Clause != null)
            return clause.Site.Clause.Parameters.Count;
        if (clause.Site.Kind == InterceptorKind.UpdateSetAction && clause.Site.Bound.Raw.SetActionParameters != null)
            return clause.Site.Bound.Raw.SetActionParameters.Count;
        return 0;
    }

    /// <summary>
    /// Formats the expanded metadata arguments for a DiagnosticParameter constructor call.
    /// </summary>
    private static string FormatParamMetadata(QueryParameter p, bool isConditional, int? bitIndex)
    {
        var esc = InterceptorCodeGenerator.EscapeStringLiteral;
        var sb = new StringBuilder();
        sb.Append($", typeName: \"{esc(p.ClrType)}\"");
        if (p.TypeMappingClass != null)
            sb.Append($", typeMappingClass: \"{esc(p.TypeMappingClass)}\"");
        if (p.IsSensitive) sb.Append(", isSensitive: true");
        if (p.IsEnum) sb.Append(", isEnum: true");
        if (p.IsCollection) sb.Append(", isCollection: true");
        if (isConditional) sb.Append(", isConditional: true");
        if (bitIndex.HasValue) sb.Append($", conditionalBitIndex: {bitIndex.Value}");
        return sb.ToString();
    }

    /// <summary>
    /// Emits DiagnosticParameter[] from carrier fields with full metadata.
    /// Handles scalar, collection, enum, sensitive, and conditional parameters.
    /// </summary>
    internal static void EmitDiagnosticParameterArray(
        StringBuilder sb, AssembledPlan chain, CarrierPlan carrier)
    {
        var hasScalar = chain.ChainParameters.Any(p => !p.IsCollection);
        var hasCollection = chain.ChainParameters.Any(p => p.IsCollection);
        var hasLimitField = CarrierEmitter.HasCarrierField(carrier, FieldRole.Limit);
        var hasOffsetField = CarrierEmitter.HasCarrierField(carrier, FieldRole.Offset);
        var paginationBaseIdx = chain.ChainParameters.Count;
        var condMap = BuildParamConditionalMap(chain);

        if (!hasScalar && !hasCollection && !hasLimitField && !hasOffsetField)
        {
            sb.AppendLine("        var __params = Array.Empty<DiagnosticParameter>();");
            return;
        }

        if (!hasCollection)
        {
            var entries = new List<string>();
            foreach (var p in chain.ChainParameters.Where(p => !p.IsCollection))
            {
                condMap.TryGetValue(p.GlobalIndex, out var ci);
                var meta = FormatParamMetadata(p, ci.IsConditional, ci.BitIndex);
                entries.Add($"new(\"@p{p.GlobalIndex}\", __pVal{p.GlobalIndex}{meta})");
            }
            if (hasLimitField)
                entries.Add($"new(\"@p{paginationBaseIdx}\", __pValL, typeName: \"Int32\")");
            if (hasOffsetField)
            {
                var offsetIdx = paginationBaseIdx + (hasLimitField ? 1 : 0);
                entries.Add($"new(\"@p{offsetIdx}\", __pValO, typeName: \"Int32\")");
            }
            sb.Append("        var __params = new DiagnosticParameter[] { ");
            sb.Append(string.Join(", ", entries));
            sb.AppendLine(" };");
            return;
        }

        // Mixed scalar + collection case: use List<> then ToArray()
        var scalarCount = chain.ChainParameters.Count(p => !p.IsCollection)
            + (hasLimitField ? 1 : 0) + (hasOffsetField ? 1 : 0);
        var collectionLenExprs = chain.ChainParameters
            .Where(p => p.IsCollection)
            .Select(p => $"__col{p.GlobalIndex}Len");
        var capacityExpr = scalarCount > 0
            ? $"{scalarCount} + {string.Join(" + ", collectionLenExprs)}"
            : string.Join(" + ", collectionLenExprs);

        sb.AppendLine($"        var __paramList = new System.Collections.Generic.List<DiagnosticParameter>({capacityExpr});");
        sb.AppendLine("        var __diagShift = 0;");

        foreach (var p in chain.ChainParameters.OrderBy(p => p.GlobalIndex))
        {
            condMap.TryGetValue(p.GlobalIndex, out var ci);
            if (p.IsCollection)
            {
                var meta = FormatParamMetadata(p, ci.IsConditional, ci.BitIndex);
                sb.AppendLine($"        for (int __pi = 0; __pi < __col{p.GlobalIndex}Len; __pi++)");
                sb.AppendLine($"            __paramList.Add(new DiagnosticParameter(__col{p.GlobalIndex}Parts[__pi], __col{p.GlobalIndex}[__pi]{meta}));");
                sb.AppendLine($"        __diagShift += __col{p.GlobalIndex}Len - 1;");
            }
            else
            {
                var meta = FormatParamMetadata(p, ci.IsConditional, ci.BitIndex);
                var nameExpr = EmitDiagParamNameExprWithVar(chain.Dialect, p.GlobalIndex, "__diagShift");
                sb.AppendLine($"        __paramList.Add(new DiagnosticParameter({nameExpr}, __pVal{p.GlobalIndex}{meta}));");
            }
        }

        if (hasLimitField)
        {
            // __diagShift == __colShift here: all collections processed, same accumulation.
            var limitNameExpr = EmitDiagParamNameExprWithVar(chain.Dialect, paginationBaseIdx, "__diagShift");
            sb.AppendLine($"        __paramList.Add(new DiagnosticParameter({limitNameExpr}, __pValL, typeName: \"Int32\"));");
        }
        if (hasOffsetField)
        {
            var offsetIdx = paginationBaseIdx + (hasLimitField ? 1 : 0);
            var offsetNameExpr = EmitDiagParamNameExprWithVar(chain.Dialect, offsetIdx, "__diagShift");
            sb.AppendLine($"        __paramList.Add(new DiagnosticParameter({offsetNameExpr}, __pValO, typeName: \"Int32\"));");
        }

        sb.AppendLine("        var __params = __paramList.ToArray();");
    }

    /// <summary>
    /// Gets the expanded metadata args string for a DiagnosticParameter given its global index.
    /// Returns empty string if the parameter cannot be found.
    /// </summary>
    private static string GetParamMetadataByGlobalIndex(
        AssembledPlan chain, int globalIdx, Dictionary<int, (bool IsConditional, int? BitIndex)> condMap)
    {
        if (globalIdx < chain.ChainParameters.Count)
        {
            var qp = chain.ChainParameters[globalIdx];
            condMap.TryGetValue(globalIdx, out var ci);
            return FormatParamMetadata(qp, ci.IsConditional, ci.BitIndex);
        }
        return "";
    }

    /// <summary>
    /// Emits ClauseDiagnostic[] with SQL fragments, per-clause parameters, and conditional status.
    /// </summary>
    internal static void EmitDiagnosticClauseArray(
        StringBuilder sb, AssembledPlan chain, CarrierPlan? carrier = null)
    {
        var hasChainCollections = chain.ChainParameters.Any(p => p.IsCollection);
        var diagnosticClauses = chain.GetClauseEntries()
            .Where(c => InterceptorCodeGenerator.IsDiagnosticClauseRole(c.Role))
            .ToList();
        var condMap = BuildParamConditionalMap(chain);

        if (diagnosticClauses.Count == 0)
        {
            sb.AppendLine("        var __clauses = Array.Empty<ClauseDiagnostic>();");
            return;
        }

        var globalParamOffset = 0;
        var clauseParamOffsets = new Dictionary<string, int>();
        foreach (var clause in chain.GetClauseEntries())
        {
            clauseParamOffsets[clause.Site.UniqueId] = globalParamOffset;
            globalParamOffset += GetClauseParamCount(clause);
        }

        var paginationBaseIdx = chain.ChainParameters.Count;
        var hasLimitField = carrier != null && CarrierEmitter.HasCarrierField(carrier, FieldRole.Limit);
        var hasOffsetField = carrier != null && CarrierEmitter.HasCarrierField(carrier, FieldRole.Offset);
        var maskType = diagnosticClauses.Any(c => c.IsConditional) ? CarrierEmitter.GetMaskType(chain) : null;

        // Section 1: Collection SQL fragment expansion
        for (int clauseIdx = 0; clauseIdx < diagnosticClauses.Count; clauseIdx++)
        {
            var clause = diagnosticClauses[clauseIdx];
            if (carrier == null || clause.Site.Clause?.Parameters.Any(p => p.IsCollection) != true)
                continue;

            var sqlFrag = clause.Site.Clause?.SqlFragment ?? "";
            var offset = clauseParamOffsets[clause.Site.UniqueId];
            var tokenizedFrag = sqlFrag;

            foreach (var p in clause.Site.Clause!.Parameters.Where(p => p.IsCollection))
            {
                var globalIdx = offset + p.Index;
                var token = $"{{__COL_P{globalIdx}__}}";
                if (chain.Dialect == SqlDialect.MySQL)
                {
                    var qCount = 0;
                    for (int ci = 0; ci < tokenizedFrag.Length; ci++)
                    {
                        if (tokenizedFrag[ci] == '?')
                        {
                            if (qCount == p.Index)
                            {
                                tokenizedFrag = tokenizedFrag.Substring(0, ci) + token + tokenizedFrag.Substring(ci + 1);
                                break;
                            }
                            qCount++;
                        }
                    }
                }
                else
                {
                    var placeholder = chain.Dialect switch
                    {
                        SqlDialect.PostgreSQL => $"${globalIdx + 1}",
                        _ => $"@p{globalIdx}"
                    };
                    tokenizedFrag = tokenizedFrag.Replace(placeholder, token);
                }
            }

            sb.AppendLine($"        var __clauseSql{clauseIdx} = @\"{InterceptorCodeGenerator.EscapeStringLiteral(tokenizedFrag)}\";");
            foreach (var p in clause.Site.Clause!.Parameters.Where(p => p.IsCollection))
            {
                var globalIdx = offset + p.Index;
                sb.AppendLine($"        __clauseSql{clauseIdx} = __clauseSql{clauseIdx}.Replace(\"{{__COL_P{globalIdx}__}}\", __col{globalIdx}Len == 0 ? \"SELECT 1 WHERE 1=0\" : string.Join(\", \", __col{globalIdx}Parts));");
            }

            // Shift scalar parameter references in the fragment so diagnostic SQL
            // matches the actual expanded SQL (where indices are shifted by collections).
            if (chain.Dialect != SqlDialect.MySQL)
            {
                foreach (var p in clause.Site.Clause!.Parameters.Where(p => !p.IsCollection))
                {
                    var globalIdx = offset + p.Index;
                    var shiftExpr = ComputeShiftExprForIndex(chain, globalIdx);
                    if (shiftExpr == "0") continue; // no preceding collections → no shift needed
                    var originalPlaceholder = chain.Dialect == SqlDialect.PostgreSQL
                        ? $"${globalIdx + 1}"
                        : $"@p{globalIdx}";
                    var nameExpr = EmitDiagParamNameExprWithVar(chain.Dialect, globalIdx, shiftExpr);
                    sb.AppendLine($"        __clauseSql{clauseIdx} = __clauseSql{clauseIdx}.Replace(\"{originalPlaceholder}\", {nameExpr});");
                }
            }
        }

        // Section 2: Collection parameter array construction
        for (int clauseIdx = 0; clauseIdx < diagnosticClauses.Count; clauseIdx++)
        {
            var clause = diagnosticClauses[clauseIdx];
            if (carrier == null || clause.Site.Clause?.Parameters.Any(p => p.IsCollection) != true)
                continue;

            var offset = clauseParamOffsets[clause.Site.UniqueId];
            var clauseParams = clause.Site.Clause!.Parameters;
            var hasScalarInClause = clauseParams.Any(p => !p.IsCollection);

            if (!hasScalarInClause)
            {
                var collectionParam = clauseParams.First(p => p.IsCollection);
                var globalIdx = offset + collectionParam.Index;
                var meta = GetParamMetadataByGlobalIndex(chain, globalIdx, condMap);
                sb.AppendLine($"        var __clauseParams{clauseIdx} = new DiagnosticParameter[__col{globalIdx}Len];");
                sb.AppendLine($"        for (int __ci = 0; __ci < __col{globalIdx}Len; __ci++)");
                sb.AppendLine($"            __clauseParams{clauseIdx}[__ci] = new DiagnosticParameter(__col{globalIdx}Parts[__ci], __col{globalIdx}[__ci]{meta});");
            }
            else
            {
                var scalarCount2 = clauseParams.Count(p => !p.IsCollection);
                var collectionLens = clauseParams
                    .Where(p => p.IsCollection)
                    .Select(p => $"__col{offset + p.Index}Len");
                var capExpr = $"{scalarCount2} + {string.Join(" + ", collectionLens)}";

                sb.AppendLine($"        var __cpList{clauseIdx} = new System.Collections.Generic.List<DiagnosticParameter>({capExpr});");
                foreach (var p in clauseParams.OrderBy(p => p.Index))
                {
                    var globalIdx = offset + p.Index;
                    var meta = GetParamMetadataByGlobalIndex(chain, globalIdx, condMap);
                    if (p.IsCollection)
                    {
                        sb.AppendLine($"        for (int __ci = 0; __ci < __col{globalIdx}Len; __ci++)");
                        sb.AppendLine($"            __cpList{clauseIdx}.Add(new DiagnosticParameter(__col{globalIdx}Parts[__ci], __col{globalIdx}[__ci]{meta}));");
                    }
                    else
                    {
                        var shiftExpr = ComputeShiftExprForIndex(chain, globalIdx);
                        var nameExpr = EmitDiagParamNameExprWithVar(chain.Dialect, globalIdx, shiftExpr);
                        sb.AppendLine($"        __cpList{clauseIdx}.Add(new DiagnosticParameter({nameExpr}, __pVal{globalIdx}{meta}));");
                    }
                }
                sb.AppendLine($"        var __clauseParams{clauseIdx} = __cpList{clauseIdx}.ToArray();");
            }
        }

        // Section 3: ClauseDiagnostic array construction
        var allPaths = diagnosticClauses.Select(c => c.Site.FilePath).Distinct().ToList();
        var commonDir = ComputeCommonDirectory(allPaths);

        sb.AppendLine("        var __clauses = new ClauseDiagnostic[]");
        sb.AppendLine("        {");
        foreach (var clause in diagnosticClauses)
        {
            var clauseType = clause.Role.ToString();
            var sqlFragment = clause.Site.Clause?.SqlFragment ?? "";
            var escapedFragment = InterceptorCodeGenerator.EscapeStringLiteral(sqlFragment);
            var isConditional = clause.IsConditional ? "true" : "false";

            string isActive;
            if (!clause.IsConditional)
            {
                isActive = "true";
            }
            else
            {
                isActive = $"(__c.Mask & unchecked(({maskType})(1 << {clause.BitIndex!.Value}))) != 0";
            }

            var hasCollectionParam = carrier != null && clause.Site.Clause?.Parameters.Any(p => p.IsCollection) == true;
            var clauseSqlExpr = hasCollectionParam
                ? $"__clauseSql{diagnosticClauses.IndexOf(clause)}"
                : $"@\"{escapedFragment}\"";

            string paramsArg;
            var clauseParamCount = clause.Site.Clause?.Parameters.Count ?? 0;

            if (hasCollectionParam)
            {
                paramsArg = $", parameters: __clauseParams{diagnosticClauses.IndexOf(clause)}";
            }
            else if (carrier != null && clause.Role == ClauseRole.Limit && hasLimitField)
            {
                if (hasChainCollections)
                {
                    var shiftExpr = ComputeShiftExprForIndex(chain, paginationBaseIdx);
                    var nameExpr = EmitDiagParamNameExprWithVar(chain.Dialect, paginationBaseIdx, shiftExpr);
                    paramsArg = $", parameters: new DiagnosticParameter[] {{ new({nameExpr}, __pValL, typeName: \"Int32\") }}";
                }
                else
                    paramsArg = $", parameters: new DiagnosticParameter[] {{ new(\"@p{paginationBaseIdx}\", __pValL, typeName: \"Int32\") }}";
            }
            else if (carrier != null && clause.Role == ClauseRole.Offset && hasOffsetField)
            {
                var offsetIdx = paginationBaseIdx + (hasLimitField ? 1 : 0);
                if (hasChainCollections)
                {
                    var shiftExpr = ComputeShiftExprForIndex(chain, offsetIdx);
                    var nameExpr = EmitDiagParamNameExprWithVar(chain.Dialect, offsetIdx, shiftExpr);
                    paramsArg = $", parameters: new DiagnosticParameter[] {{ new({nameExpr}, __pValO, typeName: \"Int32\") }}";
                }
                else
                    paramsArg = $", parameters: new DiagnosticParameter[] {{ new(\"@p{offsetIdx}\", __pValO, typeName: \"Int32\") }}";
            }
            else if (carrier != null && clauseParamCount > 0)
            {
                var offset = clauseParamOffsets[clause.Site.UniqueId];
                var paramEntries = new List<string>();
                for (int i = 0; i < clauseParamCount; i++)
                {
                    var globalIdx = offset + i;
                    var meta = GetParamMetadataByGlobalIndex(chain, globalIdx, condMap);
                    if (hasChainCollections)
                    {
                        var shiftExpr = ComputeShiftExprForIndex(chain, globalIdx);
                        var nameExpr = EmitDiagParamNameExprWithVar(chain.Dialect, globalIdx, shiftExpr);
                        paramEntries.Add($"new({nameExpr}, __pVal{globalIdx}{meta})");
                    }
                    else
                        paramEntries.Add($"new(\"@p{globalIdx}\", __pVal{globalIdx}{meta})");
                }
                paramsArg = $", parameters: new DiagnosticParameter[] {{ {string.Join(", ", paramEntries)} }}";
            }
            else
            {
                paramsArg = "";
            }

            // Source location — use project-relative path to avoid leaking user directory structure
            var relPath = MakeProjectRelativePath(clause.Site.FilePath, commonDir).Replace("\\", "/");
            var locationArg = $", sourceLocation: new ClauseSourceLocation(\"{InterceptorCodeGenerator.EscapeStringLiteral(relPath)}\", {clause.Site.Line}, {clause.Site.Column})";

            // Conditional bit index and branch kind
            var bitIndexArg = clause.BitIndex.HasValue
                ? $", conditionalBitIndex: {clause.BitIndex.Value}"
                : "";
            var branchKindArg = "";
            if (clause.IsConditional && clause.Site.Bound.Raw.NestingContext != null)
            {
                var bk = clause.Site.Bound.Raw.NestingContext.BranchKind == Models.BranchKind.MutuallyExclusive
                    ? "DiagnosticBranchKind.MutuallyExclusive"
                    : "DiagnosticBranchKind.Independent";
                branchKindArg = $", branchKind: {bk}";
            }

            sb.AppendLine($"            new(\"{clauseType}\", {clauseSqlExpr}, isConditional: {isConditional}, isActive: {isActive}{paramsArg}{locationArg}{bitIndexArg}{branchKindArg}),");
        }
        sb.AppendLine("        };");
    }

    /// <summary>
    /// Emits the new QueryDiagnostics(...) constructor call with all fields.
    /// Single source of truth for diagnostic construction.
    /// </summary>
    internal static void EmitDiagnosticsConstruction(
        StringBuilder sb, AssembledPlan chain, CarrierPlan carrier,
        string diagnosticKind)
    {
        var plan = chain.Plan;
        var esc = InterceptorCodeGenerator.EscapeStringLiteral;

        // Tier reason
        string tierReasonLiteral;
        if (plan.Tier == OptimizationTier.RuntimeBuild)
            tierReasonLiteral = plan.NotAnalyzableReason != null
                ? $"\"{esc(plan.NotAnalyzableReason)}\""
                : "\"runtime build\"";
        else if (plan.ConditionalTerms.Count > 0)
            tierReasonLiteral = $"\"{plan.ConditionalTerms.Count} conditional bits, {chain.SqlVariants.Count} mask variants\"";
        else
            tierReasonLiteral = "\"unconditional chain, single SQL variant\"";

        var disqualifyLiteral = plan.NotAnalyzableReason != null
            ? $"\"{esc(plan.NotAnalyzableReason)}\""
            : "null";

        // SqlVariants dictionary — references carrier's static _sql field
        var hasVariants = chain.SqlVariants.Count > 0;
        if (hasVariants)
        {
            sb.AppendLine("        var __variants = new System.Collections.Generic.Dictionary<int, SqlVariantDiagnostic>");
            sb.AppendLine("        {");
            if (chain.SqlVariants.Count == 1)
            {
                foreach (var kvp in chain.SqlVariants)
                {
                    sb.AppendLine($"            {{ {kvp.Key}, new SqlVariantDiagnostic({carrier.ClassName}._sql, {kvp.Value.ParameterCount}) }},");
                }
            }
            else
            {
                foreach (var kvp in chain.SqlVariants.OrderBy(kv => kv.Key))
                {
                    sb.AppendLine($"            {{ {kvp.Key}, new SqlVariantDiagnostic({carrier.ClassName}._sql[{kvp.Key}], {kvp.Value.ParameterCount}) }},");
                }
            }
            sb.AppendLine("        };");
        }

        // Projection columns
        var projInfo = chain.ProjectionInfo;
        var hasProjection = projInfo != null && projInfo.Columns.Count > 0;
        if (hasProjection)
        {
            sb.AppendLine("        var __projCols = new ProjectionColumnDiagnostic[]");
            sb.AppendLine("        {");
            foreach (var col in projInfo!.Columns)
            {
                var typeMappingArg = col.CustomTypeMapping != null ? $", typeMappingClass: \"{esc(col.CustomTypeMapping)}\"" : "";
                var fkArgs = col.IsForeignKey ? $", isForeignKey: true, foreignKeyEntityName: \"{esc(col.ForeignKeyEntityName!)}\"" : "";
                var enumArg = col.IsEnum ? ", isEnum: true" : "";
                sb.AppendLine($"            new(\"{esc(col.PropertyName)}\", \"{esc(col.ColumnName)}\", \"{esc(col.ClrType)}\", {col.Ordinal}, isNullable: {(col.IsNullable ? "true" : "false")}{typeMappingArg}{fkArgs}{enumArg}),");
            }
            sb.AppendLine("        };");
        }

        // Joins
        var joins = plan.Joins;
        var hasJoins = joins.Count > 0;
        if (hasJoins)
        {
            sb.AppendLine("        var __joins = new JoinDiagnostic[]");
            sb.AppendLine("        {");
            foreach (var join in joins)
            {
                var schemaArg = join.Table.SchemaName != null ? $"\"{esc(join.Table.SchemaName)}\"" : "null";
                var alias = join.Table.Alias ?? join.Table.TableName;
                var onSql = join.OnCondition != null ? Quarry.Generators.IR.SqlExprRenderer.Render(join.OnCondition, chain.Dialect) : "";
                sb.AppendLine($"            new(\"{esc(join.Table.TableName)}\", {schemaArg}, \"{join.Kind}\", \"{esc(alias)}\", @\"{esc(onSql)}\"),");
            }
            sb.AppendLine("        };");
        }

        // Pagination
        var pagination = plan.Pagination;
        string limitArg = "null";
        string offsetArg = "null";
        if (pagination != null)
        {
            if (pagination.LiteralLimit.HasValue)
                limitArg = pagination.LiteralLimit.Value.ToString();
            else if (pagination.LimitParamIndex.HasValue)
                limitArg = "(int)__pValL";
            if (pagination.LiteralOffset.HasValue)
                offsetArg = pagination.LiteralOffset.Value.ToString();
            else if (pagination.OffsetParamIndex.HasValue)
                offsetArg = "(int)__pValO";
        }

        // Identity column
        var insertInfo = chain.InsertInfo;
        var identityLiteral = insertInfo?.IdentityColumnName != null
            ? $"\"{esc(insertInfo.IdentityColumnName)}\""
            : "null";

        // Unmatched method names
        var unmatchedNames = plan.UnmatchedMethodNames;
        var hasUnmatched = unmatchedNames != null && unmatchedNames.Count > 0;
        if (hasUnmatched)
        {
            sb.Append("        var __unmatched = new string[] { ");
            sb.Append(string.Join(", ", unmatchedNames!.Select(n => $"\"{esc(n)}\"")));
            sb.AppendLine(" };");
        }

        // Schema
        var schemaLiteral = chain.ExecutionSite.SchemaName != null
            ? $"\"{esc(chain.ExecutionSite.SchemaName)}\""
            : "null";

        // Active mask
        var maskExpr = plan.ConditionalTerms.Count > 0 ? "(int)__c.Mask" : "0";

        // Projection kind
        var projKindLiteral = projInfo != null ? $"\"{projInfo.Kind}\"" : "null";
        var projNonOptLiteral = projInfo != null && projInfo.NonOptimalReason != null
            ? $"\"{esc(projInfo.NonOptimalReason)}\""
            : "null";

        // Construct the QueryDiagnostics
        sb.AppendLine($"        return new QueryDiagnostics(sql, __params, {diagnosticKind}, SqlDialect.{chain.Dialect}, \"{esc(chain.TableName)}\",");
        sb.AppendLine($"            clauses: __clauses,");
        sb.AppendLine($"            tierReason: {tierReasonLiteral},");
        sb.AppendLine($"            disqualifyReason: {disqualifyLiteral},");
        sb.AppendLine($"            activeMask: {maskExpr},");
        sb.AppendLine($"            conditionalBitCount: {plan.ConditionalTerms.Count},");
        sb.AppendLine($"            sqlVariants: {(hasVariants ? "__variants" : "null")},");
        sb.AppendLine($"            allParameters: __params,");
        sb.AppendLine($"            projectionColumns: {(hasProjection ? "__projCols" : "null")},");
        sb.AppendLine($"            projectionKind: {projKindLiteral},");
        sb.AppendLine($"            projectionNonOptimalReason: {projNonOptLiteral},");
        sb.AppendLine($"            carrierClassName: \"{esc(carrier.ClassName)}\",");
        sb.AppendLine($"            schemaName: {schemaLiteral},");
        sb.AppendLine($"            joins: {(hasJoins ? "__joins" : "null")},");
        sb.AppendLine($"            isDistinct: {(plan.IsDistinct ? "true" : "false")},");
        sb.AppendLine($"            limit: {limitArg},");
        sb.AppendLine($"            offset: {offsetArg},");
        sb.AppendLine($"            identityColumnName: {identityLiteral},");
        sb.AppendLine($"            unmatchedMethodNames: {(hasUnmatched ? "__unmatched" : "null")});");
    }

    /// <summary>
    /// Resolves the return type for a terminal interceptor method based on the execution kind.
    /// </summary>
    internal static string ResolveTerminalReturnType(
        InterceptorKind kind, string resultType, string scalarTypeArg, bool isValueType)
    {
        var firstOrDefaultSuffix = isValueType ? "" : "?";
        return kind switch
        {
            InterceptorKind.ExecuteFetchAll => $"Task<List<{resultType}>>",
            InterceptorKind.ExecuteFetchFirst => $"Task<{resultType}>",
            InterceptorKind.ExecuteFetchFirstOrDefault => $"Task<{resultType}{firstOrDefaultSuffix}>",
            InterceptorKind.ExecuteFetchSingle => $"Task<{resultType}>",
            InterceptorKind.ExecuteFetchSingleOrDefault => $"Task<{resultType}{firstOrDefaultSuffix}>",
            InterceptorKind.ExecuteScalar => $"Task<{scalarTypeArg}>",
            InterceptorKind.ToAsyncEnumerable => $"IAsyncEnumerable<{resultType}>",
            _ => ""
        };
    }

    /// <summary>
    /// Resolves the carrier executor method name for a terminal execution kind.
    /// </summary>
    internal static string ResolveCarrierExecutorMethod(
        InterceptorKind kind, string resultType, string scalarTypeArg)
    {
        return kind switch
        {
            InterceptorKind.ExecuteFetchAll => $"ExecuteCarrierWithCommandAsync<{resultType}>",
            InterceptorKind.ExecuteFetchFirst => $"ExecuteCarrierFirstWithCommandAsync<{resultType}>",
            InterceptorKind.ExecuteFetchFirstOrDefault => $"ExecuteCarrierFirstOrDefaultWithCommandAsync<{resultType}>",
            InterceptorKind.ExecuteFetchSingle => $"ExecuteCarrierSingleWithCommandAsync<{resultType}>",
            InterceptorKind.ExecuteFetchSingleOrDefault => $"ExecuteCarrierSingleOrDefaultWithCommandAsync<{resultType}>",
            InterceptorKind.ExecuteScalar => $"ExecuteCarrierScalarWithCommandAsync<{scalarTypeArg}>",
            InterceptorKind.ToAsyncEnumerable => $"ToCarrierAsyncEnumerableWithCommandAsync<{resultType}>",
            _ => ""
        };
    }

    /// <summary>
    /// Computes the value expression and DbType flag for an insert column binding.
    /// Consolidates the GetColumnValueExpression call arguments (9 parameters) into a single source of truth.
    /// </summary>
    internal static (string ValueExpr, bool NeedsIntType) GetInsertColumnBinding(
        InsertColumnInfo col, string entityVar, bool convertBool)
    {
        var needsIntType = col.IsEnum || (col.IsBoolean && convertBool);
        var valueExpr = InterceptorCodeGenerator.GetColumnValueExpression(
            entityVar, col.PropertyName, col.IsForeignKey, col.CustomTypeMappingClass,
            col.IsBoolean, col.IsEnum, col.IsNullable, convertBool,
            col.EnumUnderlyingType ?? "int");
        return (valueExpr, needsIntType);
    }

    /// <summary>
    /// Gets the inline value expression for a parameter based on its type classification.
    /// </summary>
    internal static string GetParameterValueExpression(QueryParameter param, int index)
    {
        // Entity-sourced parameter (SetPoco): read from Entity field, not P{n}
        if (param.EntityPropertyExpression != null)
        {
            if (param.TypeMappingClass != null)
                return $"(object?){InterceptorCodeGenerator.GetMappingFieldName(param.TypeMappingClass)}.ToDb({param.EntityPropertyExpression}) ?? DBNull.Value";
            return $"(object?){param.EntityPropertyExpression} ?? DBNull.Value";
        }

        // Mapped type: use ToDb() conversion
        if (param.TypeMappingClass != null)
            return $"(object?){InterceptorCodeGenerator.GetMappingFieldName(param.TypeMappingClass)}.ToDb(__c.P{index}) ?? DBNull.Value";

        // Enum with known underlying type: inline cast to underlying integral type.
        // Carrier fields for enum types are always nullable — NormalizeFieldType
        // does not recognize enums as value types and appends '?'.
        // Always emit the null-safe HasValue path to avoid CS8629.
        if (param.IsEnum && param.EnumUnderlyingType != null)
        {
            return $"__c.P{index}.HasValue ? (object)({param.EnumUnderlyingType})__c.P{index}.Value : DBNull.Value";
        }

        // Default: null-safe boxing
        return $"(object?)__c.P{index} ?? DBNull.Value";
    }

    /// <summary>
    /// Returns a C# expression for a shifted parameter name at runtime, using the specified shift variable.
    /// </summary>
    private static string EmitDiagParamNameExprWithVar(SqlDialect dialect, int originalIndex, string shiftVar)
    {
        return dialect switch
        {
            SqlDialect.MySQL => "\"?\"",
            SqlDialect.PostgreSQL => $"Quarry.Internal.ParameterNames.Dollar({originalIndex} + {shiftVar})",
            _ => $"Quarry.Internal.ParameterNames.AtP({originalIndex} + {shiftVar})"
        };
    }

    /// <summary>
    /// Computes the shift expression for a parameter at the given globalIndex, based on preceding collections.
    /// Returns the sum of (__col{j}Len - 1) for all collection params with GlobalIndex &lt; globalIndex.
    /// </summary>
    private static string ComputeShiftExprForIndex(AssembledPlan chain, int globalIndex)
    {
        var precedingCollections = chain.ChainParameters
            .Where(p => p.IsCollection && p.GlobalIndex < globalIndex)
            .ToList();
        if (precedingCollections.Count == 0)
            return "0";
        var parts = precedingCollections.Select(p => $"(__col{p.GlobalIndex}Len - 1)");
        return string.Join(" + ", parts);
    }

    // ── SQL Segment Parser ─────────────────────────────────────────

    internal enum SqlSegmentKind { Literal, ScalarParam, CollectionExpand }

    internal readonly struct SqlSegment
    {
        public readonly SqlSegmentKind Kind;
        public readonly string? Text;       // Literal: verbatim SQL fragment
        public readonly int ParamIndex;     // ScalarParam: original @pN index; CollectionExpand: GlobalIndex

        public SqlSegment(SqlSegmentKind kind, string? text, int paramIndex)
        {
            Kind = kind;
            Text = text;
            ParamIndex = paramIndex;
        }

        public static SqlSegment Literal(string text) => new(SqlSegmentKind.Literal, text, -1);
        public static SqlSegment Scalar(int globalIndex) => new(SqlSegmentKind.ScalarParam, null, globalIndex);
        public static SqlSegment Collection(int globalIndex) => new(SqlSegmentKind.CollectionExpand, null, globalIndex);
    }

    /// <summary>
    /// Parses tokenized SQL (containing {__COL_PN__} and @pN/$N placeholders)
    /// into an ordered list of typed segments for inline StringBuilder emission.
    /// MySQL: only splits at collection tokens (? markers have no index).
    /// </summary>
    internal static List<SqlSegment> ParseSqlSegments(string tokenizedSql, SqlDialect dialect)
    {
        var segments = new List<SqlSegment>();
        var literal = new StringBuilder();
        int i = 0;
        int len = tokenizedSql.Length;

        while (i < len)
        {
            // Try collection token: {__COL_P(\d+)__}
            if (tokenizedSql[i] == '{' && i + 10 < len && tokenizedSql.Substring(i, 8) == "{__COL_P")
            {
                var endBrace = tokenizedSql.IndexOf("__}", i + 8);
                if (endBrace > i + 8)
                {
                    var numStr = tokenizedSql.Substring(i + 8, endBrace - (i + 8));
                    if (int.TryParse(numStr, out var colIdx))
                    {
                        if (literal.Length > 0) { segments.Add(SqlSegment.Literal(literal.ToString())); literal.Clear(); }
                        segments.Add(SqlSegment.Collection(colIdx));
                        i = endBrace + 3; // skip past "__}"
                        continue;
                    }
                }
            }

            // Try scalar param: dialect-specific
            if (dialect != SqlDialect.MySQL)
            {
                if (dialect == SqlDialect.PostgreSQL)
                {
                    // Match $(\d+) — 1-indexed
                    if (tokenizedSql[i] == '$' && i + 1 < len && tokenizedSql[i + 1] >= '0' && tokenizedSql[i + 1] <= '9')
                    {
                        int numStart = i + 1;
                        int numEnd = numStart;
                        while (numEnd < len && tokenizedSql[numEnd] >= '0' && tokenizedSql[numEnd] <= '9') numEnd++;
                        if (int.TryParse(tokenizedSql.Substring(numStart, numEnd - numStart), out var pgIdx))
                        {
                            if (literal.Length > 0) { segments.Add(SqlSegment.Literal(literal.ToString())); literal.Clear(); }
                            segments.Add(SqlSegment.Scalar(pgIdx - 1)); // convert 1-based to GlobalIndex
                            i = numEnd;
                            continue;
                        }
                    }
                }
                else
                {
                    // SQLite / SQL Server: match @p(\d+)
                    if (tokenizedSql[i] == '@' && i + 2 < len && tokenizedSql[i + 1] == 'p'
                        && tokenizedSql[i + 2] >= '0' && tokenizedSql[i + 2] <= '9')
                    {
                        int numStart = i + 2;
                        int numEnd = numStart;
                        while (numEnd < len && tokenizedSql[numEnd] >= '0' && tokenizedSql[numEnd] <= '9') numEnd++;
                        if (int.TryParse(tokenizedSql.Substring(numStart, numEnd - numStart), out var atIdx))
                        {
                            if (literal.Length > 0) { segments.Add(SqlSegment.Literal(literal.ToString())); literal.Clear(); }
                            segments.Add(SqlSegment.Scalar(atIdx));
                            i = numEnd;
                            continue;
                        }
                    }
                }
            }

            // Default: accumulate literal
            literal.Append(tokenizedSql[i]);
            i++;
        }

        if (literal.Length > 0)
            segments.Add(SqlSegment.Literal(literal.ToString()));

        return segments;
    }

    /// <summary>
    /// Emits StringBuilder.Append() calls for each parsed SQL segment.
    /// Literal → verbatim append. ScalarParam → append shifted index.
    /// CollectionExpand → empty guard + join parts array + shift accumulation.
    /// </summary>
    internal static void EmitInlineSqlBuilder(
        StringBuilder sb,
        string indent,
        List<SqlSegment> segments,
        SqlDialect dialect,
        IReadOnlyList<(int GlobalIndex, int CollectionOrdinal)> collections)
    {
        var esc = InterceptorCodeGenerator.EscapeStringLiteral;

        foreach (var seg in segments)
        {
            switch (seg.Kind)
            {
                case SqlSegmentKind.Literal:
                    sb.AppendLine($"{indent}__sb.Append(@\"{esc(seg.Text!)}\");");
                    break;

                case SqlSegmentKind.ScalarParam:
                    if (dialect == SqlDialect.PostgreSQL)
                    {
                        sb.AppendLine($"{indent}__sb.Append('$');");
                        sb.AppendLine($"{indent}__sb.Append({seg.ParamIndex} + 1 + __colShift);");
                    }
                    else
                    {
                        sb.AppendLine($"{indent}__sb.Append(\"@p\");");
                        sb.AppendLine($"{indent}__sb.Append({seg.ParamIndex} + __colShift);");
                    }
                    break;

                case SqlSegmentKind.CollectionExpand:
                {
                    sb.AppendLine($"{indent}if (__col{seg.ParamIndex}Len == 0)");
                    sb.AppendLine($"{indent}    __sb.Append(\"SELECT 1 WHERE 1=0\");");
                    sb.AppendLine($"{indent}else");
                    sb.AppendLine($"{indent}    __sb.Append(string.Join(\", \", __col{seg.ParamIndex}Parts));");
                    sb.AppendLine($"{indent}__colShift += __col{seg.ParamIndex}Len - 1;");
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Emits the __col{N}Parts array population loop for a collection parameter.
    /// </summary>
    internal static void EmitCollectionPartsPopulation(
        StringBuilder sb,
        string indent,
        int globalIndex,
        SqlDialect dialect)
    {
        if (dialect == SqlDialect.MySQL)
        {
            sb.AppendLine($"{indent}__col{globalIndex}Parts = new string[__col{globalIndex}Len];");
            sb.AppendLine($"{indent}for (int __i = 0; __i < __col{globalIndex}Len; __i++)");
            sb.AppendLine($"{indent}    __col{globalIndex}Parts[__i] = \"?\";");
        }
        else if (dialect == SqlDialect.PostgreSQL)
        {
            sb.AppendLine($"{indent}__col{globalIndex}Parts = new string[__col{globalIndex}Len];");
            sb.AppendLine($"{indent}for (int __i = 0; __i < __col{globalIndex}Len; __i++)");
            sb.AppendLine($"{indent}    __col{globalIndex}Parts[__i] = Quarry.Internal.ParameterNames.Dollar({globalIndex} + __colShift + __i);");
        }
        else
        {
            sb.AppendLine($"{indent}__col{globalIndex}Parts = new string[__col{globalIndex}Len];");
            sb.AppendLine($"{indent}for (int __i = 0; __i < __col{globalIndex}Len; __i++)");
            sb.AppendLine($"{indent}    __col{globalIndex}Parts[__i] = Quarry.Internal.ParameterNames.AtP({globalIndex} + __colShift + __i);");
        }
    }

}
