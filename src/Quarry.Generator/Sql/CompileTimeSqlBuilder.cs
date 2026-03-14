using System;
using System.Collections.Generic;
using System.Text;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;
using Quarry;

namespace Quarry.Generators.Sql;

/// <summary>
/// Builds complete SQL strings at compile time (source generator time) for pre-built SQL optimization.
/// Mirrors the runtime <c>SqlBuilder.BuildSelectSql()</c> and <c>SqlModificationBuilder.Build*Sql()</c>
/// but runs in the generator process. Produces byte-identical SQL to what the runtime would generate
/// for the same query state.
/// </summary>
/// <remarks>
/// <para>
/// All parameter-bearing SQL fragments are represented as <see cref="SqlFragmentTemplate"/> instances,
/// which separate static SQL text from parameter slot positions. When building SQL for a specific
/// mask variant, each template is rendered with the correct global parameter index and dialect format
/// from the start — no regex or string replacement is needed.
/// </para>
/// </remarks>
internal static class CompileTimeSqlBuilder
{
    /// <summary>
    /// Maximum number of conditional bits for tier 1 dispatch (16 variants).
    /// </summary>
    internal const int MaxDispatchBits = 4;

    /// <summary>
    /// Builds a SELECT SQL string for a specific clause combination (mask value).
    /// </summary>
    /// <param name="mask">The ClauseMask value identifying which conditional clauses are active.</param>
    /// <param name="clauses">All clause sites in the chain, ordered by execution flow.
    /// Each clause with parameters must have a pre-built <see cref="SqlFragmentTemplate"/>.</param>
    /// <param name="templates">
    /// Pre-built templates keyed by clause index in the <paramref name="clauses"/> list.
    /// Produced by <see cref="BuildTemplates"/>.
    /// </param>
    /// <param name="dialect">The SQL dialect for quoting and formatting.</param>
    /// <param name="tableName">The database table name.</param>
    /// <param name="schemaName">The optional schema name.</param>
    /// <param name="fromTableAlias">The optional FROM table alias.</param>
    /// <returns>The complete SELECT SQL string for this mask variant.</returns>
    public static PrebuiltSqlResult BuildSelectSql(
        ulong mask,
        IReadOnlyList<ChainedClauseSite> clauses,
        IReadOnlyDictionary<int, SqlFragmentTemplate> templates,
        SqlDialect dialect,
        string tableName,
        string? schemaName,
        string? fromTableAlias = null)
    {
        var activeClauses = GetActiveClauses(mask, clauses);
        var activeIndices = GetActiveClauseIndices(mask, clauses);

        // Compute the global parameter base offset for each active clause
        var clauseBaseOffsets = ComputeParameterBaseOffsets(activeIndices, templates);
        int totalParams = ComputeTotalParameterCount(activeIndices, templates);

        var sb = new StringBuilder();

        // SELECT clause
        sb.Append("SELECT ");
        var selectClauses = GetClausesByRole(activeClauses, ClauseRole.Select);
        var distinctClauses = GetClausesByRole(activeClauses, ClauseRole.Distinct);
        if (distinctClauses.Count > 0)
        {
            sb.Append("DISTINCT ");
        }

        if (selectClauses.Count > 0)
        {
            var selectSite = selectClauses[0];
            if (selectSite.Site.ProjectionInfo != null)
            {
                AppendSelectColumns(sb, dialect, selectSite.Site.ProjectionInfo.Columns);
            }
            else
            {
                sb.Append('*');
            }
        }
        else
        {
            sb.Append('*');
        }

        // FROM clause
        sb.Append(" FROM ");
        sb.Append(FormatTableName(dialect, tableName, schemaName));
        if (fromTableAlias != null)
        {
            sb.Append(" AS ");
            sb.Append(QuoteIdentifier(dialect, fromTableAlias));
        }

        // JOIN clauses
        var joinClauses = GetClausesByRoleWithIndex(activeClauses, activeIndices, ClauseRole.Join);
        var joinIndex = 0;
        foreach (var (join, clauseIdx) in joinClauses)
        {
            if (join.Site.ClauseInfo is JoinClauseInfo joinInfo)
            {
                sb.Append(' ');
                sb.Append(GetJoinKeyword(joinInfo.JoinKind));
                sb.Append(' ');
                sb.Append(FormatTableName(dialect, joinInfo.JoinedTableName, joinInfo.JoinedSchemaName));
                // Assign table alias: use explicit alias if set, otherwise auto-assign
                // t1, t2, ... when fromTableAlias is set (matching runtime QueryState.WithJoin)
                var effectiveAlias = joinInfo.TableAlias;
                if (effectiveAlias == null && fromTableAlias != null)
                    effectiveAlias = $"t{joinIndex + 1}";
                if (effectiveAlias != null)
                {
                    sb.Append(" AS ");
                    sb.Append(QuoteIdentifier(dialect, effectiveAlias));
                }
                sb.Append(" ON ");
                if (templates.TryGetValue(clauseIdx, out var template))
                {
                    template.RenderTo(sb, dialect, clauseBaseOffsets[clauseIdx]);
                }
                else
                {
                    sb.Append(joinInfo.OnConditionSql);
                }
            }
            joinIndex++;
        }

        // WHERE clause
        var whereClauses = GetClausesByRoleWithIndex(activeClauses, activeIndices, ClauseRole.Where);
        AppendWhereClause(sb, whereClauses, templates, clauseBaseOffsets, dialect);

        // GROUP BY clause
        var groupByClauses = GetClausesByRoleWithIndex(activeClauses, activeIndices, ClauseRole.GroupBy);
        if (groupByClauses.Count > 0)
        {
            sb.Append(" GROUP BY ");
            var first = true;
            foreach (var (gb, clauseIdx) in groupByClauses)
            {
                if (!first) sb.Append(", ");
                first = false;
                AppendClauseFragment(sb, gb, clauseIdx, templates, clauseBaseOffsets, dialect);
            }
        }

        // HAVING clause
        var havingClauses = GetClausesByRoleWithIndex(activeClauses, activeIndices, ClauseRole.Having);
        if (havingClauses.Count > 0)
        {
            sb.Append(" HAVING ");
            SqlClauseJoining.AppendAndJoinedConditions(sb, havingClauses.Count, (sb, i) =>
            {
                var (hc, hIdx) = havingClauses[i];
                AppendClauseFragment(sb, hc, hIdx, templates, clauseBaseOffsets, dialect);
            });
        }

        // ORDER BY clause
        var orderByClauses = GetClausesByRoleWithIndex(activeClauses, activeIndices, ClauseRole.OrderBy);
        var thenByClauses = GetClausesByRoleWithIndex(activeClauses, activeIndices, ClauseRole.ThenBy);
        var allOrderClauses = new List<(ChainedClauseSite, int)>(orderByClauses.Count + thenByClauses.Count);
        allOrderClauses.AddRange(orderByClauses);
        allOrderClauses.AddRange(thenByClauses);

        if (allOrderClauses.Count > 0)
        {
            sb.Append(" ORDER BY ");
            var first = true;
            foreach (var (ob, clauseIdx) in allOrderClauses)
            {
                if (!first) sb.Append(", ");
                first = false;

                if (ob.Site.ClauseInfo is OrderByClauseInfo orderByInfo)
                {
                    // OrderBy columns typically have no parameters, but support them via template
                    if (templates.TryGetValue(clauseIdx, out var template) && template.HasParameters)
                    {
                        template.RenderTo(sb, dialect, clauseBaseOffsets[clauseIdx]);
                    }
                    else
                    {
                        sb.Append(orderByInfo.ColumnSql);
                    }
                    sb.Append(orderByInfo.IsDescending ? " DESC" : " ASC");
                }
                else
                {
                    AppendClauseFragment(sb, ob, clauseIdx, templates, clauseBaseOffsets, dialect);
                }
            }
        }

        // Pagination (Limit/Offset as parameters)
        var limitClauses = GetClausesByRoleWithIndex(activeClauses, activeIndices, ClauseRole.Limit);
        var offsetClauses = GetClausesByRoleWithIndex(activeClauses, activeIndices, ClauseRole.Offset);
        bool hasLimit = limitClauses.Count > 0;
        bool hasOffset = offsetClauses.Count > 0;

        if (hasLimit || hasOffset)
        {
            // SQL Server requires ORDER BY for OFFSET/FETCH
            if (dialect == SqlDialect.SqlServer && allOrderClauses.Count == 0)
            {
                sb.Append(" ORDER BY (SELECT NULL)");
            }

            int? limitParamIndex = null;
            if (hasLimit && clauseBaseOffsets.TryGetValue(limitClauses[0].Item2, out var limitBase))
                limitParamIndex = limitBase;

            int? offsetParamIndex = null;
            if (hasOffset && clauseBaseOffsets.TryGetValue(offsetClauses[0].Item2, out var offsetBase))
                offsetParamIndex = offsetBase;

            var pagination = FormatParameterizedPagination(dialect, limitParamIndex, offsetParamIndex);
            if (!string.IsNullOrEmpty(pagination))
            {
                sb.Append(' ');
                sb.Append(pagination);
            }
        }

        return new PrebuiltSqlResult(sb.ToString(), totalParams);
    }

