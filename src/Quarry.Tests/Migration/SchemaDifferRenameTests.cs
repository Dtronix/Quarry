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

    // --- Convention-aware deterministic rename (step 2) ---

    [Test]
    public void Diff_ColumnRename_SnakeToPascal_IsDeterministic_UnderDefaultCallback()
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
                BuildColumn("UserName", "string", ColumnKind.Standard)
            })
        });

        // NO accept-all callback — the default (null) path. Convention rename must still fire.
        var steps = SchemaDiffer.Diff(from, to);

        Assert.That(steps.Any(s => s.StepType == MigrationStepType.RenameColumn), Is.True);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.DropColumn), Is.False);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.AddColumn), Is.False);
    }

    [Test]
    public void Diff_ColumnRename_Canonical_IgnoresRejectCallback()
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
                BuildColumn("UserName", "string", ColumnKind.Standard)
            })
        });

        // Even an explicit reject must NOT turn a canonical rename into drop+add.
        var steps = SchemaDiffer.Diff(from, to, _ => false);

        Assert.That(steps.Any(s => s.StepType == MigrationStepType.RenameColumn), Is.True);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.DropColumn), Is.False);
    }

    [Test]
    public void Diff_TableRename_SnakeToPascal_IsDeterministic()
    {
        var from = BuildSnapshot(1, new[]
        {
            BuildTable("order_items", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) })
        });
        var to = BuildSnapshot(2, new[]
        {
            BuildTable("OrderItems", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) })
        });

        var steps = SchemaDiffer.Diff(from, to);

        Assert.That(steps.Any(s => s.StepType == MigrationStepType.RenameTable), Is.True);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.DropTable), Is.False);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.CreateTable), Is.False);
    }

    [Test]
    public void Diff_CanonicalRename_WithTypeChange_EmitsRenameAndAlter()
    {
        var from = BuildSnapshot(1, new[]
        {
            BuildTable("users", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                BuildColumn("is_active", "bool", ColumnKind.Standard)
            })
        });
        var to = BuildSnapshot(2, new[]
        {
            BuildTable("users", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                BuildColumn("IsActive", "int", ColumnKind.Standard) // canonical-equal name, type changed
            })
        });

        var steps = SchemaDiffer.Diff(from, to, _ => false);

        Assert.That(steps.Any(s => s.StepType == MigrationStepType.RenameColumn), Is.True);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.AlterColumn), Is.True);
    }

    [Test]
    public void Diff_CanonicalCollision_DoesNotDeterministicallyRename()
    {
        // Two added columns share a canonical form ("username") — ambiguous, so the
        // deterministic pass must NOT match; with a reject callback it falls to drop+add.
        // (Separator variants, not case variants, so they don't collapse under the
        // case-insensitive same-name match that runs first.)
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
                BuildColumn("username", "string", ColumnKind.Standard),
                BuildColumn("user-name", "string", ColumnKind.Standard)
            })
        });

        var steps = SchemaDiffer.Diff(from, to, _ => false);

        Assert.That(steps.Any(s => s.StepType == MigrationStepType.RenameColumn), Is.False);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.DropColumn), Is.True);
    }

    [Test]
    public void Diff_GenuinelyDifferentNames_AreNotCanonicalMatched()
    {
        var from = BuildSnapshot(1, new[]
        {
            BuildTable("users", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                BuildColumn("first_name", "string", ColumnKind.Standard)
            })
        });
        var to = BuildSnapshot(2, new[]
        {
            BuildTable("users", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                BuildColumn("full_name", "string", ColumnKind.Standard)
            })
        });

        // Reject heuristic renames; canonical forms differ ("firstname" vs "fullname"),
        // so no rename should be emitted — it must be drop+add.
        var steps = SchemaDiffer.Diff(from, to, _ => false);

        Assert.That(steps.Any(s => s.StepType == MigrationStepType.RenameColumn), Is.False);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.DropColumn), Is.True);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.AddColumn), Is.True);
    }

    [Test]
    public void Diff_CanonicalTableRename_WithSchemaMove_TransfersSchema()
    {
        // A canonical name rename (order_items -> OrderItems) that ALSO moves schema (legacy -> dbo)
        // must carry the schema transfer, not silently drop it.
        var from = BuildSnapshot(1, new[]
        {
            BuildTable("order_items", "legacy", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) })
        });
        var to = BuildSnapshot(2, new[]
        {
            BuildTable("OrderItems", "dbo", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) })
        });

        var steps = SchemaDiffer.Diff(from, to);

        var rename = steps.SingleOrDefault(s => s.StepType == MigrationStepType.RenameTable);
        Assert.That(rename, Is.Not.Null);
        Assert.That((string?)rename!.OldValue, Is.EqualTo("order_items"));
        Assert.That((string?)rename.NewValue, Is.EqualTo("OrderItems"));
        Assert.That(rename.OldSchemaName, Is.EqualTo("legacy"));   // schema move preserved
        Assert.That(rename.SchemaName, Is.EqualTo("dbo"));
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.DropTable), Is.False);
    }

    [Test]
    public void Diff_CanonicalTableRename_SameSchema_HasNoSchemaTransfer()
    {
        var from = BuildSnapshot(1, new[]
        {
            BuildTable("order_items", "sales", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) })
        });
        var to = BuildSnapshot(2, new[]
        {
            BuildTable("OrderItems", "sales", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) })
        });

        var steps = SchemaDiffer.Diff(from, to);

        var rename = steps.Single(s => s.StepType == MigrationStepType.RenameTable);
        Assert.That(rename.OldSchemaName, Is.Null);            // no spurious transfer when schema is unchanged
        Assert.That(rename.SchemaName, Is.EqualTo("sales"));
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

    private static TableDef BuildTable(string name, string? schema, IReadOnlyList<ColumnDef> columns)
    {
        return new TableDef(name, schema, NamingStyleKind.Exact, columns,
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
