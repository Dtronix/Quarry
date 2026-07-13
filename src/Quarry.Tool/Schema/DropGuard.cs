using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using Quarry.Shared.Migration;
using Quarry.Shared.Sql;

namespace Quarry.Tool.Schema;

/// <summary>
/// Guards against silent data loss when a diff is computed against a live database. A
/// <see cref="MigrationStepType.DropColumn"/> or <see cref="MigrationStepType.DropTable"/>
/// step that targets a populated object is a data-loss operation; the CLI refuses to emit
/// such a migration unless <c>--allow-data-loss</c> is passed. This is the safety net for a
/// rename that the differ failed to detect and degraded to drop+add.
/// </summary>
internal static class DropGuard
{
    /// <summary>A destructive step that would drop data from a populated object.</summary>
    public sealed record Violation(MigrationStepType StepType, string Table, string? Column, long RowCount)
    {
        public string Describe() => Column == null
            ? $"DROP TABLE {Table} ({RowCount} row(s))"
            : $"DROP COLUMN {Table}.{Column} ({RowCount} non-null value(s))";
    }

    /// <summary>
    /// Returns the destructive steps in <paramref name="steps"/> that would lose data from a
    /// populated object in the connected database. An empty list means no data would be lost.
    /// </summary>
    public static async Task<IReadOnlyList<Violation>> FindViolationsAsync(
        DbConnection connection, SqlDialect dialect, IReadOnlyList<MigrationStep> steps)
    {
        var violations = new List<Violation>();

        foreach (var step in steps)
        {
            switch (step.StepType)
            {
                case MigrationStepType.DropTable:
                {
                    var count = await CountAsync(connection, dialect, step.TableName, step.SchemaName, null);
                    if (count > 0)
                        violations.Add(new Violation(step.StepType, step.TableName, null, count));
                    break;
                }
                case MigrationStepType.DropColumn:
                {
                    var count = await CountAsync(connection, dialect, step.TableName, step.SchemaName, step.ColumnName);
                    if (count > 0)
                        violations.Add(new Violation(step.StepType, step.TableName, step.ColumnName, count));
                    break;
                }
            }
        }

        return violations;
    }

    private static async Task<long> CountAsync(
        DbConnection connection, SqlDialect dialect, string table, string? schema, string? column)
    {
        using var cmd = connection.CreateCommand();
        var target = FormatTable(dialect, schema, table);
        cmd.CommandText = column == null
            ? $"SELECT COUNT(*) FROM {target};"
            : $"SELECT COUNT(*) FROM {target} WHERE {SqlFormatting.QuoteIdentifier(dialect, column)} IS NOT NULL;";

        var result = await cmd.ExecuteScalarAsync();
        return result is null or DBNull ? 0L : Convert.ToInt64(result);
    }

    private static string FormatTable(SqlDialect dialect, string? schema, string table)
    {
        var quotedTable = SqlFormatting.QuoteIdentifier(dialect, table);
        // SQLite ignores database-schema qualification; other dialects honor it.
        return schema == null || dialect == SqlDialect.SQLite
            ? quotedTable
            : $"{SqlFormatting.QuoteIdentifier(dialect, schema)}.{quotedTable}";
    }
}
