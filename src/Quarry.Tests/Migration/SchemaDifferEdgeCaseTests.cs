using Quarry.Shared.Migration;

namespace Quarry.Tests.Migration;

public class SchemaDifferEdgeCaseTests
{
    [Test]
    public void Diff_BothEmpty_NoSteps()
    {
        var from = BuildSnapshot(1, Array.Empty<TableDef>());
        var to = BuildSnapshot(2, Array.Empty<TableDef>());

        var steps = SchemaDiffer.Diff(from, to);

        Assert.That(steps, Is.Empty);
    }

    [Test]
    public void Diff_NullFrom_EmptyTo_NoSteps()
    {
        var to = BuildSnapshot(1, Array.Empty<TableDef>());

        var steps = SchemaDiffer.Diff(null, to);

        Assert.That(steps, Is.Empty);
    }

    [Test]
    public void Diff_TablesWithSchemaName_MatchesByName()
    {
        var from = BuildSnapshot(1, new[]
        {
            new TableDef("users", "dbo", NamingStyleKind.Exact,
                new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) },
                Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
        });
        var to = BuildSnapshot(2, new[]
        {
            new TableDef("users", "dbo", NamingStyleKind.Exact,
                new[]
                {
                    BuildColumn("id", "int", ColumnKind.PrimaryKey),
                    BuildColumn("name", "string", ColumnKind.Standard, isNullable: true)
                },
                Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
        });

        var steps = SchemaDiffer.Diff(from, to);

        Assert.That(steps, Has.Count.EqualTo(1));
        Assert.That(steps[0].StepType, Is.EqualTo(MigrationStepType.AddColumn));
    }

    [Test]
    public void Diff_ColumnMultipleModifiersChanged_SingleAlterColumn()
    {
        var from = BuildSnapshot(1, new[]
        {
            BuildTable("users", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                new ColumnDef("bio", "string", true, ColumnKind.Standard, maxLength: 100)
            })
        });
        var to = BuildSnapshot(2, new[]
        {
            BuildTable("users", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                new ColumnDef("bio", "string", false, ColumnKind.Standard, maxLength: 500)
            })
        });

        var steps = SchemaDiffer.Diff(from, to);

        var alterSteps = steps.Where(s => s.StepType == MigrationStepType.AlterColumn && s.ColumnName == "bio").ToList();
        Assert.That(alterSteps, Has.Count.EqualTo(1));
    }

    [Test]
    public void Diff_CaseInsensitive_MatchesCorrectly()
    {
        var from = BuildSnapshot(1, new[]
        {
            BuildTable("Users", new[] { BuildColumn("Id", "int", ColumnKind.PrimaryKey) })
        });
        var to = BuildSnapshot(2, new[]
        {
            BuildTable("users", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) })
        });

        var steps = SchemaDiffer.Diff(from, to);

        // Should match as same table (case insensitive), no create/drop
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.CreateTable), Is.False);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.DropTable), Is.False);
    }

    [Test]
    public void Diff_OnlyIndexChanges_NoTableSteps()
    {
        var from = BuildSnapshot(1, new[]
        {
            BuildTable("users", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                BuildColumn("email", "string", ColumnKind.Standard)
            })
        });
        var to = BuildSnapshot(2, new[]
        {
            new TableDef("users", null, NamingStyleKind.Exact,
                new[]
                {
                    BuildColumn("id", "int", ColumnKind.PrimaryKey),
                    BuildColumn("email", "string", ColumnKind.Standard)
                },
                Array.Empty<ForeignKeyDef>(),
                new[] { new IndexDef("IX_users_email", new[] { "email" }, true, null) })
        });

        var steps = SchemaDiffer.Diff(from, to);

        Assert.That(steps.Any(s => s.StepType == MigrationStepType.CreateTable), Is.False);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.DropTable), Is.False);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.AddIndex), Is.True);
    }

    #region SchemaSnapshot.GetTable

    [Test]
    public void GetTable_ExactMatch_ReturnsTable()
    {
        var snapshot = BuildSnapshot(1, new[]
        {
            new TableDef("users", null, NamingStyleKind.Exact,
                new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) },
                Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
        });

        var result = snapshot.GetTable("users");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.TableName, Is.EqualTo("users"));
    }

    [Test]
    public void GetTable_CaseInsensitive_ReturnsTable()
    {
        var snapshot = BuildSnapshot(1, new[]
        {
            new TableDef("users", null, NamingStyleKind.Exact,
                new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) },
                Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
        });

        var result = snapshot.GetTable("Users");
        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public void GetTable_NotFound_ReturnsNull()
    {
        var snapshot = BuildSnapshot(1, new[]
        {
            new TableDef("users", null, NamingStyleKind.Exact,
                new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) },
                Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
        });

        Assert.That(snapshot.GetTable("posts"), Is.Null);
    }

    [Test]
    public void GetTable_EmptyTables_ReturnsNull()
    {
        var snapshot = BuildSnapshot(1, Array.Empty<TableDef>());
        Assert.That(snapshot.GetTable("users"), Is.Null);
    }

    #endregion

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
