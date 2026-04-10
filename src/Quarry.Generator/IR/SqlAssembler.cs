using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Quarry.Generators.Models;
using Quarry.Generators.Parsing;
using Quarry.Generators.Sql;

namespace Quarry.Generators.IR;

/// <summary>
/// Renders SQL from a QueryPlan for each possible mask value.
/// Clean-room replacement for CompileTimeSqlBuilder.
/// Takes AnalyzedChain → AssembledPlan with SQL variants per mask.
/// </summary>
internal static class SqlAssembler
{
    /// <summary>
    /// Resolves the result type name from the execution site, falling back to the
    /// projection's result type for non-identity projections (e.g., Select with a lambda).
    /// </summary>
    private static string? ResolveResultTypeName(TranslatedCallSite executionSite, QueryPlan plan)
    {
        return executionSite.Bound.Raw.ResultTypeName
            ?? (plan.Projection?.IsIdentity == false ? plan.Projection.ResultTypeName : null);
    }

    /// <summary>
    /// Assembles an AnalyzedChain into an AssembledPlan with rendered SQL variants.
    /// </summary>
    public static AssembledPlan Assemble(AnalyzedChain chain, EntityRegistry registry)
    {
        var plan = chain.Plan;
        var executionSite = chain.ExecutionSite;
        var dialect = executionSite.Bound.Dialect;
        var resultTypeName = ResolveResultTypeName(executionSite, plan);

        var sqlVariants = new Dictionary<int, AssembledSqlVariant>();

        if (plan.Tier == OptimizationTier.RuntimeBuild)
        {
            // No SQL assembly for tier 3
            return new AssembledPlan(
                plan: plan,
                sqlVariants: sqlVariants,
                readerDelegateCode: null,
                maxParameterCount: 0,
                executionSite: executionSite,
                clauseSites: chain.ClauseSites,
                entityTypeName: executionSite.Bound.Raw.EntityTypeName,
                resultTypeName: resultTypeName,
                dialect: dialect,
                entitySchemaNamespace: executionSite.Bound.Entity?.SchemaNamespace,
                isTraced: chain.IsTraced);
        }

        // Build SQL for each mask
        // Only include identity RETURNING/OUTPUT clause for ExecuteScalar (which returns the identity).
        // ExecuteNonQuery does not need it.
        // For Prepare chains, prefer the Prepare site's InsertInfo (has initializer-derived columns)
        // over the execution terminal's (which may have all-columns fallback for prepared terminals).
        var insertInfo = chain.PrepareSite?.Bound.InsertInfo ?? executionSite.Bound.InsertInfo;
        var needsIdentityReturning = executionSite.Bound.Raw.Kind is not InterceptorKind.InsertExecuteNonQuery
            and not InterceptorKind.BatchInsertExecuteNonQuery;
        if (insertInfo != null && !needsIdentityReturning)
        {
            // Strip identity column info so RETURNING/OUTPUT is not appended
            insertInfo = new Models.InsertInfo(insertInfo.Columns, null, null, null);
        }
        var maxParamCount = 0;
        foreach (var mask in plan.PossibleMasks)
        {
            var result = RenderSqlForMask(plan, mask, dialect, insertInfo);
            sqlVariants[mask] = result;
            if (result.ParameterCount > maxParamCount)
                maxParamCount = result.ParameterCount;
        }

        // Build reader delegate code for SELECT queries
        string? readerDelegateCode = null;
        if (plan.Kind == QueryKind.Select && plan.Projection != null)
        {
            readerDelegateCode = BuildReaderDelegateCode(plan.Projection, executionSite);
        }

        // Trace logging: assembly results per mask (only for traced chains)
        if (chain.IsTraced)
        {
            var asmUid = executionSite.Bound.Raw.UniqueId;
            foreach (var kvp in sqlVariants)
            {
                TraceCapture.Log(asmUid, $"[Trace] Assembly (mask={kvp.Key}):");
                TraceCapture.Log(asmUid, $"  sql={kvp.Value.Sql}");
                TraceCapture.Log(asmUid, $"  paramCount={kvp.Value.ParameterCount}");
            }
        }

        // Build batch insert metadata
        string? batchReturningSuffix = null;
        int batchColumnsPerRow = 0;
        if (plan.Kind == QueryKind.BatchInsert && insertInfo != null)
        {
            batchColumnsPerRow = insertInfo.Columns.Count;
            if (needsIdentityReturning && insertInfo.IdentityColumnName != null)
            {
                batchReturningSuffix = RenderReturningSuffix(dialect, insertInfo.IdentityColumnName);
            }
        }

        return new AssembledPlan(
            plan: plan,
            sqlVariants: sqlVariants,
            readerDelegateCode: readerDelegateCode,
            maxParameterCount: maxParamCount,
            executionSite: executionSite,
            clauseSites: chain.ClauseSites,
            entityTypeName: executionSite.Bound.Raw.EntityTypeName,
            resultTypeName: resultTypeName,
            dialect: dialect,
            entitySchemaNamespace: executionSite.Bound.Entity?.SchemaNamespace,
            isTraced: chain.IsTraced,
            batchInsertReturningSuffix: batchReturningSuffix,
            batchInsertColumnsPerRow: batchColumnsPerRow,
            preparedTerminals: chain.PreparedTerminals,
            prepareSite: chain.PrepareSite,
            insertInfo: insertInfo,
            isOperandChain: chain.IsOperandChain);
    }

