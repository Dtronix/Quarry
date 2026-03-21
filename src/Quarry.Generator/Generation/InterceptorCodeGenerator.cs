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

/// <summary>
/// Generates interceptor source code for Quarry method call sites.
/// </summary>
/// <remarks>
/// Interceptors use the [InterceptsLocation] attribute to redirect method calls
/// to generated code that provides compile-time SQL generation and typed reader delegates.
/// All interceptors use the 'file' scope modifier to prevent visibility to end users.
/// </remarks>
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
        IReadOnlyList<UsageSiteInfo> usageSites,
        IReadOnlyList<PrebuiltChainInfo>? prebuiltChains = null)
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
    internal static List<CachedExtractorField> CollectStaticFields(IReadOnlyList<UsageSiteInfo> usageSites, HashSet<string> chainMemberIds)
    {
        var fields = new List<CachedExtractorField>();

        foreach (var site in usageSites.Where(s => s.IsAnalyzable || chainMemberIds.Contains(s.UniqueId)))
        {
            var methodName = $"{site.MethodName}_{site.UniqueId}";

            // Skip sites that won't generate captured parameter extraction
            if (site.Kind == InterceptorKind.Select && ShouldSkipSelectInterceptor(site))
                continue;

            var clauseInfo = site.ClauseInfo;
            if (clauseInfo == null || !clauseInfo.IsSuccess)
                continue;

            var capturedParams = clauseInfo.Parameters
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
    internal static Dictionary<string, string> CollectMappingInstances(IReadOnlyList<UsageSiteInfo> usageSites, HashSet<string> chainMemberIds)
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
            if (site.ClauseInfo != null)
            {
                foreach (var p in site.ClauseInfo.Parameters)
                {
                    if (p.CustomTypeMappingClass != null)
                        AddIfMissing(mappings, GetMappingFieldName(p.CustomTypeMappingClass), p.CustomTypeMappingClass);
                }

                // From Set clause mapping
                if (site.ClauseInfo is SetClauseInfo setInfo && setInfo.CustomTypeMappingClass != null)
                    AddIfMissing(mappings, GetMappingFieldName(setInfo.CustomTypeMappingClass), setInfo.CustomTypeMappingClass);
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
    internal static Dictionary<string, string> CollectEntityReaderInstances(IReadOnlyList<UsageSiteInfo> usageSites, HashSet<string> chainMemberIds)
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
}
