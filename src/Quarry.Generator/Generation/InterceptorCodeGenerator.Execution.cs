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
    // ───────────────────────────────────────────────────────────────
    // Execution terminal methods moved to CodeGen.TerminalBodyEmitter
    // Remaining: helper methods for dispatch tables, diagnostics, and carrier terminals
    // ───────────────────────────────────────────────────────────────

    // GeneratePrebuiltSelectExecutionInterceptor → CodeGen.TerminalBodyEmitter.EmitReaderTerminal
    // GeneratePrebuiltJoinExecutionInterceptor → CodeGen.TerminalBodyEmitter.EmitJoinReaderTerminal
    // GeneratePrebuiltNonQueryExecutionInterceptor → CodeGen.TerminalBodyEmitter.EmitNonQueryTerminal
    // GeneratePrebuiltToDiagnosticsInterceptor → CodeGen.TerminalBodyEmitter.EmitDiagnosticsTerminal
    // GenerateInsertToDiagnosticsInterceptor → CodeGen.TerminalBodyEmitter.EmitInsertDiagnosticsTerminal
    // GenerateRuntimeToDiagnosticsInterceptor → CodeGen.TerminalBodyEmitter.EmitRuntimeDiagnosticsTerminal

    /// <summary>
    /// Emits a carrier insert ToDiagnostics terminal.
    /// Extracts entity property values into DiagnosticParameter[] using the same access patterns
    /// as <see cref="EmitCarrierInsertTerminal"/>.
    /// </summary>
    internal static void EmitCarrierInsertToDiagnosticsTerminal(
        StringBuilder sb, CarrierClassInfo carrier, PrebuiltChainInfo chain)
    {
        EmitCarrierPreamble(sb, carrier, chain, emitOpId: false);

        var insertInfo = chain.Analysis.ExecutionSite.InsertInfo;
        if (insertInfo != null && insertInfo.Columns.Count > 0)
        {
            sb.AppendLine("        var __params = new DiagnosticParameter[]");
            sb.AppendLine("        {");
            for (int i = 0; i < insertInfo.Columns.Count; i++)
            {
                var col = insertInfo.Columns[i];
                var valueExpr = GetColumnValueExpression("__c.Entity!", col.PropertyName, col.IsForeignKey, col.CustomTypeMappingClass);
                sb.AppendLine($"            new(\"@p{i}\", (object?){valueExpr} ?? DBNull.Value),");
            }
            sb.AppendLine("        };");
        }
        else
        {
            sb.AppendLine("        var __params = Array.Empty<DiagnosticParameter>();");
        }

        sb.AppendLine($"        return new QueryDiagnostics(sql, __params, DiagnosticQueryKind.Insert, SqlDialect.{chain.Dialect}, \"{EscapeStringLiteral(chain.TableName)}\", DiagnosticOptimizationTier.PrebuiltDispatch, true);");
    }

    /// <summary>
    /// Emits a carrier ToDiagnostics terminal with full parameter and clause diagnostic output.
    /// Uses the shared preamble for cast + SQL dispatch, then builds DiagnosticParameter[]
    /// and ClauseDiagnostic[] arrays from carrier state and compile-time clause metadata.
    /// </summary>
    internal static void EmitCarrierToDiagnosticsTerminal(
        StringBuilder sb, CarrierClassInfo carrier, PrebuiltChainInfo chain,
        string diagnosticKind, string isCarrierOptimized)
    {
        EmitCarrierPreamble(sb, carrier, chain, emitOpId: false);
        EmitCarrierParameterLocals(sb, chain, carrier);
        EmitDiagnosticClauseArray(sb, chain, carrier);
        EmitDiagnosticParameterArray(sb, chain, carrier);
        sb.AppendLine($"        return new QueryDiagnostics(sql, __params, {diagnosticKind}, SqlDialect.{chain.Dialect}, \"{EscapeStringLiteral(chain.TableName)}\", DiagnosticOptimizationTier.PrebuiltDispatch, {isCarrierOptimized}, __clauses);");
    }

    /// <summary>
    /// Emits a <c>DiagnosticParameter[]</c> array (<c>__params</c>) from carrier state.
    /// Handles scalar-only, collection-only, and mixed scalar+collection chains.
    /// </summary>
    internal static void EmitDiagnosticParameterArray(
        StringBuilder sb, PrebuiltChainInfo chain, CarrierClassInfo carrier)
    {
        var hasScalar = chain.ChainParameters.Any(p => !p.IsCollection);
        var hasCollection = chain.ChainParameters.Any(p => p.IsCollection);
        var hasLimitField = HasCarrierField(carrier, FieldRole.Limit);
        var hasOffsetField = HasCarrierField(carrier, FieldRole.Offset);
        var paginationBaseIdx = chain.ChainParameters.Count;

        if (!hasScalar && !hasCollection && !hasLimitField && !hasOffsetField)
        {
            sb.AppendLine("        var __params = Array.Empty<DiagnosticParameter>();");
            return;
        }

        if (!hasCollection)
        {
            // Scalar-only (possibly with pagination): compile-time-sized array
            var entries = new List<string>();
            foreach (var p in chain.ChainParameters.Where(p => !p.IsCollection))
                entries.Add($"new(\"@p{p.Index}\", __pVal{p.Index})");
            if (hasLimitField)
                entries.Add($"new(\"@p{paginationBaseIdx}\", __pValL)");
            if (hasOffsetField)
            {
                var offsetIdx = paginationBaseIdx + (hasLimitField ? 1 : 0);
                entries.Add($"new(\"@p{offsetIdx}\", __pValO)");
            }
            sb.Append("        var __params = new DiagnosticParameter[] { ");
            sb.Append(string.Join(", ", entries));
            sb.AppendLine(" };");
            return;
        }

        // Has collection params: use List<DiagnosticParameter> for runtime sizing
        var scalarCount = chain.ChainParameters.Count(p => !p.IsCollection)
            + (hasLimitField ? 1 : 0) + (hasOffsetField ? 1 : 0);
        var collectionLenExprs = chain.ChainParameters
            .Where(p => p.IsCollection)
            .Select(p => $"__col{p.Index}Len");
        var capacityExpr = scalarCount > 0
            ? $"{scalarCount} + {string.Join(" + ", collectionLenExprs)}"
            : string.Join(" + ", collectionLenExprs);

        sb.AppendLine($"        var __paramList = new System.Collections.Generic.List<DiagnosticParameter>({capacityExpr});");

        // Emit entries in global index order (preserves parameter numbering)
        foreach (var p in chain.ChainParameters.OrderBy(p => p.Index))
        {
            if (p.IsCollection)
            {
                sb.AppendLine($"        for (int __pi = 0; __pi < __col{p.Index}Len; __pi++)");
                sb.AppendLine($"            __paramList.Add(new DiagnosticParameter(__col{p.Index}Parts[__pi], __col{p.Index}[__pi]));");
            }
            else
            {
                sb.AppendLine($"        __paramList.Add(new DiagnosticParameter(\"@p{p.Index}\", __pVal{p.Index}));");
            }
        }

        // Pagination parameters
        if (hasLimitField)
            sb.AppendLine($"        __paramList.Add(new DiagnosticParameter(\"@p{paginationBaseIdx}\", __pValL));");
        if (hasOffsetField)
        {
            var offsetIdx = paginationBaseIdx + (hasLimitField ? 1 : 0);
            sb.AppendLine($"        __paramList.Add(new DiagnosticParameter(\"@p{offsetIdx}\", __pValO));");
        }

        sb.AppendLine("        var __params = __paramList.ToArray();");
    }

    /// <summary>
    /// Returns true if a clause role should be included in diagnostic output.
    /// Excludes transition roles and state-management clauses (ChainRoot, WithTimeout, etc.).
    /// </summary>
    internal static bool IsDiagnosticClauseRole(ClauseRole role)
        => role is ClauseRole.Select or ClauseRole.Where or ClauseRole.OrderBy
            or ClauseRole.ThenBy or ClauseRole.GroupBy or ClauseRole.Having
            or ClauseRole.Join or ClauseRole.Set or ClauseRole.Limit or ClauseRole.Offset
            or ClauseRole.Distinct or ClauseRole.DeleteWhere or ClauseRole.UpdateWhere
            or ClauseRole.UpdateSet;

    /// <summary>
    /// Emits a ClauseDiagnostic[] array from compile-time clause metadata and runtime clause mask.
    /// When a carrier is provided, each clause gets per-clause DiagnosticParameter[] referencing
    /// the __pVal* locals. Skips transition roles and state-management clauses.
    /// </summary>
    internal static void EmitDiagnosticClauseArray(
        StringBuilder sb, PrebuiltChainInfo chain, CarrierClassInfo? carrier = null)
    {
        var diagnosticClauses = chain.Analysis.Clauses
            .Where(c => IsDiagnosticClauseRole(c.Role))
            .ToList();

        if (diagnosticClauses.Count == 0)
        {
            sb.AppendLine("        var __clauses = Array.Empty<ClauseDiagnostic>();");
            return;
        }

        // Track global parameter offset to map each clause to its __pVal* locals
        var globalParamOffset = 0;
        // Pre-compute per-clause offsets by walking ALL clauses (including non-diagnostic ones)
        var clauseParamOffsets = new Dictionary<string, int>();
        foreach (var clause in chain.Analysis.Clauses)
        {
            clauseParamOffsets[clause.Site.UniqueId] = globalParamOffset;
            if (clause.Site.ClauseInfo != null)
                globalParamOffset += clause.Site.ClauseInfo.Parameters.Count;
        }

        // Compute pagination parameter indices (they follow chain params)
        var paginationBaseIdx = chain.ChainParameters.Count;
        var hasLimitField = carrier != null && HasCarrierField(carrier, FieldRole.Limit);
        var hasOffsetField = carrier != null && HasCarrierField(carrier, FieldRole.Offset);

        // Hoist mask type outside the loop — same for all conditional clauses in the chain
        var maskType = diagnosticClauses.Any(c => c.IsConditional) ? GetMaskType(chain) : null;

        // Pre-compute runtime clause SQL for clauses with collection parameters.
        // These need token expansion using __col{n}Parts from EmitCollectionExpansion.
        for (int clauseIdx = 0; clauseIdx < diagnosticClauses.Count; clauseIdx++)
        {
            var clause = diagnosticClauses[clauseIdx];
            if (carrier == null || clause.Site.ClauseInfo?.Parameters.Any(p => p.IsCollection) != true)
                continue;

            var sqlFrag = clause.Site.ClauseInfo?.SqlFragment ?? "";
            var offset = clauseParamOffsets[clause.Site.UniqueId];
            var tokenizedFrag = sqlFrag;

            foreach (var p in clause.Site.ClauseInfo!.Parameters.Where(p => p.IsCollection))
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

            sb.AppendLine($"        var __clauseSql{clauseIdx} = @\"{EscapeStringLiteral(tokenizedFrag)}\";");
            foreach (var p in clause.Site.ClauseInfo!.Parameters.Where(p => p.IsCollection))
            {
                var globalIdx = offset + p.Index;
                sb.AppendLine($"        __clauseSql{clauseIdx} = __clauseSql{clauseIdx}.Replace(\"{{__COL_P{globalIdx}__}}\", string.Join(\", \", __col{globalIdx}Parts));");
            }
        }

        // Pre-compute per-clause DiagnosticParameter[] for clauses with collection parameters.
        // These must be built before the __clauses array since they require loops.
        for (int clauseIdx = 0; clauseIdx < diagnosticClauses.Count; clauseIdx++)
        {
            var clause = diagnosticClauses[clauseIdx];
            if (carrier == null || clause.Site.ClauseInfo?.Parameters.Any(p => p.IsCollection) != true)
                continue;

            var offset = clauseParamOffsets[clause.Site.UniqueId];
            var clauseParams = clause.Site.ClauseInfo!.Parameters;
            var hasScalarInClause = clauseParams.Any(p => !p.IsCollection);
            var hasCollectionInClause = clauseParams.Any(p => p.IsCollection);

            if (!hasScalarInClause)
            {
                // Collection-only clause: fixed-size array from collection elements
                var collectionParam = clauseParams.First(p => p.IsCollection);
                var globalIdx = offset + collectionParam.Index;
                sb.AppendLine($"        var __clauseParams{clauseIdx} = new DiagnosticParameter[__col{globalIdx}Len];");
                sb.AppendLine($"        for (int __ci = 0; __ci < __col{globalIdx}Len; __ci++)");
                sb.AppendLine($"            __clauseParams{clauseIdx}[__ci] = new DiagnosticParameter(__col{globalIdx}Parts[__ci], __col{globalIdx}[__ci]);");
            }
            else
            {
                // Mixed scalar + collection clause: use List for runtime sizing
                var scalarCount = clauseParams.Count(p => !p.IsCollection);
                var collectionLens = clauseParams
                    .Where(p => p.IsCollection)
                    .Select(p => $"__col{offset + p.Index}Len");
                var capacityExpr = $"{scalarCount} + {string.Join(" + ", collectionLens)}";

                sb.AppendLine($"        var __cpList{clauseIdx} = new System.Collections.Generic.List<DiagnosticParameter>({capacityExpr});");
                foreach (var p in clauseParams.OrderBy(p => p.Index))
                {
                    var globalIdx = offset + p.Index;
                    if (p.IsCollection)
                    {
                        sb.AppendLine($"        for (int __ci = 0; __ci < __col{globalIdx}Len; __ci++)");
                        sb.AppendLine($"            __cpList{clauseIdx}.Add(new DiagnosticParameter(__col{globalIdx}Parts[__ci], __col{globalIdx}[__ci]));");
                    }
                    else
                    {
                        sb.AppendLine($"        __cpList{clauseIdx}.Add(new DiagnosticParameter(\"@p{globalIdx}\", __pVal{globalIdx}));");
                    }
                }
                sb.AppendLine($"        var __clauseParams{clauseIdx} = __cpList{clauseIdx}.ToArray();");
            }
        }

        sb.AppendLine("        var __clauses = new ClauseDiagnostic[]");
        sb.AppendLine("        {");
        foreach (var clause in diagnosticClauses)
        {
            var clauseType = clause.Role.ToString();
            var sqlFragment = clause.Site.ClauseInfo?.SqlFragment ?? "";
            var escapedFragment = EscapeStringLiteral(sqlFragment);
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

            // Emit per-clause parameters when carrier provides __pVal* locals
            var clauseParamCount = clause.Site.ClauseInfo?.Parameters.Count ?? 0;
            string paramsArg;

            // Check if this clause has collection parameters (runtime-expanded SQL)
            var hasCollectionParam = carrier != null && clause.Site.ClauseInfo?.Parameters.Any(p => p.IsCollection) == true;
            // Use runtime variable for collection clauses, compile-time literal otherwise
            var clauseSqlExpr = hasCollectionParam
                ? $"__clauseSql{diagnosticClauses.IndexOf(clause)}"
                : $"@\"{escapedFragment}\"";

            if (hasCollectionParam)
            {
                paramsArg = $", parameters: __clauseParams{diagnosticClauses.IndexOf(clause)}";
            }
            else if (carrier != null && clause.Role == ClauseRole.Limit && hasLimitField)
            {
                paramsArg = $", parameters: new DiagnosticParameter[] {{ new(\"@p{paginationBaseIdx}\", __pValL) }}";
            }
            else if (carrier != null && clause.Role == ClauseRole.Offset && hasOffsetField)
            {
                var offsetIdx = paginationBaseIdx + (hasLimitField ? 1 : 0);
                paramsArg = $", parameters: new DiagnosticParameter[] {{ new(\"@p{offsetIdx}\", __pValO) }}";
            }
            else if (carrier != null && clauseParamCount > 0)
            {
                var offset = clauseParamOffsets[clause.Site.UniqueId];
                var paramEntries = new List<string>();
                for (int i = 0; i < clauseParamCount; i++)
                {
                    paramEntries.Add($"new(\"@p{offset + i}\", __pVal{offset + i})");
                }
                paramsArg = $", parameters: new DiagnosticParameter[] {{ {string.Join(", ", paramEntries)} }}";
            }
            else
            {
                paramsArg = "";
            }

            sb.AppendLine($"            new(\"{clauseType}\", {clauseSqlExpr}, isConditional: {isConditional}, isActive: {isActive}{paramsArg}),");
        }
        sb.AppendLine("        };");
    }

    /// <summary>
    /// Emits a ClauseDiagnostic[] array for non-carrier prebuilt chains.
    /// Uses __b.ClauseMask for conditional clause IsActive checks (single-variant chains have no __b).
    /// </summary>
    internal static void EmitNonCarrierDiagnosticClauseArray(
        StringBuilder sb, PrebuiltChainInfo chain, string concreteParamType)
    {
        var diagnosticClauses = chain.Analysis.Clauses
            .Where(c => IsDiagnosticClauseRole(c.Role))
            .ToList();

        if (diagnosticClauses.Count == 0)
        {
            sb.AppendLine("        var __clauses = Array.Empty<ClauseDiagnostic>();");
            return;
        }

        // For multi-variant chains, we need __b for ClauseMask access.
        // For single-variant chains, __b may not exist — all conditional clauses default to active
        // since the single SQL variant was selected at compile time.
        var hasConditional = diagnosticClauses.Any(c => c.IsConditional);
        var needsMaskAccess = hasConditional && chain.SqlMap.Count > 1;

        sb.AppendLine("        var __clauses = new ClauseDiagnostic[]");
        sb.AppendLine("        {");
        foreach (var clause in diagnosticClauses)
        {
            var clauseType = clause.Role.ToString();
            var sqlFragment = clause.Site.ClauseInfo?.SqlFragment ?? "";
            var escapedFragment = EscapeStringLiteral(sqlFragment);
            var isConditional = clause.IsConditional ? "true" : "false";

            string isActive;
            if (!clause.IsConditional)
            {
                isActive = "true";
            }
            else if (needsMaskAccess)
            {
                isActive = $"(__b.ClauseMask & {(1UL << clause.BitIndex!.Value)}UL) != 0";
            }
            else
            {
                // Single-variant chain: conditional clause is active if it was included in the SQL
                isActive = "true";
            }

            sb.AppendLine($"            new(\"{clauseType}\", @\"{escapedFragment}\", isConditional: {isConditional}, isActive: {isActive}),");
        }
        sb.AppendLine("        };");
    }

    /// <summary>
    /// Generates the dispatch table switch expression that maps ClauseMask values
    /// to pre-built SQL string literals.
    /// </summary>
    internal static void GenerateDispatchTable(
        StringBuilder sb,
        Dictionary<ulong, PrebuiltSqlResult> sqlMap,
        string builderVar = "builder")
    {
        Debug.Assert(sqlMap.Count > 0, "Dispatch table must have at least one SQL variant.");

        if (sqlMap.Count == 1)
        {
            // Single variant — no switch needed, use const
            foreach (var kvp in sqlMap)
            {
                var escapedSql = EscapeStringLiteral(kvp.Value.Sql);
                sb.AppendLine($"        const string sql = @\"{escapedSql}\";");
            }
        }
        else
        {
            // Multiple variants — switch expression on ClauseMask
            sb.AppendLine($"        var sql = {builderVar}.ClauseMask switch");
            sb.AppendLine($"        {{");

            foreach (var kvp in sqlMap.OrderBy(k => k.Key))
            {
                var escapedSql = EscapeStringLiteral(kvp.Value.Sql);
                sb.AppendLine($"            {kvp.Key}UL => @\"{escapedSql}\",");
            }

            sb.AppendLine($"            _ => throw new InvalidOperationException(\"Unexpected ClauseMask: \" + {builderVar}.ClauseMask)");
            sb.AppendLine($"        }};");
        }
    }
}