    /// <summary>
    /// Builds an UPDATE SQL string for a specific clause combination.
    /// </summary>
    public static PrebuiltSqlResult BuildUpdateSql(
        ulong mask,
        IReadOnlyList<ChainedClauseSite> clauses,
        IReadOnlyDictionary<int, SqlFragmentTemplate> templates,
        SqlDialect dialect,
        string tableName,
        string? schemaName)
    {
        var activeClauses = GetActiveClauses(mask, clauses);
        var activeIndices = GetActiveClauseIndices(mask, clauses);

        // Compute the global parameter base offset for each active clause
        var clauseBaseOffsets = ComputeParameterBaseOffsets(activeIndices, templates);
        int totalParams = ComputeTotalParameterCount(activeIndices, templates);

        var sb = new StringBuilder();

        // UPDATE table
        sb.Append("UPDATE ");
        sb.Append(FormatTableName(dialect, tableName, schemaName));

        // SET clauses — parameters use centralized base offsets
        var setClauses = GetClausesByRoleWithIndex(activeClauses, activeIndices, ClauseRole.UpdateSet);

        if (setClauses.Count > 0)
        {
            sb.Append(" SET ");
            var setEmitted = 0;
            for (int i = 0; i < setClauses.Count; i++)
            {
                var (setClause, clauseIdx) = setClauses[i];

                // UpdateSetPoco: expand all columns from UpdateInfo
                if (setClause.Site.Kind == InterceptorKind.UpdateSetPoco && setClause.Site.UpdateInfo != null)
                {
                    var baseOffset = clauseBaseOffsets[clauseIdx];
                    var localIndex = 0;
                    foreach (var col in setClause.Site.UpdateInfo.Columns)
                    {
                        if (setEmitted > 0) sb.Append(", ");
                        sb.Append(col.QuotedColumnName);
                        sb.Append(" = ");
                        sb.Append(FormatParameter(dialect, baseOffset + localIndex));
                        localIndex++;
                        setEmitted++;
                    }
                    continue;
                }

                // Resolve column SQL from SetClauseInfo or plain ClauseInfo.SqlFragment
                string? columnSql = null;
                if (setClause.Site.ClauseInfo is SetClauseInfo setInfo)
                    columnSql = setInfo.ColumnSql;
                else if (setClause.Site.ClauseInfo != null && setClause.Site.ClauseInfo.IsSuccess)
                    columnSql = setClause.Site.ClauseInfo.SqlFragment;

                if (columnSql != null)
                {
                    if (setEmitted > 0) sb.Append(", ");
                    sb.Append(columnSql);
                    sb.Append(" = ");
                    sb.Append(FormatParameter(dialect, clauseBaseOffsets[clauseIdx]));
                    setEmitted++;
                }
            }
        }

        // WHERE clause — uses shared helper with centralized offsets
        var whereClauses = GetClausesByRoleWithIndex(activeClauses, activeIndices, ClauseRole.UpdateWhere);
        AppendWhereClause(sb, whereClauses, templates, clauseBaseOffsets, dialect);

        return new PrebuiltSqlResult(sb.ToString(), totalParams);
    }

