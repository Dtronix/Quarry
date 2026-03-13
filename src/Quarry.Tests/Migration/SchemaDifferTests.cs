using Quarry.Shared.Migration;

namespace Quarry.Tests.Migration;

public class SchemaDifferTests
{
    #region Table-level diff

    [Test]
    public void Diff_NullFrom_ReturnsCreateTableSteps()
    {
        var to = BuildSnapshot(1, "v1", new[]
        {
            BuildTable("users", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) })
        });

        var steps = SchemaDiffer.Diff(null, to);

        Assert.That(steps, Has.Count.EqualTo(1));
        Assert.That(steps[0].StepType, Is.EqualTo(MigrationStepType.CreateTable));
        Assert.That(steps[0].TableName, Is.EqualTo("users"));
        Assert.That(steps[0].Classification, Is.EqualTo(StepClassification.Safe));
    }

    [Test]
    public void Diff_TableRemoved_ReturnsDropTableStep()
    {
        var from = BuildSnapshot(1, "v1", new[]
        {
            BuildTable("users", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) }),
            BuildTable("posts", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) })
        });
        var to = BuildSnapshot(2, "v2", new[]
        {
            BuildTable("users", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) })
        });

        var steps = SchemaDiffer.Diff(from, to);

        Assert.That(steps.Any(s => s.StepType == MigrationStepType.DropTable && s.TableName == "posts"), Is.True);
    }

    [Test]
    public void Diff_TableAdded_ReturnsCreateTableStep()
    {
        var from = BuildSnapshot(1, "v1", new[]
        {
            BuildTable("users", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) })
        });
        var to = BuildSnapshot(2, "v2", new[]
        {
            BuildTable("users", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) }),
            BuildTable("posts", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) })
        });

        var steps = SchemaDiffer.Diff(from, to);

        Assert.That(steps.Any(s => s.StepType == MigrationStepType.CreateTable && s.TableName == "posts"), Is.True);
    }

    [Test]
    public void Diff_IdenticalSnapshots_ReturnsNoSteps()
    {
        var tables = new[] { BuildTable("users", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) }) };
        var from = BuildSnapshot(1, "v1", tables);
        var to = BuildSnapshot(2, "v2", tables);

        var steps = SchemaDiffer.Diff(from, to);

        Assert.That(steps, Is.Empty);
    }

    #endregion

    #region Column-level diff

    [Test]
    public void Diff_ColumnAdded_ReturnsAddColumnStep()
    {
        var from = BuildSnapshot(1, "v1", new[]
        {
            BuildTable("users", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) })
        });
        var to = BuildSnapshot(2, "v2", new[]
        {
            BuildTable("users", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                BuildColumn("name", "string", ColumnKind.Standard, isNullable: true)
            })
        });

        var steps = SchemaDiffer.Diff(from, to);

        Assert.That(steps, Has.Count.EqualTo(1));
        Assert.That(steps[0].StepType, Is.EqualTo(MigrationStepType.AddColumn));
        Assert.That(steps[0].ColumnName, Is.EqualTo("name"));
    }

    [Test]
    public void Diff_ColumnDropped_ReturnsDropColumnStep()
    {
        var from = BuildSnapshot(1, "v1", new[]
        {
            BuildTable("users", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                BuildColumn("name", "string", ColumnKind.Standard)
            })
        });
        var to = BuildSnapshot(2, "v2", new[]
        {
            BuildTable("users", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) })
        });

        var steps = SchemaDiffer.Diff(from, to);

        Assert.That(steps.Any(s => s.StepType == MigrationStepType.DropColumn && s.ColumnName == "name"), Is.True);
    }

    [Test]
    public void Diff_ColumnTypeChanged_ReturnsAlterColumnStep()
    {
        var from = BuildSnapshot(1, "v1", new[]
        {
            BuildTable("users", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                BuildColumn("age", "string", ColumnKind.Standard)
            })
        });
        var to = BuildSnapshot(2, "v2", new[]
        {
            BuildTable("users", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                BuildColumn("age", "int", ColumnKind.Standard)
            })
        });

        var steps = SchemaDiffer.Diff(from, to);

        Assert.That(steps.Any(s => s.StepType == MigrationStepType.AlterColumn && s.ColumnName == "age"), Is.True);
    }

    [Test]
    public void Diff_ColumnNullabilityChanged_ReturnsAlterColumnStep()
    {
        var from = BuildSnapshot(1, "v1", new[]
        {
            BuildTable("users", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                BuildColumn("name", "string", ColumnKind.Standard, isNullable: true)
            })
        });
        var to = BuildSnapshot(2, "v2", new[]
        {
            BuildTable("users", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                BuildColumn("name", "string", ColumnKind.Standard, isNullable: false)
            })
        });

        var steps = SchemaDiffer.Diff(from, to);

        Assert.That(steps.Any(s => s.StepType == MigrationStepType.AlterColumn && s.ColumnName == "name"), Is.True);
    }

    #endregion

    #region Classification

    [Test]
    public void Diff_DropTable_IsDestructive()
    {
        var from = BuildSnapshot(1, "v1", new[]
        {
            BuildTable("users", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) })
        });
        var to = BuildSnapshot(2, "v2", Array.Empty<TableDef>());

        var steps = SchemaDiffer.Diff(from, to);

        Assert.That(steps[0].Classification, Is.EqualTo(StepClassification.Destructive));
    }

    [Test]
    public void Diff_AddNullableColumn_IsSafe()
    {
        var from = BuildSnapshot(1, "v1", new[]
        {
            BuildTable("users", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) })
        });
        var to = BuildSnapshot(2, "v2", new[]
        {
            BuildTable("users", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                BuildColumn("bio", "string", ColumnKind.Standard, isNullable: true)
            })
        });

        var steps = SchemaDiffer.Diff(from, to);

        Assert.That(steps[0].Classification, Is.EqualTo(StepClassification.Safe));
    }

    [Test]
    public void Diff_AddNonNullableColumnWithoutDefault_IsDestructive()
    {
        var from = BuildSnapshot(1, "v1", new[]
        {
            BuildTable("users", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) })
        });
        var to = BuildSnapshot(2, "v2", new[]
        {
            BuildTable("users", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                BuildColumn("name", "string", ColumnKind.Standard, isNullable: false)
            })
        });

        var steps = SchemaDiffer.Diff(from, to);

        Assert.That(steps[0].Classification, Is.EqualTo(StepClassification.Destructive));
    }

    #endregion

    #region Step ordering

    [Test]
    public void Diff_CreateTableBeforeAddForeignKey()
    {
        var to = BuildSnapshot(1, "v1", new[]
        {
            BuildTable("users", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) }),
            BuildTable("posts", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                BuildColumn("user_id", "int", ColumnKind.ForeignKey)
            },
            new[] { new ForeignKeyDef("FK_posts_users", "user_id", "users", "id", ForeignKeyAction.NoAction, ForeignKeyAction.NoAction) })
        });

        var steps = SchemaDiffer.Diff(null, to);

        var createTableIndexes = steps
            .Select((s, i) => (s, i))
            .Where(x => x.s.StepType == MigrationStepType.CreateTable)
            .Select(x => x.i)
            .ToList();

        var addFkIndexes = steps
            .Select((s, i) => (s, i))
            .Where(x => x.s.StepType == MigrationStepType.AddForeignKey)
            .Select(x => x.i)
            .ToList();

        if (addFkIndexes.Count > 0 && createTableIndexes.Count > 0)
        {
            Assert.That(createTableIndexes.Max(), Is.LessThan(addFkIndexes.Min()));
        }
    }

    #endregion

    #region Missing scenarios

    [Test]
    public void Diff_ColumnDefaultExpressionChanged_EmitsAlterColumn()
    {
        var from = BuildSnapshot(1, "v1", new[]
        {
            BuildTable("users", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                new ColumnDef("status", "string", false, ColumnKind.Standard, hasDefault: true, defaultExpression: "0")
            })
        });
        var to = BuildSnapshot(2, "v2", new[]
        {
            BuildTable("users", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                new ColumnDef("status", "string", false, ColumnKind.Standard, hasDefault: true, defaultExpression: "1")
            })
        });

        var steps = SchemaDiffer.Diff(from, to);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.AlterColumn && s.ColumnName == "status"), Is.True);
    }

    [Test]
    public void Diff_ColumnIdentityChanged_EmitsAlterColumn()
    {
        var from = BuildSnapshot(1, "v1", new[]
        {
            BuildTable("users", new[]
            {
                new ColumnDef("id", "int", false, ColumnKind.PrimaryKey, isIdentity: false)
            })
        });
        var to = BuildSnapshot(2, "v2", new[]
        {
            BuildTable("users", new[]
            {
                new ColumnDef("id", "int", false, ColumnKind.PrimaryKey, isIdentity: true)
            })
        });

        var steps = SchemaDiffer.Diff(from, to);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.AlterColumn && s.ColumnName == "id"), Is.True);
    }

    [Test]
    public void Diff_ForeignKeyOnDeleteChanged_EmitsDropAndAdd()
    {
        var from = BuildSnapshot(1, "v1", new[]
        {
            BuildTable("posts", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) },
                new[] { new ForeignKeyDef("FK_posts_users", "user_id", "users", "id", ForeignKeyAction.NoAction, ForeignKeyAction.NoAction) })
        });
        var to = BuildSnapshot(2, "v2", new[]
        {
            BuildTable("posts", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) },
                new[] { new ForeignKeyDef("FK_posts_users", "user_id", "users", "id", ForeignKeyAction.Cascade, ForeignKeyAction.NoAction) })
        });

        var steps = SchemaDiffer.Diff(from, to);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.DropForeignKey), Is.True);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.AddForeignKey), Is.True);
    }

    [Test]
    public void Diff_IndexUniquenessChanged_EmitsDropAndAdd()
    {
        var from = BuildSnapshot(1, "v1", new[]
        {
            BuildTable("users", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey), BuildColumn("email", "string", ColumnKind.Standard) },
                indexes: new[] { new IndexDef("IX_users_email", new[] { "email" }, false) })
        });
        var to = BuildSnapshot(2, "v2", new[]
        {
            BuildTable("users", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey), BuildColumn("email", "string", ColumnKind.Standard) },
                indexes: new[] { new IndexDef("IX_users_email", new[] { "email" }, true) })
        });

        var steps = SchemaDiffer.Diff(from, to);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.DropIndex), Is.True);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.AddIndex), Is.True);
    }

    [Test]
    public void Diff_MixedOperations_TableAddDropColumnAlter()
    {
        var from = BuildSnapshot(1, "v1", new[]
        {
            BuildTable("table_a", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                BuildColumn("name", "string", ColumnKind.Standard)
            }),
            BuildTable("logs", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) })
        });
        var to = BuildSnapshot(2, "v2", new[]
        {
            BuildTable("table_a", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                BuildColumn("name", "string", ColumnKind.Standard),
                BuildColumn("email", "string", ColumnKind.Standard, isNullable: true)
            }),
            BuildTable("notifications", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) })
        });

        var steps = SchemaDiffer.Diff(from, to);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.DropTable && s.TableName == "logs"), Is.True);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.CreateTable && s.TableName == "notifications"), Is.True);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.AddColumn && s.ColumnName == "email"), Is.True);
    }

    [Test]
    public void Diff_ColumnMaxLengthChanged_EmitsAlterColumn()
    {
        var from = BuildSnapshot(1, "v1", new[]
        {
            BuildTable("users", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                new ColumnDef("name", "string", false, ColumnKind.Standard, maxLength: 100)
            })
        });
        var to = BuildSnapshot(2, "v2", new[]
        {
            BuildTable("users", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                new ColumnDef("name", "string", false, ColumnKind.Standard, maxLength: 200)
            })
        });

        var steps = SchemaDiffer.Diff(from, to);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.AlterColumn && s.ColumnName == "name"), Is.True);
    }

    [Test]
    public void Diff_AddColumn_NullableWithDefault_IsSafe()
    {
        var from = BuildSnapshot(1, "v1", new[]
        {
            BuildTable("users", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) })
        });
        var to = BuildSnapshot(2, "v2", new[]
        {
            BuildTable("users", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                BuildColumn("status", "string", ColumnKind.Standard, isNullable: false, hasDefault: true)
            })
        });

        var steps = SchemaDiffer.Diff(from, to);
        Assert.That(steps[0].Classification, Is.EqualTo(StepClassification.Safe));
    }

    #endregion

    #region Helpers

    private static SchemaSnapshot BuildSnapshot(int version, string name, IReadOnlyList<TableDef> tables)
    {
        return new SchemaSnapshot(version, name, DateTimeOffset.UtcNow, null, tables);
    }

    private static TableDef BuildTable(string name, IReadOnlyList<ColumnDef> columns,
        IReadOnlyList<ForeignKeyDef>? fks = null, IReadOnlyList<IndexDef>? indexes = null)
    {
        return new TableDef(name, null, NamingStyleKind.Exact, columns,
            fks ?? Array.Empty<ForeignKeyDef>(),
            indexes ?? Array.Empty<IndexDef>());
    }

    private static ColumnDef BuildColumn(string name, string clrType, ColumnKind kind,
        bool isNullable = false, bool hasDefault = false)
    {
        return new ColumnDef(name, clrType, isNullable, kind,
            kind == ColumnKind.PrimaryKey, false, false,
            null, null, null, hasDefault, null, null, null, null);
    }

    #endregion
}
