using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Quarry.Generators.CodeGen;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;
using Quarry;
using Quarry.Generators.Projection;
using Quarry.Generators.Translation;

namespace Quarry.Generators.Generation;

/// <summary>
/// Shared utility methods for interceptor code generation.
/// File-level orchestration lives in <see cref="CodeGen.FileEmitter"/>.
/// Body emitters live in CodeGen/ (ClauseBodyEmitter, TerminalBodyEmitter, etc.).
/// Carrier-specific emission lives in <see cref="CarrierEmitter"/>.
/// </summary>
internal static partial class InterceptorCodeGenerator
{
    /// <summary>
    /// Generates the interceptors file for a context.
    /// Delegates to <see cref="CodeGen.FileEmitter"/> which owns the full generation pipeline.
    /// </summary>
    public static string GenerateInterceptorsFile(
        string contextClassName,
        string? contextNamespace,
        string fileTag,
        IReadOnlyList<TranslatedCallSite> usageSites,
        IReadOnlyList<AssembledPlan>? prebuiltChains = null)
    {
        var emitter = new CodeGen.FileEmitter(
            contextClassName, contextNamespace, fileTag, usageSites, prebuiltChains);
        return emitter.Emit();
    }

    /// <summary>
    /// Represents a static field for caching a FieldInfo used for parameter extraction.
    /// </summary>
    internal sealed class CachedExtractorField
    {
        public CachedExtractorField(
            string fieldName,
            string methodName,
            int parameterIndex,
            string expressionPath,
            string? siteUniqueId = null)
        {
            FieldName = fieldName;
            MethodName = methodName;
            ParameterIndex = parameterIndex;
            ExpressionPath = expressionPath;
            SiteUniqueId = siteUniqueId;
        }

        public string FieldName { get; }
        public string MethodName { get; }
        public int ParameterIndex { get; }
        /// <summary>Raw dot-separated path like "Body.Right" or "Body.Arguments[0]".</summary>
        public string ExpressionPath { get; }
        /// <summary>The unique ID of the usage site this field belongs to.</summary>
        public string? SiteUniqueId { get; }
    }

    /// <summary>
    /// Collects all static fields needed for cached extractors across all usage sites.
    /// </summary>
    internal static List<CachedExtractorField> CollectStaticFields(IReadOnlyList<TranslatedCallSite> usageSites, HashSet<string> chainMemberIds)
    {
        var fields = new List<CachedExtractorField>();

        foreach (var site in usageSites.Where(s => s.IsAnalyzable || chainMemberIds.Contains(s.UniqueId)))
        {
            var methodName = $"{site.MethodName}_{site.UniqueId}";

            // Skip sites that won't generate captured parameter extraction
            if (site.Kind == InterceptorKind.Select && ShouldSkipSelectInterceptor(site))
                continue;

            var clause = site.Clause;
            if (clause == null || !clause.IsSuccess)
                continue;

            var capturedParams = clause.Parameters
                .Where(p => p.IsCaptured && p.CanGenerateDirectPath && !p.IsCollection)
                .ToList();
            foreach (var param in capturedParams)
            {
                var fieldName = $"_{methodName}_p{param.Index}";
                fields.Add(new CachedExtractorField(
                    fieldName,
                    methodName,
                    param.Index,
                    param.ExpressionPath!,
                    siteUniqueId: site.UniqueId));
            }
        }

        return fields;
    }

    /// <summary>
    /// Collects all unique TypeMapping class FQNs used across all usage sites and returns
    /// a mapping from field name to FQN for generating cached static readonly instances.
    /// </summary>
    internal static Dictionary<string, string> CollectMappingInstances(IReadOnlyList<TranslatedCallSite> usageSites, HashSet<string> chainMemberIds)
    {
        var mappings = new Dictionary<string, string>(); // fieldName → FQN

        foreach (var site in usageSites.Where(s => s.IsAnalyzable || chainMemberIds.Contains(s.UniqueId)))
        {
            // From insert columns
            if (site.InsertInfo != null)
            {
                foreach (var col in site.InsertInfo.Columns)
                {
                    if (col.CustomTypeMappingClass != null)
                        AddIfMissing(mappings, GetMappingFieldName(col.CustomTypeMappingClass), col.CustomTypeMappingClass);
                }
            }

            // From projection columns
            if (site.ProjectionInfo != null)
            {
                foreach (var col in site.ProjectionInfo.Columns)
                {
                    if (col.CustomTypeMapping != null)
                        AddIfMissing(mappings, GetMappingFieldName(col.CustomTypeMapping), col.CustomTypeMapping);
                }
            }

            // From where clause parameters
            if (site.Clause != null)
            {
                foreach (var p in site.Clause.Parameters)
                {
                    if (p.CustomTypeMappingClass != null)
                        AddIfMissing(mappings, GetMappingFieldName(p.CustomTypeMappingClass), p.CustomTypeMappingClass);
                }

                // From Set clause mapping
                if (site.Clause.CustomTypeMappingClass != null)
                    AddIfMissing(mappings, GetMappingFieldName(site.Clause.CustomTypeMappingClass), site.Clause.CustomTypeMappingClass);
            }
        }

        return mappings;
    }