    /// <summary>
    /// Renders the complete SQL for a given mask value.
    /// </summary>
    private static AssembledSqlVariant RenderSqlForMask(QueryPlan plan, int mask, SqlDialect dialect, Models.InsertInfo? insertInfo = null)
    {
        return plan.Kind switch
        {
            QueryKind.Select => RenderSelectSql(plan, mask, dialect),
            QueryKind.Delete => RenderDeleteSql(plan, mask, dialect),
            QueryKind.Update => RenderUpdateSql(plan, mask, dialect),
            QueryKind.Insert => RenderInsertSql(plan, mask, dialect, insertInfo),
            QueryKind.BatchInsert => RenderBatchInsertSql(plan, mask, dialect, insertInfo),
            _ => new AssembledSqlVariant("", 0)
        };
    }

    private static AssembledSqlVariant RenderSelectSql(QueryPlan plan, int mask, SqlDialect dialect, int paramBaseOffset = 0)
    {
        var sb = new StringBuilder();
        var paramIndex = paramBaseOffset;

        // WITH clause for CTE definitions
        if (plan.CteDefinitions.Count > 0)
        {
            sb.Append("WITH ");
            for (int i = 0; i < plan.CteDefinitions.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                var cte = plan.CteDefinitions[i];
                sb.Append(SqlFormatting.QuoteIdentifier(dialect, cte.Name));
                sb.Append(" AS (");

                // Re-render the inner CTE SQL with the outer chain's parameter offset so that
                // multi-CTE chains do not collide on placeholder names. The inner chain was
                // assembled standalone with paramBaseOffset = 0, producing inner SQL with
                // @p0/@p1 (or $1/$2) starting from zero. When concatenated into the outer
                // WITH clause, multiple CTEs would each carry their own @p0 — silently
                // colliding on named-placeholder dialects (sqlite/pg/ss). Re-rendering with
                // paramBaseOffset = cte.ParameterOffset rebases the placeholders into the
                // outer chain's global parameter index space, matching the carrier P-slots.
                //
                // Mask=0 is used because CTE inner chains are analyzed standalone, before
                // being embedded into the outer chain, and the existing analyzer does not
                // propagate outer conditional-clause masks into inner chains. If a future
                // inner chain has multiple masks, mask=0 is its base/canonical variant.
                //
                // Fallback: if cte.InnerPlan is null (inner-chain analysis failed — the
                // user already received a QRY080 diagnostic), use the pre-rendered raw
                // string so the SQL is at least non-empty for downstream tooling.
                if (cte.InnerPlan != null)
                {
                    // Fail-loud guard: the mask=0 assumption below is only valid for
                    // inner chains that have exactly one mask variant. No current code
                    // path produces multi-mask inner chains, but if a future analyzer
                    // change starts propagating conditional clauses into inner chains,
                    // this Debug.Assert will surface the violation instead of silently
                    // picking the base variant and dropping the others.
                    System.Diagnostics.Debug.Assert(
                        cte.InnerPlan.PossibleMasks.Count <= 1,
                        $"CTE inner chain '{cte.Name}' has {cte.InnerPlan.PossibleMasks.Count} mask variants; placeholder rebasing only handles mask=0. Extend RenderSelectSql to render per outer mask if this triggers.");
                    var rebased = RenderSelectSql(cte.InnerPlan, mask: 0, dialect, paramBaseOffset: cte.ParameterOffset);
                    sb.Append(rebased.Sql);
                }
                else
                {
                    sb.Append(cte.InnerSql);
                }

                sb.Append(')');
                // CTE inner parameters precede outer parameters
                paramIndex += cte.InnerParameters.Count;
            }
            sb.Append(' ');
        }

        // SELECT
        sb.Append("SELECT ");
        if (plan.IsDistinct)
            sb.Append("DISTINCT ");

        if (plan.Projection.Columns.Count > 0)
        {
            AppendSelectColumns(sb, dialect, plan.Projection.Columns);
        }
        else
        {
            sb.Append('*');
        }

        // FROM
        sb.Append(" FROM ");
        AppendTableRef(sb, dialect, plan.PrimaryTable);

        // Determine if we need table aliases for joins (explicit or implicit)
        string? fromAlias = null;
        if (plan.Joins.Count > 0 || plan.ImplicitJoins.Count > 0)
        {
            fromAlias = "t0";
            sb.Append(" AS ");
            sb.Append(SqlFormatting.QuoteIdentifier(dialect, fromAlias));
        }

        // Explicit JOINs
        for (int i = 0; i < plan.Joins.Count; i++)
        {
            var join = plan.Joins[i];
            sb.Append(' ');
            sb.Append(GetJoinKeyword(join.Kind));
            sb.Append(' ');
            AppendTableRef(sb, dialect, join.Table);

            var alias = join.Table.Alias ?? $"t{i + 1}";
            sb.Append(" AS ");
            sb.Append(SqlFormatting.QuoteIdentifier(dialect, alias));

            if (join.OnCondition != null)
            {
                sb.Append(" ON ");
                var paramsBefore = CountParameters(join.OnCondition);
                sb.Append(SqlExprRenderer.Render(join.OnCondition, dialect, paramIndex, stripOuterParens: true));
                paramIndex += paramsBefore;
            }
        }

        // Implicit JOINs from One<T> navigation access
        for (int i = 0; i < plan.ImplicitJoins.Count; i++)
        {
            var ij = plan.ImplicitJoins[i];
            sb.Append(' ');
            sb.Append(ij.JoinKind == JoinClauseKind.Left ? "LEFT JOIN" : "INNER JOIN");
            sb.Append(' ');
            sb.Append(SqlFormatting.QuoteIdentifier(dialect, ij.TargetTableName));
            sb.Append(" AS ");
            sb.Append(SqlFormatting.QuoteIdentifier(dialect, ij.TargetAlias));
            sb.Append(" ON ");
            sb.Append(SqlFormatting.QuoteIdentifier(dialect, ij.SourceAlias));
            sb.Append('.');
            sb.Append(SqlFormatting.QuoteIdentifier(dialect, ij.FkColumnName));
            sb.Append(" = ");
            sb.Append(SqlFormatting.QuoteIdentifier(dialect, ij.TargetAlias));
            sb.Append('.');
            sb.Append(SqlFormatting.QuoteIdentifier(dialect, ij.TargetPkColumnName));
        }

        // WHERE — compute each term's global parameter offset from ALL terms (not just active),
        // so conditional variants reference the correct carrier parameter slots.
        // Without this, skipped conditional terms cause parameter renumbering that mismatches
        // the carrier field indices (e.g., @p0 in SQL maps to P2 in the carrier).
        var activeWhereSet = new HashSet<WhereTerm>(GetActiveTerms(plan.WhereTerms, mask));
        var nonTrivialActive = new List<(WhereTerm Term, int ParamOffset)>();
        var whereParamOffset = paramIndex;
        foreach (var w in plan.WhereTerms)
        {
            var termParamCount = CountParameters(w.Condition);
            if (activeWhereSet.Contains(w) && !IsTrivialTrueCondition(w.Condition))
                nonTrivialActive.Add((w, whereParamOffset));
            whereParamOffset += termParamCount;
        }
        if (nonTrivialActive.Count > 0)
        {
            sb.Append(" WHERE ");
            for (int i = 0; i < nonTrivialActive.Count; i++)
            {
                if (i > 0) sb.Append(" AND ");
                var (w, termOffset) = nonTrivialActive[i];
                if (nonTrivialActive.Count > 1) sb.Append('(');
                sb.Append(RenderWhereCondition(w.Condition, dialect, termOffset));
                if (nonTrivialActive.Count > 1) sb.Append(')');
            }
        }
        paramIndex = whereParamOffset;

        // GROUP BY
        if (plan.GroupByExprs.Count > 0)
        {
            sb.Append(" GROUP BY ");
            for (int i = 0; i < plan.GroupByExprs.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                var paramsBefore = CountParameters(plan.GroupByExprs[i]);
                sb.Append(SqlExprRenderer.Render(plan.GroupByExprs[i], dialect, paramIndex));
                paramIndex += paramsBefore;
            }
        }

        // HAVING
        if (plan.HavingExprs.Count > 0)
        {
            sb.Append(" HAVING ");
            for (int i = 0; i < plan.HavingExprs.Count; i++)
            {
                if (i > 0) sb.Append(" AND ");
                var paramsBefore = CountParameters(plan.HavingExprs[i]);
                sb.Append(RenderWhereCondition(plan.HavingExprs[i], dialect, paramIndex));
                paramIndex += paramsBefore;
            }
        }

        // SET OPERATIONS (UNION, INTERSECT, EXCEPT)
        if (plan.SetOperations.Count > 0)
        {
            foreach (var setOp in plan.SetOperations)
            {
                sb.Append(' ');
                sb.Append(GetSetOperatorKeyword(setOp.Kind));
                sb.Append(' ');
                // Render the operand's SELECT SQL inline with global parameter offset
                var operandSql = RenderSelectSql(setOp.Operand, 0, dialect, paramIndex);
                sb.Append(operandSql.Sql);
                // ParameterCount includes the base offset, so compute the delta
                paramIndex = operandSql.ParameterCount;
            }

            // Post-union clauses (WHERE/GROUP BY/HAVING): wrap the set operation in a derived table
            var hasPostUnionClauses = plan.PostUnionWhereTerms.Count > 0
                || plan.PostUnionGroupByExprs.Count > 0
                || plan.PostUnionHavingExprs.Count > 0;
            if (hasPostUnionClauses)
            {
                var innerSql = sb.ToString();
                sb.Clear();
                sb.Append("SELECT * FROM (");
                sb.Append(innerSql);
                sb.Append(") AS ");
                sb.Append(SqlFormatting.QuoteIdentifier(dialect, "__set"));

                // Render post-union WHERE terms
                var postWhereActive = GetActiveTerms(plan.PostUnionWhereTerms, mask);
                if (postWhereActive.Count > 0)
                {
                    sb.Append(" WHERE ");
                    for (int i = 0; i < postWhereActive.Count; i++)
                    {
                        if (i > 0) sb.Append(" AND ");
                        var w = postWhereActive[i];
                        var termParamCount = CountParameters(w.Condition);
                        if (postWhereActive.Count > 1) sb.Append('(');
                        sb.Append(RenderWhereCondition(w.Condition, dialect, paramIndex));
                        if (postWhereActive.Count > 1) sb.Append(')');
                        paramIndex += termParamCount;
                    }
                }

                // Render post-union GROUP BY
                if (plan.PostUnionGroupByExprs.Count > 0)
                {
                    sb.Append(" GROUP BY ");
                    for (int i = 0; i < plan.PostUnionGroupByExprs.Count; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        var paramsBefore = CountParameters(plan.PostUnionGroupByExprs[i]);
                        sb.Append(SqlExprRenderer.Render(plan.PostUnionGroupByExprs[i], dialect, paramIndex));
                        paramIndex += paramsBefore;
                    }
                }

                // Render post-union HAVING
                if (plan.PostUnionHavingExprs.Count > 0)
                {
                    sb.Append(" HAVING ");
                    for (int i = 0; i < plan.PostUnionHavingExprs.Count; i++)
                    {
                        if (i > 0) sb.Append(" AND ");
                        var paramsBefore = CountParameters(plan.PostUnionHavingExprs[i]);
                        sb.Append(RenderWhereCondition(plan.PostUnionHavingExprs[i], dialect, paramIndex));
                        paramIndex += paramsBefore;
                    }
                }
            }
        }

        // ORDER BY (applies to combined result when set operations present)
        var activeOrders = GetActiveTerms(plan.OrderTerms, mask);
        if (activeOrders.Count > 0)
        {
            sb.Append(" ORDER BY ");
            for (int i = 0; i < activeOrders.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                var o = activeOrders[i];
                var paramsBefore = CountParameters(o.Expression);
                sb.Append(SqlExprRenderer.Render(o.Expression, dialect, paramIndex));
                paramIndex += paramsBefore;
                sb.Append(o.IsDescending ? " DESC" : " ASC");
            }
        }

        // PAGINATION (applies to combined result when set operations present)
        AppendPagination(sb, plan, dialect, activeOrders.Count > 0, ref paramIndex);

        return new AssembledSqlVariant(sb.ToString(), paramIndex);
    }