    /// <summary>
    /// Builds a DELETE SQL string for a specific clause combination.
    /// </summary>
    public static PrebuiltSqlResult BuildDeleteSql(
        ulong mask,
        IReadOnlyList<ChainedClauseSite> clauses,
        IReadOnlyDictionary<int, SqlFragmentTemplate> templates,
        SqlDialect dialect,
        string tableName,
        string? schemaName)
    {
        var activeClauses = GetActiveClauses(mask, clauses);
        var activeIndices = GetActiveClauseIndices(mask, clauses);
        var clauseBaseOffsets = ComputeParameterBaseOffsets(activeIndices, templates);
        int totalParams = ComputeTotalParameterCount(activeIndices, templates);

        var sb = new StringBuilder();

        // DELETE FROM table
        sb.Append("DELETE FROM ");
        sb.Append(FormatTableName(dialect, tableName, schemaName));

        // WHERE clause
        var whereClauses = GetClausesByRoleWithIndex(activeClauses, activeIndices, ClauseRole.DeleteWhere);
        if (whereClauses.Count > 0)
        {
            sb.Append(" WHERE ");
            if (whereClauses.Count == 1)
            {
                var (wc, clauseIdx) = whereClauses[0];
                if (templates.TryGetValue(clauseIdx, out var tmpl))
                {
                    tmpl.RenderTo(sb, dialect, clauseBaseOffsets[clauseIdx]);
                }
                else
                {
                    sb.Append(wc.Site.ClauseInfo!.SqlFragment);
                }
            }
            else
            {
                for (int i = 0; i < whereClauses.Count; i++)
                {
                    if (i > 0) sb.Append(" AND ");
                    var (wc, clauseIdx) = whereClauses[i];
                    if (templates.TryGetValue(clauseIdx, out var tmpl))
                    {
                        tmpl.RenderTo(sb, dialect, clauseBaseOffsets[clauseIdx]);
                    }
                    else
                    {
                        sb.Append(wc.Site.ClauseInfo!.SqlFragment);
                    }
                }
            }
        }

        return new PrebuiltSqlResult(sb.ToString(), totalParams);
    }

