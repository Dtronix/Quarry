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
        // The DISTINCT + ORDER BY-on-non-projected-column wrap restructures the SELECT
        // shape entirely (outer SELECT around inner SELECT), so it doesn't share the
        // batch path's prefix/middle/suffix decomposition. When the plan can need the
        // wrap on any mask, fall back to per-mask single rendering.
        var canBatch = plan.PossibleMasks.Count > 1 && plan.Kind is QueryKind.Select or QueryKind.Delete;
        if (canBatch && plan.Kind == QueryKind.Select && MayNeedDistinctOrderByWrap(plan, dialect))
            canBatch = false;
        if (canBatch)
        {
            // Batch rendering: pre-render shared segments once and assemble per mask
            if (plan.Kind == QueryKind.Select)
                RenderSelectSqlBatch(plan, dialect, plan.PossibleMasks, sqlVariants);
            else
                RenderDeleteSqlBatch(plan, dialect, plan.PossibleMasks, sqlVariants);
            foreach (var v in sqlVariants.Values)
            {
                if (v.ParameterCount > maxParamCount)
                    maxParamCount = v.ParameterCount;
            }
        }
        else
        {
            foreach (var mask in plan.PossibleMasks)
            {
                var result = RenderSqlForMask(plan, mask, dialect, insertInfo);
                sqlVariants[mask] = result;
                if (result.ParameterCount > maxParamCount)
                    maxParamCount = result.ParameterCount;
            }
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

        // Build batch insert metadata. SQL Server's OUTPUT clause is folded
        // into the prefix in RenderBatchInsertSql (the OUTPUT must precede
        // VALUES on SQL Server, not follow the row tuples), so the suffix
        // is empty for that dialect.
        string? batchReturningSuffix = null;
        int batchColumnsPerRow = 0;
        if (plan.Kind == QueryKind.BatchInsert && insertInfo != null)
        {
            batchColumnsPerRow = insertInfo.Columns.Count;
            if (needsIdentityReturning && insertInfo.IdentityColumnName != null && dialect != SqlDialect.SqlServer)
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
        // DISTINCT + ORDER BY on a non-projected column: wrap in a derived table so the
        // ORDER BY columns appear in the inner SELECT list. PG/SS reject the flat form
        // (42P10 / equivalent); SQLite/MySQL accept it but with implementation-defined
        // semantics. Unifying on the wrap gives all four dialects the same standard-SQL
        // semantic. See #267.
        if (NeedsDistinctOrderByWrap(plan, mask, dialect))
            return RenderSelectSqlWithDistinctOrderByWrap(plan, mask, dialect, paramBaseOffset);

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
            AppendSelectColumns(sb, dialect, plan.Projection.Columns, paramIndex);
        }
        else
        {
            sb.Append('*');
        }

        AppendFromAndJoinsForPlan(sb, plan, dialect, ref paramIndex);
        AppendWhereForMask(sb, plan.WhereTerms, mask, dialect, ref paramIndex);
        AppendGroupByAndHaving(sb, plan, dialect, ref paramIndex);

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

                // Render post-union WHERE terms — compute each term's global parameter
                // offset from ALL terms (not just active), matching the main WHERE pattern.
                var postWhereActiveSet = new HashSet<WhereTerm>(GetActiveTerms(plan.PostUnionWhereTerms, mask));
                var postWhereNonTrivialActive = new List<(WhereTerm Term, int ParamOffset)>();
                var postWhereParamOffset = paramIndex;
                foreach (var w in plan.PostUnionWhereTerms)
                {
                    var termParamCount = CountParameters(w.Condition);
                    if (postWhereActiveSet.Contains(w) && !IsTrivialTrueCondition(w.Condition))
                        postWhereNonTrivialActive.Add((w, postWhereParamOffset));
                    postWhereParamOffset += termParamCount;
                }
                if (postWhereNonTrivialActive.Count > 0)
                {
                    sb.Append(" WHERE ");
                    for (int i = 0; i < postWhereNonTrivialActive.Count; i++)
                    {
                        if (i > 0) sb.Append(" AND ");
                        var (w, termOffset) = postWhereNonTrivialActive[i];
                        if (postWhereNonTrivialActive.Count > 1) sb.Append('(');
                        sb.Append(RenderWhereCondition(w.Condition, dialect, termOffset));
                        if (postWhereNonTrivialActive.Count > 1) sb.Append(')');
                    }
                }
                paramIndex = postWhereParamOffset;

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

        // ORDER BY — compute each term's parameter offset from ALL terms (not just active),
        // matching the WHERE pre-computation pattern. This ensures parameter indices align
        // with carrier GlobalIndex values for conditional ORDER BY terms.
        var activeOrderSet = new HashSet<OrderTerm>(GetActiveTerms(plan.OrderTerms, mask));
        var activeOrderRendered = new List<string>();
        var orderParamOffset = paramIndex;
        foreach (var o in plan.OrderTerms)
        {
            var termParamCount = CountParameters(o.Expression);
            if (activeOrderSet.Contains(o))
            {
                var rendered = SqlExprRenderer.Render(o.Expression, dialect, orderParamOffset);
                activeOrderRendered.Add(rendered + (o.IsDescending ? " DESC" : " ASC"));
            }
            orderParamOffset += termParamCount;
        }
        if (activeOrderRendered.Count > 0)
        {
            sb.Append(" ORDER BY ");
            sb.Append(string.Join(", ", activeOrderRendered));
        }
        paramIndex = orderParamOffset;

        // PAGINATION (applies to combined result when set operations present)
        AppendPagination(sb, plan, dialect, activeOrderRendered.Count > 0, ref paramIndex);

        // Ensure returned ParameterCount includes projection params.  Projection
        // column {@N} placeholders are resolved by AppendSelectColumns (rendered in
        // SELECT) but paramIndex only tracks clause-level params (WHERE, ORDER BY,
        // etc.).  When this plan is a set-operation operand, the caller uses the
        // returned count as the base offset for the next operand — omitting
        // projection params would cause index collisions.
        var totalPlanParams = paramBaseOffset + plan.Parameters.Count;
        paramIndex = Math.Max(paramIndex, totalPlanParams);

        return new AssembledSqlVariant(sb.ToString(), paramIndex);
    }

    /// <summary>
    /// Renders all mask variants for a SELECT query at once by pre-rendering shared
    /// segments and assembling per-mask variants via string concatenation.
    /// Avoids re-rendering the shared prefix (CTE, SELECT, FROM, JOINs), middle
    /// (GROUP BY, HAVING), and suffix (pagination) for each mask.
    /// </summary>
    private static void RenderSelectSqlBatch(
        QueryPlan plan, SqlDialect dialect,
        IReadOnlyList<int> masks,
        Dictionary<int, AssembledSqlVariant> results)
    {
        var paramIndex = 0;

        // ── Shared prefix: CTE + SELECT + FROM + JOINs ──

        var prefixSb = new StringBuilder();

        // WITH clause for CTE definitions
        if (plan.CteDefinitions.Count > 0)
        {
            prefixSb.Append("WITH ");
            for (int i = 0; i < plan.CteDefinitions.Count; i++)
            {
                if (i > 0) prefixSb.Append(", ");
                var cte = plan.CteDefinitions[i];
                prefixSb.Append(SqlFormatting.QuoteIdentifier(dialect, cte.Name));
                prefixSb.Append(" AS (");
                if (cte.InnerPlan != null)
                {
                    System.Diagnostics.Debug.Assert(
                        cte.InnerPlan.PossibleMasks.Count <= 1,
                        $"CTE inner chain '{cte.Name}' has {cte.InnerPlan.PossibleMasks.Count} mask variants; placeholder rebasing only handles mask=0.");
                    var rebased = RenderSelectSql(cte.InnerPlan, mask: 0, dialect, paramBaseOffset: cte.ParameterOffset);
                    prefixSb.Append(rebased.Sql);
                }
                else
                {
                    prefixSb.Append(cte.InnerSql);
                }
                prefixSb.Append(')');
                paramIndex += cte.InnerParameters.Count;
            }
            prefixSb.Append(' ');
        }

        // SELECT
        prefixSb.Append("SELECT ");
        if (plan.IsDistinct)
            prefixSb.Append("DISTINCT ");

        if (plan.Projection.Columns.Count > 0)
        {
            AppendSelectColumns(prefixSb, dialect, plan.Projection.Columns, paramIndex);
        }
        else
        {
            prefixSb.Append('*');
        }

        // FROM
        prefixSb.Append(" FROM ");
        AppendTableRef(prefixSb, dialect, plan.PrimaryTable);

        // Join aliases
        if (plan.Joins.Count > 0 || plan.ImplicitJoins.Count > 0)
        {
            prefixSb.Append(" AS ");
            prefixSb.Append(SqlFormatting.QuoteIdentifier(dialect, "t0"));
        }

        // Explicit JOINs
        for (int i = 0; i < plan.Joins.Count; i++)
        {
            var join = plan.Joins[i];
            prefixSb.Append(' ');
            prefixSb.Append(GetJoinKeyword(join.Kind));
            prefixSb.Append(' ');
            AppendTableRef(prefixSb, dialect, join.Table);
            var alias = join.Table.Alias ?? $"t{i + 1}";
            prefixSb.Append(" AS ");
            prefixSb.Append(SqlFormatting.QuoteIdentifier(dialect, alias));
            if (join.OnCondition != null)
            {
                prefixSb.Append(" ON ");
                var paramsBefore = CountParameters(join.OnCondition);
                prefixSb.Append(SqlExprRenderer.Render(join.OnCondition, dialect, paramIndex, stripOuterParens: true));
                paramIndex += paramsBefore;
            }
        }

        // Implicit JOINs
        for (int i = 0; i < plan.ImplicitJoins.Count; i++)
        {
            var ij = plan.ImplicitJoins[i];
            prefixSb.Append(' ');
            prefixSb.Append(ij.JoinKind == JoinClauseKind.Left ? "LEFT JOIN" : "INNER JOIN");
            prefixSb.Append(' ');
            prefixSb.Append(SqlFormatting.QuoteIdentifier(dialect, ij.TargetTableName));
            prefixSb.Append(" AS ");
            prefixSb.Append(SqlFormatting.QuoteIdentifier(dialect, ij.TargetAlias));
            prefixSb.Append(" ON ");
            prefixSb.Append(SqlFormatting.QuoteIdentifier(dialect, ij.SourceAlias));
            prefixSb.Append('.');
            prefixSb.Append(SqlFormatting.QuoteIdentifier(dialect, ij.FkColumnName));
            prefixSb.Append(" = ");
            prefixSb.Append(SqlFormatting.QuoteIdentifier(dialect, ij.TargetAlias));
            prefixSb.Append('.');
            prefixSb.Append(SqlFormatting.QuoteIdentifier(dialect, ij.TargetPkColumnName));
        }

        var prefixStr = prefixSb.ToString();

        // ── Pre-render WHERE terms ──

        var whereTerms = PreRenderWhereTerms(plan.WhereTerms, dialect, paramIndex);
        foreach (var w in plan.WhereTerms)
            paramIndex += CountParameters(w.Condition);

        // ── Shared middle: GROUP BY + HAVING ──

        var middleSb = new StringBuilder();

        if (plan.GroupByExprs.Count > 0)
        {
            middleSb.Append(" GROUP BY ");
            for (int i = 0; i < plan.GroupByExprs.Count; i++)
            {
                if (i > 0) middleSb.Append(", ");
                var paramsBefore = CountParameters(plan.GroupByExprs[i]);
                middleSb.Append(SqlExprRenderer.Render(plan.GroupByExprs[i], dialect, paramIndex));
                paramIndex += paramsBefore;
            }
        }

        if (plan.HavingExprs.Count > 0)
        {
            middleSb.Append(" HAVING ");
            for (int i = 0; i < plan.HavingExprs.Count; i++)
            {
                if (i > 0) middleSb.Append(" AND ");
                var paramsBefore = CountParameters(plan.HavingExprs[i]);
                middleSb.Append(RenderWhereCondition(plan.HavingExprs[i], dialect, paramIndex));
                paramIndex += paramsBefore;
            }
        }

        var middleStr = middleSb.ToString();

        // ── Set operations (if any) ──

        var setOpsStr = "";
        var hasPostUnionWrapping = false;
        var quotedSetAlias = "";
        List<(WhereTerm Term, string Rendered)>? postUnionWhereTerms = null;
        var postUnionMiddleStr = "";

        if (plan.SetOperations.Count > 0)
        {
            var setOpsSb = new StringBuilder();
            foreach (var setOp in plan.SetOperations)
            {
                setOpsSb.Append(' ');
                setOpsSb.Append(GetSetOperatorKeyword(setOp.Kind));
                setOpsSb.Append(' ');
                var operandSql = RenderSelectSql(setOp.Operand, 0, dialect, paramIndex);
                setOpsSb.Append(operandSql.Sql);
                paramIndex = operandSql.ParameterCount;
            }
            setOpsStr = setOpsSb.ToString();

            var hasPostUnionClauses = plan.PostUnionWhereTerms.Count > 0
                || plan.PostUnionGroupByExprs.Count > 0
                || plan.PostUnionHavingExprs.Count > 0;

            if (hasPostUnionClauses)
            {
                hasPostUnionWrapping = true;
                quotedSetAlias = SqlFormatting.QuoteIdentifier(dialect, "__set");

                // Pre-render post-union WHERE terms
                postUnionWhereTerms = PreRenderWhereTerms(plan.PostUnionWhereTerms, dialect, paramIndex);
                foreach (var w in plan.PostUnionWhereTerms)
                    paramIndex += CountParameters(w.Condition);

                // Render post-union GROUP BY + HAVING (shared)
                var postUnionMiddleSb = new StringBuilder();
                if (plan.PostUnionGroupByExprs.Count > 0)
                {
                    postUnionMiddleSb.Append(" GROUP BY ");
                    for (int i = 0; i < plan.PostUnionGroupByExprs.Count; i++)
                    {
                        if (i > 0) postUnionMiddleSb.Append(", ");
                        var paramsBefore = CountParameters(plan.PostUnionGroupByExprs[i]);
                        postUnionMiddleSb.Append(SqlExprRenderer.Render(plan.PostUnionGroupByExprs[i], dialect, paramIndex));
                        paramIndex += paramsBefore;
                    }
                }
                if (plan.PostUnionHavingExprs.Count > 0)
                {
                    postUnionMiddleSb.Append(" HAVING ");
                    for (int i = 0; i < plan.PostUnionHavingExprs.Count; i++)
                    {
                        if (i > 0) postUnionMiddleSb.Append(" AND ");
                        var paramsBefore = CountParameters(plan.PostUnionHavingExprs[i]);
                        postUnionMiddleSb.Append(RenderWhereCondition(plan.PostUnionHavingExprs[i], dialect, paramIndex));
                        paramIndex += paramsBefore;
                    }
                }
                postUnionMiddleStr = postUnionMiddleSb.ToString();
            }
        }

        // ── Pre-render ORDER BY terms ──

        var orderTerms = PreRenderOrderByTerms(plan.OrderTerms, dialect, paramIndex);
        foreach (var o in plan.OrderTerms)
            paramIndex += CountParameters(o.Expression);

        // ── Pagination (shared, SQL Server ORDER BY fallback handled per mask) ──

        var paginationStr = "";
        var needsSqlServerOrderByFallback = false;
        if (plan.Pagination != null)
        {
            needsSqlServerOrderByFallback = dialect == SqlDialect.SqlServer;
            // Render pagination without SQL Server ORDER BY fallback — handled per mask
            var pagSb = new StringBuilder();
            AppendPagination(pagSb, plan, dialect, hasOrderBy: true, ref paramIndex);
            paginationStr = pagSb.ToString();
        }

        // Final param count
        var finalParamCount = Math.Max(paramIndex, plan.Parameters.Count);

        // ── Assemble per mask ──
        // Reuse a single StringBuilder across masks to reduce allocations.

        var sb = new StringBuilder();
        foreach (var mask in masks)
        {
            sb.Clear();

            if (hasPostUnionWrapping)
            {
                sb.Append("SELECT * FROM (");
                sb.Append(prefixStr);
                AppendWhereClause(sb, whereTerms, mask);
                sb.Append(middleStr);
                sb.Append(setOpsStr);
                sb.Append(") AS ");
                sb.Append(quotedSetAlias);
                AppendWhereClause(sb, postUnionWhereTerms!, mask);
                sb.Append(postUnionMiddleStr);
            }
            else if (setOpsStr.Length > 0)
            {
                sb.Append(prefixStr);
                AppendWhereClause(sb, whereTerms, mask);
                sb.Append(middleStr);
                sb.Append(setOpsStr);
            }
            else
            {
                sb.Append(prefixStr);
                AppendWhereClause(sb, whereTerms, mask);
                sb.Append(middleStr);
            }

            // ORDER BY
            var hasActiveOrderBy = AppendOrderByClause(sb, orderTerms, mask);
            if (!hasActiveOrderBy && needsSqlServerOrderByFallback)
                sb.Append(" ORDER BY (SELECT NULL)");

            // Pagination
            sb.Append(paginationStr);

            results[mask] = new AssembledSqlVariant(sb.ToString(), finalParamCount);
        }
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

    /// <summary>
    /// Renders all mask variants for a DELETE query at once by pre-rendering the
    /// shared prefix and WHERE terms, then assembling per mask via string concatenation.
    /// </summary>
    private static void RenderDeleteSqlBatch(
        QueryPlan plan, SqlDialect dialect,
        IReadOnlyList<int> masks,
        Dictionary<int, AssembledSqlVariant> results)
    {
        // Shared prefix: DELETE FROM table
        var prefixSb = new StringBuilder();
        prefixSb.Append("DELETE FROM ");
        AppendTableRef(prefixSb, dialect, plan.PrimaryTable);
        var prefixStr = prefixSb.ToString();

        // Pre-render WHERE terms (paramBaseOffset = 0 for DELETE)
        var whereTerms = PreRenderWhereTerms(plan.WhereTerms, dialect, 0);
        var paramIndex = 0;
        foreach (var w in plan.WhereTerms)
            paramIndex += CountParameters(w.Condition);

        // Assemble per mask — reuse StringBuilder
        var sb = new StringBuilder();
        foreach (var mask in masks)
        {
            sb.Clear();
            sb.Append(prefixStr);
            AppendWhereClause(sb, whereTerms, mask);
            results[mask] = new AssembledSqlVariant(sb.ToString(), paramIndex);
        }
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
            sb.Append(')');

            // SQL Server's OUTPUT clause is positionally distinct from
            // SQLite/PG's RETURNING and MySQL's separate SELECT — it lives
            // between the column list and the VALUES clause:
            //   INSERT INTO tbl (cols) OUTPUT INSERTED.[Id] VALUES (params)
            // Putting it after the VALUES clause produces "Incorrect syntax
            // near 'OUTPUT'" at runtime.
            if (dialect == SqlDialect.SqlServer && insertInfo?.IdentityColumnName != null)
            {
                sb.Append(" OUTPUT INSERTED.");
                sb.Append(SqlFormatting.QuoteIdentifier(dialect, insertInfo.IdentityColumnName));
            }

            sb.Append(" VALUES (");
            for (int i = 0; i < plan.InsertColumns.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(SqlFormatting.FormatParameter(dialect, plan.InsertColumns[i].ParameterIndex));
            }
            sb.Append(')');
        }

        // RETURNING / LAST_INSERT_ID suffix for non-SqlServer dialects.
        // SqlServer was handled inline above.
        if (insertInfo?.IdentityColumnName != null)
        {
            switch (dialect)
            {
                case SqlDialect.SQLite:
                case SqlDialect.PostgreSQL:
                    sb.Append(" RETURNING ");
                    sb.Append(SqlFormatting.QuoteIdentifier(dialect, insertInfo.IdentityColumnName));
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
    /// <remarks>
    /// SQL Server's <c>OUTPUT INSERTED.[Id]</c> clause must precede <c>VALUES</c>,
    /// so for that dialect we fold the OUTPUT into the prefix and skip the
    /// trailing returning suffix. Other dialects still emit RETURNING /
    /// LAST_INSERT_ID() as a suffix appended after the row tuples.
    /// </remarks>
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
            sb.Append(')');

            if (dialect == SqlDialect.SqlServer && insertInfo?.IdentityColumnName != null)
            {
                sb.Append(" OUTPUT INSERTED.");
                sb.Append(SqlFormatting.QuoteIdentifier(dialect, insertInfo.IdentityColumnName));
            }

            sb.Append(" VALUES ");
        }

        // The SQL prefix ends here — runtime will append (param, param), (param, param), ...
        // Return with 0 parameter count since params are runtime-determined
        return new AssembledSqlVariant(sb.ToString(), 0);
    }

    #region Helpers

    /// <summary>
    /// Pre-renders each WHERE term's SQL string with its pre-computed parameter offset.
    /// Trivial-true conditions are excluded. Parameter offsets are computed from ALL terms
    /// (active + inactive) to match the carrier's GlobalIndex assignment.
    /// </summary>
    private static List<(WhereTerm Term, string Rendered)> PreRenderWhereTerms(
        IReadOnlyList<WhereTerm> terms, SqlDialect dialect, int startParamOffset)
    {
        var result = new List<(WhereTerm, string)>();
        var paramOffset = startParamOffset;
        foreach (var w in terms)
        {
            var termParamCount = CountParameters(w.Condition);
            if (!IsTrivialTrueCondition(w.Condition))
            {
                var rendered = RenderWhereCondition(w.Condition, dialect, paramOffset);
                result.Add((w, rendered));
            }
            paramOffset += termParamCount;
        }
        return result;
    }

    /// <summary>
    /// Pre-renders each ORDER BY term's SQL string with its pre-computed parameter offset.
    /// Parameter offsets are computed from ALL terms to match the carrier's GlobalIndex assignment.
    /// </summary>
    private static List<(OrderTerm Term, string Rendered)> PreRenderOrderByTerms(
        IReadOnlyList<OrderTerm> terms, SqlDialect dialect, int startParamOffset)
    {
        var result = new List<(OrderTerm, string)>();
        var paramOffset = startParamOffset;
        foreach (var o in terms)
        {
            var termParamCount = CountParameters(o.Expression);
            var rendered = SqlExprRenderer.Render(o.Expression, dialect, paramOffset);
            result.Add((o, rendered + (o.IsDescending ? " DESC" : " ASC")));
            paramOffset += termParamCount;
        }
        return result;
    }

    /// <summary>
    /// Appends a WHERE clause to <paramref name="sb"/> from pre-rendered term strings,
    /// selecting only active terms for the given mask. Appends nothing if no terms are active.
    /// </summary>
    private static void AppendWhereClause(
        StringBuilder sb, List<(WhereTerm Term, string Rendered)> preRendered, int mask)
    {
        // Count active terms first to determine parenthesization without an intermediate list.
        int activeCount = 0;
        foreach (var (term, _) in preRendered)
        {
            if (term.BitIndex == null || (mask & (1 << term.BitIndex.Value)) != 0)
                activeCount++;
        }
        if (activeCount == 0) return;

        sb.Append(" WHERE ");
        int written = 0;
        foreach (var (term, rendered) in preRendered)
        {
            if (term.BitIndex == null || (mask & (1 << term.BitIndex.Value)) != 0)
            {
                if (written > 0) sb.Append(" AND ");
                if (activeCount > 1) sb.Append('(');
                sb.Append(rendered);
                if (activeCount > 1) sb.Append(')');
                written++;
            }
        }
    }

    /// <summary>
    /// Appends an ORDER BY clause to <paramref name="sb"/> from pre-rendered term strings,
    /// selecting only active terms for the given mask.
    /// Returns true if any ORDER BY terms were appended.
    /// </summary>
    private static bool AppendOrderByClause(
        StringBuilder sb, List<(OrderTerm Term, string Rendered)> preRendered, int mask)
    {
        bool first = true;
        foreach (var (term, rendered) in preRendered)
        {
            if (term.BitIndex == null || (mask & (1 << term.BitIndex.Value)) != 0)
            {
                if (first)
                {
                    sb.Append(" ORDER BY ");
                    first = false;
                }
                else
                {
                    sb.Append(", ");
                }
                sb.Append(rendered);
            }
        }
        return !first;
    }

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

    private static void AppendSelectColumns(StringBuilder sb, SqlDialect dialect, IReadOnlyList<ProjectedColumn> columns, int paramOffset = 0)
    {
        for (int i = 0; i < columns.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            var col = columns[i];
            AppendProjectionColumnSql(sb, col, dialect, paramOffset);
            // Aggregates carry an explicit user-given alias (e.g., "Item2") that the flat
            // path emits as `AS "Item2"`. Non-aggregate columns flow through unaliased.
            if (col.IsAggregateFunction && !string.IsNullOrEmpty(col.Alias))
            {
                sb.Append(" AS ");
                sb.Append(SqlFormatting.QuoteIdentifier(dialect, col.Alias!));
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

    /// <summary>
    /// Appends <c>FROM &lt;table&gt; [AS &quot;t0&quot;]</c> followed by all explicit and implicit
    /// JOINs to <paramref name="sb"/>, advancing <paramref name="paramIndex"/> past any
    /// JOIN-ON parameters. Shared by the flat path (<see cref="RenderSelectSql"/>) and
    /// the wrap path (<see cref="RenderSelectSqlWithDistinctOrderByWrap"/>).
    /// </summary>
    private static void AppendFromAndJoinsForPlan(
        StringBuilder sb, QueryPlan plan, SqlDialect dialect, ref int paramIndex)
    {
        sb.Append(" FROM ");
        AppendTableRef(sb, dialect, plan.PrimaryTable);
        if (plan.Joins.Count > 0 || plan.ImplicitJoins.Count > 0)
        {
            sb.Append(" AS ");
            sb.Append(SqlFormatting.QuoteIdentifier(dialect, "t0"));
        }
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
    }

    /// <summary>
    /// Appends a WHERE clause for the given mask, with mask-aware parameter offset
    /// accounting (skipped conditional terms still consume their carrier parameter slots).
    /// Advances <paramref name="paramIndex"/> by the total parameters across ALL terms,
    /// active or not. Emits nothing if no terms are active or all active terms are
    /// trivial-true. Shared by flat and wrap paths.
    /// </summary>
    private static void AppendWhereForMask(
        StringBuilder sb, IReadOnlyList<WhereTerm> terms, int mask, SqlDialect dialect, ref int paramIndex)
    {
        var activeWhereSet = new HashSet<WhereTerm>(GetActiveTerms(terms, mask));
        var nonTrivialActive = new List<(WhereTerm Term, int ParamOffset)>();
        var whereParamOffset = paramIndex;
        foreach (var w in terms)
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
    }

    /// <summary>
    /// Appends GROUP BY and HAVING clauses if present, advancing
    /// <paramref name="paramIndex"/> past any contained parameters. Shared by flat and
    /// wrap paths.
    /// </summary>
    private static void AppendGroupByAndHaving(
        StringBuilder sb, QueryPlan plan, SqlDialect dialect, ref int paramIndex)
    {
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
    }

    /// <summary>
    /// Appends a single projection column's SQL reference (without an AS-alias suffix) to
    /// <paramref name="sb"/>. Aggregate columns render their <see cref="ProjectedColumn.SqlExpression"/>
    /// template (which may contain <c>{N}</c> parameter placeholders resolved against
    /// <paramref name="paramOffset"/>); other columns render <c>"alias"."col"</c> or just
    /// <c>"col"</c>. Shared by detection (<see cref="RenderProjectionColumnRef"/>) and the
    /// wrap's inner-projection emission so both paths produce byte-identical output.
    /// </summary>
    private static void AppendProjectionColumnSql(
        StringBuilder sb, ProjectedColumn col, SqlDialect dialect, int paramOffset)
    {
        if (col.IsAggregateFunction && !string.IsNullOrEmpty(col.SqlExpression))
        {
            sb.Append(SqlFormatting.QuoteSqlExpression(col.SqlExpression!, dialect, paramOffset));
            return;
        }
        // The placeholder analysis path for non-joined CTE post-Select chains assigns
        // an empty-string TableAlias (per-param lookup is built with `Alias: ""` to
        // mean "no alias"); empty-string must produce no prefix here, otherwise the
        // emitted SQL contains a literal `""."col"` that fails parse.
        if (!string.IsNullOrEmpty(col.TableAlias))
        {
            sb.Append(SqlFormatting.QuoteIdentifier(dialect, col.TableAlias!));
            sb.Append('.');
        }
        sb.Append(SqlFormatting.QuoteIdentifier(dialect, col.ColumnName));
    }

    /// <summary>
    /// Renders a single projection column's SQL reference string (without an AS-alias
    /// suffix). Used for DISTINCT + ORDER BY wrap detection: the rendered ORDER BY
    /// expression is compared against the set of rendered projection-column references
    /// via string equality. Wraps <see cref="AppendProjectionColumnSql"/> with
    /// <paramref name="paramOffset"/>=0 — projection-clause params are rendered with
    /// absolute global indices via <c>{N}</c> substitution rather than the running
    /// paramIndex, so the comparison string doesn't depend on the chain's current offset.
    /// </summary>
    private static string RenderProjectionColumnRef(ProjectedColumn col, SqlDialect dialect)
    {
        var sb = new StringBuilder();
        AppendProjectionColumnSql(sb, col, dialect, paramOffset: 0);
        return sb.ToString();
    }

    /// <summary>
    /// Returns the alias to emit for an inner-projection column inside the wrap. Aggregate
    /// columns with an existing alias keep it (e.g., tuple element names like "Item2");
    /// non-aggregate columns use the projection's <see cref="ProjectedColumn.PropertyName"/>
    /// (e.g., "UserName") for readable manifest/diagnostics output. Falls back to
    /// <c>c{index}</c> only when neither is available — projection columns in valid Quarry
    /// chains always carry one of these.
    /// </summary>
    private static string GetInnerProjectionAlias(ProjectedColumn col, int index)
    {
        if (col.IsAggregateFunction && !string.IsNullOrEmpty(col.Alias))
            return col.Alias!;
        if (!string.IsNullOrEmpty(col.PropertyName))
            return col.PropertyName;
        return "c" + index;
    }

    /// <summary>
    /// Returns true when this mask emits SQL that combines DISTINCT with an ORDER BY whose
    /// expression is not in the SELECT projection — the construct PG rejects with 42P10
    /// and that SQL Server rejects under standard rules. SQLite and MySQL tolerate the
    /// flat form but with implementation-defined row selection. The wrap restructures the
    /// query so the ORDER BY columns are part of the inner SELECT list.
    /// </summary>
    /// <remarks>
    /// Empty projection (<see cref="SelectProjection.Columns"/> count is 0) is treated as
    /// "no wrap": the wrap shape requires concrete columns to alias and project through
    /// the derived table. Quarry's analyzer always populates <see cref="SelectProjection.Columns"/>
    /// for valid SELECT chains — explicit <c>Select(...)</c> populates it from the
    /// projection lambda, and chains without an explicit Select populate it from the
    /// entity's columns via the identity-projection path. So this guard is defensive
    /// against malformed plans rather than a routinely-hit branch.
    /// </remarks>
    private static bool NeedsDistinctOrderByWrap(QueryPlan plan, int mask, SqlDialect dialect)
    {
        if (!plan.IsDistinct) return false;
        if (plan.SetOperations.Count > 0) return false;
        if (plan.Projection == null || plan.Projection.Columns.Count == 0) return false;
        if (plan.OrderTerms.Count == 0) return false;

        var hasActive = false;
        foreach (var t in plan.OrderTerms)
        {
            if (t.BitIndex == null || (mask & (1 << t.BitIndex.Value)) != 0)
            {
                hasActive = true;
                break;
            }
        }
        if (!hasActive) return false;

        var projColumnSqls = new HashSet<string>();
        foreach (var c in plan.Projection.Columns)
            projColumnSqls.Add(RenderProjectionColumnRef(c, dialect));

        foreach (var t in plan.OrderTerms)
        {
            if (t.BitIndex != null && (mask & (1 << t.BitIndex.Value)) == 0) continue;
            var rendered = SqlExprRenderer.Render(t.Expression, dialect, parameterBaseIndex: 0);
            if (!projColumnSqls.Contains(rendered)) return true;
        }
        return false;
    }

    /// <summary>
    /// Conservative variant of <see cref="NeedsDistinctOrderByWrap"/> that ignores the
    /// mask. Returns true if any ORDER BY term's expression is non-projected — used by
    /// the batch dispatch in <see cref="Assemble"/> to decide whether to bypass the
    /// batch fast path.
    /// </summary>
    private static bool MayNeedDistinctOrderByWrap(QueryPlan plan, SqlDialect dialect)
    {
        if (!plan.IsDistinct) return false;
        if (plan.SetOperations.Count > 0) return false;
        if (plan.Projection == null || plan.Projection.Columns.Count == 0) return false;
        if (plan.OrderTerms.Count == 0) return false;

        var projColumnSqls = new HashSet<string>();
        foreach (var c in plan.Projection.Columns)
            projColumnSqls.Add(RenderProjectionColumnRef(c, dialect));

        foreach (var t in plan.OrderTerms)
        {
            var rendered = SqlExprRenderer.Render(t.Expression, dialect, parameterBaseIndex: 0);
            if (!projColumnSqls.Contains(rendered)) return true;
        }
        return false;
    }

    /// <summary>
    /// Renders a SELECT with DISTINCT + ORDER BY-on-non-projected-column as a derived-table
    /// wrap. Inner SELECT carries DISTINCT, the original projection (with explicit aliases),
    /// and the ORDER BY expressions that aren't in the projection (aliased <c>__o{i}</c>).
    /// Outer SELECT projects the original columns from the inner aliases and applies the
    /// ORDER BY (referencing inner aliases) plus pagination. CTE prefix is emitted at the
    /// outer level. Set operations are not supported here — the caller's
    /// <see cref="NeedsDistinctOrderByWrap"/> already excludes them.
    /// </summary>
    private static AssembledSqlVariant RenderSelectSqlWithDistinctOrderByWrap(
        QueryPlan plan, int mask, SqlDialect dialect, int paramBaseOffset)
    {
        var sb = new StringBuilder();
        var paramIndex = paramBaseOffset;

        // CTE prefix (same logic as RenderSelectSql)
        if (plan.CteDefinitions.Count > 0)
        {
            sb.Append("WITH ");
            for (int i = 0; i < plan.CteDefinitions.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                var cte = plan.CteDefinitions[i];
                sb.Append(SqlFormatting.QuoteIdentifier(dialect, cte.Name));
                sb.Append(" AS (");
                if (cte.InnerPlan != null)
                {
                    System.Diagnostics.Debug.Assert(
                        cte.InnerPlan.PossibleMasks.Count <= 1,
                        $"CTE inner chain '{cte.Name}' has {cte.InnerPlan.PossibleMasks.Count} mask variants; placeholder rebasing only handles mask=0.");
                    var rebased = RenderSelectSql(cte.InnerPlan, mask: 0, dialect, paramBaseOffset: cte.ParameterOffset);
                    sb.Append(rebased.Sql);
                }
                else
                {
                    sb.Append(cte.InnerSql);
                }
                sb.Append(')');
                paramIndex += cte.InnerParameters.Count;
            }
            sb.Append(' ');
        }

        // ParamIndex bookkeeping: chain-order parameter assignment in ChainAnalyzer means
        // plan.Parameters is ordered by CHAIN order, not SQL order — and Quarry chains
        // always have WHERE/HAVING/etc. before ORDER BY in chain order (OrderBy is on
        // IQueryBuilder<T>, requires a transitioning clause first). Therefore an ORDER BY
        // param's global slot is AFTER body-clause params (JOIN/WHERE/GROUP/HAVING).
        // The wrap renders ORDER BY exprs INSIDE the inner SELECT (textually before the
        // body), so each non-projected ORDER BY expression must be pre-rendered at its
        // post-body global offset. Inactive and projected ORDER BY terms still reserve
        // their slots in the global numbering (mirroring the flat path).
        var bodyParamCount = 0;
        foreach (var join in plan.Joins)
            if (join.OnCondition != null) bodyParamCount += CountParameters(join.OnCondition);
        foreach (var w in plan.WhereTerms)
            bodyParamCount += CountParameters(w.Condition);
        foreach (var g in plan.GroupByExprs)
            bodyParamCount += CountParameters(g);
        foreach (var h in plan.HavingExprs)
            bodyParamCount += CountParameters(h);

        // Walk plan.OrderTerms once: for each term decide active/inactive, projected/non-projected,
        // and record its pre-allocated global offset.
        var projColIndexBySql = new Dictionary<string, int>();
        for (int i = 0; i < plan.Projection.Columns.Count; i++)
            projColIndexBySql[RenderProjectionColumnRef(plan.Projection.Columns[i], dialect)] = i;

        var extraOrderRendered = new List<(string Sql, string Alias)>();
        var outerOrderByEntries = new List<(string Alias, bool IsDescending)>();
        var nextO = 0;
        var perTermOffset = paramIndex + bodyParamCount;
        for (int i = 0; i < plan.OrderTerms.Count; i++)
        {
            var term = plan.OrderTerms[i];
            var termParamCount = CountParameters(term.Expression);
            bool isActive = term.BitIndex == null || (mask & (1 << term.BitIndex.Value)) != 0;
            if (isActive)
            {
                var renderedRef = SqlExprRenderer.Render(term.Expression, dialect, parameterBaseIndex: 0);
                if (projColIndexBySql.TryGetValue(renderedRef, out var projIdx))
                {
                    outerOrderByEntries.Add((GetInnerProjectionAlias(plan.Projection.Columns[projIdx], projIdx), term.IsDescending));
                }
                else
                {
                    var alias = "_o" + nextO++;
                    extraOrderRendered.Add((SqlExprRenderer.Render(term.Expression, dialect, perTermOffset), alias));
                    outerOrderByEntries.Add((alias, term.IsDescending));
                }
            }
            perTermOffset += termParamCount;
        }
        var paramIndexAfterOrderBy = perTermOffset;

        // Outer SELECT: project from inner aliases.
        sb.Append("SELECT ");
        for (int i = 0; i < plan.Projection.Columns.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(SqlFormatting.QuoteIdentifier(dialect, "d"));
            sb.Append('.');
            sb.Append(SqlFormatting.QuoteIdentifier(dialect, GetInnerProjectionAlias(plan.Projection.Columns[i], i)));
        }
        sb.Append(" FROM (");

        // Inner SELECT DISTINCT: original projection + extra ORDER BY columns.
        // AppendProjectionColumnSql is shared with RenderProjectionColumnRef so the inner
        // emission and the detection comparison cannot drift apart.
        sb.Append("SELECT DISTINCT ");
        for (int i = 0; i < plan.Projection.Columns.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            var col = plan.Projection.Columns[i];
            AppendProjectionColumnSql(sb, col, dialect, paramIndex);
            sb.Append(" AS ");
            sb.Append(SqlFormatting.QuoteIdentifier(dialect, GetInnerProjectionAlias(col, i)));
        }
        foreach (var (renderedExpr, alias) in extraOrderRendered)
        {
            sb.Append(", ");
            sb.Append(renderedExpr);
            sb.Append(" AS ");
            sb.Append(SqlFormatting.QuoteIdentifier(dialect, alias));
        }

        AppendFromAndJoinsForPlan(sb, plan, dialect, ref paramIndex);
        AppendWhereForMask(sb, plan.WhereTerms, mask, dialect, ref paramIndex);
        AppendGroupByAndHaving(sb, plan, dialect, ref paramIndex);

        // Close inner, name the derived table, then outer ORDER BY + pagination.
        // paramIndex is post-HAVING; advance past pre-allocated ORDER BY slots so
        // pagination claims the next available slot range.
        sb.Append(") AS ");
        sb.Append(SqlFormatting.QuoteIdentifier(dialect, "d"));

        if (outerOrderByEntries.Count > 0)
        {
            sb.Append(" ORDER BY ");
            for (int i = 0; i < outerOrderByEntries.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(SqlFormatting.QuoteIdentifier(dialect, "d"));
                sb.Append('.');
                sb.Append(SqlFormatting.QuoteIdentifier(dialect, outerOrderByEntries[i].Alias));
                sb.Append(outerOrderByEntries[i].IsDescending ? " DESC" : " ASC");
            }
        }

        paramIndex = paramIndexAfterOrderBy;
        AppendPagination(sb, plan, dialect, hasOrderBy: outerOrderByEntries.Count > 0, ref paramIndex);

        var totalPlanParams = paramBaseOffset + plan.Parameters.Count;
        paramIndex = Math.Max(paramIndex, totalPlanParams);

        return new AssembledSqlVariant(sb.ToString(), paramIndex);
    }

    #endregion
}