    private static AssembledSqlVariant RenderDeleteSql(QueryPlan plan, int mask, SqlDialect dialect)
    {
        var sb = new StringBuilder();
        var paramIndex = 0;

        sb.Append("DELETE FROM ");
        AppendTableRef(sb, dialect, plan.PrimaryTable);

        // WHERE — compute each term's global parameter offset from ALL terms (not just active),
        // so conditional variants reference the correct carrier parameter slots.
        // Without this, skipped conditional terms cause parameter renumbering that mismatches
        // the carrier field indices (e.g., @p0 in SQL maps to P2 in the carrier).
        var activeWhereSet = new HashSet<WhereTerm>(GetActiveTerms(plan.WhereTerms, mask));
        var nonTrivialActive = new List<(WhereTerm Term, int ParamOffset)>();
        var whereParamOffset = paramIndex;
        foreach (var w in plan.WhereTerms)
        {
            var termParamCount = CountParameters(w.Condition);
            if (activeWhereSet.Contains(w) && !IsTrivialTrueCondition(w.Condition))
                nonTrivialActive.Add((w, whereParamOffset));
            whereParamOffset += termParamCount;
        }
        if (nonTrivialActive.Count > 0)
        {
            sb.Append(" WHERE ");
            for (int i = 0; i < nonTrivialActive.Count; i++)
            {
                if (i > 0) sb.Append(" AND ");
                var (w, termOffset) = nonTrivialActive[i];
                if (nonTrivialActive.Count > 1) sb.Append('(');
                sb.Append(RenderWhereCondition(w.Condition, dialect, termOffset));
                if (nonTrivialActive.Count > 1) sb.Append(')');
            }
        }
        paramIndex = whereParamOffset;

        return new AssembledSqlVariant(sb.ToString(), paramIndex);
    }

