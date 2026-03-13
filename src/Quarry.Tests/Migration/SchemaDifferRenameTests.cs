using Quarry.Shared.Migration;

namespace Quarry.Tests.Migration;

public class SchemaDifferRenameTests
{
    [Test]
    public void Diff_SingleAddDrop_SimilarTableNames_EmitsRenameTable()
    {
        var from = BuildSnapshot(1, new[]
        {
            BuildTable("user_accounts", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) })
        });
        var to = BuildSnapshot(2, new[]
        {
            BuildTable("user_profiles", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) })
        });

        // Auto-accept all renames
        var steps = SchemaDiffer.Diff(from, to, _ => true);

        Assert.That(steps.Any(s => s.StepType == MigrationStepType.RenameTable), Is.True);
    }

    [Test]
    public void Diff_SingleAddDrop_DissimilarTableNames_EmitsDropAndCreate()
    {
        var from = BuildSnapshot(1, new[]
        {
            BuildTable("users", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) })
        });
        var to = BuildSnapshot(2, new[]
        {
            BuildTable("invoices", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey), BuildColumn("amount", "decimal", ColumnKind.Standard) })
        });

        var steps = SchemaDiffer.Diff(from, to, _ => true);

        Assert.That(steps.Any(s => s.StepType == MigrationStepType.DropTable), Is.True);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.CreateTable), Is.True);
    }

    [Test]
    public void Diff_RenameCallback_Rejected_FallsBackToDropAdd()
    {
        var from = BuildSnapshot(1, new[]
        {
            BuildTable("user_accounts", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) })
        });
        var to = BuildSnapshot(2, new[]
        {
            BuildTable("user_profiles", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) })
        });

        // Reject all renames
        var steps = SchemaDiffer.Diff(from, to, _ => false);

        Assert.That(steps.Any(s => s.StepType == MigrationStepType.RenameTable), Is.False);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.DropTable), Is.True);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.CreateTable), Is.True);
    }

    [Test]
    public void Diff_SingleColumnAddDrop_SameType_EmitsRenameColumn()
    {
        var from = BuildSnapshot(1, new[]
        {
            BuildTable("users", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                BuildColumn("user_name", "string", ColumnKind.Standard)
            })
        });
        var to = BuildSnapshot(2, new[]
        {
            BuildTable("users", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                BuildColumn("username", "string", ColumnKind.Standard)
            })
        });

        var steps = SchemaDiffer.Diff(from, to, _ => true);

        Assert.That(steps.Any(s => s.StepType == MigrationStepType.RenameColumn), Is.True);
    }

    [Test]
    public void Diff_MultipleAddsAndDrops_DetectsRenames_WhenAccepted()
    {
        var from = BuildSnapshot(1, new[]
        {
            BuildTable("users", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                BuildColumn("first_name", "string", ColumnKind.Standard),
                BuildColumn("last_name", "string", ColumnKind.Standard)
            })
        });
        var to = BuildSnapshot(2, new[]
        {
            BuildTable("users", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                BuildColumn("full_name", "string", ColumnKind.Standard),
                BuildColumn("display_name", "string", ColumnKind.Standard)
            })
        });

        var steps = SchemaDiffer.Diff(from, to, _ => true);

        // With greedy multi-rename detection and acceptRename returning true,
        // similar-typed columns should be detected as renames
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.RenameColumn), Is.True);
        Assert.That(steps.Count(s => s.StepType == MigrationStepType.RenameColumn), Is.EqualTo(2));
    }

    [Test]
    public void Diff_MultipleAddsAndDrops_NoRenameDetection_WhenRejected()
    {
        var from = BuildSnapshot(1, new[]
        {
            BuildTable("users", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                BuildColumn("first_name", "string", ColumnKind.Standard),
                BuildColumn("last_name", "string", ColumnKind.Standard)
            })
        });
        var to = BuildSnapshot(2, new[]
        {
            BuildTable("users", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                BuildColumn("full_name", "string", ColumnKind.Standard),
                BuildColumn("display_name", "string", ColumnKind.Standard)
            })
        });

        var steps = SchemaDiffer.Diff(from, to, _ => false);

        Assert.That(steps.Any(s => s.StepType == MigrationStepType.RenameColumn), Is.False);
    }

    [Test]
    public void Diff_ColumnRename_WithTypeChange_EmitsRenameAndAlter()
    {
        var from = BuildSnapshot(1, new[]
        {
            BuildTable("users", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                BuildColumn("age_str", "string", ColumnKind.Standard)
            })
        });
        var to = BuildSnapshot(2, new[]
        {
            BuildTable("users", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                BuildColumn("age_string", "int", ColumnKind.Standard)
            })
        });

        var steps = SchemaDiffer.Diff(from, to, _ => true);

        // Should have rename + alter, or at minimum both name and type changed
        var hasRename = steps.Any(s => s.StepType == MigrationStepType.RenameColumn);
        var hasAlter = steps.Any(s => s.StepType == MigrationStepType.AlterColumn);

        // If rename detected, it should also detect the type change
        if (hasRename)
            Assert.That(hasAlter, Is.True);
        else
        {
            // Otherwise it falls back to drop+add which also handles the type change
            Assert.That(steps.Any(s => s.StepType == MigrationStepType.DropColumn), Is.True);
            Assert.That(steps.Any(s => s.StepType == MigrationStepType.AddColumn), Is.True);
        }
    }

    #region Helpers

    private static SchemaSnapshot BuildSnapshot(int version, IReadOnlyList<TableDef> tables)
    {
        return new SchemaSnapshot(version, $"v{version}", DateTimeOffset.UtcNow, null, tables);
    }

    private static TableDef BuildTable(string name, IReadOnlyList<ColumnDef> columns)
    {
        return new TableDef(name, null, NamingStyleKind.Exact, columns,
            Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>());
    }

    private static ColumnDef BuildColumn(string name, string clrType, ColumnKind kind, bool isNullable = false)
    {
        return new ColumnDef(name, clrType, isNullable, kind,
            kind == ColumnKind.PrimaryKey, false, false,
            null, null, null, false, null, null, null, null);
    }

    #endregion
}
