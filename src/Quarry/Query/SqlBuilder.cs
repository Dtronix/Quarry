using System.Text;
using Quarry.Shared.Sql;

namespace Quarry.Internal;

/// <summary>
/// Generates SQL from query state.
/// </summary>
internal static class SqlBuilder
{
    [ThreadStatic]
    private static StringBuilder? t_cachedSb;

    private static StringBuilder AcquireStringBuilder()
    {
        var sb = t_cachedSb ?? new StringBuilder(256);
        t_cachedSb = null;
        sb.Clear();
        return sb;
    }

    private static string ToStringAndRelease(StringBuilder sb)
    {
        var result = sb.ToString();
        if (sb.Capacity <= 1024) // don't cache oversized buffers
            t_cachedSb = sb;
        return result;
    }

    /// <summary>
    /// Builds the complete SELECT SQL from the query state.
    /// </summary>
    public static string BuildSelectSql(QueryState state)
    {
        var sb = AcquireStringBuilder();
        var dialect = state.Dialect;

        // SELECT clause
        sb.Append("SELECT ");
        if (state.IsDistinct)
        {
            sb.Append("DISTINCT ");
        }

        if (state.HasSelect)
        {
            var first = true;
            foreach (var col in state.SelectColumns)
            {
                if (!first) sb.Append(", ");
                first = false;
                sb.Append(IsSimpleIdentifier(col) ? SqlFormatting.QuoteIdentifier(dialect, col) : col);
            }
        }
        else
        {
            sb.Append('*');
        }

        // FROM clause
        sb.Append(" FROM ");
        sb.Append(SqlFormatting.FormatTableName(dialect, state.TableName, state.SchemaName));
        if (state.FromTableAlias != null)
        {
            sb.Append(" AS ");
            sb.Append(SqlFormatting.QuoteIdentifier(dialect, state.FromTableAlias));
        }

        // JOIN clauses
        foreach (var join in state.JoinClauses)
        {
            sb.Append(' ');
            sb.Append(GetJoinKeyword(join.Kind));
            sb.Append(' ');
            sb.Append(SqlFormatting.FormatTableName(dialect, join.JoinedTableName, join.JoinedSchemaName));
            if (join.TableAlias != null)
            {
                sb.Append(" AS ");
                sb.Append(SqlFormatting.QuoteIdentifier(dialect, join.TableAlias));
            }
            sb.Append(" ON ");
            sb.Append(join.OnConditionSql);
        }

        // WHERE clause
        if (state.WhereConditions.Length > 0)
        {
            sb.Append(" WHERE ");
            SqlClauseJoining.AppendAndJoinedConditions(sb, state.WhereConditions);
        }

        // GROUP BY clause
        if (state.GroupByColumns.Length > 0)
        {
            sb.Append(" GROUP BY ");
            sb.Append(string.Join(", ", state.GroupByColumns));
        }

        // HAVING clause
        if (state.HavingConditions.Length > 0)
        {
            sb.Append(" HAVING ");
            SqlClauseJoining.AppendAndJoinedConditions(sb, state.HavingConditions);
        }

        // ORDER BY clause
        if (state.OrderByClauses.Length > 0)
        {
            sb.Append(" ORDER BY ");
            var first = true;
            foreach (var orderBy in state.OrderByClauses)
            {
                if (!first) sb.Append(", ");
                first = false;

                sb.Append(orderBy.Column);
                sb.Append(orderBy.Direction == Direction.Ascending ? " ASC" : " DESC");
            }
        }

        // Pagination
        if (state.Limit.HasValue || state.Offset.HasValue)
        {
            // For SQL Server, ORDER BY is required for OFFSET/FETCH
            if (dialect == SqlDialect.SqlServer && state.OrderByClauses.Length == 0)
            {
                sb.Append(" ORDER BY (SELECT NULL)");
            }

            var pagination = SqlFormatting.FormatPagination(dialect, state.Limit, state.Offset);
            if (!string.IsNullOrEmpty(pagination))
            {
                sb.Append(' ');
                sb.Append(pagination);
            }
        }

        return ToStringAndRelease(sb);
    }

    /// <summary>
    /// Builds a SELECT SQL string from pre-quoted fragments stored on the state (tier 2).
    /// </summary>
    public static string BuildFromPrequotedFragments(QueryState state)
    {
        var sb = AcquireStringBuilder();

        // SELECT clause (pre-quoted fragment includes "SELECT [DISTINCT] columns")
        if (state.PrebuiltSelectFragment != null)
        {
            sb.Append(state.PrebuiltSelectFragment);
        }
        else
        {
            sb.Append("SELECT *");
        }

        // FROM clause (pre-quoted fragment includes "FROM table [AS alias]")
        if (state.PrebuiltFromFragment != null)
        {
            sb.Append(' ');
            sb.Append(state.PrebuiltFromFragment);
        }

        // JOIN clauses (pre-quoted fragment includes all joins)
        if (state.PrebuiltJoinFragment != null)
        {
            sb.Append(' ');
            sb.Append(state.PrebuiltJoinFragment);
        }

        // WHERE clause (assembled from WhereConditions, which are already pre-quoted)
        if (state.WhereConditions.Length > 0)
        {
            sb.Append(" WHERE ");
            SqlClauseJoining.AppendAndJoinedConditions(sb, state.WhereConditions);
        }

        // GROUP BY clause
        if (state.PrebuiltGroupByFragment != null)
        {
            sb.Append(' ');
            sb.Append(state.PrebuiltGroupByFragment);
        }

        // HAVING clause
        if (state.PrebuiltHavingFragment != null)
        {
            sb.Append(' ');
            sb.Append(state.PrebuiltHavingFragment);
        }

        // ORDER BY clause
        if (state.PrebuiltOrderByFragment != null)
        {
            sb.Append(' ');
            sb.Append(state.PrebuiltOrderByFragment);
        }

        // Pagination
        if (state.PrebuiltPaginationFragment != null)
        {
            sb.Append(' ');
            sb.Append(state.PrebuiltPaginationFragment);
        }

        return ToStringAndRelease(sb);
    }

    /// <summary>
    /// Gets the SQL keyword for a join kind.
    /// </summary>
    private static string GetJoinKeyword(JoinKind kind)
    {
        return kind switch
        {
            JoinKind.Inner => "INNER JOIN",
            JoinKind.Left => "LEFT JOIN",
            JoinKind.Right => "RIGHT JOIN",
            _ => "JOIN"
        };
    }

    /// <summary>
    /// Determines if a string is a simple SQL identifier (column name).
    /// </summary>
    private static bool IsSimpleIdentifier(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        // Already quoted identifiers (double quotes, backticks, brackets)
        if ((value.StartsWith("\"") && value.EndsWith("\"")) ||
            (value.StartsWith("`") && value.EndsWith("`")) ||
            (value.StartsWith("[") && value.EndsWith("]")))
            return false;

        // SQL expressions contain these characters
        foreach (var c in value)
        {
            if (c != '_' && !char.IsLetterOrDigit(c))
                return false;
        }

        return true;
    }
}
