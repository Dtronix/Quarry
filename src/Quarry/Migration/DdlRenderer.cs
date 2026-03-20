using System;
using System.Collections.Generic;
using System.Text;
using Quarry.Shared.Sql;

namespace Quarry.Migration;

/// <summary>
/// Renders migration operations to DDL SQL strings per dialect.
/// </summary>
internal static class DdlRenderer
{
    public static string Render(IReadOnlyList<MigrationOperation> operations, SqlDialect dialect, bool idempotent = false)
    {
        // For SQLite, fold standalone AddForeignKey operations into their
        // matching CreateTable operations (SQLite doesn't support ALTER TABLE ADD CONSTRAINT).
        var ops = dialect == SqlDialect.SQLite
            ? FoldForeignKeysForSQLite(operations)
            : operations;

        var sb = new StringBuilder();
        foreach (var op in ops)
        {
            RenderOperation(sb, op, dialect, idempotent);
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private static void RenderOperation(StringBuilder sb, MigrationOperation op, SqlDialect dialect, bool idempotent)
    {
        switch (op)
        {
            case CreateTableOperation ct:
                RenderCreateTable(sb, ct, dialect, idempotent);
                break;
            case DropTableOperation dt:
                RenderDropTable(sb, dt, dialect, idempotent);
                break;
            case RenameTableOperation rt:
                RenderRenameTable(sb, rt, dialect);
                break;
            case AddColumnOperation ac:
                RenderAddColumn(sb, ac, dialect, idempotent);
                break;
            case DropColumnOperation dc:
                RenderDropColumn(sb, dc, dialect, idempotent);
                break;
            case RenameColumnOperation rc:
                RenderRenameColumn(sb, rc, dialect);
                break;
            case AlterColumnOperation alt:
                RenderAlterColumn(sb, alt, dialect);
                break;
            case AddForeignKeyOperation afk:
                RenderAddForeignKey(sb, afk, dialect);
                break;
            case DropForeignKeyOperation dfk:
                if (dialect == SqlDialect.SQLite)
                {
                    sb.AppendLine($"-- SQLite does not support DROP/ADD CONSTRAINT; table rebuild required.");
                    sb.AppendLine($"-- A table rebuild is required to remove constraint '{dfk.Name}'.");
                }
                else
                {
                    sb.Append("ALTER TABLE ").Append(FormatTable(dfk.Table, dfk.Schema, dialect));
                    sb.Append(" DROP CONSTRAINT ").Append(SqlFormatting.QuoteIdentifier(dialect, dfk.Name)).AppendLine(";");
                }
                break;
            case AddIndexOperation ai:
                RenderAddIndex(sb, ai, dialect, idempotent);
                break;
            case DropIndexOperation di:
                RenderDropIndex(sb, di, dialect, idempotent);
                break;
            case RawSqlOperation raw:
                sb.AppendLine(raw.Sql);
                break;
        }
    }

    private static void RenderCreateTable(StringBuilder sb, CreateTableOperation op, SqlDialect dialect, bool idempotent)
    {
        if (idempotent)
        {
            if (dialect == SqlDialect.SqlServer)
            {
                sb.Append("IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '")
                    .Append(op.Name).AppendLine("')");
            }
            else
            {
                // SQLite, PostgreSQL, MySQL all support CREATE TABLE IF NOT EXISTS
                sb.Append("CREATE TABLE IF NOT EXISTS ").Append(FormatTable(op.Name, op.Schema, dialect)).AppendLine(" (");
                RenderCreateTableBody(sb, op, dialect);
                sb.AppendLine();
                sb.AppendLine(");");
                RenderCreateTableIndexes(sb, op, dialect, idempotent);
                return;
            }
        }

        sb.Append("CREATE TABLE ").Append(FormatTable(op.Name, op.Schema, dialect)).AppendLine(" (");
        RenderCreateTableBody(sb, op, dialect);
        sb.AppendLine();
        sb.AppendLine(");");
        RenderCreateTableIndexes(sb, op, dialect, idempotent);
    }

    private static void RenderCreateTableBody(StringBuilder sb, CreateTableOperation op, SqlDialect dialect)
    {
        var first = true;
        foreach (var col in op.Table.Columns)
        {
            if (!first) sb.AppendLine(",");
            first = false;
            sb.Append("    ").Append(SqlFormatting.QuoteIdentifier(dialect, col.Name)).Append(" ");
            sb.Append(ResolveType(col, dialect));
            if (col.IsIdentity)
            {
                if (dialect == SqlDialect.SQLite)
                    sb.Append(" PRIMARY KEY AUTOINCREMENT");
                else
                    sb.Append(" ").Append(SqlFormatting.GetIdentitySyntax(dialect));
            }
            if (!col.IsNullable)
                sb.Append(" NOT NULL");
            if (col.DefaultExpression != null)
                sb.Append(" DEFAULT ").Append(ValidateSqlFragment(col.DefaultExpression, "DefaultExpression"));
            else if (col.DefaultValue != null)
                sb.Append(" DEFAULT ").Append(ValidateSqlFragment(col.DefaultValue, "DefaultValue"));
        }

        foreach (var c in op.Table.Constraints)
        {
            if (!first) sb.AppendLine(",");
            first = false;
            switch (c)
            {
                case PrimaryKeyConstraint pk:
                    sb.Append("    CONSTRAINT ").Append(SqlFormatting.QuoteIdentifier(dialect, pk.Name));
                    sb.Append(" PRIMARY KEY (");
                    for (var i = 0; i < pk.Columns.Length; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(SqlFormatting.QuoteIdentifier(dialect, pk.Columns[i]));
                    }
                    sb.Append(")");
                    break;
                case ForeignKeyConstraint fk:
                    sb.Append("    CONSTRAINT ").Append(SqlFormatting.QuoteIdentifier(dialect, fk.Name));
                    sb.Append(" FOREIGN KEY (").Append(SqlFormatting.QuoteIdentifier(dialect, fk.Column)).Append(")");
                    sb.Append(" REFERENCES ").Append(SqlFormatting.QuoteIdentifier(dialect, fk.RefTable));
                    sb.Append(" (").Append(SqlFormatting.QuoteIdentifier(dialect, fk.RefColumn)).Append(")");
                    AppendFkActions(sb, fk.OnDelete, fk.OnUpdate);
                    break;
            }
        }
    }

    private static void RenderCreateTableIndexes(StringBuilder sb, CreateTableOperation op, SqlDialect dialect, bool idempotent)
    {
        foreach (var c in op.Table.Constraints)
        {
            if (c is IndexConstraint idx)
            {
                RenderAddIndex(sb, new AddIndexOperation(idx.Name, op.Name, op.Schema, idx.Columns, idx.IsUnique, idx.Filter), dialect, idempotent);
            }
        }
    }

    private static void RenderDropTable(StringBuilder sb, DropTableOperation op, SqlDialect dialect, bool idempotent)
    {
        if (idempotent)
        {
            if (dialect == SqlDialect.SqlServer)
            {
                sb.Append("IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '")
                    .Append(op.Name).AppendLine("')");
                sb.Append("DROP TABLE ").Append(FormatTable(op.Name, op.Schema, dialect)).AppendLine(";");
            }
            else
            {
                // SQLite, PostgreSQL, MySQL
                sb.Append("DROP TABLE IF EXISTS ").Append(FormatTable(op.Name, op.Schema, dialect)).AppendLine(";");
            }
        }
        else
        {
            sb.Append("DROP TABLE ").Append(FormatTable(op.Name, op.Schema, dialect)).AppendLine(";");
        }
    }

    private static void RenderRenameTable(StringBuilder sb, RenameTableOperation op, SqlDialect dialect)
    {
        switch (dialect)
        {
            case SqlDialect.SqlServer:
                sb.Append("EXEC sp_rename ").Append(SqlFormatting.QuoteIdentifier(dialect, op.OldName));
                sb.Append(", ").Append(SqlFormatting.QuoteIdentifier(dialect, op.NewName)).AppendLine(";");
                break;
            case SqlDialect.MySQL:
                sb.Append("RENAME TABLE ").Append(SqlFormatting.QuoteIdentifier(dialect, op.OldName));
                sb.Append(" TO ").Append(SqlFormatting.QuoteIdentifier(dialect, op.NewName)).AppendLine(";");
                break;
            default:
                sb.Append("ALTER TABLE ").Append(FormatTable(op.OldName, op.Schema, dialect));
                sb.Append(" RENAME TO ").Append(SqlFormatting.QuoteIdentifier(dialect, op.NewName)).AppendLine(";");
                break;
        }
    }

    private static void RenderAddColumn(StringBuilder sb, AddColumnOperation op, SqlDialect dialect, bool idempotent)
    {
        if (idempotent)
        {
            switch (dialect)
            {
                case SqlDialect.PostgreSQL:
                    sb.Append("ALTER TABLE ").Append(FormatTable(op.Table, op.Schema, dialect));
                    sb.Append(" ADD COLUMN IF NOT EXISTS ").Append(SqlFormatting.QuoteIdentifier(dialect, op.Column)).Append(" ");
                    break;
                case SqlDialect.SQLite:
                    // SQLite doesn't support IF NOT EXISTS on ADD COLUMN, but we can use a pragma check pattern
                    // For simplicity, just emit the standard ALTER TABLE — SQLite errors are non-destructive
                    sb.Append("ALTER TABLE ").Append(FormatTable(op.Table, op.Schema, dialect));
                    sb.Append(" ADD COLUMN ").Append(SqlFormatting.QuoteIdentifier(dialect, op.Column)).Append(" ");
                    break;
                case SqlDialect.SqlServer:
                    sb.Append("IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('")
                        .Append(op.Schema != null ? $"{op.Schema}.{op.Table}" : op.Table)
                        .Append("') AND name = '").Append(op.Column).AppendLine("')");
                    sb.Append("ALTER TABLE ").Append(FormatTable(op.Table, op.Schema, dialect));
                    sb.Append(" ADD ").Append(SqlFormatting.QuoteIdentifier(dialect, op.Column)).Append(" ");
                    break;
                default: // MySQL
                    // MySQL doesn't have native IF NOT EXISTS for ADD COLUMN, use INFORMATION_SCHEMA
                    sb.AppendLine($"SET @col_exists = (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{op.Table}' AND COLUMN_NAME = '{op.Column}');");
                    sb.AppendLine($"SET @sql = IF(@col_exists = 0,");
                    sb.Append("    'ALTER TABLE ").Append(FormatTable(op.Table, op.Schema, dialect));
                    sb.Append(" ADD ").Append(SqlFormatting.QuoteIdentifier(dialect, op.Column)).Append(" ");
                    AppendColumnTypeAndConstraints(sb, op.Definition, dialect);
                    var suffix = OnlineSuffix(op, dialect);
                    if (suffix != null) sb.Append(" ").Append(suffix);
                    sb.AppendLine("', 'SELECT 1');");
                    sb.AppendLine("PREPARE stmt FROM @sql;");
                    sb.AppendLine("EXECUTE stmt;");
                    sb.AppendLine("DEALLOCATE PREPARE stmt;");
                    return;
            }

            AppendColumnTypeAndConstraints(sb, op.Definition, dialect);
            var sfx = OnlineSuffix(op, dialect);
            if (sfx != null) sb.Append(" ").Append(sfx);
            sb.AppendLine(";");
            return;
        }

        sb.Append("ALTER TABLE ").Append(FormatTable(op.Table, op.Schema, dialect));
        sb.Append(" ADD ").Append(SqlFormatting.QuoteIdentifier(dialect, op.Column)).Append(" ");
        AppendColumnTypeAndConstraints(sb, op.Definition, dialect);
        var onlineSuffix = OnlineSuffix(op, dialect);
        if (onlineSuffix != null) sb.Append(" ").Append(onlineSuffix);
        sb.AppendLine(";");
    }

    private static void AppendColumnTypeAndConstraints(StringBuilder sb, ColumnDefinition def, SqlDialect dialect)
    {
        sb.Append(ResolveType(def, dialect));
        if (!def.IsNullable) sb.Append(" NOT NULL");
        if (def.DefaultExpression != null)
            sb.Append(" DEFAULT ").Append(ValidateSqlFragment(def.DefaultExpression, "DefaultExpression"));
        else if (def.DefaultValue != null)
            sb.Append(" DEFAULT ").Append(ValidateSqlFragment(def.DefaultValue, "DefaultValue"));
    }

    private static void RenderDropColumn(StringBuilder sb, DropColumnOperation op, SqlDialect dialect, bool idempotent)
    {
        if (dialect == SqlDialect.SQLite && op.SourceTable != null)
        {
            RenderSQLiteTableRebuild(sb, op.Table, op.Schema, op.SourceTable, dialect,
                excludeColumn: op.Column, alteredColumn: null);
            return;
        }

        if (idempotent && dialect == SqlDialect.SqlServer)
        {
            sb.Append("IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('")
                .Append(op.Schema != null ? $"{op.Schema}.{op.Table}" : op.Table)
                .Append("') AND name = '").Append(op.Column).AppendLine("')");
        }

        if (dialect == SqlDialect.SQLite)
        {
            sb.Append("ALTER TABLE ").Append(SqlFormatting.QuoteIdentifier(dialect, op.Table));
            sb.Append(" DROP COLUMN ").Append(SqlFormatting.QuoteIdentifier(dialect, op.Column)).AppendLine(";");
            return;
        }
        sb.Append("ALTER TABLE ").Append(FormatTable(op.Table, op.Schema, dialect));
        sb.Append(" DROP COLUMN ").Append(SqlFormatting.QuoteIdentifier(dialect, op.Column)).AppendLine(";");
    }

    private static void RenderRenameColumn(StringBuilder sb, RenameColumnOperation op, SqlDialect dialect)
    {
        switch (dialect)
        {
            case SqlDialect.SqlServer:
                sb.Append("EXEC sp_rename '");
                sb.Append(op.Table.Replace("'", "''")).Append(".");
                sb.Append(op.OldName.Replace("'", "''"));
                sb.Append("', '").Append(op.NewName.Replace("'", "''")).AppendLine("', 'COLUMN';");
                break;
            case SqlDialect.MySQL:
                // MySQL RENAME COLUMN is supported in 8.0+
                sb.Append("ALTER TABLE ").Append(SqlFormatting.QuoteIdentifier(dialect, op.Table));
                sb.Append(" RENAME COLUMN ").Append(SqlFormatting.QuoteIdentifier(dialect, op.OldName));
                sb.Append(" TO ").Append(SqlFormatting.QuoteIdentifier(dialect, op.NewName)).AppendLine(";");
                break;
            default:
                sb.Append("ALTER TABLE ").Append(FormatTable(op.Table, op.Schema, dialect));
                sb.Append(" RENAME COLUMN ").Append(SqlFormatting.QuoteIdentifier(dialect, op.OldName));
                sb.Append(" TO ").Append(SqlFormatting.QuoteIdentifier(dialect, op.NewName)).AppendLine(";");
                break;
        }
    }

    private static void RenderAlterColumn(StringBuilder sb, AlterColumnOperation op, SqlDialect dialect)
    {
        var typeSql = ResolveType(op.Definition, dialect);
        switch (dialect)
        {
            case SqlDialect.PostgreSQL:
                sb.Append("ALTER TABLE ").Append(FormatTable(op.Table, op.Schema, dialect));
                sb.Append(" ALTER COLUMN ").Append(SqlFormatting.QuoteIdentifier(dialect, op.Column));
                sb.Append(" TYPE ").Append(typeSql).AppendLine(";");
                if (!op.Definition.IsNullable)
                {
                    sb.Append("ALTER TABLE ").Append(FormatTable(op.Table, op.Schema, dialect));
                    sb.Append(" ALTER COLUMN ").Append(SqlFormatting.QuoteIdentifier(dialect, op.Column));
                    sb.AppendLine(" SET NOT NULL;");
                }
                else
                {
                    sb.Append("ALTER TABLE ").Append(FormatTable(op.Table, op.Schema, dialect));
                    sb.Append(" ALTER COLUMN ").Append(SqlFormatting.QuoteIdentifier(dialect, op.Column));
                    sb.AppendLine(" DROP NOT NULL;");
                }
                break;
            case SqlDialect.MySQL:
                sb.Append("ALTER TABLE ").Append(SqlFormatting.QuoteIdentifier(dialect, op.Table));
                sb.Append(" MODIFY COLUMN ").Append(SqlFormatting.QuoteIdentifier(dialect, op.Column)).Append(" ").Append(typeSql);
                if (!op.Definition.IsNullable) sb.Append(" NOT NULL");
                var mySuffix = OnlineSuffix(op, dialect);
                if (mySuffix != null) sb.Append(", ").Append(mySuffix);
                sb.AppendLine(";");
                break;
            case SqlDialect.SqlServer:
                sb.Append("ALTER TABLE ").Append(FormatTable(op.Table, op.Schema, dialect));
                sb.Append(" ALTER COLUMN ").Append(SqlFormatting.QuoteIdentifier(dialect, op.Column)).Append(" ").Append(typeSql);
                if (!op.Definition.IsNullable) sb.Append(" NOT NULL");
                var ssSuffix = OnlineSuffix(op, dialect);
                if (ssSuffix != null) sb.Append(" ").Append(ssSuffix);
                sb.AppendLine(";");
                break;
            default: // SQLite
                if (op.SourceTable != null)
                {
                    RenderSQLiteTableRebuild(sb, op.Table, op.Schema, op.SourceTable, dialect,
                        excludeColumn: null, alteredColumn: (op.Column, op.Definition));
                }
                else
                {
                    sb.AppendLine("-- SQLite does not natively support ALTER COLUMN.");
                    sb.AppendLine("-- Consider a table rebuild for column type changes.");
                }
                break;
        }
    }

    private static void RenderAddForeignKey(StringBuilder sb, AddForeignKeyOperation op, SqlDialect dialect)
    {
        if (dialect == SqlDialect.SQLite)
        {
            // SQLite does not support ALTER TABLE ... ADD CONSTRAINT.
            // FKs should be folded into CREATE TABLE during pre-processing.
            // If we reach here, the FK targets a table not created in this migration
            // and would require a table rebuild.
            sb.AppendLine($"-- SQLite does not support adding foreign keys to existing tables.");
            sb.AppendLine($"-- A table rebuild is required to add constraint '{op.Name}'.");
            return;
        }
        sb.Append("ALTER TABLE ").Append(FormatTable(op.Table, op.Schema, dialect));
        sb.Append(" ADD CONSTRAINT ").Append(SqlFormatting.QuoteIdentifier(dialect, op.Name));
        sb.Append(" FOREIGN KEY (").Append(SqlFormatting.QuoteIdentifier(dialect, op.Column)).Append(")");
        sb.Append(" REFERENCES ").Append(SqlFormatting.QuoteIdentifier(dialect, op.RefTable));
        sb.Append(" (").Append(SqlFormatting.QuoteIdentifier(dialect, op.RefColumn)).Append(")");
        AppendFkActions(sb, op.OnDelete, op.OnUpdate);
        sb.AppendLine(";");
    }

    private static void RenderAddIndex(StringBuilder sb, AddIndexOperation op, SqlDialect dialect, bool idempotent)
    {
        if (idempotent)
        {
            switch (dialect)
            {
                case SqlDialect.SqlServer:
                    sb.Append("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '")
                        .Append(op.Name).Append("' AND object_id = OBJECT_ID('")
                        .Append(op.Schema != null ? $"{op.Schema}.{op.Table}" : op.Table)
                        .AppendLine("'))");
                    break;
                case SqlDialect.MySQL:
                    // MySQL doesn't have IF NOT EXISTS for CREATE INDEX, but we can check
                    sb.AppendLine($"SET @idx_exists = (SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS WHERE TABLE_NAME = '{op.Table}' AND INDEX_NAME = '{op.Name}');");
                    sb.AppendLine("SET @sql = IF(@idx_exists = 0,");
                    var idxSb = new StringBuilder();
                    RenderAddIndexCore(idxSb, op, dialect);
                    sb.Append("    '").Append(idxSb.ToString().TrimEnd('\r', '\n', ';')).AppendLine("', 'SELECT 1');");
                    sb.AppendLine("PREPARE stmt FROM @sql;");
                    sb.AppendLine("EXECUTE stmt;");
                    sb.AppendLine("DEALLOCATE PREPARE stmt;");
                    return;
                default:
                    // PostgreSQL and SQLite support CREATE INDEX IF NOT EXISTS
                    sb.Append("CREATE ");
                    if (op.IsUnique) sb.Append("UNIQUE ");
                    sb.Append("INDEX ");
                    if (op.IsConcurrent && dialect == SqlDialect.PostgreSQL)
                        sb.Append("CONCURRENTLY ");
                    sb.Append("IF NOT EXISTS ");
                    sb.Append(SqlFormatting.QuoteIdentifier(dialect, op.Name));
                    sb.Append(" ON ").Append(FormatTable(op.Table, op.Schema, dialect)).Append(" (");
                    for (var i = 0; i < op.Columns.Length; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(SqlFormatting.QuoteIdentifier(dialect, op.Columns[i]));
                    }
                    sb.Append(")");
                    if (op.Filter != null)
                        sb.Append(" WHERE ").Append(ValidateSqlFragment(op.Filter, "Index.Filter"));
                    sb.AppendLine(";");
                    return;
            }
        }

        RenderAddIndexCore(sb, op, dialect);
    }

    private static void RenderAddIndexCore(StringBuilder sb, AddIndexOperation op, SqlDialect dialect)
    {
        sb.Append("CREATE ");
        if (op.IsUnique) sb.Append("UNIQUE ");
        sb.Append("INDEX ");
        if (op.IsConcurrent && dialect == SqlDialect.PostgreSQL)
            sb.Append("CONCURRENTLY ");
        sb.Append(SqlFormatting.QuoteIdentifier(dialect, op.Name));
        sb.Append(" ON ").Append(FormatTable(op.Table, op.Schema, dialect)).Append(" (");
        for (var i = 0; i < op.Columns.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(SqlFormatting.QuoteIdentifier(dialect, op.Columns[i]));
        }
        sb.Append(")");
        if (op.Filter != null)
            sb.Append(" WHERE ").Append(ValidateSqlFragment(op.Filter, "Index.Filter"));
        if (op.IsConcurrent && dialect == SqlDialect.SqlServer)
            sb.Append(" WITH (ONLINE = ON)");
        sb.AppendLine(";");
    }

    private static void RenderDropIndex(StringBuilder sb, DropIndexOperation op, SqlDialect dialect, bool idempotent)
    {
        if (idempotent)
        {
            switch (dialect)
            {
                case SqlDialect.SqlServer:
                    sb.Append("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = '")
                        .Append(op.Name).Append("' AND object_id = OBJECT_ID('")
                        .Append(op.Schema != null ? $"{op.Schema}.{op.Table}" : op.Table)
                        .AppendLine("'))");
                    break;
                case SqlDialect.PostgreSQL:
                    sb.Append("DROP INDEX IF EXISTS ").Append(SqlFormatting.QuoteIdentifier(dialect, op.Name)).AppendLine(";");
                    return;
                case SqlDialect.SQLite:
                    sb.Append("DROP INDEX IF EXISTS ").Append(SqlFormatting.QuoteIdentifier(dialect, op.Name)).AppendLine(";");
                    return;
                case SqlDialect.MySQL:
                    // MySQL doesn't have IF EXISTS for DROP INDEX; need to check first
                    sb.AppendLine($"SET @idx_exists = (SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS WHERE TABLE_NAME = '{op.Table}' AND INDEX_NAME = '{op.Name}');");
                    sb.AppendLine("SET @sql = IF(@idx_exists > 0,");
                    sb.Append("    'DROP INDEX ").Append(SqlFormatting.QuoteIdentifier(dialect, op.Name));
                    sb.Append(" ON ").Append(SqlFormatting.QuoteIdentifier(dialect, op.Table)).AppendLine("', 'SELECT 1');");
                    sb.AppendLine("PREPARE stmt FROM @sql;");
                    sb.AppendLine("EXECUTE stmt;");
                    sb.AppendLine("DEALLOCATE PREPARE stmt;");
                    return;
            }
        }

        switch (dialect)
        {
            case SqlDialect.SqlServer:
                sb.Append("DROP INDEX ").Append(SqlFormatting.QuoteIdentifier(dialect, op.Name));
                sb.Append(" ON ").Append(FormatTable(op.Table, op.Schema, dialect)).AppendLine(";");
                break;
            case SqlDialect.MySQL:
                sb.Append("DROP INDEX ").Append(SqlFormatting.QuoteIdentifier(dialect, op.Name));
                sb.Append(" ON ").Append(SqlFormatting.QuoteIdentifier(dialect, op.Table)).AppendLine(";");
                break;
            default:
                sb.Append("DROP INDEX ").Append(SqlFormatting.QuoteIdentifier(dialect, op.Name)).AppendLine(";");
                break;
        }
    }

    // --- SQLite table rebuild ---

    private static void RenderSQLiteTableRebuild(
        StringBuilder sb, string table, string? schema, TableDefinition sourceTable, SqlDialect dialect,
        string? excludeColumn, (string Name, ColumnDefinition Def)? alteredColumn)
    {
        var tmpName = $"_quarry_tmp_{table}";

        // 1. Rename original to temp
        sb.Append("ALTER TABLE ").Append(SqlFormatting.QuoteIdentifier(dialect, table));
        sb.Append(" RENAME TO ").Append(SqlFormatting.QuoteIdentifier(dialect, tmpName)).AppendLine(";");

        // 2. Build new column list
        var newColumns = new List<ColumnDefinition>();
        foreach (var col in sourceTable.Columns)
        {
            if (excludeColumn != null && string.Equals(col.Name, excludeColumn, StringComparison.OrdinalIgnoreCase))
                continue;

            if (alteredColumn.HasValue && string.Equals(col.Name, alteredColumn.Value.Name, StringComparison.OrdinalIgnoreCase))
                newColumns.Add(alteredColumn.Value.Def with { Name = col.Name });
            else
                newColumns.Add(col);
        }

        // 3. CREATE TABLE with new columns
        var newTable = new TableDefinition(table, schema, newColumns, sourceTable.Constraints);
        var createOp = new CreateTableOperation(table, schema, newTable);
        RenderCreateTable(sb, createOp, dialect, idempotent: false);

        // 4. INSERT INTO ... SELECT ...
        var colNames = new List<string>();
        foreach (var col in newColumns)
            colNames.Add(SqlFormatting.QuoteIdentifier(dialect, col.Name));

        var colList = string.Join(", ", colNames);
        sb.Append("INSERT INTO ").Append(SqlFormatting.QuoteIdentifier(dialect, table));
        sb.Append(" (").Append(colList).Append(")");
        sb.Append(" SELECT ").Append(colList);
        sb.Append(" FROM ").Append(SqlFormatting.QuoteIdentifier(dialect, tmpName)).AppendLine(";");

        // 5. Drop temp table
        sb.Append("DROP TABLE ").Append(SqlFormatting.QuoteIdentifier(dialect, tmpName)).AppendLine(";");
    }

    // --- Helpers ---

    private static string FormatTable(string name, string? schema, SqlDialect dialect)
    {
        return SqlFormatting.FormatTableName(dialect, name, schema);
    }

    private static string ResolveType(ColumnDefinition col, SqlDialect dialect)
    {
        if (col.SqlType != null) return col.SqlType;
        if (col.ClrType != null) return SqlTypeMapper.MapClrType(col.ClrType, dialect, col.MaxLength, col.Precision, col.Scale);
        return "TEXT";
    }

    private static void AppendFkActions(StringBuilder sb,
        ForeignKeyAction? onDelete,
        ForeignKeyAction? onUpdate)
    {
        if (onDelete.HasValue && onDelete.Value != ForeignKeyAction.NoAction)
            sb.Append(" ON DELETE ").Append(FormatFkAction(onDelete.Value));
        if (onUpdate.HasValue && onUpdate.Value != ForeignKeyAction.NoAction)
            sb.Append(" ON UPDATE ").Append(FormatFkAction(onUpdate.Value));
    }

    private static string FormatFkAction(ForeignKeyAction action)
    {
        return action switch
        {
            ForeignKeyAction.Cascade => "CASCADE",
            ForeignKeyAction.SetNull => "SET NULL",
            ForeignKeyAction.SetDefault => "SET DEFAULT",
            ForeignKeyAction.Restrict => "RESTRICT",
            _ => "NO ACTION"
        };
    }

    /// <summary>
    /// For SQLite: moves AddForeignKeyOperations into corresponding CreateTableOperations
    /// in the same migration batch. Returns a new list with folded operations removed.
    /// </summary>
    private static IReadOnlyList<MigrationOperation> FoldForeignKeysForSQLite(IReadOnlyList<MigrationOperation> operations)
    {
        // Index CreateTableOperations by table name
        var createOps = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < operations.Count; i++)
        {
            if (operations[i] is CreateTableOperation ct)
                createOps[ct.Name] = i;
        }

        // Collect FK operations that can be folded
        var foldedIndexes = new HashSet<int>();
        var augmentedCreateOps = new Dictionary<int, List<ForeignKeyConstraint>>();

        for (var i = 0; i < operations.Count; i++)
        {
            if (operations[i] is AddForeignKeyOperation afk && createOps.ContainsKey(afk.Table))
            {
                var createIndex = createOps[afk.Table];
                if (!augmentedCreateOps.ContainsKey(createIndex))
                    augmentedCreateOps[createIndex] = new List<ForeignKeyConstraint>();

                augmentedCreateOps[createIndex].Add(new ForeignKeyConstraint(
                    afk.Name, afk.Column, afk.RefTable, afk.RefColumn, afk.OnDelete, afk.OnUpdate));
                foldedIndexes.Add(i);
            }
        }

        if (foldedIndexes.Count == 0)
            return operations;

        // Build new operation list with FKs folded into CREATE TABLE constraints
        var result = new List<MigrationOperation>(operations.Count - foldedIndexes.Count);
        for (var i = 0; i < operations.Count; i++)
        {
            if (foldedIndexes.Contains(i))
                continue;

            if (augmentedCreateOps.TryGetValue(i, out var fks) && operations[i] is CreateTableOperation ct)
            {
                var newConstraints = new List<TableConstraint>(ct.Table.Constraints);
                newConstraints.AddRange(fks);
                var newTable = new TableDefinition(ct.Table.Name, ct.Table.Schema, ct.Table.Columns, newConstraints);
                result.Add(new CreateTableOperation(ct.Name, ct.Schema, newTable));
            }
            else
            {
                result.Add(operations[i]);
            }
        }

        return result;
    }

    private static string? OnlineSuffix(MigrationOperation op, SqlDialect dialect)
    {
        if (op.IsOnline)
        {
            return dialect switch
            {
                SqlDialect.MySQL => "ALGORITHM=INPLACE, LOCK=NONE",
                SqlDialect.SqlServer => "WITH (ONLINE = ON)",
                _ => null
            };
        }
        return null;
    }

    /// <summary>
    /// Validates that a SQL expression fragment does not contain obvious injection patterns.
    /// </summary>
    private static string ValidateSqlFragment(string fragment, string context)
    {
        if (fragment.Contains(';'))
            throw new InvalidOperationException($"SQL fragment for {context} contains invalid character ';'. Value: {fragment}");
        if (fragment.Contains("--"))
            throw new InvalidOperationException($"SQL fragment for {context} contains line comment '--'. Value: {fragment}");
        if (fragment.Contains("/*"))
            throw new InvalidOperationException($"SQL fragment for {context} contains block comment '/*'. Value: {fragment}");
        return fragment;
    }
}