    /// <summary>
    /// Gets a safe field name for a TypeMapping class instance cache.
    /// E.g. "MyApp.MoneyMapping" → "_mapper_MyApp_MoneyMapping"
    /// </summary>
    private static void AddIfMissing(Dictionary<string, string> dict, string key, string value)
    {
        if (!dict.ContainsKey(key))
            dict[key] = value;
    }

    /// <summary>
    /// Collects all unique EntityReader class FQNs used across all usage sites.
    /// </summary>
    internal static Dictionary<string, string> CollectEntityReaderInstances(
        IReadOnlyList<TranslatedCallSite> usageSites,
        HashSet<string> chainMemberIds,
        IReadOnlyList<IR.AssembledPlan>? chains = null)
    {
        var readers = new Dictionary<string, string>(); // fieldName → FQN

        foreach (var site in usageSites.Where(s => s.IsAnalyzable || chainMemberIds.Contains(s.UniqueId)))
        {
            if (site.ProjectionInfo?.CustomEntityReaderClass != null)
            {
                var fqn = site.ProjectionInfo.CustomEntityReaderClass;
                AddIfMissing(readers, GetEntityReaderFieldName(fqn), fqn);
            }
        }

        // Also check chain-level ProjectionInfo (enriched with entity metadata)
        if (chains != null)
        {
            foreach (var chain in chains)
            {
                if (chain.ProjectionInfo?.CustomEntityReaderClass != null)
                {
                    var fqn = chain.ProjectionInfo.CustomEntityReaderClass;
                    AddIfMissing(readers, GetEntityReaderFieldName(fqn), fqn);
                }
            }
        }

        return readers;
    }

    /// <summary>
    /// Gets a safe field name for an EntityReader class instance cache.
    /// E.g. "MyApp.UserReader" → "_entityReader_MyApp_UserReader"
    /// </summary>
    internal static string GetEntityReaderFieldName(string readerClassFqn)
    {
        return "_entityReader_" + readerClassFqn.Replace('.', '_').Replace('+', '_');
    }

    internal static string GetMappingFieldName(string mappingClassFqn)
    {
        return "_mapper_" + mappingClassFqn.Replace('.', '_').Replace('+', '_');
    }

    /// <summary>
    /// Wraps a parameter value expression with ToDb() if the parameter has a custom type mapping.
    /// </summary>
    internal static string WrapWithToDb(string valueExpr, ParameterInfo param)
    {
        if (param.CustomTypeMappingClass != null)
            return $"{GetMappingFieldName(param.CustomTypeMappingClass)}.ToDb({valueExpr})";
        return valueExpr;
    }