    private static AssembledSqlVariant RenderUpdateSql(QueryPlan plan, int mask, SqlDialect dialect)
    {
        var sb = new StringBuilder();
        var paramIndex = 0;

        sb.Append("UPDATE ");
        AppendTableRef(sb, dialect, plan.PrimaryTable);

        // SET
        var activeSetTerms = GetActiveTerms(plan.SetTerms, mask);
        if (activeSetTerms.Count > 0)
        {
            sb.Append(" SET ");
            for (int i = 0; i < activeSetTerms.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                var s = activeSetTerms[i];
                sb.Append(SqlExprRenderer.Render(s.Column, dialect));
                sb.Append(" = ");
                var paramsBefore = CountParameters(s.Value);
                sb.Append(SqlExprRenderer.Render(s.Value, dialect, paramIndex));
                paramIndex += paramsBefore;
            }
        }

        // WHERE — compute each term's global parameter offset from ALL terms (not just active),
        // so conditional variants reference the correct carrier parameter slots.
        // Without this, skipped conditional terms cause parameter renumbering that mismatches
        // the carrier field indices (e.g., @p0 in SQL maps to P2 in the carrier).
        var activeWhereSet = new HashSet<WhereTerm>(GetActiveTerms(plan.WhereTerms, mask));
        var nonTrivialActive = new List<(WhereTerm Term, int ParamOffset)>();
        var whereParamOffset = paramIndex;
        foreach (var w in plan.WhereTerms)
        {
            var termParamCount = CountParameters(w.Condition);
            if (activeWhereSet.Contains(w) && !IsTrivialTrueCondition(w.Condition))
                nonTrivialActive.Add((w, whereParamOffset));
            whereParamOffset += termParamCount;
        }
        if (nonTrivialActive.Count > 0)
        {
            sb.Append(" WHERE ");
            for (int i = 0; i < nonTrivialActive.Count; i++)
            {
                if (i > 0) sb.Append(" AND ");
                var (w, termOffset) = nonTrivialActive[i];
                if (nonTrivialActive.Count > 1) sb.Append('(');
                sb.Append(RenderWhereCondition(w.Condition, dialect, termOffset));
                if (nonTrivialActive.Count > 1) sb.Append(')');
            }
        }
        paramIndex = whereParamOffset;

        return new AssembledSqlVariant(sb.ToString(), paramIndex);
    }