    /// <summary>
    /// Builds an INSERT SQL string. Inserts have no conditional branching, so no mask is needed.
    /// </summary>
    /// <param name="dialect">The SQL dialect.</param>
    /// <param name="tableName">The database table name.</param>
    /// <param name="schemaName">The optional schema name.</param>
    /// <param name="columns">The column names (already quoted by the translator).</param>
    /// <param name="parameterCount">The number of parameters (one per column).</param>
    /// <param name="identityColumn">The identity column name for RETURNING clause, or null.</param>
    /// <returns>The complete INSERT SQL string.</returns>
    public static InsertSqlResult BuildInsertSql(
        SqlDialect dialect,
        string tableName,
        string? schemaName,
        IReadOnlyList<string> columns,
        int parameterCount,
        string? identityColumn)
    {
        var sb = new StringBuilder();

        // INSERT INTO table
        sb.Append("INSERT INTO ");
        sb.Append(FormatTableName(dialect, tableName, schemaName));

        // Column list
        if (columns.Count > 0)
        {
            sb.Append(" (");
            for (int i = 0; i < columns.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(columns[i]);
            }
            sb.Append(')');
        }

        // VALUES clause
        if (parameterCount > 0)
        {
            sb.Append(" VALUES (");
            for (int i = 0; i < parameterCount; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(FormatParameter(dialect, i));
            }
            sb.Append(')');
        }

        // RETURNING clause
        string? lastInsertIdQuery = null;
        if (!string.IsNullOrEmpty(identityColumn))
        {
            var returningClause = FormatReturningClause(dialect, identityColumn!);
            if (returningClause != null)
            {
                sb.Append(' ');
                sb.Append(returningClause);
            }
            else
            {
                // MySQL: no RETURNING, need separate query
                lastInsertIdQuery = GetLastInsertIdQuery(dialect);
            }
        }

        return new InsertSqlResult(sb.ToString(), lastInsertIdQuery);
    }

