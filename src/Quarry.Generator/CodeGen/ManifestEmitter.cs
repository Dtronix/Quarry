// RS1035: File IO is intentional — the manifest is a non-source markdown artifact
// written to a user-specified directory, not a generated .cs file.
#pragma warning disable RS1035

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;

namespace Quarry.Generators.CodeGen;

/// <summary>
/// Emits a per-dialect markdown manifest documenting every SQL statement the generator produces.
/// Opt-in via the <c>QuarrySqlManifestPath</c> MSBuild property.
/// </summary>
internal static class ManifestEmitter
{
    /// <summary>
    /// Entry point called from <c>RegisterImplementationSourceOutput</c>.
    /// Groups all assembled plans by dialect, renders one markdown file per dialect,
    /// and writes to the user-specified directory.
    /// </summary>
    public static void Emit(
        ImmutableArray<FileInterceptorGroup> groups,
        string manifestRelativePath,
        string? projectDir,
        SourceProductionContext spc)
    {
        if (groups.IsDefaultOrEmpty)
            return;

        // Resolve the output directory
        string manifestDir;
        if (Path.IsPathRooted(manifestRelativePath))
        {
            manifestDir = manifestRelativePath;
        }
        else if (!string.IsNullOrEmpty(projectDir))
        {
            manifestDir = Path.Combine(projectDir, manifestRelativePath);
        }
        else
        {
            // Cannot resolve relative path without ProjectDir — skip silently
            return;
        }

        var rendered = RenderAllDialects(groups);

        foreach (var kvp in rendered)
        {
            var filePath = Path.Combine(manifestDir, kvp.Key);
            try
            {
                WriteIfChanged(filePath, kvp.Value);
            }
            catch (Exception ex)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.ManifestWriteFailed,
                    Location.None,
                    filePath, ex.Message));
            }
        }
    }

    /// <summary>
    /// Flattens, deduplicates, and renders all assembled plans grouped by dialect.
    /// Returns a dictionary of dialect file name → rendered markdown content.
    /// </summary>
    internal static Dictionary<string, string> RenderAllDialects(
        ImmutableArray<FileInterceptorGroup> groups)
    {
        var result = new Dictionary<string, string>();

        if (groups.IsDefaultOrEmpty)
            return result;

        // Flatten all AssembledPlans from all groups, deduplicate by (Context, ExecutionSite.UniqueId)
        var allPlans = new Dictionary<(string Context, string UniqueId), (AssembledPlan Plan, string ContextNamespace)>();
        var totalByDialect = new Dictionary<SqlDialect, int>();
        var excludedByDialect = new Dictionary<SqlDialect, int>();
        foreach (var group in groups)
        {
            foreach (var plan in group.AssembledPlans)
            {
                totalByDialect.TryGetValue(plan.Dialect, out var t);
                totalByDialect[plan.Dialect] = t + 1;

                // Skip error/runtime-build chains, count them per dialect
                if (plan.Tier == OptimizationTier.RuntimeBuild
                    || plan.ExecutionSite.PipelineError != null
                    || plan.SqlVariants.Count == 0)
                {
                    excludedByDialect.TryGetValue(plan.Dialect, out var c);
                    excludedByDialect[plan.Dialect] = c + 1;
                    continue;
                }

                var key = (group.ContextClassName, plan.ExecutionSite.UniqueId);
                if (!allPlans.ContainsKey(key))
                    allPlans[key] = (plan, group.ContextNamespace ?? "");
            }
        }

        if (allPlans.Count == 0)
            return result;

        // Group by dialect
        var byDialect = new Dictionary<SqlDialect, List<(AssembledPlan Plan, string ContextClassName, string ContextNamespace)>>();
        foreach (var kvp in allPlans)
        {
            var (plan, contextNamespace) = kvp.Value;
            var dialect = plan.Dialect;
            if (!byDialect.TryGetValue(dialect, out var list))
            {
                list = new List<(AssembledPlan, string, string)>();
                byDialect[dialect] = list;
            }
            list.Add((plan, kvp.Key.Context, contextNamespace));
        }

        // Render one entry per dialect
        foreach (var kvp in byDialect)
        {
            var dialect = kvp.Key;
            var plans = kvp.Value;
            totalByDialect.TryGetValue(dialect, out var totalCount);
            excludedByDialect.TryGetValue(dialect, out var excludedCount);
            var markdown = RenderManifest(dialect, plans, totalCount, excludedCount);
            var fileName = GetDialectFileName(dialect);
            result[fileName] = markdown;
        }

        return result;
    }

    /// <summary>
    /// Renders the full markdown document for a single dialect.
    /// </summary>
    internal static string RenderManifest(
        SqlDialect dialect,
        List<(AssembledPlan Plan, string ContextClassName, string ContextNamespace)> plans,
        int totalCount = 0,
        int excludedCount = 0)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Quarry SQL Manifest \u2014 {GetDialectDisplayName(dialect)}");
        sb.AppendLine();
        sb.AppendLine("> Auto-generated by Quarry. Do not edit manually.");

        // Group by context, sorted alphabetically
        var byContext = plans
            .GroupBy(p => p.ContextClassName, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        int preDedup = plans.Count;
        int renderedCount = 0;

        foreach (var contextGroup in byContext)
        {
            sb.AppendLine();
            sb.AppendLine($"## {contextGroup.Key}");

            // Sort plans by chain shape for stability, deduplicate by (shape, SQL)
            var withShapes = contextGroup
                .Select(p => (p.Plan, Shape: BuildChainShape(p.Plan)))
                .ToList();

            var sortedPlans = withShapes
                .GroupBy(p => (p.Shape, Sql: GetBaseSql(p.Plan)))
                .Select(g => g.First())
                .OrderBy(p => p.Shape, StringComparer.Ordinal)
                .ThenBy(p => GetBaseSql(p.Plan), StringComparer.Ordinal)
                .ToList();

            renderedCount += sortedPlans.Count;

            for (int i = 0; i < sortedPlans.Count; i++)
            {
                var (plan, shape) = sortedPlans[i];
                sb.AppendLine();
                RenderPlan(sb, plan, shape);

                if (i < sortedPlans.Count - 1)
                {
                    sb.AppendLine();
                    sb.AppendLine("---");
                }
            }
        }

        // Summary stats table
        int consolidatedCount = preDedup - renderedCount;
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| Metric | Count |");
        sb.AppendLine("|--------|------:|");
        sb.AppendLine($"| Total discovered | {totalCount} |");
        sb.AppendLine($"| Skipped (errors) | {excludedCount} |");
        sb.AppendLine($"| Consolidated (deduped) | {consolidatedCount} |");
        sb.AppendLine($"| Rendered | {renderedCount} |");

        return sb.ToString();
    }

    /// <summary>
    /// Renders a single chain's SQL block and parameter table.
    /// </summary>
    private static void RenderPlan(StringBuilder sb, AssembledPlan plan, string shape)
    {
        var variants = plan.SqlVariants;
        var possibleMasks = plan.PossibleMasks;
        var isConditional = possibleMasks.Count > 1;

        // H3 heading
        if (isConditional)
            sb.AppendLine($"### {shape} \u2014 {possibleMasks.Count} variants");
        else
            sb.AppendLine($"### {shape}");

        sb.AppendLine();

        // Build bit index → condition text mapping for conditional chains
        Dictionary<int, string>? bitToCondition = null;
        if (isConditional)
            bitToCondition = BuildBitIndexToConditionText(plan);

        // SQL block(s) — all variants in a single fenced block
        sb.AppendLine("```sql");
        if (isConditional && bitToCondition != null)
        {
            var sortedMasks = possibleMasks.OrderBy(m => m).ToList();
            for (int i = 0; i < sortedMasks.Count; i++)
            {
                var mask = sortedMasks[i];
                if (variants.TryGetValue(mask, out var variant))
                {
                    var label = BuildVariantLabel(mask, bitToCondition);
                    sb.AppendLine($"-- {label}");
                    sb.AppendLine(AppendBatchInsertRow(variant.Sql, plan));

                    if (i < sortedMasks.Count - 1)
                        sb.AppendLine();
                }
            }
        }
        else
        {
            // Single variant (mask 0 or the only mask present)
            var variant = variants.Values.First();
            sb.AppendLine(AppendBatchInsertRow(variant.Sql, plan));
        }
        sb.AppendLine("```");

        // Parameter table — either from Plan.Parameters (clause-sourced) or InsertInfo.Columns (entity-sourced)
        RenderParameterTable(sb, plan, isConditional);
    }

    /// <summary>
    /// Renders the parameter table for a plan, sourcing from Plan.Parameters or InsertInfo.Columns.
    /// Adds Conditional and Sensitive columns only when needed.
    /// </summary>
    private static void RenderParameterTable(StringBuilder sb, AssembledPlan plan, bool isConditional)
    {
        // Collect parameter rows from either Plan.Parameters or InsertInfo.Columns
        var rows = new List<(string Name, string Type, bool IsSensitive, string? Condition)>();

        var parameters = plan.ChainParameters;
        if (parameters.Count > 0)
        {
            Dictionary<int, string>? paramConditions = null;
            if (isConditional)
                paramConditions = BuildParamConditionality(plan);

            foreach (var param in parameters.OrderBy(p => p.GlobalIndex))
            {
                var typeDisplay = SimplifyTypeName(param.IsCollection && param.ElementTypeName != null
                    ? param.ElementTypeName : param.ClrType);
                if (param.IsCollection)
                    typeDisplay += "[]";

                string? condition = null;
                if (paramConditions != null && paramConditions.TryGetValue(param.GlobalIndex, out var cond))
                    condition = cond;

                rows.Add(($"@p{param.GlobalIndex}", typeDisplay, param.IsSensitive, condition));
            }
        }
        else if (plan.InsertInfo != null && plan.InsertInfo.Columns.Count > 0)
        {
            for (int idx = 0; idx < plan.InsertInfo.Columns.Count; idx++)
            {
                var col = plan.InsertInfo.Columns[idx];
                var typeDisplay = SimplifyTypeName(col.FullClrType);
                rows.Add(($"@p{idx}", typeDisplay, col.IsSensitive, null));
            }
        }

        // Add pagination parameters (LIMIT/OFFSET) — these are tracked in
        // PaginationPlan rather than in ChainParameters.
        var pagination = plan.Plan.Pagination;
        if (pagination != null)
        {
            if (pagination.LimitParamIndex is int limitIdx)
                rows.Add(($"@p{limitIdx}", "int", false, null));
            if (pagination.OffsetParamIndex is int offsetIdx)
                rows.Add(($"@p{offsetIdx}", "int", false, null));
        }

        if (rows.Count == 0)
            return;

        sb.AppendLine();

        var hasSensitive = rows.Any(r => r.IsSensitive);
        var hasConditional = rows.Any(r => r.Condition != null);

        // Build header dynamically based on which extra columns are needed
        var header = "| Parameter | Type |";
        var separator = "|-----------|------|";
        if (hasSensitive) { header += " Sensitive |"; separator += "-----------|"; }
        if (hasConditional) { header += " Conditional |"; separator += "-------------|"; }

        sb.AppendLine(header);
        sb.AppendLine(separator);

        foreach (var (name, type, isSensitive, condition) in rows)
        {
            var line = $"| `{name}` | `{type}` |";
            if (hasSensitive) line += isSensitive ? " Yes |" : " |";
            if (hasConditional) line += condition != null ? $" {condition} |" : " |";
            sb.AppendLine(line);
        }
    }

    /// <summary>
    /// For BatchInsert plans where the SQL template ends at "VALUES ", appends a representative
    /// row placeholder so the manifest shows the complete SQL pattern.
    /// </summary>
    private static string AppendBatchInsertRow(string sql, AssembledPlan plan)
    {
        if (plan.QueryKind != QueryKind.BatchInsert)
            return sql;

        var insertInfo = plan.InsertInfo;
        if (insertInfo == null || insertInfo.Columns.Count == 0)
            return sql;

        // Only append if the SQL ends with the VALUES keyword (possibly with trailing whitespace)
        var trimmed = sql.TrimEnd();
        if (!trimmed.EndsWith("VALUES", StringComparison.OrdinalIgnoreCase))
            return sql;

        var placeholders = new StringBuilder("(");
        for (int i = 0; i < insertInfo.Columns.Count; i++)
        {
            if (i > 0) placeholders.Append(", ");
            placeholders.Append($"@p{i}");
        }
        placeholders.Append("), ...");

        return trimmed + " " + placeholders.ToString();
    }

    /// <summary>
    /// Reconstructs a human-readable chain expression shape from an AssembledPlan's clause and execution sites.
    /// </summary>
    internal static string BuildChainShape(AssembledPlan plan)
    {
        var sb = new StringBuilder();

        foreach (var site in plan.ClauseSites)
        {
            if (site.Kind == InterceptorKind.ChainRoot)
            {
                sb.Append(site.MethodName).Append("()");
                continue;
            }

            // Omit leading dot when no ChainRoot preceded this site
            var prefix = sb.Length > 0 ? "." : "";

            // Transitions (Delete, Update, Insert, All) appear without arguments
            if (IsTransitionKind(site.Kind))
            {
                sb.Append(prefix).Append(site.MethodName).Append("()");
                continue;
            }

            // Modifiers without expressions (Limit, Offset, Distinct, WithTimeout)
            if (IsModifierKind(site.Kind))
            {
                sb.Append(prefix).Append(site.MethodName).Append("(...)");
                continue;
            }

            // Regular clause methods have arguments
            sb.Append(prefix).Append(site.MethodName).Append("(...)");
        }

        // Append Prepare if present
        if (plan.PrepareSite != null)
            sb.Append(".Prepare()");

        // Append the terminal — omit leading dot when no clause sites preceded it
        var termPrefix = sb.Length > 0 ? "." : "";
        sb.Append(termPrefix).Append(plan.ExecutionSite.MethodName).Append("()");

        return sb.ToString();
    }

    /// <summary>
    /// Builds a mapping from conditional bit index to the human-readable condition text.
    /// Correlates ConditionalTerms (bit indices) with clause sites' NestingContext.
    /// </summary>
    internal static Dictionary<int, string> BuildBitIndexToConditionText(AssembledPlan plan)
    {
        var result = new Dictionary<int, string>();
        var conditionalTerms = plan.ConditionalTerms;
        if (conditionalTerms.Count == 0)
            return result;

        // Determine baseline nesting depth from the execution terminal
        var baselineDepth = plan.ExecutionSite.Bound.Raw.NestingContext?.NestingDepth ?? 0;

        // Walk clause sites in order (same order ChainAnalyzer uses to assign bit indices)
        int termIndex = 0;
        foreach (var site in plan.ClauseSites)
        {
            if (termIndex >= conditionalTerms.Count)
                break;

            var nestingCtx = site.Bound.Raw.NestingContext;
            if (nestingCtx == null)
                continue;

            var relativeDepth = nestingCtx.NestingDepth - baselineDepth;
            if (relativeDepth <= 0)
                continue;

            // This clause is conditional — it corresponds to the next ConditionalTerm
            var term = conditionalTerms[termIndex];
            result[term.BitIndex] = TruncateConditionText(nestingCtx.ConditionText);
            termIndex++;
        }

        return result;
    }

    /// <summary>
    /// Builds a mapping from parameter GlobalIndex to the condition text of the clause that produced it.
    /// Only conditional parameters are included in the result.
    /// </summary>
    internal static Dictionary<int, string> BuildParamConditionality(AssembledPlan plan)
    {
        var result = new Dictionary<int, string>();
        var bitToCondition = BuildBitIndexToConditionText(plan);
        if (bitToCondition.Count == 0)
            return result;

        var baselineDepth = plan.ExecutionSite.Bound.Raw.NestingContext?.NestingDepth ?? 0;

        // Walk clause sites, accumulate parameter indices, and mark conditional ones
        // ChainAnalyzer collects parameters from clauses in order with sequential global indices.
        // We need to find which global index range each clause owns.
        var planParams = plan.ChainParameters;
        if (planParams.Count == 0)
            return result;

        // Build a clause-site to condition-text mapping
        int termIndex = 0;
        var conditionalTerms = plan.ConditionalTerms;
        foreach (var site in plan.ClauseSites)
        {
            var nestingCtx = site.Bound.Raw.NestingContext;
            string? conditionText = null;

            if (nestingCtx != null)
            {
                var relativeDepth = nestingCtx.NestingDepth - baselineDepth;
                if (relativeDepth > 0 && termIndex < conditionalTerms.Count)
                {
                    conditionText = TruncateConditionText(nestingCtx.ConditionText);
                    termIndex++;
                }
            }

            if (conditionText == null)
                continue;

            // Find parameters that belong to this clause via the clause's translated parameters
            var clauseParams = site.Clause?.Parameters;
            if (clauseParams == null || clauseParams.Count == 0)
                continue;

            foreach (var cp in clauseParams)
            {
                // ParameterInfo.Index is the local index within the clause translation.
                // Find the matching QueryParameter by matching on the value expression.
                foreach (var qp in planParams)
                {
                    if (qp.ValueExpression == cp.ValueExpression && !result.ContainsKey(qp.GlobalIndex))
                    {
                        result[qp.GlobalIndex] = conditionText;
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Builds the variant label for a given bitmask value.
    /// </summary>
    private static string BuildVariantLabel(int mask, Dictionary<int, string> bitToCondition)
    {
        if (mask == 0)
            return "base";

        var parts = new List<string>();
        for (int bit = 0; bit < 8; bit++)
        {
            if ((mask & (1 << bit)) != 0)
            {
                var label = bitToCondition.TryGetValue(bit, out var text) ? text : $"bit{bit}";
                parts.Add($"+{label}");
            }
        }

        return parts.Count > 0 ? string.Join(", ", parts) : $"mask={mask}";
    }


    /// <summary>
    /// Simplifies a fully-qualified CLR type name to its C# keyword or short form.
    /// </summary>
    internal static string SimplifyTypeName(string clrType)
    {
        // Handle unresolved types from the pipeline
        if (string.IsNullOrEmpty(clrType) || clrType == "?")
            return "object";

        // Handle nullable wrapper
        if (clrType.StartsWith("System.Nullable<", StringComparison.Ordinal) && clrType.EndsWith(">", StringComparison.Ordinal))
        {
            var inner = clrType.Substring("System.Nullable<".Length, clrType.Length - "System.Nullable<".Length - 1);
            return SimplifyTypeName(inner) + "?";
        }

        // Handle generic Nullable<T> form
        if (clrType.StartsWith("Nullable<", StringComparison.Ordinal) && clrType.EndsWith(">", StringComparison.Ordinal))
        {
            var inner = clrType.Substring("Nullable<".Length, clrType.Length - "Nullable<".Length - 1);
            return SimplifyTypeName(inner) + "?";
        }

        // C# keyword aliases
        switch (clrType)
        {
            case "System.String":
            case "string":
                return "string";
            case "System.Int32":
            case "int":
                return "int";
            case "System.Int64":
            case "long":
                return "long";
            case "System.Int16":
            case "short":
                return "short";
            case "System.Byte":
            case "byte":
                return "byte";
            case "System.Boolean":
            case "bool":
                return "bool";
            case "System.Single":
            case "float":
                return "float";
            case "System.Double":
            case "double":
                return "double";
            case "System.Decimal":
            case "decimal":
                return "decimal";
            case "System.Char":
            case "char":
                return "char";
            case "System.Object":
            case "object":
                return "object";
            case "System.SByte":
            case "sbyte":
                return "sbyte";
            case "System.UInt16":
            case "ushort":
                return "ushort";
            case "System.UInt32":
            case "uint":
                return "uint";
            case "System.UInt64":
            case "ulong":
                return "ulong";
            default:
                // Strip System. prefix for common types
                if (clrType.StartsWith("System.", StringComparison.Ordinal))
                    return clrType.Substring("System.".Length);
                return clrType;
        }
    }

    private static string GetBaseSql(AssembledPlan plan)
    {
        // Return the SQL for mask 0 or the first available variant
        if (plan.SqlVariants.TryGetValue(0, out var baseVariant))
            return baseVariant.Sql;
        return plan.SqlVariants.Values.FirstOrDefault()?.Sql ?? "";
    }

    private static string TruncateConditionText(string text)
    {
        const int maxLength = 60;
        if (text.Length <= maxLength)
            return text;
        return text.Substring(0, maxLength - 3) + "...";
    }

    private static bool IsTransitionKind(InterceptorKind kind) =>
        kind == InterceptorKind.ChainRoot ||
        kind == InterceptorKind.DeleteTransition ||
        kind == InterceptorKind.UpdateTransition ||
        kind == InterceptorKind.InsertTransition ||
        kind == InterceptorKind.AllTransition;

    private static bool IsModifierKind(InterceptorKind kind) =>
        kind == InterceptorKind.Limit ||
        kind == InterceptorKind.Offset ||
        kind == InterceptorKind.Distinct ||
        kind == InterceptorKind.WithTimeout;

    private static string GetDialectDisplayName(SqlDialect dialect)
    {
        switch (dialect)
        {
            case SqlDialect.SQLite: return "SQLite";
            case SqlDialect.PostgreSQL: return "PostgreSQL";
            case SqlDialect.MySQL: return "MySQL";
            case SqlDialect.SqlServer: return "SQL Server";
            default: return dialect.ToString();
        }
    }

    private static string GetDialectFileName(SqlDialect dialect)
    {
        switch (dialect)
        {
            case SqlDialect.SQLite: return "quarry-manifest.sqlite.md";
            case SqlDialect.PostgreSQL: return "quarry-manifest.postgresql.md";
            case SqlDialect.MySQL: return "quarry-manifest.mysql.md";
            case SqlDialect.SqlServer: return "quarry-manifest.sqlserver.md";
            default: return $"quarry-manifest.{dialect.ToString().ToLowerInvariant()}.md";
        }
    }

    private static void WriteIfChanged(string filePath, string content)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory!);

        if (File.Exists(filePath))
        {
            var existing = File.ReadAllText(filePath);
            if (existing == content)
                return;
        }

        File.WriteAllText(filePath, content);
    }
}
