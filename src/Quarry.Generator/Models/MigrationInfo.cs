using System.Collections.Generic;

namespace Quarry.Generators.Models;

/// <summary>
/// Metadata extracted from a [Migration] attributed class.
/// </summary>
internal sealed class MigrationInfo
{
    public int Version { get; }
    public string Name { get; }
    public string ClassName { get; }
    public string Namespace { get; }

    /// <summary>Whether the Upgrade method contains DropTable or DropColumn calls.</summary>
    public bool HasDestructiveSteps { get; }

    /// <summary>Whether the Backup method body is non-empty (has statements).</summary>
    public bool HasBackup { get; }

    /// <summary>Whether the Upgrade method contains builder.Sql() calls (data migration).</summary>
    public bool HasSqlStep { get; }

    /// <summary>Whether the Upgrade method contains AlterColumn with .NotNull() (nullable→non-null change).</summary>
    public bool HasAlterColumnNotNull { get; }

    /// <summary>Table names referenced in Upgrade/Downgrade method bodies.</summary>
    public IReadOnlyList<string> ReferencedTableNames { get; }

    /// <summary>Column names referenced in Upgrade/Downgrade method bodies (as "table.column").</summary>
    public IReadOnlyList<string> ReferencedColumnNames { get; }

    public MigrationInfo(int version, string name, string className, string ns,
        bool hasDestructiveSteps = false, bool hasBackup = false, bool hasSqlStep = false,
        bool hasAlterColumnNotNull = false,
        IReadOnlyList<string>? referencedTableNames = null,
        IReadOnlyList<string>? referencedColumnNames = null)
    {
        Version = version;
        Name = name;
        ClassName = className;
        Namespace = ns;
        HasDestructiveSteps = hasDestructiveSteps;
        HasBackup = hasBackup;
        HasSqlStep = hasSqlStep;
        HasAlterColumnNotNull = hasAlterColumnNotNull;
        ReferencedTableNames = referencedTableNames ?? System.Array.Empty<string>();
        ReferencedColumnNames = referencedColumnNames ?? System.Array.Empty<string>();
    }
}