    // ───────────────────────────────────────────────────────────────
    // Template building
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds <see cref="SqlFragmentTemplate"/> instances for all clauses in a chain that
    /// carry parameters. Templates are keyed by the clause's index in the <paramref name="clauses"/> list.
    /// Clauses without parameters are not included (their SQL is static and used directly).
    /// </summary>
    public static Dictionary<int, SqlFragmentTemplate> BuildTemplates(
        IReadOnlyList<ChainedClauseSite> clauses)
    {
        var templates = new Dictionary<int, SqlFragmentTemplate>();
        for (int i = 0; i < clauses.Count; i++)
        {
            var clause = clauses[i];
            if (clause.Site.ClauseInfo != null && clause.Site.ClauseInfo.Parameters.Count > 0)
            {
                templates[i] = SqlFragmentTemplate.FromClauseInfo(clause.Site.ClauseInfo);
            }
            else if (clause.Role is ClauseRole.Limit or ClauseRole.Offset)
            {
                // Limit/Offset each contribute 1 parameter (the integer value).
                // They don't have ClauseInfo but need a template for parameter offset computation.
                templates[i] = new SqlFragmentTemplate(new[] { "", "" }, new[] { 0 });
            }
        }
        return templates;
    }

    // ───────────────────────────────────────────────────────────────
    // Map builders (all mask variants at once)
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the complete pre-built SQL map for all mask variants of a tier 1 SELECT chain.
    /// </summary>
    public static Dictionary<ulong, PrebuiltSqlResult> BuildSelectSqlMap(
        IReadOnlyList<ulong> possibleMasks,
        IReadOnlyList<ChainedClauseSite> clauses,
        SqlDialect dialect,
        string tableName,
        string? schemaName,
        string? fromTableAlias = null)
    {
        var templates = BuildTemplates(clauses);
        var map = new Dictionary<ulong, PrebuiltSqlResult>(possibleMasks.Count);
        foreach (var mask in possibleMasks)
        {
            map[mask] = BuildSelectSql(mask, clauses, templates, dialect, tableName, schemaName, fromTableAlias);
        }
        return map;
    }

    /// <summary>
    /// Builds the complete pre-built SQL map for all mask variants of a tier 1 UPDATE chain.
    /// </summary>
    public static Dictionary<ulong, PrebuiltSqlResult> BuildUpdateSqlMap(
        IReadOnlyList<ulong> possibleMasks,
        IReadOnlyList<ChainedClauseSite> clauses,
        SqlDialect dialect,
        string tableName,
        string? schemaName)
    {
        var templates = BuildTemplates(clauses);
        var map = new Dictionary<ulong, PrebuiltSqlResult>(possibleMasks.Count);
        foreach (var mask in possibleMasks)
        {
            map[mask] = BuildUpdateSql(mask, clauses, templates, dialect, tableName, schemaName);
        }
        return map;
    }