    private static AssembledSqlVariant RenderInsertSql(QueryPlan plan, int mask, SqlDialect dialect, Models.InsertInfo? insertInfo = null)
    {
        var sb = new StringBuilder();

        sb.Append("INSERT INTO ");
        AppendTableRef(sb, dialect, plan.PrimaryTable);

        if (plan.InsertColumns.Count > 0)
        {
            sb.Append(" (");
            for (int i = 0; i < plan.InsertColumns.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(plan.InsertColumns[i].QuotedColumnName);
            }
            sb.Append(") VALUES (");
            for (int i = 0; i < plan.InsertColumns.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(SqlFormatting.FormatParameter(dialect, plan.InsertColumns[i].ParameterIndex));
            }
            sb.Append(')');
        }

        // RETURNING clause for identity column
        if (insertInfo?.QuotedIdentityColumnName != null)
        {
            switch (dialect)
            {
                case SqlDialect.SQLite:
                case SqlDialect.PostgreSQL:
                    sb.Append(" RETURNING ");
                    sb.Append(SqlFormatting.QuoteIdentifier(dialect, insertInfo.IdentityColumnName!));
                    break;
                case SqlDialect.SqlServer:
                    sb.Append(" OUTPUT INSERTED.");
                    sb.Append(SqlFormatting.QuoteIdentifier(dialect, insertInfo.IdentityColumnName!));
                    break;
                case SqlDialect.MySQL:
                    sb.Append("; SELECT LAST_INSERT_ID()");
                    break;
            }
        }

        return new AssembledSqlVariant(sb.ToString(), plan.InsertColumns.Count);
    }

