using System.Text;
using Quarry.Shared.Sql;

namespace Quarry.Internal;

/// <summary>
/// Builds batch INSERT SQL at runtime from compile-time templates.
/// Called by generated terminal interceptors for batch insert chains.
/// </summary>
internal static class BatchInsertSqlBuilder
{
    /// <summary>
    /// Builds a complete batch INSERT SQL string from a pre-assembled prefix,
    /// row template, and entity count.
    /// </summary>
    /// <param name="sqlPrefix">
    /// The compile-time prefix including INSERT INTO table (columns) VALUES.
    /// e.g., <c>INSERT INTO "users" ("UserName", "Password") VALUES </c>
    /// </param>
    /// <param name="rowTemplate">
    /// The per-row parameter template with relative column indices.
    /// e.g., <c>(@p{0}, @p{1})</c> for SQLite/SqlServer,
    ///       <c>(${0}, ${1})</c> for PostgreSQL,
    ///       <c>(?, ?)</c> for MySQL.
    /// </param>
    /// <param name="entityCount">Number of entities to insert.</param>
    /// <param name="columnsPerRow">Number of columns per row.</param>
    /// <param name="dialect">SQL dialect for parameter formatting.</param>
    /// <param name="returningSuffix">
    /// Optional RETURNING/OUTPUT suffix for identity retrieval.
    /// e.g., <c> RETURNING "UserId"</c>
    /// </param>
    public static string Build(
        string sqlPrefix,
        int entityCount,
        int columnsPerRow,
        SqlDialect dialect,
        string? returningSuffix)
    {
        if (entityCount == 0)
            return sqlPrefix;

        var sb = new StringBuilder(sqlPrefix.Length + entityCount * (columnsPerRow * 5 + 4) + (returningSuffix?.Length ?? 0));
        sb.Append(sqlPrefix);

        for (int row = 0; row < entityCount; row++)
        {
            if (row > 0) sb.Append(", ");
            sb.Append('(');
            for (int col = 0; col < columnsPerRow; col++)
            {
                if (col > 0) sb.Append(", ");
                sb.Append(SqlFormatting.FormatParameter(dialect, row * columnsPerRow + col));
            }
            sb.Append(')');
        }

        if (returningSuffix != null)
            sb.Append(returningSuffix);

        return sb.ToString();
    }
}