    /// <summary>
    /// Builds the complete pre-built SQL map for all mask variants of a tier 1 DELETE chain.
    /// </summary>
    public static Dictionary<ulong, PrebuiltSqlResult> BuildDeleteSqlMap(
        IReadOnlyList<ulong> possibleMasks,
        IReadOnlyList<ChainedClauseSite> clauses,
        SqlDialect dialect,
        string tableName,
        string? schemaName)
    {
        var templates = BuildTemplates(clauses);
        var map = new Dictionary<ulong, PrebuiltSqlResult>(possibleMasks.Count);
        foreach (var mask in possibleMasks)
        {
            map[mask] = BuildDeleteSql(mask, clauses, templates, dialect, tableName, schemaName);
        }
        return map;
    }

    // ───────────────────────────────────────────────────────────────
    // Dialect formatting helpers — mirror runtime dialect implementations
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Quotes an identifier using the dialect's quoting rules.
    /// Delegates to <see cref="SqlFormatting.QuoteIdentifier"/>.
    /// </summary>
    public static string QuoteIdentifier(SqlDialect dialect, string identifier)
        => SqlFormatting.QuoteIdentifier(dialect, identifier);

    /// <summary>
    /// Formats a table name with optional schema, using dialect-specific quoting.
    /// Delegates to <see cref="SqlFormatting.FormatTableName"/>.
    /// </summary>
    public static string FormatTableName(SqlDialect dialect, string tableName, string? schemaName)
        => SqlFormatting.FormatTableName(dialect, tableName, schemaName);

    /// <summary>
    /// Formats a parameter placeholder for the dialect.
    /// Delegates to <see cref="SqlFormatting.FormatParameter"/>.
    /// </summary>
    public static string FormatParameter(SqlDialect dialect, int index)
        => SqlFormatting.FormatParameter(dialect, index);

    /// <summary>
    /// Formats the RETURNING clause for identity retrieval.
    /// Delegates to <see cref="SqlFormatting.FormatReturningClause"/>.
    /// </summary>
    public static string? FormatReturningClause(SqlDialect dialect, string identityColumn)
        => SqlFormatting.FormatReturningClause(dialect, identityColumn);

    /// <summary>
    /// Gets the SQL query to retrieve the last inserted identity value.
    /// Delegates to <see cref="SqlFormatting.GetLastInsertIdQuery"/>.
    /// </summary>
    public static string? GetLastInsertIdQuery(SqlDialect dialect)
        => SqlFormatting.GetLastInsertIdQuery(dialect);

    /// <summary>
    /// Formats pagination with parameterized limit/offset values.
    /// Delegates to <see cref="SqlFormatting.FormatParameterizedPagination"/>.
    /// </summary>
    public static string FormatParameterizedPagination(
        SqlDialect dialect,
        int? limitParamIndex,
        int? offsetParamIndex)
        => SqlFormatting.FormatParameterizedPagination(dialect, limitParamIndex, offsetParamIndex);

    /// <summary>
    /// Formats a boolean literal according to the dialect.
    /// Delegates to <see cref="SqlFormatting.FormatBoolean"/>.
    /// </summary>
    public static string FormatBoolean(SqlDialect dialect, bool value)
        => SqlFormatting.FormatBoolean(dialect, value);

    // ───────────────────────────────────────────────────────────────
    // Internal helpers
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Gets the list of active clauses for a given mask value.
    /// Unconditional clauses are always active; conditional clauses are active
    /// only if their bit is set in the mask.
    /// </summary>
    internal static List<ChainedClauseSite> GetActiveClauses(
        ulong mask,
        IReadOnlyList<ChainedClauseSite> clauses)
    {
        var active = new List<ChainedClauseSite>(clauses.Count);
        foreach (var clause in clauses)
        {
            if (!clause.IsConditional)
            {
                active.Add(clause);
            }
            else if (clause.BitIndex.HasValue && (mask & (1UL << clause.BitIndex.Value)) != 0)
            {
                active.Add(clause);
            }
        }
        return active;
    }

