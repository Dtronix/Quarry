using System.IO;
using System.Text;
using Quarry.Generators.Generation;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;

namespace Quarry.Generators.CodeGen;

/// <summary>
/// Shared helpers for emitting terminal method bodies.
/// Both execution terminals and diagnostic terminals call these methods,
/// ensuring SQL dispatch and parameter binding cannot drift between paths.
/// </summary>
internal static class TerminalEmitHelpers
{
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
    /// Emits code to expand collection parameter tokens in the SQL template.
    /// </summary>
    internal static void EmitCollectionExpansion(StringBuilder sb, AssembledPlan chain)
    {
        foreach (var param in chain.ChainParameters)
        {
            if (!param.IsCollection) continue;

            sb.AppendLine($"        var __col{param.GlobalIndex} = __c.P{param.GlobalIndex};");
            sb.AppendLine($"        var __col{param.GlobalIndex}Len = __col{param.GlobalIndex}.Count;");

            var dialectPrefix = chain.Dialect switch
            {
                SqlDialect.PostgreSQL => "$",
                _ => "@p"
            };
            var isPostgres = chain.Dialect == SqlDialect.PostgreSQL;
            var isMySQL = chain.Dialect == SqlDialect.MySQL;

            sb.AppendLine($"        var __col{param.GlobalIndex}Parts = new string[__col{param.GlobalIndex}Len];");
            sb.AppendLine($"        for (int __i = 0; __i < __col{param.GlobalIndex}Len; __i++)");
            if (isMySQL)
                sb.AppendLine($"            __col{param.GlobalIndex}Parts[__i] = \"?\";");
            else if (isPostgres)
                sb.AppendLine($"            __col{param.GlobalIndex}Parts[__i] = \"$\" + (__i + 1);");
            else
                sb.AppendLine($"            __col{param.GlobalIndex}Parts[__i] = \"{dialectPrefix}\" + __i;");
            sb.AppendLine($"        sql = sql.Replace(\"{{__COL_P{param.GlobalIndex}__}}\", string.Join(\", \", __col{param.GlobalIndex}Parts));");
        }
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
            var paramCount = clause.Site.Clause?.Parameters.Count ?? 0;
            for (int i = 0; i < paramCount; i++)
                map[globalOffset + i] = (clause.IsConditional, clause.BitIndex);
            globalOffset += paramCount;
        }
        return map;
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

        foreach (var p in chain.ChainParameters.OrderBy(p => p.GlobalIndex))
        {
            condMap.TryGetValue(p.GlobalIndex, out var ci);
            if (p.IsCollection)
            {
                var meta = FormatParamMetadata(p, ci.IsConditional, ci.BitIndex);
                sb.AppendLine($"        for (int __pi = 0; __pi < __col{p.GlobalIndex}Len; __pi++)");
                sb.AppendLine($"            __paramList.Add(new DiagnosticParameter(__col{p.GlobalIndex}Parts[__pi], __col{p.GlobalIndex}[__pi]{meta}));");
            }
            else
            {
                var meta = FormatParamMetadata(p, ci.IsConditional, ci.BitIndex);
                sb.AppendLine($"        __paramList.Add(new DiagnosticParameter(\"@p{p.GlobalIndex}\", __pVal{p.GlobalIndex}{meta}));");
            }
        }

        if (hasLimitField)
            sb.AppendLine($"        __paramList.Add(new DiagnosticParameter(\"@p{paginationBaseIdx}\", __pValL, typeName: \"Int32\"));");
        if (hasOffsetField)
        {
            var offsetIdx = paginationBaseIdx + (hasLimitField ? 1 : 0);
            sb.AppendLine($"        __paramList.Add(new DiagnosticParameter(\"@p{offsetIdx}\", __pValO, typeName: \"Int32\"));");
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
            if (clause.Site.Clause != null)
                globalParamOffset += clause.Site.Clause.Parameters.Count;
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
                sb.AppendLine($"        __clauseSql{clauseIdx} = __clauseSql{clauseIdx}.Replace(\"{{__COL_P{globalIdx}__}}\", string.Join(\", \", __col{globalIdx}Parts));");
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
                        sb.AppendLine($"        __cpList{clauseIdx}.Add(new DiagnosticParameter(\"@p{globalIdx}\", __pVal{globalIdx}{meta}));");
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
                paramsArg = $", parameters: new DiagnosticParameter[] {{ new(\"@p{paginationBaseIdx}\", __pValL, typeName: \"Int32\") }}";
            }
            else if (carrier != null && clause.Role == ClauseRole.Offset && hasOffsetField)
            {
                var offsetIdx = paginationBaseIdx + (hasLimitField ? 1 : 0);
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
            if (clause.IsConditional && clause.Site.Bound.Raw.ConditionalInfo != null)
            {
                var bk = clause.Site.Bound.Raw.ConditionalInfo.BranchKind == Models.BranchKind.MutuallyExclusive
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
        string diagnosticKind, string isCarrierOptimized)
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
                var onSql = Quarry.Generators.IR.SqlExprRenderer.Render(join.OnCondition, chain.Dialect);
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
        sb.AppendLine($"            tier: DiagnosticOptimizationTier.PrebuiltDispatch,");
        sb.AppendLine($"            isCarrierOptimized: {isCarrierOptimized},");
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

        // Enum with known underlying type: inline cast to underlying integral type
        if (param.IsEnum && param.EnumUnderlyingType != null)
        {
            if (!param.ClrType.EndsWith("?"))
                return $"(object)({param.EnumUnderlyingType})__c.P{index}";

            // Nullable enum: HasValue check + underlying cast
            return $"__c.P{index}.HasValue ? (object)({param.EnumUnderlyingType})__c.P{index}.Value : DBNull.Value";
        }

        // Default: null-safe boxing
        return $"(object?)__c.P{index} ?? DBNull.Value";
    }
}
