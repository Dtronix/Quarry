using Quarry.Shared.Migration;

namespace Quarry.Tests.Migration;

public class SchemaDifferForeignKeyTests
{
    [Test]
    public void Diff_ForeignKeyAdded_EmitsAddForeignKey()
    {
        var from = BuildSnapshot(1, new[]
        {
            BuildTable("users", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) }),
            BuildTable("posts", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey), BuildColumn("user_id", "int", ColumnKind.ForeignKey) })
        });
        var to = BuildSnapshot(2, new[]
        {
            BuildTable("users", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) }),
            BuildTable("posts", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey), BuildColumn("user_id", "int", ColumnKind.ForeignKey) },
                new[] { new ForeignKeyDef("FK_posts_users", "user_id", "users", "id", ForeignKeyAction.Cascade, ForeignKeyAction.NoAction) })
        });

        var steps = SchemaDiffer.Diff(from, to);

        Assert.That(steps.Any(s => s.StepType == MigrationStepType.AddForeignKey), Is.True);
    }

    [Test]
    public void Diff_ForeignKeyRemoved_EmitsDropForeignKey()
    {
        var from = BuildSnapshot(1, new[]
        {
            BuildTable("users", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) }),
            BuildTable("posts", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey), BuildColumn("user_id", "int", ColumnKind.ForeignKey) },
                new[] { new ForeignKeyDef("FK_posts_users", "user_id", "users", "id", ForeignKeyAction.Cascade, ForeignKeyAction.NoAction) })
        });
        var to = BuildSnapshot(2, new[]
        {
            BuildTable("users", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) }),
            BuildTable("posts", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey), BuildColumn("user_id", "int", ColumnKind.ForeignKey) })
        });

        var steps = SchemaDiffer.Diff(from, to);

        Assert.That(steps.Any(s => s.StepType == MigrationStepType.DropForeignKey), Is.True);
    }

    [Test]
    public void Diff_ForeignKeyChanged_EmitsDropAndAdd()
    {
        var from = BuildSnapshot(1, new[]
        {
            BuildTable("users", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) }),
            BuildTable("accounts", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) }),
            BuildTable("posts", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey), BuildColumn("owner_id", "int", ColumnKind.ForeignKey) },
                new[] { new ForeignKeyDef("FK_posts_owner", "owner_id", "users", "id", ForeignKeyAction.NoAction, ForeignKeyAction.NoAction) })
        });
        var to = BuildSnapshot(2, new[]
        {
            BuildTable("users", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) }),
            BuildTable("accounts", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) }),
            BuildTable("posts", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey), BuildColumn("owner_id", "int", ColumnKind.ForeignKey) },
                new[] { new ForeignKeyDef("FK_posts_owner", "owner_id", "accounts", "id", ForeignKeyAction.Cascade, ForeignKeyAction.NoAction) })
        });

        var steps = SchemaDiffer.Diff(from, to);

        Assert.That(steps.Any(s => s.StepType == MigrationStepType.DropForeignKey), Is.True);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.AddForeignKey), Is.True);
    }

    [Test]
    public void Diff_IndexAdded_EmitsAddIndex()
    {
        var from = BuildSnapshot(1, new[]
        {
            BuildTable("users", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey), BuildColumn("email", "string", ColumnKind.Standard) })
        });
        var to = BuildSnapshot(2, new[]
        {
            BuildTable("users", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey), BuildColumn("email", "string", ColumnKind.Standard) },
                indexes: new[] { new IndexDef("IX_users_email", new[] { "email" }, true, null) })
        });

        var steps = SchemaDiffer.Diff(from, to);

        Assert.That(steps.Any(s => s.StepType == MigrationStepType.AddIndex), Is.True);
    }

    [Test]
    public void Diff_IndexRemoved_EmitsDropIndex()
    {
        var from = BuildSnapshot(1, new[]
        {
            BuildTable("users", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey), BuildColumn("email", "string", ColumnKind.Standard) },
                indexes: new[] { new IndexDef("IX_users_email", new[] { "email" }, true, null) })
        });
        var to = BuildSnapshot(2, new[]
        {
            BuildTable("users", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey), BuildColumn("email", "string", ColumnKind.Standard) })
        });

        var steps = SchemaDiffer.Diff(from, to);

        Assert.That(steps.Any(s => s.StepType == MigrationStepType.DropIndex), Is.True);
    }

    [Test]
    public void Diff_IndexChanged_EmitsDropAndAdd()
    {
        var from = BuildSnapshot(1, new[]
        {
            BuildTable("users", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                BuildColumn("email", "string", ColumnKind.Standard),
                BuildColumn("name", "string", ColumnKind.Standard)
            },
            indexes: new[] { new IndexDef("IX_users_email", new[] { "email" }, false, null) })
        });
        var to = BuildSnapshot(2, new[]
        {
            BuildTable("users", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                BuildColumn("email", "string", ColumnKind.Standard),
                BuildColumn("name", "string", ColumnKind.Standard)
            },
            indexes: new[] { new IndexDef("IX_users_email", new[] { "email", "name" }, false, null) })
        });

        var steps = SchemaDiffer.Diff(from, to);

        Assert.That(steps.Any(s => s.StepType == MigrationStepType.DropIndex), Is.True);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.AddIndex), Is.True);
    }

    [Test]
    public void Diff_DroppedTableWithFKs_DropForeignKeyBeforeDropTable()
    {
        var from = BuildSnapshot(1, new[]
        {
            BuildTable("users", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) }),
            BuildTable("posts", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey), BuildColumn("user_id", "int", ColumnKind.ForeignKey) },
                new[] { new ForeignKeyDef("FK_posts_users", "user_id", "users", "id", ForeignKeyAction.Cascade, ForeignKeyAction.NoAction) })
        });
        var to = BuildSnapshot(2, new[]
        {
            BuildTable("users", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) })
        });

        var steps = SchemaDiffer.Diff(from, to);

        var dropFkIdx = steps.Select((s, i) => (s, i)).Where(x => x.s.StepType == MigrationStepType.DropForeignKey).Select(x => x.i).ToList();
        var dropTableIdx = steps.Select((s, i) => (s, i)).Where(x => x.s.StepType == MigrationStepType.DropTable).Select(x => x.i).ToList();

        if (dropFkIdx.Count > 0 && dropTableIdx.Count > 0)
        {
            Assert.That(dropFkIdx.Max(), Is.LessThan(dropTableIdx.Min()));
        }
    }

    #region Helpers

    private static SchemaSnapshot BuildSnapshot(int version, IReadOnlyList<TableDef> tables)
    {
        return new SchemaSnapshot(version, $"v{version}", DateTimeOffset.UtcNow, null, tables);
    }

    private static TableDef BuildTable(string name, IReadOnlyList<ColumnDef> columns,
        IReadOnlyList<ForeignKeyDef>? fks = null, IReadOnlyList<IndexDef>? indexes = null)
    {
        return new TableDef(name, null, NamingStyleKind.Exact, columns,
            fks ?? Array.Empty<ForeignKeyDef>(),
            indexes ?? Array.Empty<IndexDef>());
    }

    private static ColumnDef BuildColumn(string name, string clrType, ColumnKind kind, bool isNullable = false)
    {
        return new ColumnDef(name, clrType, isNullable, kind,
            kind == ColumnKind.PrimaryKey, false, false,
            null, null, null, false, null, null, null, null);
    }

    #endregion
}