    /// <summary>
    /// Gets the original indices (in the full clause list) of active clauses for a given mask.
    /// Used to look up templates and base offsets by clause index.
    /// </summary>
    internal static List<int> GetActiveClauseIndices(
        ulong mask,
        IReadOnlyList<ChainedClauseSite> clauses)
    {
        var indices = new List<int>(clauses.Count);
        for (int i = 0; i < clauses.Count; i++)
        {
            var clause = clauses[i];
            if (!clause.IsConditional)
            {
                indices.Add(i);
            }
            else if (clause.BitIndex.HasValue && (mask & (1UL << clause.BitIndex.Value)) != 0)
            {
                indices.Add(i);
            }
        }
        return indices;
    }

    /// <summary>
    /// Computes the global parameter base offset for each active clause.
    /// Parameters are numbered sequentially across active clauses in execution order.
    /// </summary>
    /// <returns>
    /// A mapping from clause index (in the full clause list) to its parameter base offset.
    /// Clauses with no parameters still get an entry (their offset equals the running total).
    /// </returns>
    internal static Dictionary<int, int> ComputeParameterBaseOffsets(
        IReadOnlyList<int> activeClauseIndices,
        IReadOnlyDictionary<int, SqlFragmentTemplate> templates)
    {
        var offsets = new Dictionary<int, int>(activeClauseIndices.Count);
        int runningOffset = 0;

        foreach (var clauseIdx in activeClauseIndices)
        {
            offsets[clauseIdx] = runningOffset;
            if (templates.TryGetValue(clauseIdx, out var template))
            {
                runningOffset += template.ParameterCount;
            }
        }

        return offsets;
    }

    /// <summary>
    /// Computes the total parameter count across all active clauses.
    /// </summary>
    internal static int ComputeTotalParameterCount(
        IReadOnlyList<int> activeClauseIndices,
        IReadOnlyDictionary<int, SqlFragmentTemplate> templates)
    {
        int total = 0;
        foreach (var clauseIdx in activeClauseIndices)
        {
            if (templates.TryGetValue(clauseIdx, out var template))
            {
                total += template.ParameterCount;
            }
        }
        return total;
    }

    /// <summary>
    /// Gets clauses from the active list that match a specific role, along with their
    /// original indices in the full clause list.
    /// </summary>
    private static List<(ChainedClauseSite Clause, int ClauseIndex)> GetClausesByRoleWithIndex(
        List<ChainedClauseSite> activeClauses,
        List<int> activeIndices,
        ClauseRole role)
    {
        var result = new List<(ChainedClauseSite, int)>();
        for (int i = 0; i < activeClauses.Count; i++)
        {
            if (activeClauses[i].Role == role)
                result.Add((activeClauses[i], activeIndices[i]));
        }
        return result;
    }

    /// <summary>
    /// Gets clauses from the active list that match a specific role (without index tracking).
    /// </summary>
    private static List<ChainedClauseSite> GetClausesByRole(
        List<ChainedClauseSite> activeClauses,
        ClauseRole role)
    {
        var result = new List<ChainedClauseSite>();
        foreach (var clause in activeClauses)
        {
            if (clause.Role == role)
                result.Add(clause);
        }
        return result;
    }

    /// <summary>
    /// Appends a WHERE clause to the StringBuilder from the given clause list.
    /// </summary>
    private static void AppendWhereClause(
        StringBuilder sb,
        List<(ChainedClauseSite Clause, int ClauseIndex)> whereClauses,
        IReadOnlyDictionary<int, SqlFragmentTemplate> templates,
        Dictionary<int, int> clauseBaseOffsets,
        SqlDialect dialect)
    {
        if (whereClauses.Count == 0) return;

        sb.Append(" WHERE ");
        SqlClauseJoining.AppendAndJoinedConditions(sb, whereClauses.Count, (sb, i) =>
        {
            var (wc, clauseIdx) = whereClauses[i];
            AppendClauseFragment(sb, wc, clauseIdx, templates, clauseBaseOffsets, dialect);
        });
    }

