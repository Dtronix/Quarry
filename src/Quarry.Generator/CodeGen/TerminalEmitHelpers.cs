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
    /// Emits SQL dispatch: either a const string (single variant) or a mask switch (multiple variants).
    /// Also emits collection parameter expansion when needed.
    /// </summary>
    internal static void EmitSqlDispatch(StringBuilder sb, AssembledPlan chain)
    {
        var hasCollections = chain.ChainParameters.Any(p => p.IsCollection);

        if (chain.SqlVariants.Count == 1)
        {
            foreach (var kvp in chain.SqlVariants)
            {
                if (hasCollections)
                    sb.AppendLine($"        var sql = @\"{InterceptorCodeGenerator.EscapeStringLiteral(kvp.Value.Sql)}\";");
                else
                    sb.AppendLine($"        const string sql = @\"{InterceptorCodeGenerator.EscapeStringLiteral(kvp.Value.Sql)}\";");
            }
        }
        else
        {
            sb.AppendLine("        var sql = __c.Mask switch");
            sb.AppendLine("        {");
            foreach (var kvp in chain.SqlVariants)
            {
                sb.AppendLine($"            {kvp.Key} => @\"{InterceptorCodeGenerator.EscapeStringLiteral(kvp.Value.Sql)}\",");
            }
            sb.AppendLine("            _ => throw new InvalidOperationException(\"Unexpected ClauseMask value.\")");
            sb.AppendLine("        };");
        }

        if (hasCollections)
        {
            EmitCollectionExpansion(sb, chain);
        }
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

        if (!hasScalar && !hasCollection && !hasLimitField && !hasOffsetField)
        {
            sb.AppendLine("        var __params = Array.Empty<DiagnosticParameter>();");
            return;
        }

        if (!hasCollection)
        {
            var entries = new List<string>();
            foreach (var p in chain.ChainParameters.Where(p => !p.IsCollection))
                entries.Add($"new(\"@p{p.GlobalIndex}\", __pVal{p.GlobalIndex})");
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
            if (p.IsCollection)
            {
                sb.AppendLine($"        for (int __pi = 0; __pi < __col{p.GlobalIndex}Len; __pi++)");
                sb.AppendLine($"            __paramList.Add(new DiagnosticParameter(__col{p.GlobalIndex}Parts[__pi], __col{p.GlobalIndex}[__pi]));");
            }
            else
            {
                sb.AppendLine($"        __paramList.Add(new DiagnosticParameter(\"@p{p.GlobalIndex}\", __pVal{p.GlobalIndex}));");
            }
        }

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
    /// Emits ClauseDiagnostic[] with SQL fragments, per-clause parameters, and conditional status.
    /// </summary>
    internal static void EmitDiagnosticClauseArray(
        StringBuilder sb, AssembledPlan chain, CarrierPlan? carrier = null)
    {
        var diagnosticClauses = chain.GetClauseEntries()
            .Where(c => InterceptorCodeGenerator.IsDiagnosticClauseRole(c.Role))
            .ToList();

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
                sb.AppendLine($"        var __clauseParams{clauseIdx} = new DiagnosticParameter[__col{globalIdx}Len];");
                sb.AppendLine($"        for (int __ci = 0; __ci < __col{globalIdx}Len; __ci++)");
                sb.AppendLine($"            __clauseParams{clauseIdx}[__ci] = new DiagnosticParameter(__col{globalIdx}Parts[__ci], __col{globalIdx}[__ci]);");
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

        // Section 3: ClauseDiagnostic array construction
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
    /// Emits the new QueryDiagnostics(...) constructor call with all fields.
    /// Single source of truth for diagnostic construction.
    /// </summary>
    internal static void EmitDiagnosticsConstruction(
        StringBuilder sb, AssembledPlan chain, CarrierPlan carrier,
        string diagnosticKind, string isCarrierOptimized)
    {
        sb.AppendLine($"        return new QueryDiagnostics(sql, __params, {diagnosticKind}, SqlDialect.{chain.Dialect}, \"{InterceptorCodeGenerator.EscapeStringLiteral(chain.TableName)}\", DiagnosticOptimizationTier.PrebuiltDispatch, {isCarrierOptimized}, __clauses);");
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
                return $"(object?){param.EntityPropertyExpression} ?? DBNull.Value";
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
