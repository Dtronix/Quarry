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
    /// Assembles an AnalyzedChain into an AssembledPlan with rendered SQL variants.
    /// </summary>
    public static AssembledPlan Assemble(AnalyzedChain chain, EntityRegistry registry)
    {
        var plan = chain.Plan;
        var executionSite = chain.ExecutionSite;
        var dialect = executionSite.Bound.Dialect;

        var sqlVariants = new Dictionary<ulong, AssembledSqlVariant>();

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
                resultTypeName: executionSite.Bound.Raw.ResultTypeName,
                dialect: dialect,
                entitySchemaNamespace: executionSite.Bound.Entity?.SchemaNamespace);
        }

        // Build SQL for each mask
        var insertInfo = executionSite.Bound.InsertInfo;
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

        return new AssembledPlan(
            plan: plan,
            sqlVariants: sqlVariants,
            readerDelegateCode: readerDelegateCode,
            maxParameterCount: maxParamCount,
            executionSite: executionSite,
            clauseSites: chain.ClauseSites,
            entityTypeName: executionSite.Bound.Raw.EntityTypeName,
            resultTypeName: executionSite.Bound.Raw.ResultTypeName,
            dialect: dialect,
            entitySchemaNamespace: executionSite.Bound.Entity?.SchemaNamespace);
    }

    /// <summary>
    /// Renders the complete SQL for a given mask value.
    /// </summary>
    private static AssembledSqlVariant RenderSqlForMask(QueryPlan plan, ulong mask, SqlDialect dialect, Models.InsertInfo? insertInfo = null)
    {
        return plan.Kind switch
        {
            QueryKind.Select => RenderSelectSql(plan, mask, dialect),
            QueryKind.Delete => RenderDeleteSql(plan, mask, dialect),
            QueryKind.Update => RenderUpdateSql(plan, mask, dialect),
            QueryKind.Insert => RenderInsertSql(plan, mask, dialect, insertInfo),
            _ => new AssembledSqlVariant("", 0)
        };
    }

    private static AssembledSqlVariant RenderSelectSql(QueryPlan plan, ulong mask, SqlDialect dialect)
    {
        var sb = new StringBuilder();
        var paramIndex = 0;

        // SELECT
        sb.Append("SELECT ");
        if (plan.IsDistinct)
            sb.Append("DISTINCT ");

        if (plan.Projection.Columns.Count > 0 && !plan.Projection.IsIdentity)
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

        // Determine if we need table aliases for joins
        string? fromAlias = null;
        if (plan.Joins.Count > 0)
        {
            fromAlias = "t0";
            sb.Append(" AS ");
            sb.Append(SqlFormatting.QuoteIdentifier(dialect, fromAlias));
        }

        // JOINs
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
            sb.Append(" ON ");

            var paramsBefore = CountParameters(join.OnCondition);
            sb.Append(SqlExprRenderer.Render(join.OnCondition, dialect, paramIndex));
            paramIndex += paramsBefore;
        }

        // WHERE (skip trivial always-true conditions like WHERE 1 / WHERE true)
        var activeWheres = GetActiveTerms(plan.WhereTerms, mask);
        var nonTrivialWheres = activeWheres.Where(w => !IsTrivialTrueCondition(w.Condition)).ToList();
        if (nonTrivialWheres.Count > 0)
        {
            sb.Append(" WHERE ");
            for (int i = 0; i < nonTrivialWheres.Count; i++)
            {
                if (i > 0) sb.Append(" AND ");
                var w = nonTrivialWheres[i];
                var paramsBefore = CountParameters(w.Condition);
                // Wrap each WHERE term in parentheses when there are multiple terms
                if (nonTrivialWheres.Count > 1) sb.Append('(');
                sb.Append(RenderWhereCondition(w.Condition, dialect, paramIndex));
                if (nonTrivialWheres.Count > 1) sb.Append(')');
                paramIndex += paramsBefore;
            }
        }

        // GROUP BY
        if (plan.GroupByExprs.Count > 0)
        {
            sb.Append(" GROUP BY ");
            for (int i = 0; i < plan.GroupByExprs.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(SqlExprRenderer.Render(plan.GroupByExprs[i], dialect, paramIndex));
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
                sb.Append(SqlExprRenderer.Render(plan.HavingExprs[i], dialect, paramIndex));
                paramIndex += paramsBefore;
            }
        }

        // ORDER BY
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

        // PAGINATION
        AppendPagination(sb, plan, dialect, activeOrders.Count > 0, ref paramIndex);

        return new AssembledSqlVariant(sb.ToString(), paramIndex);
    }

    private static AssembledSqlVariant RenderDeleteSql(QueryPlan plan, ulong mask, SqlDialect dialect)
    {
        var sb = new StringBuilder();
        var paramIndex = 0;

        sb.Append("DELETE FROM ");
        AppendTableRef(sb, dialect, plan.PrimaryTable);

        // WHERE (skip trivial always-true conditions like WHERE 1 / WHERE true)
        var activeWheres = GetActiveTerms(plan.WhereTerms, mask);
        var nonTrivialWheres = activeWheres.Where(w => !IsTrivialTrueCondition(w.Condition)).ToList();
        if (nonTrivialWheres.Count > 0)
        {
            sb.Append(" WHERE ");
            for (int i = 0; i < nonTrivialWheres.Count; i++)
            {
                if (i > 0) sb.Append(" AND ");
                var w = nonTrivialWheres[i];
                var paramsBefore = CountParameters(w.Condition);
                // Wrap each WHERE term in parentheses when there are multiple terms
                if (nonTrivialWheres.Count > 1) sb.Append('(');
                sb.Append(RenderWhereCondition(w.Condition, dialect, paramIndex));
                if (nonTrivialWheres.Count > 1) sb.Append(')');
                paramIndex += paramsBefore;
            }
        }

        return new AssembledSqlVariant(sb.ToString(), paramIndex);
    }

    private static AssembledSqlVariant RenderUpdateSql(QueryPlan plan, ulong mask, SqlDialect dialect)
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

        // WHERE (skip trivial always-true conditions like WHERE 1 / WHERE true)
        var activeWheres = GetActiveTerms(plan.WhereTerms, mask);
        var nonTrivialWheres = activeWheres.Where(w => !IsTrivialTrueCondition(w.Condition)).ToList();
        if (nonTrivialWheres.Count > 0)
        {
            sb.Append(" WHERE ");
            for (int i = 0; i < nonTrivialWheres.Count; i++)
            {
                if (i > 0) sb.Append(" AND ");
                var w = nonTrivialWheres[i];
                var paramsBefore = CountParameters(w.Condition);
                // Wrap each WHERE term in parentheses when there are multiple terms
                if (nonTrivialWheres.Count > 1) sb.Append('(');
                sb.Append(RenderWhereCondition(w.Condition, dialect, paramIndex));
                if (nonTrivialWheres.Count > 1) sb.Append(')');
                paramIndex += paramsBefore;
            }
        }

        return new AssembledSqlVariant(sb.ToString(), paramIndex);
    }

    private static AssembledSqlVariant RenderInsertSql(QueryPlan plan, ulong mask, SqlDialect dialect, Models.InsertInfo? insertInfo = null)
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
                    // MySQL uses LAST_INSERT_ID() separately
                    break;
            }
        }

        return new AssembledSqlVariant(sb.ToString(), plan.InsertColumns.Count);
    }

    #region Helpers

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
            if (col.TableAlias != null)
            {
                sb.Append(SqlFormatting.QuoteIdentifier(dialect, col.TableAlias));
                sb.Append('.');
            }
            sb.Append(SqlFormatting.QuoteIdentifier(dialect, col.ColumnName));
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

        if (pag.LiteralLimit != null || pag.LiteralOffset != null)
        {
            var pagination = SqlFormatting.FormatPagination(dialect, pag.LiteralLimit, pag.LiteralOffset);
            if (!string.IsNullOrEmpty(pagination))
            {
                sb.Append(' ');
                sb.Append(pagination);
            }
        }
        else
        {
            int? limitIdx = pag.LimitParamIndex;
            int? offsetIdx = pag.OffsetParamIndex;

            // Remap to current paramIndex if needed
            if (limitIdx != null)
            {
                limitIdx = paramIndex;
                paramIndex++;
            }
            if (offsetIdx != null)
            {
                offsetIdx = paramIndex;
                paramIndex++;
            }

            var pagination = SqlFormatting.FormatParameterizedPagination(dialect, limitIdx, offsetIdx);
            if (!string.IsNullOrEmpty(pagination))
            {
                sb.Append(' ');
                sb.Append(pagination);
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
    private static List<WhereTerm> GetActiveTerms(IReadOnlyList<WhereTerm> terms, ulong mask)
    {
        var result = new List<WhereTerm>();
        foreach (var t in terms)
        {
            if (t.BitIndex == null || (mask & (1UL << t.BitIndex.Value)) != 0)
                result.Add(t);
        }
        return result;
    }

    private static List<OrderTerm> GetActiveTerms(IReadOnlyList<OrderTerm> terms, ulong mask)
    {
        var result = new List<OrderTerm>();
        foreach (var t in terms)
        {
            if (t.BitIndex == null || (mask & (1UL << t.BitIndex.Value)) != 0)
                result.Add(t);
        }
        return result;
    }

    private static List<SetTerm> GetActiveTerms(IReadOnlyList<SetTerm> terms, ulong mask)
    {
        var result = new List<SetTerm>();
        foreach (var t in terms)
        {
            if (t.BitIndex == null || (mask & (1UL << t.BitIndex.Value)) != 0)
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
        // Strip outer parens for compound AND/OR (redundant in WHERE context)
        // and for comparisons involving subqueries (subqueries provide their own grouping)
        if (condition is BinaryOpExpr bin)
        {
            var strip = bin.Operator == SqlBinaryOperator.And
                || bin.Operator == SqlBinaryOperator.Or
                || bin.Left is SubqueryExpr
                || bin.Right is SubqueryExpr;
            if (strip)
                return SqlExprRenderer.Render(condition, dialect, paramIndex, stripOuterParens: true);
        }
        return SqlExprRenderer.Render(condition, dialect, paramIndex);
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
