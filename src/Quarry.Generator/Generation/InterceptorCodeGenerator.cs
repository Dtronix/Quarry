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
    /// Collects all unique EntityReader class FQNs used across all usage sites,
    /// rewriting each schema-namespace FQN to a per-context FQN
    /// (<c>contextNamespace + "." + simpleName</c>) so the emitted file references
    /// the per-context reader class in the consuming context's namespace.
    /// When <paramref name="contextNamespace"/> is null/empty, the original FQN is used.
    /// </summary>
    internal static Dictionary<string, string> CollectEntityReaderInstances(
        IReadOnlyList<TranslatedCallSite> usageSites,
        HashSet<string> chainMemberIds,
        IReadOnlyList<IR.AssembledPlan>? chains = null,
        string? contextNamespace = null)
    {
        var readers = new Dictionary<string, string>(); // fieldName → FQN

        foreach (var site in usageSites.Where(s => s.IsAnalyzable || chainMemberIds.Contains(s.UniqueId)))
        {
            if (site.ProjectionInfo?.CustomEntityReaderClass != null)
            {
                var fqn = ResolvePerContextReaderFqn(site.ProjectionInfo.CustomEntityReaderClass, contextNamespace);
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
                    var fqn = ResolvePerContextReaderFqn(chain.ProjectionInfo.CustomEntityReaderClass, contextNamespace);
                    AddIfMissing(readers, GetEntityReaderFieldName(fqn), fqn);
                }
            }
        }

        return readers;
    }

    /// <summary>
    /// Rewrites a schema-namespace reader FQN to a per-context FQN. The simple
    /// reader class name is preserved; only the enclosing namespace is replaced
    /// with the consuming <see cref="QuarryContext"/>'s namespace. When the
    /// context namespace is null/empty (e.g. test scaffolding without a
    /// namespace), the original FQN passes through unchanged.
    /// </summary>
    /// <remarks>
    /// Per-context resolution is the default for [EntityReader]: each context
    /// is expected to provide a per-context reader class at
    /// <c>&lt;contextNamespace&gt;.&lt;readerSimpleName&gt;</c> inheriting
    /// <c>EntityReader&lt;TPerContextEntity&gt;</c>. When schema and context
    /// share a namespace, the per-context FQN equals the schema-namespace FQN —
    /// preserving the existing single-context behavior.
    /// </remarks>
    internal static string ResolvePerContextReaderFqn(string schemaReaderFqn, string? contextNamespace)
    {
        if (string.IsNullOrEmpty(contextNamespace))
            return schemaReaderFqn;

        var lastDot = schemaReaderFqn.LastIndexOf('.');
        var simpleName = lastDot >= 0 ? schemaReaderFqn.Substring(lastDot + 1) : schemaReaderFqn;
        return contextNamespace + "." + simpleName;
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
    /// Delegates to <see cref="TerminalEmitHelpers.EmitDiagnosticParameterArray"/>.
    /// </summary>
    internal static void EmitDiagnosticParameterArray(
        StringBuilder sb, AssembledPlan chain, CarrierPlan carrier)
        => TerminalEmitHelpers.EmitDiagnosticParameterArray(sb, chain, carrier);

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
        => TerminalEmitHelpers.EmitDiagnosticClauseArray(sb, chain, carrier);

    #endregion

    #region Join Helpers

    /// <summary>
    /// Gets the builder type name for a given entity count in joins.
    /// </summary>
    internal static string GetJoinedBuilderTypeName(int entityCount)
        => JoinArityHelpers.GetInterfaceName(entityCount);

    #endregion

    #region Modification Helpers

    /// <summary>
    /// Gets the value expression for an entity column property, handling FK navigation and type mapping.
    /// </summary>
    internal static string GetColumnValueExpression(
        string entityVar, string propertyName, bool isForeignKey,
        string? customTypeMappingClass, bool isBoolean = false, bool isEnum = false,
        bool isNullable = false, bool convertBoolToInt = false,
        string? enumUnderlyingType = "int")
    {
        var valueExpr = isForeignKey
            ? $"{entityVar}.{propertyName}.Id"
            : $"{entityVar}.{propertyName}";
        if (customTypeMappingClass != null)
            valueExpr = $"{GetMappingFieldName(customTypeMappingClass)}.ToDb({valueExpr})";
        else if (isBoolean && convertBoolToInt)
        {
            // SQLite and MySQL reject boxed System.Boolean — convert to 0/1 integer.
            // Nullable path returns null (not DBNull.Value) so the caller's
            // standard "(object?)expr ?? DBNull.Value" wrapper handles null uniformly.
            if (isNullable)
                valueExpr = $"({valueExpr} != null ? (object)({valueExpr}.Value ? 1 : 0) : null)";
            else
                valueExpr = $"({valueExpr} ? 1 : 0)";
        }
        else if (isEnum)
        {
            // All dialects: enums must be cast to their underlying type.
            // Same nullable convention as bool — return null, let caller wrap with DBNull.
            var castType = enumUnderlyingType ?? "int";
            if (isNullable)
                valueExpr = $"({valueExpr} != null ? (object)({castType}){valueExpr}.Value : null)";
            else
                valueExpr = $"({castType}){valueExpr}";
        }
        return valueExpr;
    }

    /// <summary>
    /// Returns whether the given dialect requires bool-to-int conversion for parameter binding.
    /// SQLite and MySQL reject boxed System.Boolean; PostgreSQL and SQL Server handle it natively.
    /// </summary>
    internal static bool RequiresBoolToIntConversion(SqlDialect dialect)
    {
        return dialect is SqlDialect.SQLite or SqlDialect.MySQL;
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