    /// <summary>
    /// Renders the SQL prefix for batch inserts. Entity count is unknown at compile time,
    /// so only the prefix (INSERT INTO table (columns) VALUES ) is rendered.
    /// The runtime BatchInsertSqlBuilder expands the row template per entity.
    /// </summary>
    private static AssembledSqlVariant RenderBatchInsertSql(QueryPlan plan, int mask, SqlDialect dialect, Models.InsertInfo? insertInfo = null)
    {
        var sb = new StringBuilder();

        sb.Append("INSERT INTO ");
        AppendTableRef(sb, dialect, plan.PrimaryTable);

        if (plan.InsertColumns.Count > 0)
        {
            sb.Append(" (");
            for (int i = 0; i < plan.InsertColumns.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(plan.InsertColumns[i].QuotedColumnName);
            }
            sb.Append(") VALUES ");
        }

        // The SQL prefix ends here — runtime will append (param, param), (param, param), ...
        // Return with 0 parameter count since params are runtime-determined
        return new AssembledSqlVariant(sb.ToString(), 0);
    }

    #region Helpers

    /// <summary>
    /// Renders the RETURNING/OUTPUT suffix for identity column retrieval.
    /// </summary>
    private static string? RenderReturningSuffix(SqlDialect dialect, string identityColumnName)
    {
        return dialect switch
        {
            SqlDialect.SQLite or SqlDialect.PostgreSQL
                => $" RETURNING {SqlFormatting.QuoteIdentifier(dialect, identityColumnName)}",
            SqlDialect.SqlServer
                => $" OUTPUT INSERTED.{SqlFormatting.QuoteIdentifier(dialect, identityColumnName)}",
            SqlDialect.MySQL
                => "; SELECT LAST_INSERT_ID()",
            _ => null
        };
    }

    private static void AppendTableRef(StringBuilder sb, SqlDialect dialect, TableRef table)
    {
        sb.Append(SqlFormatting.FormatTableName(dialect, table.TableName, table.SchemaName));
    }

