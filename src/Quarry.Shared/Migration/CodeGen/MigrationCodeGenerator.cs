using System;
using System.Collections.Generic;
using System.Text;

namespace Quarry.Shared.Migration;

/// <summary>
/// Generates C# migration class files from diff results.
/// </summary>
#if QUARRY_GENERATOR
internal
#else
public
#endif
static class MigrationCodeGenerator
{
    public static string GenerateMigrationClass(
        int version,
        string name,
        IReadOnlyList<MigrationStep> steps,
        SchemaSnapshot? oldSnapshot,
        SchemaSnapshot newSnapshot,
        string namespaceName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using Quarry;");
        sb.AppendLine("using Quarry.Migration;");
        sb.AppendLine();
        sb.Append("namespace ").Append(namespaceName).AppendLine(";");
        sb.AppendLine();

        var className = "M" + version.ToString().PadLeft(4, '0') + "_" + SanitizeName(name);

        sb.Append("[Migration(Version = ").Append(version);
        sb.Append(", Name = \"").Append(EscapeString(name)).Append("\"");
        sb.AppendLine(")]");
        sb.Append("internal static partial class ").AppendLine(className);
        sb.AppendLine("{");

        // Upgrade method
        sb.AppendLine("    public static void Upgrade(MigrationBuilder builder)");
        sb.AppendLine("    {");
        sb.AppendLine("        BeforeUpgrade(builder);");
        GenerateWarnings(sb, steps);
        foreach (var step in steps)
        {
            GenerateUpgradeStep(sb, step, newSnapshot);
        }
        sb.AppendLine("        AfterUpgrade(builder);");
        sb.AppendLine("    }");
        sb.AppendLine();

        // Downgrade method
        sb.AppendLine("    public static void Downgrade(MigrationBuilder builder)");
        sb.AppendLine("    {");
        sb.AppendLine("        BeforeDowngrade(builder);");
        for (var i = steps.Count - 1; i >= 0; i--)
        {
            GenerateDowngradeStep(sb, steps[i], oldSnapshot);
        }
        sb.AppendLine("        AfterDowngrade(builder);");
        sb.AppendLine("    }");
        sb.AppendLine();

        // Backup method
        var hasDestructive = false;
        foreach (var step in steps)
        {
            if (step.Classification == StepClassification.Destructive)
            {
                hasDestructive = true;
                break;
            }
        }

        sb.AppendLine("    public static void Backup(MigrationBuilder builder)");
        sb.AppendLine("    {");
        if (hasDestructive)
        {
            sb.AppendLine("        // Auto-generated backup for destructive steps.");
            sb.AppendLine("        // Actual backup SQL is generated at apply-time using BackupGenerator.");
        }
        sb.AppendLine("    }");
        sb.AppendLine();

        // Partial hook declarations
        sb.AppendLine("    static partial void BeforeUpgrade(MigrationBuilder builder);");
        sb.AppendLine("    static partial void AfterUpgrade(MigrationBuilder builder);");
        sb.AppendLine("    static partial void BeforeDowngrade(MigrationBuilder builder);");
        sb.AppendLine("    static partial void AfterDowngrade(MigrationBuilder builder);");

        sb.AppendLine("}");

        return sb.ToString();
    }

    private static void GenerateWarnings(StringBuilder sb, IReadOnlyList<MigrationStep> steps)
    {
        foreach (var step in steps)
        {
            if (step.StepType == MigrationStepType.AlterColumn && step.OldValue is ColumnDef oldCol && step.NewValue is ColumnDef newCol)
            {
                if (oldCol.IsNullable && !newCol.IsNullable)
                    sb.AppendLine($"        // WARNING: Column '{step.ColumnName}' changed from nullable to non-null. Add builder.Sql() to handle existing NULLs.");
                if (oldCol.ClrType != newCol.ClrType)
                    sb.AppendLine($"        // WARNING: Column '{step.ColumnName}' type changed from '{oldCol.ClrType}' to '{newCol.ClrType}'. Add data conversion via builder.Sql().");
            }
        }
    }

