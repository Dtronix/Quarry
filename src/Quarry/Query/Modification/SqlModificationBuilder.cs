using System.Text;
using Quarry.Internal;
using Quarry.Shared.Sql;

namespace Quarry;

/// <summary>
/// Static utility for building DML SQL statements from modification state.
/// </summary>
internal static class SqlModificationBuilder
{
    /// <summary>
    /// Builds an INSERT SQL statement from the given state.
    /// </summary>
    public static string BuildInsertSql(InsertState state, int rowCount)
    {
        var dk = state.Dialect;
        var sb = new StringBuilder();

        // INSERT INTO
        sb.Append("INSERT INTO ");
        sb.Append(SqlFormatting.FormatTableName(dk, state.TableName, state.SchemaName));

        // Column list
        if (state.HasColumns)
        {
            sb.Append(" (");
            sb.Append(string.Join(", ", state.Columns));
            sb.Append(')');
        }

        // VALUES clause(s)
        if (state.Rows.Count > 0)
        {
            // Use pre-built rows with parameter indices
            sb.Append(" VALUES ");
            for (int rowIdx = 0; rowIdx < state.Rows.Count; rowIdx++)
            {
                if (rowIdx > 0) sb.Append(", ");
                sb.Append('(');
                var row = state.Rows[rowIdx];
                for (int colIdx = 0; colIdx < row.Count; colIdx++)
                {
                    if (colIdx > 0) sb.Append(", ");
                    sb.Append(SqlFormatting.FormatParameter(dk, row[colIdx]));
                }
                sb.Append(')');
            }
        }
        else if (rowCount > 0 && state.HasColumns)
        {
            // Generate placeholder VALUES for ToDiagnostics preview
            sb.Append(" VALUES ");
            int paramIdx = 0;
            for (int rowIdx = 0; rowIdx < rowCount; rowIdx++)
            {
                if (rowIdx > 0) sb.Append(", ");
                sb.Append('(');
                for (int colIdx = 0; colIdx < state.Columns.Count; colIdx++)
                {
                    if (colIdx > 0) sb.Append(", ");
                    sb.Append(SqlFormatting.FormatParameter(dk, paramIdx++));
                }
                sb.Append(')');
            }
        }

        // RETURNING clause for identity retrieval
        if (!string.IsNullOrEmpty(state.IdentityColumn))
        {
            var returningClause = SqlFormatting.FormatReturningClause(dk, state.IdentityColumn);
            if (!string.IsNullOrEmpty(returningClause))
            {
                sb.Append(' ');
                sb.Append(returningClause);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds an UPDATE SQL statement from the given state.
    /// </summary>
    public static string BuildUpdateSql(UpdateState state)
    {
        var dk = state.Dialect;
        var sb = new StringBuilder();

        // UPDATE table
        sb.Append("UPDATE ");
        sb.Append(SqlFormatting.FormatTableName(dk, state.TableName, state.SchemaName));

        // SET clause
        if (state.HasSetClauses)
        {
            sb.Append(" SET ");
            for (int i = 0; i < state.SetClauses.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                var setClause = state.SetClauses[i];
                sb.Append(setClause.ColumnSql);
                sb.Append(" = ");
                sb.Append(SqlFormatting.FormatParameter(dk, setClause.ParameterIndex));
            }
        }

        // WHERE clause
        if (state.HasWhereConditions)
        {
            sb.Append(" WHERE ");
            sb.Append(string.Join(" AND ", state.WhereConditions));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds a DELETE SQL statement from the given state.
    /// </summary>
    public static string BuildDeleteSql(DeleteState state)
    {
        var dk = state.Dialect;
        var sb = new StringBuilder();

        // DELETE FROM table
        sb.Append("DELETE FROM ");
        sb.Append(SqlFormatting.FormatTableName(dk, state.TableName, state.SchemaName));

        // WHERE clause
        if (state.HasWhereConditions)
        {
            sb.Append(" WHERE ");
            sb.Append(string.Join(" AND ", state.WhereConditions));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Gets the SQL query to retrieve the last inserted identity value.
    /// </summary>
    public static string? GetLastInsertIdQuery(SqlDialect dialect)
    {
        return SqlFormatting.GetLastInsertIdQuery(dialect);
    }
}