    private static void AppendSelectColumns(StringBuilder sb, SqlDialect dialect, IReadOnlyList<ProjectedColumn> columns)
    {
        for (int i = 0; i < columns.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            var col = columns[i];
            if (col.IsAggregateFunction && !string.IsNullOrEmpty(col.SqlExpression))
            {
                // Aggregate function: render the SQL expression with an alias
                sb.Append(SqlFormatting.QuoteSqlExpression(col.SqlExpression, dialect));
                if (!string.IsNullOrEmpty(col.Alias))
                {
                    sb.Append(" AS ");
                    sb.Append(SqlFormatting.QuoteIdentifier(dialect, col.Alias!));
                }
            }
            else
            {
                if (col.TableAlias != null)
                {
                    sb.Append(SqlFormatting.QuoteIdentifier(dialect, col.TableAlias));
                    sb.Append('.');
                }
                sb.Append(SqlFormatting.QuoteIdentifier(dialect, col.ColumnName));
            }
        }
    }

    private static void AppendPagination(StringBuilder sb, QueryPlan plan, SqlDialect dialect, bool hasOrderBy, ref int paramIndex)
    {
        if (plan.Pagination == null) return;

        var pag = plan.Pagination;

        // SQL Server requires ORDER BY for OFFSET/FETCH
        if (dialect == SqlDialect.SqlServer && !hasOrderBy)
        {
            sb.Append(" ORDER BY (SELECT NULL)");
        }

        bool hasParamLimit = pag.LimitParamIndex != null;
        bool hasParamOffset = pag.OffsetParamIndex != null;

        if (!hasParamLimit && !hasParamOffset)
        {
            // All values are literal (or absent) — use the literal formatter
            var pagination = SqlFormatting.FormatPagination(dialect, pag.LiteralLimit, pag.LiteralOffset);
            if (!string.IsNullOrEmpty(pagination))
            {
                sb.Append(' ');
                sb.Append(pagination);
            }
        }
        else
        {
            // At least one value is parameterized — remap param indices and
            // use the mixed formatter that handles literal/parameterized combinations
            int? limitIdx = null;
            int? offsetIdx = null;

            if (hasParamLimit)
            {
                limitIdx = paramIndex;
                paramIndex++;
            }
            if (hasParamOffset)
            {
                offsetIdx = paramIndex;
                paramIndex++;
            }

            var pagination = SqlFormatting.FormatMixedPagination(
                dialect, pag.LiteralLimit, limitIdx, pag.LiteralOffset, offsetIdx);
            if (!string.IsNullOrEmpty(pagination))
            {
                sb.Append(' ');
                sb.Append(pagination);
            }
        }
    }

    private static string GetSetOperatorKeyword(SetOperatorKind kind)
    {
        return kind switch
        {
            SetOperatorKind.Union => "UNION",
            SetOperatorKind.UnionAll => "UNION ALL",
            SetOperatorKind.Intersect => "INTERSECT",
            SetOperatorKind.IntersectAll => "INTERSECT ALL",
            SetOperatorKind.Except => "EXCEPT",
            SetOperatorKind.ExceptAll => "EXCEPT ALL",
            _ => throw new InvalidOperationException($"Unknown set operator kind: {kind}")
        };
    }

    private static string GetJoinKeyword(JoinClauseKind kind)
    {
        return kind switch
        {
            JoinClauseKind.Inner => "INNER JOIN",
            JoinClauseKind.Left => "LEFT JOIN",
            JoinClauseKind.Right => "RIGHT JOIN",
            JoinClauseKind.Cross => "CROSS JOIN",
            JoinClauseKind.FullOuter => "FULL OUTER JOIN",
            _ => "JOIN"
        };
    }

    /// <summary>
    /// Checks if a WHERE condition is trivially true (literal 1 or true).
    /// Such conditions are optimized away (no WHERE clause needed).
    /// </summary>
    private static bool IsTrivialTrueCondition(SqlExpr expr)
    {
        if (expr is LiteralExpr literal)
        {
            var val = literal.SqlText;
            return val == "1" || val == "true" || val == "TRUE" || val == "True";
        }
        return false;
    }