    private static void GenerateUpgradeStep(StringBuilder sb, MigrationStep step, SchemaSnapshot snapshot)
    {
        switch (step.StepType)
        {
            case MigrationStepType.CreateTable when step.NewValue is TableDef table:
                sb.Append("        builder.CreateTable(\"").Append(EscapeString(step.TableName)).Append("\", ");
                sb.Append(step.SchemaName != null ? $"\"{EscapeString(step.SchemaName)}\"" : "null");
                sb.AppendLine(", t =>");
                sb.AppendLine("        {");
                foreach (var col in table.Columns)
                {
                    sb.Append("            t.Column(\"").Append(EscapeString(col.Name)).Append("\", c => c");
                    sb.Append(".ClrType(\"").Append(EscapeString(col.ClrType)).Append("\")");
                    if (col.IsIdentity) sb.Append(".Identity()");
                    if (!col.IsNullable) sb.Append(".NotNull()");
                    if (col.IsNullable) sb.Append(".Nullable()");
                    if (col.MaxLength.HasValue) sb.Append(".Length(").Append(col.MaxLength.Value).Append(")");
                    if (col.Precision.HasValue) sb.Append(".Precision(").Append(col.Precision.Value).Append(", ").Append(col.Scale ?? 0).Append(")");
                    if (col.HasDefault && col.DefaultExpression != null)
                        sb.Append(".DefaultExpression(\"").Append(EscapeString(col.DefaultExpression)).Append("\")");
                    sb.AppendLine(");");
                }

                // Primary key constraint — composite key takes precedence
                if (table.CompositeKeyColumns is { Count: > 0 })
                {
                    sb.Append("            t.PrimaryKey(\"PK_").Append(EscapeString(step.TableName)).Append("\"");
                    foreach (var pk in table.CompositeKeyColumns)
                        sb.Append(", \"").Append(EscapeString(pk)).Append("\"");
                    sb.AppendLine(");");
                }
                else
                {
                    var pkCols = new List<string>();
                    foreach (var col in table.Columns)
                    {
                        if (col.Kind == ColumnKind.PrimaryKey) pkCols.Add(col.Name);
                    }
                    if (pkCols.Count > 0)
                    {
                        sb.Append("            t.PrimaryKey(\"PK_").Append(EscapeString(step.TableName)).Append("\"");
                        foreach (var pk in pkCols)
                            sb.Append(", \"").Append(EscapeString(pk)).Append("\"");
                        sb.AppendLine(");");
                    }
                }

                sb.AppendLine("        });");
                break;

            case MigrationStepType.DropTable:
                sb.Append("        builder.DropTable(\"").Append(EscapeString(step.TableName)).Append("\"");
                if (step.SchemaName != null) sb.Append(", \"").Append(EscapeString(step.SchemaName)).Append("\"");
                sb.AppendLine(");");
                break;

            case MigrationStepType.RenameTable:
                sb.Append("        builder.RenameTable(\"").Append(EscapeString((string)step.OldValue!));
                sb.Append("\", \"").Append(EscapeString((string)step.NewValue!)).Append("\"");
                if (step.OldSchemaName != null)
                {
                    sb.Append(", oldSchema: ").Append($"\"{EscapeString(step.OldSchemaName)}\"");
                    sb.Append(", newSchema: ").Append(step.SchemaName != null ? $"\"{EscapeString(step.SchemaName)}\"" : "null");
                }
                else if (step.SchemaName != null)
                {
                    sb.Append(", \"").Append(EscapeString(step.SchemaName)).Append("\"");
                }
                sb.AppendLine(");");
                break;

            case MigrationStepType.AddColumn when step.NewValue is ColumnDef col:
                sb.Append("        builder.AddColumn(\"").Append(EscapeString(step.TableName));
                sb.Append("\", \"").Append(EscapeString(col.Name)).Append("\", c => c");
                sb.Append(".ClrType(\"").Append(EscapeString(col.ClrType)).Append("\")");
                if (!col.IsNullable) sb.Append(".NotNull()");
                if (col.IsNullable) sb.Append(".Nullable()");
                if (col.MaxLength.HasValue) sb.Append(".Length(").Append(col.MaxLength.Value).Append(")");
                sb.AppendLine(");");
                break;

            case MigrationStepType.DropColumn:
                sb.Append("        builder.DropColumn(\"").Append(EscapeString(step.TableName));
                sb.Append("\", \"").Append(EscapeString(step.ColumnName!)).AppendLine("\");");
                break;

            case MigrationStepType.RenameColumn:
                sb.Append("        builder.RenameColumn(\"").Append(EscapeString(step.TableName));
                sb.Append("\", \"").Append(EscapeString((string)step.OldValue!));
                sb.Append("\", \"").Append(EscapeString((string)step.NewValue!)).AppendLine("\");");
                break;

            case MigrationStepType.AlterColumn when step.NewValue is ColumnDef altCol:
                sb.Append("        builder.AlterColumn(\"").Append(EscapeString(step.TableName));
                sb.Append("\", \"").Append(EscapeString(step.ColumnName!)).Append("\", c => c");
                sb.Append(".ClrType(\"").Append(EscapeString(altCol.ClrType)).Append("\")");
                if (!altCol.IsNullable) sb.Append(".NotNull()");
                if (altCol.IsNullable) sb.Append(".Nullable()");
                if (altCol.Collation != null)
                    sb.Append(".Collation(\"").Append(EscapeString(altCol.Collation)).Append("\")");
                if (altCol.DefaultExpression != null)
                    sb.Append(".DefaultExpression(\"").Append(EscapeString(altCol.DefaultExpression)).Append("\")");
                sb.AppendLine(");");
                break;

            case MigrationStepType.AddForeignKey when step.NewValue is ForeignKeyDef fk:
                sb.Append("        builder.AddForeignKey(\"").Append(EscapeString(fk.ConstraintName));
                sb.Append("\", \"").Append(EscapeString(step.TableName));
                sb.Append("\", \"").Append(EscapeString(fk.ColumnName));
                sb.Append("\", \"").Append(EscapeString(fk.ReferencedTable));
                sb.Append("\", \"").Append(EscapeString(fk.ReferencedColumn)).Append("\"");
                if (fk.OnDelete != ForeignKeyAction.NoAction)
                    sb.Append(", ForeignKeyAction.").Append(fk.OnDelete);
                if (fk.OnUpdate != ForeignKeyAction.NoAction)
                    sb.Append(", ForeignKeyAction.").Append(fk.OnUpdate);
                sb.AppendLine(");");
                break;

            case MigrationStepType.DropForeignKey when step.OldValue is ForeignKeyDef dfk:
                sb.Append("        builder.DropForeignKey(\"").Append(EscapeString(dfk.ConstraintName));
                sb.Append("\", \"").Append(EscapeString(step.TableName)).AppendLine("\");");
                break;

            case MigrationStepType.AddIndex when step.NewValue is IndexDef idx:
                sb.Append("        builder.AddIndex(\"").Append(EscapeString(idx.Name));
                sb.Append("\", \"").Append(EscapeString(step.TableName)).Append("\", new[] { ");
                for (var i = 0; i < idx.Columns.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append("\"").Append(EscapeString(idx.Columns[i])).Append("\"");
                }
                sb.Append(" }");
                if (idx.IsUnique) sb.Append(", unique: true");
                if (idx.Filter != null) sb.Append(", filter: \"").Append(EscapeString(idx.Filter)).Append("\"");
                if (idx.DescendingColumns is { Length: > 0 })
                {
                    sb.Append(", descending: new[] { ");
                    for (var i = 0; i < idx.DescendingColumns.Length; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(idx.DescendingColumns[i] ? "true" : "false");
                    }
                    sb.Append(" }");
                }
                sb.AppendLine(");");
                break;

            case MigrationStepType.DropIndex when step.OldValue is IndexDef didx:
                sb.Append("        builder.DropIndex(\"").Append(EscapeString(didx.Name));
                sb.Append("\", \"").Append(EscapeString(step.TableName)).AppendLine("\");");
                break;
        }
    }

