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
    /// <para>
    /// A normalized diff clears the schema qualifier from its steps (see
    /// <see cref="DatabaseSchemaReader.NormalizeForDiff"/>), so <paramref name="tableSchemas"/>
    /// supplies the real schema per table (from the rich live-database snapshot). Without it,
    /// a drop against a table in a non-default schema (PostgreSQL/SqlServer) would query the
    /// wrong object. Build it with <see cref="BuildTableSchemaMap"/>.
    /// </para>
    /// </summary>
    public static async Task<IReadOnlyList<Violation>> FindViolationsAsync(
        DbConnection connection, SqlDialect dialect, IReadOnlyList<MigrationStep> steps,
        IReadOnlyDictionary<string, string?>? tableSchemas = null)
    {
        var violations = new List<Violation>();

        foreach (var step in steps)
        {
            switch (step.StepType)
            {
                case MigrationStepType.DropTable:
                {
                    var schema = ResolveSchema(step, tableSchemas);
                    var count = await CountAsync(connection, dialect, step.TableName, schema, null);
                    if (count > 0)
                        violations.Add(new Violation(step.StepType, step.TableName, null, count));
                    break;
                }
                case MigrationStepType.DropColumn:
                {
                    var schema = ResolveSchema(step, tableSchemas);
                    var count = await CountAsync(connection, dialect, step.TableName, schema, step.ColumnName);
                    if (count > 0)
                        violations.Add(new Violation(step.StepType, step.TableName, step.ColumnName, count));
                    break;
                }
            }
        }

        return violations;
    }

    /// <summary>
    /// Resolves the schema to qualify a drop step's table with: the step's own schema when set,
    /// otherwise the schema recorded for that table in <paramref name="tableSchemas"/> (the live
    /// database's real schema, which normalization stripped from the step).
    /// </summary>
    internal static string? ResolveSchema(MigrationStep step, IReadOnlyDictionary<string, string?>? tableSchemas)
    {
        if (step.SchemaName != null)
            return step.SchemaName;
        if (tableSchemas != null && tableSchemas.TryGetValue(step.TableName, out var schema))
            return schema;
        return null;
    }

    /// <summary>
    /// Builds a case-insensitive table-name → schema lookup from a rich (un-normalized) snapshot,
    /// so the guard can re-qualify drop steps whose schema a normalized diff cleared.
    /// </summary>
    internal static IReadOnlyDictionary<string, string?> BuildTableSchemaMap(SchemaSnapshot liveSchema)
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in liveSchema.Tables)
            map[t.TableName] = t.SchemaName;
        return map;
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

    internal static string FormatTable(SqlDialect dialect, string? schema, string table)
    {
        var quotedTable = SqlFormatting.QuoteIdentifier(dialect, table);
        // SQLite ignores database-schema qualification; other dialects honor it.
        return schema == null || dialect == SqlDialect.SQLite
            ? quotedTable
            : $"{SqlFormatting.QuoteIdentifier(dialect, schema)}.{quotedTable}";
    }
}
