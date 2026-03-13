using System;
using System.Collections.Generic;
using System.Text;

namespace Quarry.Shared.Migration;

/// <summary>
/// Generates C# source code for schema snapshot classes.
/// </summary>
#if QUARRY_GENERATOR
internal
#else
public
#endif
static class SnapshotCodeGenerator
{
    /// <summary>
    /// Generates a complete snapshot class from a SchemaSnapshot.
    /// </summary>
    public static string GenerateSnapshotClass(SchemaSnapshot snapshot, string namespaceName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using Quarry;");
        sb.AppendLine("using Quarry.Migration;");
        sb.AppendLine();
        sb.Append("namespace ").Append(namespaceName).AppendLine(";");
        sb.AppendLine();

        var className = "S" + snapshot.Version.ToString().PadLeft(4, '0') + "_" + SanitizeName(snapshot.Name);

        sb.Append(GenerateSnapshotAttribute(snapshot));
        sb.Append("internal static partial class ").AppendLine(className);
        sb.AppendLine("{");
        sb.Append(GenerateBuildMethod(snapshot));
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// Generates only the [MigrationSnapshot] attribute line for a snapshot.
    /// </summary>
    public static string GenerateSnapshotAttribute(SchemaSnapshot snapshot)
    {
        var sb = new StringBuilder();
        var timestamp = snapshot.Timestamp.ToString("o");

        sb.Append("[MigrationSnapshot(Version = ").Append(snapshot.Version);
        sb.Append(", Name = \"").Append(EscapeString(snapshot.Name)).Append("\"");
        sb.Append(", Timestamp = \"").Append(timestamp).Append("\"");
        if (snapshot.ParentVersion.HasValue)
            sb.Append(", ParentVersion = ").Append(snapshot.ParentVersion.Value);
        var schemaHash = SchemaHasher.ComputeHash(snapshot.Tables);
        sb.Append(", SchemaHash = \"").Append(schemaHash).Append("\"");
        sb.AppendLine(")]");

        return sb.ToString();
    }

    /// <summary>
    /// Generates only the Build() method body (indented for inclusion in a class).
    /// </summary>
    public static string GenerateBuildMethod(SchemaSnapshot snapshot)
    {
        var sb = new StringBuilder();
        var timestamp = snapshot.Timestamp.ToString("o");

        sb.AppendLine("    internal static SchemaSnapshot Build()");
        sb.AppendLine("    {");
        sb.AppendLine("        var builder = new SchemaSnapshotBuilder()");
        sb.Append("            .SetVersion(").Append(snapshot.Version).AppendLine(")");
        sb.Append("            .SetName(\"").Append(EscapeString(snapshot.Name)).AppendLine("\")");
        sb.Append("            .SetTimestamp(DateTimeOffset.Parse(\"").Append(timestamp).AppendLine("\"))");
        if (snapshot.ParentVersion.HasValue)
            sb.Append("            .SetParentVersion(").Append(snapshot.ParentVersion.Value).AppendLine(")");
        // Remove trailing newline added by AppendLine
        while (sb.Length > 0 && (sb[sb.Length - 1] == '\n' || sb[sb.Length - 1] == '\r'))
            sb.Length--;
        sb.AppendLine(";");
        sb.AppendLine();

        for (var i = 0; i < snapshot.Tables.Count; i++)
        {
            GenerateTable(sb, snapshot.Tables[i]);
        }

        sb.AppendLine("        return builder.Build();");
        sb.AppendLine("    }");

        return sb.ToString();
    }

    /// <summary>
    /// Generates a snapshot class from raw table definitions.
    /// </summary>
    public static string GenerateSnapshotFromSchema(
        IReadOnlyList<TableDef> tables,
        int version,
        string name,
        int? parentVersion,
        string namespaceName)
    {
        var snapshot = new SchemaSnapshot(
            version, name, DateTimeOffset.UtcNow, parentVersion, tables);
        return GenerateSnapshotClass(snapshot, namespaceName);
    }

    private static void GenerateTable(StringBuilder sb, TableDef table)
    {
        sb.Append("        builder.AddTable(t => t");
        sb.AppendLine();
        sb.Append("            .Name(\"").Append(EscapeString(table.TableName)).Append("\")");
        sb.AppendLine();

        if (table.SchemaName != null)
        {
            sb.Append("            .Schema(\"").Append(EscapeString(table.SchemaName)).Append("\")");
            sb.AppendLine();
        }

        if (table.NamingStyle != NamingStyleKind.Exact)
        {
            sb.Append("            .NamingStyle(NamingStyleKind.").Append(table.NamingStyle).Append(")");
            sb.AppendLine();
        }

        for (var i = 0; i < table.Columns.Count; i++)
        {
            GenerateColumn(sb, table.Columns[i]);
        }

        for (var i = 0; i < table.ForeignKeys.Count; i++)
        {
            GenerateForeignKey(sb, table.ForeignKeys[i]);
        }

        for (var i = 0; i < table.Indexes.Count; i++)
        {
            GenerateIndex(sb, table.Indexes[i]);
        }

        if (table.CompositeKeyColumns is { Count: > 0 })
        {
            sb.Append("            .CompositeKey(");
            for (var i = 0; i < table.CompositeKeyColumns.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append("\"").Append(EscapeString(table.CompositeKeyColumns[i])).Append("\"");
            }
            sb.AppendLine(")");
        }

        sb.AppendLine("        );");
        sb.AppendLine();
    }

    private static void GenerateColumn(StringBuilder sb, ColumnDef col)
    {
        sb.Append("            .AddColumn(c => c.Name(\"").Append(EscapeString(col.Name)).Append("\")");
        sb.Append(".ClrType(\"").Append(EscapeString(col.ClrType)).Append("\")");

        if (col.Kind == ColumnKind.PrimaryKey)
            sb.Append(".PrimaryKey()");
        if (col.Kind == ColumnKind.ForeignKey && col.ReferencedEntityName != null)
            sb.Append(".ForeignKey(\"").Append(EscapeString(col.ReferencedEntityName)).Append("\")");
        if (col.IsNullable)
            sb.Append(".Nullable()");
        if (col.IsIdentity)
            sb.Append(".Identity()");
        if (col.IsClientGenerated)
            sb.Append(".ClientGenerated()");
        if (col.IsComputed)
            sb.Append(".Computed()");
        if (col.MaxLength.HasValue)
            sb.Append(".Length(").Append(col.MaxLength.Value).Append(")");
        if (col.Precision.HasValue && col.Scale.HasValue)
            sb.Append(".Precision(").Append(col.Precision.Value).Append(", ").Append(col.Scale.Value).Append(")");
        if (col.HasDefault && col.DefaultExpression != null)
            sb.Append(".Default(\"").Append(EscapeString(col.DefaultExpression)).Append("\")");
        else if (col.HasDefault)
            sb.Append(".HasDefault()");
        if (col.MappedName != null)
            sb.Append(".MapTo(\"").Append(EscapeString(col.MappedName)).Append("\")");
        if (col.CustomTypeMapping != null)
            sb.Append(".CustomTypeMapping(\"").Append(EscapeString(col.CustomTypeMapping)).Append("\")");

        sb.AppendLine(")");
    }

    private static void GenerateForeignKey(StringBuilder sb, ForeignKeyDef fk)
    {
        sb.Append("            .AddForeignKey(\"").Append(EscapeString(fk.ConstraintName)).Append("\"");
        sb.Append(", \"").Append(EscapeString(fk.ColumnName)).Append("\"");
        sb.Append(", \"").Append(EscapeString(fk.ReferencedTable)).Append("\"");
        sb.Append(", \"").Append(EscapeString(fk.ReferencedColumn)).Append("\"");
        if (fk.OnDelete != ForeignKeyAction.NoAction)
            sb.Append(", ").Append("ForeignKeyAction.").Append(fk.OnDelete);
        if (fk.OnUpdate != ForeignKeyAction.NoAction)
            sb.Append(", ").Append("ForeignKeyAction.").Append(fk.OnUpdate);
        sb.AppendLine(")");
    }

    private static void GenerateIndex(StringBuilder sb, IndexDef idx)
    {
        sb.Append("            .AddIndex(\"").Append(EscapeString(idx.Name)).Append("\"");
        sb.Append(", new[] { ");
        for (var i = 0; i < idx.Columns.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append("\"").Append(EscapeString(idx.Columns[i])).Append("\"");
        }
        sb.Append(" }");
        if (idx.IsUnique)
            sb.Append(", isUnique: true");
        if (idx.Filter != null)
            sb.Append(", filter: \"").Append(EscapeString(idx.Filter)).Append("\"");
        if (idx.Method != null)
            sb.Append(", method: \"").Append(EscapeString(idx.Method)).Append("\"");
        sb.AppendLine(")");
    }

    private static string SanitizeName(string name) => CodeGenHelpers.SanitizeCSharpName(name);

    private static string EscapeString(string value) => CodeGenHelpers.EscapeCSharpString(value ?? "");
}