    /// <summary>
    /// Appends a single clause's SQL fragment to the StringBuilder,
    /// using the template if available for correct parameter rendering,
    /// or the static fragment if no parameters.
    /// </summary>
    private static void AppendClauseFragment(
        StringBuilder sb,
        ChainedClauseSite clause,
        int clauseIndex,
        IReadOnlyDictionary<int, SqlFragmentTemplate> templates,
        Dictionary<int, int> clauseBaseOffsets,
        SqlDialect dialect)
    {
        if (templates.TryGetValue(clauseIndex, out var template))
        {
            template.RenderTo(sb, dialect, clauseBaseOffsets[clauseIndex]);
        }
        else if (clause.Site.ClauseInfo != null)
        {
            sb.Append(clause.Site.ClauseInfo.SqlFragment);
        }
    }

    /// <summary>
    /// Appends SELECT column list from projection info.
    /// </summary>
    private static void AppendSelectColumns(
        StringBuilder sb,
        SqlDialect dialect,
        IReadOnlyList<ProjectedColumn> columns)
    {
        var first = true;
        foreach (var col in columns)
        {
            if (!first) sb.Append(", ");
            first = false;

            if (col.SqlExpression != null)
            {
                sb.Append(col.SqlExpression);
                if (col.Alias != null)
                {
                    sb.Append(" AS ");
                    sb.Append(QuoteIdentifier(dialect, col.Alias));
                }
            }
            else if (col.TableAlias != null)
            {
                sb.Append(QuoteIdentifier(dialect, col.TableAlias));
                sb.Append('.');
                sb.Append(QuoteIdentifier(dialect, col.ColumnName));
                if (col.Alias != null)
                {
                    sb.Append(" AS ");
                    sb.Append(QuoteIdentifier(dialect, col.Alias));
                }
            }
            else
            {
                sb.Append(QuoteIdentifier(dialect, col.ColumnName));
                if (col.Alias != null)
                {
                    sb.Append(" AS ");
                    sb.Append(QuoteIdentifier(dialect, col.Alias));
                }
            }
        }
    }

    private static string GetJoinKeyword(JoinClauseKind kind)
    {
        return kind switch
        {
            JoinClauseKind.Inner => "INNER JOIN",
            JoinClauseKind.Left => "LEFT JOIN",
            JoinClauseKind.Right => "RIGHT JOIN",
            _ => "JOIN"
        };
    }

}

/// <summary>
/// Result of building a pre-built SQL string for a specific mask variant.
/// </summary>
internal sealed class PrebuiltSqlResult : IEquatable<PrebuiltSqlResult>
{
    public PrebuiltSqlResult(string sql, int parameterCount)
    {
        Sql = sql;
        ParameterCount = parameterCount;
    }

    /// <summary>
    /// Gets the complete SQL string.
    /// </summary>
    public string Sql { get; }

    /// <summary>
    /// Gets the total number of parameters in this SQL variant.
    /// </summary>
    public int ParameterCount { get; }

    public bool Equals(PrebuiltSqlResult? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Sql == other.Sql && ParameterCount == other.ParameterCount;
    }

    public override bool Equals(object? obj) => Equals(obj as PrebuiltSqlResult);

    public override int GetHashCode() => HashCode.Combine(Sql, ParameterCount);
}

/// <summary>
/// Result of building a pre-built INSERT SQL string.
/// </summary>
internal sealed class InsertSqlResult
{
    public InsertSqlResult(string sql, string? lastInsertIdQuery)
    {
        Sql = sql;
        LastInsertIdQuery = lastInsertIdQuery;
    }

    /// <summary>
    /// Gets the INSERT SQL string.
    /// </summary>
    public string Sql { get; }

    /// <summary>
    /// Gets the separate query for retrieving the last inserted ID (MySQL only), or null.
    /// </summary>
    public string? LastInsertIdQuery { get; }
}
