#if !QUARRY_GENERATOR

using Quarry;
using Quarry.Shared.Sql;

namespace Quarry.Shared.Migration;

/// <summary>
/// Generates backup and restore SQL for destructive migration steps.
/// </summary>
internal static class BackupGenerator
{
    /// <summary>
    /// Generates backup SQL for a destructive migration step.
    /// </summary>
    /// <returns>The backup SQL statement, or null if no backup is needed.</returns>
    public static string? GenerateBackupSql(MigrationStep step, TableDef sourceDef, SqlDialect dialect)
    {
        switch (step.StepType)
        {
            case MigrationStepType.DropTable:
                return GenerateTableBackup(step.TableName, step.SchemaName, dialect);

            case MigrationStepType.DropColumn:
                if (step.ColumnName == null) return null;
                return GenerateColumnBackup(step.TableName, step.SchemaName, step.ColumnName, sourceDef, dialect);

            default:
                return null;
        }
    }

    /// <summary>
    /// Generates restore SQL for a destructive migration step.
    /// </summary>
    /// <returns>The restore SQL statement, or null if no restore is possible.</returns>
    public static string? GenerateRestoreSql(MigrationStep step, TableDef sourceDef, SqlDialect dialect)
    {
        var backupTable = GetBackupTableName(step);
        if (backupTable == null) return null;

        switch (step.StepType)
        {
            case MigrationStepType.DropTable:
            {
                var source = SqlFormatting.FormatTableName(dialect, backupTable, null);
                var target = SqlFormatting.FormatTableName(dialect, step.TableName, step.SchemaName);
                return $"INSERT INTO {target} SELECT * FROM {source};\nDROP TABLE {source};";
            }

            case MigrationStepType.DropColumn:
            {
                if (step.ColumnName == null) return null;
                var pkCol = FindPrimaryKeyColumn(sourceDef);
                if (pkCol == null) return null;

                var source = SqlFormatting.FormatTableName(dialect, backupTable, null);
                var target = SqlFormatting.FormatTableName(dialect, step.TableName, step.SchemaName);
                var quotedCol = SqlFormatting.QuoteIdentifier(dialect, step.ColumnName);
                var quotedPk = SqlFormatting.QuoteIdentifier(dialect, pkCol);

                return $"UPDATE {target} SET {quotedCol} = b.{quotedCol} FROM {source} b WHERE {target}.{quotedPk} = b.{quotedPk};\nDROP TABLE {source};";
            }

            default:
                return null;
        }
    }

    private static string GenerateTableBackup(string tableName, string? schemaName, SqlDialect dialect)
    {
        var backupName = $"__quarry_backup_{tableName}";
        var source = SqlFormatting.FormatTableName(dialect, tableName, schemaName);
        var backup = SqlFormatting.FormatTableName(dialect, backupName, null);

        if (dialect == SqlDialect.SQLite)
            return $"CREATE TABLE {backup} AS SELECT * FROM {source};";

        // SQL Server uses SELECT INTO
        if (dialect == SqlDialect.SqlServer)
            return $"SELECT * INTO {backup} FROM {source};";

        // PostgreSQL and MySQL
        return $"CREATE TABLE {backup} AS SELECT * FROM {source};";
    }

    private static string GenerateColumnBackup(string tableName, string? schemaName, string columnName, TableDef sourceDef, SqlDialect dialect)
    {
        var pkCol = FindPrimaryKeyColumn(sourceDef);
        if (pkCol == null) return GenerateTableBackup(tableName, schemaName, dialect);

        var backupName = $"__quarry_backup_{tableName}_{columnName}";
        var source = SqlFormatting.FormatTableName(dialect, tableName, schemaName);
        var backup = SqlFormatting.FormatTableName(dialect, backupName, null);
        var quotedPk = SqlFormatting.QuoteIdentifier(dialect, pkCol);
        var quotedCol = SqlFormatting.QuoteIdentifier(dialect, columnName);

        if (dialect == SqlDialect.SqlServer)
            return $"SELECT {quotedPk}, {quotedCol} INTO {backup} FROM {source};";

        return $"CREATE TABLE {backup} AS SELECT {quotedPk}, {quotedCol} FROM {source};";
    }

    internal static string? GetBackupTableName(MigrationStep step)
    {
        return step.StepType switch
        {
            MigrationStepType.DropTable => $"__quarry_backup_{step.TableName}",
            MigrationStepType.DropColumn when step.ColumnName != null => $"__quarry_backup_{step.TableName}_{step.ColumnName}",
            _ => null
        };
    }

    internal static string? FindPrimaryKeyColumn(TableDef table)
    {
        for (var i = 0; i < table.Columns.Count; i++)
        {
            if (table.Columns[i].Kind == ColumnKind.PrimaryKey)
                return table.Columns[i].Name;
        }
        return null;
    }
}
#endif