    private static void GenerateDowngradeStep(StringBuilder sb, MigrationStep step, SchemaSnapshot? oldSnapshot)
    {
        switch (step.StepType)
        {
            case MigrationStepType.CreateTable:
                sb.Append("        builder.DropTable(\"").Append(EscapeString(step.TableName)).Append("\"");
                if (step.SchemaName != null) sb.Append(", \"").Append(EscapeString(step.SchemaName)).Append("\"");
                sb.AppendLine(");");
                break;

            case MigrationStepType.DropTable when step.OldValue is TableDef table:
                // Reverse: re-create the table from the original definition
                sb.Append("        builder.CreateTable(\"").Append(EscapeString(step.TableName)).Append("\", ");
                sb.Append(step.SchemaName != null ? $"\"{EscapeString(step.SchemaName)}\"" : "null");
                sb.AppendLine(", t =>");
                sb.AppendLine("        {");
                foreach (var col in table.Columns)
                {
                    sb.Append("            t.Column(\"").Append(EscapeString(col.Name)).Append("\", c => c");
                    sb.Append(".ClrType(\"").Append(EscapeString(col.ClrType)).Append("\")");
                    if (col.IsIdentity) sb.Append(".Identity()");
                    if (!col.IsNullable) sb.Append(".NotNull()");
                    if (col.IsNullable) sb.Append(".Nullable()");
                    if (col.MaxLength.HasValue) sb.Append(".Length(").Append(col.MaxLength.Value).Append(")");
                    if (col.Precision.HasValue) sb.Append(".Precision(").Append(col.Precision.Value).Append(", ").Append(col.Scale ?? 0).Append(")");
                    if (col.HasDefault && col.DefaultExpression != null)
                        sb.Append(".DefaultExpression(\"").Append(EscapeString(col.DefaultExpression)).Append("\")");
                    sb.AppendLine(");");
                }

                // Primary key constraint — composite key takes precedence
                if (table.CompositeKeyColumns is { Count: > 0 })
                {
                    sb.Append("            t.PrimaryKey(\"PK_").Append(EscapeString(step.TableName)).Append("\"");
                    foreach (var pk in table.CompositeKeyColumns)
                        sb.Append(", \"").Append(EscapeString(pk)).Append("\"");
                    sb.AppendLine(");");
                }
                else
                {
                    var dPkCols = new List<string>();
                    foreach (var col in table.Columns)
                    {
                        if (col.Kind == ColumnKind.PrimaryKey) dPkCols.Add(col.Name);
                    }
                    if (dPkCols.Count > 0)
                    {
                        sb.Append("            t.PrimaryKey(\"PK_").Append(EscapeString(step.TableName)).Append("\"");
                        foreach (var pk in dPkCols)
                            sb.Append(", \"").Append(EscapeString(pk)).Append("\"");
                        sb.AppendLine(");");
                    }
                }

                sb.AppendLine("        });");
                break;

            case MigrationStepType.AddColumn:
                sb.Append("        builder.DropColumn(\"").Append(EscapeString(step.TableName));
                sb.Append("\", \"").Append(EscapeString(step.ColumnName!)).AppendLine("\");");
                break;

            case MigrationStepType.DropColumn when step.OldValue is ColumnDef col:
                sb.Append("        builder.AddColumn(\"").Append(EscapeString(step.TableName));
                sb.Append("\", \"").Append(EscapeString(col.Name)).Append("\", c => c");
                sb.Append(".ClrType(\"").Append(EscapeString(col.ClrType)).Append("\")");
                if (col.IsNullable) sb.Append(".Nullable()");
                sb.AppendLine(");");
                break;

            case MigrationStepType.RenameTable:
                sb.Append("        builder.RenameTable(\"").Append(EscapeString((string)step.NewValue!));
                sb.Append("\", \"").Append(EscapeString((string)step.OldValue!)).Append("\"");
                if (step.OldSchemaName != null)
                {
                    // Reverse: old schema becomes new, new becomes old
                    sb.Append(", oldSchema: ").Append(step.SchemaName != null ? $"\"{EscapeString(step.SchemaName)}\"" : "null");
                    sb.Append(", newSchema: ").Append($"\"{EscapeString(step.OldSchemaName)}\"");
                }
                else if (step.SchemaName != null)
                {
                    sb.Append(", \"").Append(EscapeString(step.SchemaName)).Append("\"");
                }
                sb.AppendLine(");");
                break;

            case MigrationStepType.RenameColumn:
                sb.Append("        builder.RenameColumn(\"").Append(EscapeString(step.TableName));
                sb.Append("\", \"").Append(EscapeString((string)step.NewValue!));
                sb.Append("\", \"").Append(EscapeString((string)step.OldValue!)).AppendLine("\");");
                break;

            case MigrationStepType.AlterColumn when step.OldValue is ColumnDef oldCol:
                sb.Append("        builder.AlterColumn(\"").Append(EscapeString(step.TableName));
                sb.Append("\", \"").Append(EscapeString(step.ColumnName!)).Append("\", c => c");
                sb.Append(".ClrType(\"").Append(EscapeString(oldCol.ClrType)).Append("\")");
                if (oldCol.IsNullable) sb.Append(".Nullable()");
                else sb.Append(".NotNull()");
                sb.AppendLine(");");
                break;

            case MigrationStepType.AddForeignKey when step.NewValue is ForeignKeyDef fk:
                sb.Append("        builder.DropForeignKey(\"").Append(EscapeString(fk.ConstraintName));
                sb.Append("\", \"").Append(EscapeString(step.TableName)).AppendLine("\");");
                break;

            case MigrationStepType.DropForeignKey when step.OldValue is ForeignKeyDef fk:
                sb.Append("        builder.AddForeignKey(\"").Append(EscapeString(fk.ConstraintName));
                sb.Append("\", \"").Append(EscapeString(step.TableName));
                sb.Append("\", \"").Append(EscapeString(fk.ColumnName));
                sb.Append("\", \"").Append(EscapeString(fk.ReferencedTable));
                sb.Append("\", \"").Append(EscapeString(fk.ReferencedColumn)).AppendLine("\");");
                break;

            case MigrationStepType.AddIndex when step.NewValue is IndexDef idx:
                sb.Append("        builder.DropIndex(\"").Append(EscapeString(idx.Name));
                sb.Append("\", \"").Append(EscapeString(step.TableName)).AppendLine("\");");
                break;

            case MigrationStepType.DropIndex when step.OldValue is IndexDef idx:
                sb.Append("        builder.AddIndex(\"").Append(EscapeString(idx.Name));
                sb.Append("\", \"").Append(EscapeString(step.TableName)).Append("\", new[] { ");
                for (var i = 0; i < idx.Columns.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append("\"").Append(EscapeString(idx.Columns[i])).Append("\"");
                }
                sb.Append(" }");
                if (idx.IsUnique) sb.Append(", unique: true");
                if (idx.Filter != null) sb.Append(", filter: \"").Append(EscapeString(idx.Filter)).Append("\"");
                if (idx.DescendingColumns is { Length: > 0 })
                {
                    sb.Append(", descending: new[] { ");
                    for (var i = 0; i < idx.DescendingColumns.Length; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(idx.DescendingColumns[i] ? "true" : "false");
                    }
                    sb.Append(" }");
                }
                sb.AppendLine(");");
                break;
        }
    }

    private static string SanitizeName(string name) => CodeGenHelpers.SanitizeCSharpName(name);

    private static string EscapeString(string value) => CodeGenHelpers.EscapeCSharpString(value ?? "");
}