    /// <summary>
    /// Gets terms that are active for the given mask (unconditional terms + conditional terms whose bit is set).
    /// </summary>
    private static List<WhereTerm> GetActiveTerms(IReadOnlyList<WhereTerm> terms, int mask)
    {
        var result = new List<WhereTerm>();
        foreach (var t in terms)
        {
            if (t.BitIndex == null || (mask & (1 << t.BitIndex.Value)) != 0)
                result.Add(t);
        }
        return result;
    }

    private static List<OrderTerm> GetActiveTerms(IReadOnlyList<OrderTerm> terms, int mask)
    {
        var result = new List<OrderTerm>();
        foreach (var t in terms)
        {
            if (t.BitIndex == null || (mask & (1 << t.BitIndex.Value)) != 0)
                result.Add(t);
        }
        return result;
    }

    private static List<SetTerm> GetActiveTerms(IReadOnlyList<SetTerm> terms, int mask)
    {
        var result = new List<SetTerm>();
        foreach (var t in terms)
        {
            if (t.BitIndex == null || (mask & (1 << t.BitIndex.Value)) != 0)
                result.Add(t);
        }
        return result;
    }

    /// <summary>
    /// Renders a WHERE condition, stripping outer parentheses only for compound
    /// AND/OR expressions (which are redundant in WHERE context) but keeping them
    /// for comparison operators like = > < etc.
    /// </summary>
    private static string RenderWhereCondition(SqlExpr condition, SqlDialect dialect, int paramIndex)
    {
        // For AND/OR at the WHERE top level, recursively flatten and strip inner comparison parens.
        // RenderBinary wraps ALL BinaryOpExpr in parens, but WHERE context doesn't need them
        // around comparisons (e.g., WHERE col > 50 AND col2 = 1, not WHERE (col > 50) AND (col2 = 1)).
        //
        // IMPORTANT: paramIndex is the base offset for this entire clause — it is NOT accumulated
        // across siblings. ParamSlotExpr.LocalIndex is clause-global (0, 1, 2...), so the renderer
        // computes the final index as paramIndex + LocalIndex. Accumulating leftParams would
        // double-count and produce gaps (e.g., @p0, @p2 instead of @p0, @p1).
        if (condition is BinaryOpExpr bin && (bin.Operator == SqlBinaryOperator.And || bin.Operator == SqlBinaryOperator.Or))
        {
            var left = RenderWhereChild(bin.Left, dialect, paramIndex, bin.Operator);
            var right = RenderWhereChild(bin.Right, dialect, paramIndex, bin.Operator);
            var op = bin.Operator == SqlBinaryOperator.And ? " AND " : " OR ";
            return left + op + right;
        }
        if (condition is BinaryOpExpr)
            return SqlExprRenderer.Render(condition, dialect, paramIndex, stripOuterParens: true);
        return SqlExprRenderer.Render(condition, dialect, paramIndex);
    }

    private static string RenderWhereChild(SqlExpr child, SqlDialect dialect, int paramIndex, SqlBinaryOperator parentOp)
    {
        if (child is BinaryOpExpr bin)
        {
            if (bin.Operator == SqlBinaryOperator.And || bin.Operator == SqlBinaryOperator.Or)
            {
                // Recursively flatten same or compatible logical ops
                var inner = RenderWhereCondition(child, dialect, paramIndex);
                // Wrap OR inside AND for correct precedence
                if (parentOp == SqlBinaryOperator.And && bin.Operator == SqlBinaryOperator.Or)
                    return "(" + inner + ")";
                return inner;
            }
            // Comparison operator: strip outer parens
            return SqlExprRenderer.Render(child, dialect, paramIndex, stripOuterParens: true);
        }
        return SqlExprRenderer.Render(child, dialect, paramIndex);
    }

    /// <summary>
    /// Counts the number of ParamSlotExpr nodes in an expression tree.
    /// </summary>
    private static int CountParameters(SqlExpr expr)
    {
        return SqlExprRenderer.CollectParameters(expr).Count;
    }

    /// <summary>
    /// Builds a reader delegate code string for SELECT projections.
    /// This is a simplified version — the full reader generation happens in the emitter.
    /// </summary>
    private static string? BuildReaderDelegateCode(SelectProjection projection, TranslatedCallSite executionSite)
    {
        // Reader delegate generation is handled by the emitter layer
        // which has access to full type information.
        // Return null to signal the emitter should generate the reader.
        return null;
    }

    #endregion
}