    /// <summary>
    /// Generates the InterceptsLocationAttribute if needed.
    /// Uses new format: InterceptsLocationAttribute(int version, string data)
    /// </summary>
    internal static void GenerateInterceptsLocationAttribute(StringBuilder sb)
    {
        sb.AppendLine("namespace System.Runtime.CompilerServices");
        sb.AppendLine("{");
        sb.AppendLine("    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]");
        sb.AppendLine("    file sealed class InterceptsLocationAttribute : Attribute");
        sb.AppendLine("    {");
        sb.AppendLine("        public InterceptsLocationAttribute(int version, string data) { }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
    }

    #region Dispatch Table & Diagnostics

    /// <summary>
    /// Generates the dispatch table switch expression that maps ClauseMask values
    /// to pre-built SQL string literals.
    /// </summary>
    internal static void GenerateDispatchTable(
        StringBuilder sb,
        Dictionary<ulong, AssembledSqlVariant> sqlMap,
        string builderVar = "builder")
    {
        Debug.Assert(sqlMap.Count > 0, "Dispatch table must have at least one SQL variant.");

        if (sqlMap.Count == 1)
        {
            foreach (var kvp in sqlMap)
            {
                var escapedSql = EscapeStringLiteral(kvp.Value.Sql);
                sb.AppendLine($"        const string sql = @\"{escapedSql}\";");
            }
        }
        else
        {
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

    /// <summary>
    /// Emits a <c>DiagnosticParameter[]</c> array (<c>__params</c>) from carrier state.
    /// Handles scalar-only, collection-only, and mixed scalar+collection chains.
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
    /// Returns true if a clause role should be included in diagnostic output.
    /// </summary>
    internal static bool IsDiagnosticClauseRole(ClauseRole role)
        => role is ClauseRole.Select or ClauseRole.Where or ClauseRole.OrderBy
            or ClauseRole.ThenBy or ClauseRole.GroupBy or ClauseRole.Having
            or ClauseRole.Join or ClauseRole.Set or ClauseRole.Limit or ClauseRole.Offset
            or ClauseRole.Distinct or ClauseRole.DeleteWhere or ClauseRole.UpdateWhere
            or ClauseRole.UpdateSet;

    /// <summary>
    /// Emits a ClauseDiagnostic[] array from compile-time clause metadata and runtime clause mask.
    /// </summary>
    internal static void EmitDiagnosticClauseArray(
        StringBuilder sb, AssembledPlan chain, CarrierPlan? carrier = null)
    {
        var diagnosticClauses = chain.GetClauseEntries()
            .Where(c => IsDiagnosticClauseRole(c.Role))
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

            sb.AppendLine($"        var __clauseSql{clauseIdx} = @\"{EscapeStringLiteral(tokenizedFrag)}\";");
            foreach (var p in clause.Site.Clause!.Parameters.Where(p => p.IsCollection))
            {
                var globalIdx = offset + p.Index;
                sb.AppendLine($"        __clauseSql{clauseIdx} = __clauseSql{clauseIdx}.Replace(\"{{__COL_P{globalIdx}__}}\", string.Join(\", \", __col{globalIdx}Parts));");
            }
        }

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

        sb.AppendLine("        var __clauses = new ClauseDiagnostic[]");
        sb.AppendLine("        {");
        foreach (var clause in diagnosticClauses)
        {
            var clauseType = clause.Role.ToString();
            var sqlFragment = clause.Site.Clause?.SqlFragment ?? "";
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

            var clauseParamCount = clause.Site.Clause?.Parameters.Count ?? 0;
            string paramsArg;

            var hasCollectionParam = carrier != null && clause.Site.Clause?.Parameters.Any(p => p.IsCollection) == true;
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
    /// </summary>
    internal static void EmitNonCarrierDiagnosticClauseArray(
        StringBuilder sb, AssembledPlan chain, string concreteParamType)
    {
        var diagnosticClauses = chain.GetClauseEntries()
            .Where(c => IsDiagnosticClauseRole(c.Role))
            .ToList();

        if (diagnosticClauses.Count == 0)
        {
            sb.AppendLine("        var __clauses = Array.Empty<ClauseDiagnostic>();");
            return;
        }

        var hasConditional = diagnosticClauses.Any(c => c.IsConditional);
        var needsMaskAccess = hasConditional && chain.SqlVariants.Count > 1;

        sb.AppendLine("        var __clauses = new ClauseDiagnostic[]");
        sb.AppendLine("        {");
        foreach (var clause in diagnosticClauses)
        {
            var clauseType = clause.Role.ToString();
            var sqlFragment = clause.Site.Clause?.SqlFragment ?? "";
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
                isActive = "true";
            }

            sb.AppendLine($"            new(\"{clauseType}\", @\"{escapedFragment}\", isConditional: {isConditional}, isActive: {isActive}),");
        }
        sb.AppendLine("        };");
    }

    #endregion

    #region Join Helpers

    /// <summary>
    /// Gets the builder type name for a given entity count in joins.
    /// </summary>
    internal static string GetJoinedBuilderTypeName(int entityCount)
    {
        return entityCount switch
        {
            2 => "IJoinedQueryBuilder",
            3 => "IJoinedQueryBuilder3",
            4 => "IJoinedQueryBuilder4",
            _ => throw new ArgumentOutOfRangeException(nameof(entityCount), $"Unsupported entity count: {entityCount}")
        };
    }

    #endregion

    #region Modification Helpers

    /// <summary>
    /// Gets the value expression for an entity column property, handling FK navigation and type mapping.
    /// </summary>
    internal static string GetColumnValueExpression(string entityVar, string propertyName, bool isForeignKey, string? customTypeMappingClass)
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
    internal static void EmitInsertColumnSetup(StringBuilder sb, InsertInfo insertInfo)
    {
        var columnNames = string.Join(", ", insertInfo.Columns.Select(c => $"@\"{EscapeStringLiteral(c.QuotedColumnName)}\""));
        sb.AppendLine($"        __b.SetColumns(new[] {{ {columnNames} }});");
        sb.AppendLine();
    }

    /// <summary>
    /// Emits entity property extraction and parameter binding for insert operations.
    /// </summary>
    internal static void EmitInsertEntityBindings(StringBuilder sb, InsertInfo insertInfo, string entityVar, string builderVar, string indent)
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
