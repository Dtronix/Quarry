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
    /// Maximum number of parameters allowed in a single batch insert statement.
    /// Most databases impose limits (SQL Server: 2100, SQLite: 999 default, MySQL: no hard limit,
    /// PostgreSQL: 65535). We use a conservative default that works across all dialects.
    /// </summary>
    internal const int MaxParameterCount = 2100;

    /// <summary>
    /// Builds a complete batch INSERT SQL string from a pre-assembled prefix
    /// and entity count.
    /// </summary>
    /// <param name="sqlPrefix">
    /// The compile-time prefix including INSERT INTO table (columns) VALUES.
    /// e.g., <c>INSERT INTO "users" ("UserName", "Password") VALUES </c>
    /// </param>
    /// <param name="entityCount">Number of entities to insert.</param>
    /// <param name="columnsPerRow">Number of columns per row.</param>
    /// <param name="dialect">SQL dialect for parameter formatting.</param>
    /// <param name="returningSuffix">
    /// Optional RETURNING/OUTPUT suffix for identity retrieval.
    /// e.g., <c> RETURNING "UserId"</c>
    /// </param>
    /// <exception cref="ArgumentException">Thrown when entityCount is zero or the total parameter count exceeds <see cref="MaxParameterCount"/>.</exception>
    public static string Build(
        string sqlPrefix,
        int entityCount,
        int columnsPerRow,
        SqlDialect dialect,
        string? returningSuffix)
    {
        if (entityCount == 0)
            throw new ArgumentException("Batch insert requires at least one entity.", nameof(entityCount));

        var totalParams = entityCount * columnsPerRow;
        if (totalParams > MaxParameterCount)
            throw new ArgumentException(
                $"Batch insert would generate {totalParams} parameters ({entityCount} entities x {columnsPerRow} columns), " +
                $"which exceeds the maximum of {MaxParameterCount}. Split the batch into smaller chunks.",
                nameof(entityCount));

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
