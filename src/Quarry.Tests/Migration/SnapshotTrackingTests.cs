using Quarry.Shared.Migration;

namespace Quarry.Tests.Migration;

/// <summary>
/// Tests for new snapshot tracking properties: ComputedExpression, DescendingColumns,
/// Collation, CharacterSet, and schema move detection.
/// </summary>
public class SnapshotTrackingTests
{
    #region ComputedExpression

    [Test]
    public void ColumnDef_ComputedExpression_IncludedInEquality()
    {
        var col1 = new ColumnDef("total", "decimal", false, ColumnKind.Standard, isComputed: true, computedExpression: "price * qty");
        var col2 = new ColumnDef("total", "decimal", false, ColumnKind.Standard, isComputed: true, computedExpression: "price * quantity");

        Assert.That(col1.Equals(col2), Is.False);
    }

    [Test]
    public void ColumnDef_SameComputedExpression_AreEqual()
    {
        var col1 = new ColumnDef("total", "decimal", false, ColumnKind.Standard, isComputed: true, computedExpression: "price * qty");
        var col2 = new ColumnDef("total", "decimal", false, ColumnKind.Standard, isComputed: true, computedExpression: "price * qty");

        Assert.That(col1.Equals(col2), Is.True);
    }

    [Test]
    public void Diff_ComputedExpressionChanged_EmitsAlterColumn()
    {
        var from = BuildSnapshot(1, new[]
        {
            BuildTable("orders", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                new ColumnDef("total", "decimal", false, ColumnKind.Standard, isComputed: true, computedExpression: "price * qty")
            })
        });
        var to = BuildSnapshot(2, new[]
        {
            BuildTable("orders", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                new ColumnDef("total", "decimal", false, ColumnKind.Standard, isComputed: true, computedExpression: "price * quantity")
            })
        });

        var steps = SchemaDiffer.Diff(from, to);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.AlterColumn && s.ColumnName == "total"), Is.True);
    }

    [Test]
    public void SnapshotCodeGen_EmitsComputedExpression()
    {
        var snapshot = new SchemaSnapshot(1, "test", DateTimeOffset.UtcNow, null, new[]
        {
            new TableDef("orders", null, NamingStyleKind.Exact, new[]
            {
                new ColumnDef("total", "decimal", false, ColumnKind.Standard, isComputed: true, computedExpression: "price * qty")
            }, Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
        });

        var code = SnapshotCodeGenerator.GenerateBuildMethod(snapshot);
        Assert.That(code, Does.Contain(".Computed(\"price * qty\")"));
    }

    #endregion

    #region DescendingColumns

    [Test]
    public void IndexDef_DescendingColumns_IncludedInEquality()
    {
        var idx1 = new IndexDef("IX_test", new[] { "col_a", "col_b" }, descendingColumns: new[] { false, true });
        var idx2 = new IndexDef("IX_test", new[] { "col_a", "col_b" }, descendingColumns: new[] { false, false });

        Assert.That(idx1.Equals(idx2), Is.False);
    }

    [Test]
    public void IndexDef_SameDescending_AreEqual()
    {
        var idx1 = new IndexDef("IX_test", new[] { "col_a", "col_b" }, descendingColumns: new[] { true, false });
        var idx2 = new IndexDef("IX_test", new[] { "col_a", "col_b" }, descendingColumns: new[] { true, false });

        Assert.That(idx1.Equals(idx2), Is.True);
    }

    [Test]
    public void IndexDef_NullVsEmptyDescending_AreEqual()
    {
        var idx1 = new IndexDef("IX_test", new[] { "col_a" });
        var idx2 = new IndexDef("IX_test", new[] { "col_a" }, descendingColumns: null);

        Assert.That(idx1.Equals(idx2), Is.True);
    }

    [Test]
    public void Diff_IndexDescendingChanged_EmitsDropAndRecreate()
    {
        var from = BuildSnapshot(1, new[]
        {
            BuildTable("users", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                BuildColumn("email", "string", ColumnKind.Standard)
            }, indexes: new[]
            {
                new IndexDef("IX_users_email", new[] { "email" }, descendingColumns: null)
            })
        });
        var to = BuildSnapshot(2, new[]
        {
            BuildTable("users", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                BuildColumn("email", "string", ColumnKind.Standard)
            }, indexes: new[]
            {
                new IndexDef("IX_users_email", new[] { "email" }, descendingColumns: new[] { true })
            })
        });

        var steps = SchemaDiffer.Diff(from, to);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.DropIndex), Is.True);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.AddIndex), Is.True);
    }

    [Test]
    public void SnapshotCodeGen_EmitsDescendingColumns()
    {
        var snapshot = new SchemaSnapshot(1, "test", DateTimeOffset.UtcNow, null, new[]
        {
            new TableDef("users", null, NamingStyleKind.Exact, new[]
            {
                new ColumnDef("id", "int", false, ColumnKind.PrimaryKey)
            }, Array.Empty<ForeignKeyDef>(), new[]
            {
                new IndexDef("IX_test", new[] { "col_a", "col_b" }, descendingColumns: new[] { false, true })
            })
        });

        var code = SnapshotCodeGenerator.GenerateBuildMethod(snapshot);
        Assert.That(code, Does.Contain("descendingColumns: new[] { false, true }"));
    }

    [Test]
    public void MigrationCodeGen_EmitsDescendingInAddIndex()
    {
        var steps = new List<MigrationStep>
        {
            new MigrationStep(
                MigrationStepType.AddIndex,
                StepClassification.Safe,
                "users", null, null,
                null,
                new IndexDef("IX_test", new[] { "name", "date" }, descendingColumns: new[] { false, true }),
                "Add index")
        };

        var snapshot = new SchemaSnapshot(1, "test", DateTimeOffset.UtcNow, null, Array.Empty<TableDef>());
        var code = MigrationCodeGenerator.GenerateMigrationClass(1, "test", steps, null, snapshot, "TestNs");
        Assert.That(code, Does.Contain("descending: new[] { false, true }"));
    }

    #endregion

    #region Collation

    [Test]
    public void ColumnDef_Collation_IncludedInEquality()
    {
        var col1 = new ColumnDef("name", "string", false, ColumnKind.Standard, collation: "Latin1_General_CI_AS");
        var col2 = new ColumnDef("name", "string", false, ColumnKind.Standard, collation: "Latin1_General_CS_AS");

        Assert.That(col1.Equals(col2), Is.False);
    }

    [Test]
    public void Diff_CollationChanged_EmitsAlterColumn()
    {
        var from = BuildSnapshot(1, new[]
        {
            BuildTable("users", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                new ColumnDef("name", "string", false, ColumnKind.Standard, collation: "Latin1_General_CI_AS")
            })
        });
        var to = BuildSnapshot(2, new[]
        {
            BuildTable("users", new[]
            {
                BuildColumn("id", "int", ColumnKind.PrimaryKey),
                new ColumnDef("name", "string", false, ColumnKind.Standard, collation: "Latin1_General_CS_AS")
            })
        });

        var steps = SchemaDiffer.Diff(from, to);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.AlterColumn && s.ColumnName == "name"), Is.True);
    }

    [Test]
    public void SnapshotCodeGen_EmitsCollation()
    {
        var snapshot = new SchemaSnapshot(1, "test", DateTimeOffset.UtcNow, null, new[]
        {
            new TableDef("users", null, NamingStyleKind.Exact, new[]
            {
                new ColumnDef("name", "string", false, ColumnKind.Standard, collation: "Latin1_General_CI_AS")
            }, Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
        });

        var code = SnapshotCodeGenerator.GenerateBuildMethod(snapshot);
        Assert.That(code, Does.Contain(".Collation(\"Latin1_General_CI_AS\")"));
    }

    [Test]
    public void MigrationCodeGen_EmitsCollationInAlterColumn()
    {
        var oldCol = new ColumnDef("name", "string", false, ColumnKind.Standard, collation: "Latin1_General_CI_AS");
        var newCol = new ColumnDef("name", "string", false, ColumnKind.Standard, collation: "Latin1_General_CS_AS");

        var steps = new List<MigrationStep>
        {
            new MigrationStep(
                MigrationStepType.AlterColumn,
                StepClassification.Cautious,
                "users", null, "name",
                oldCol, newCol,
                "Alter column")
        };

        var snapshot = new SchemaSnapshot(1, "test", DateTimeOffset.UtcNow, null, Array.Empty<TableDef>());
        var code = MigrationCodeGenerator.GenerateMigrationClass(1, "test", steps, null, snapshot, "TestNs");
        Assert.That(code, Does.Contain(".Collation(\"Latin1_General_CS_AS\")"));
    }

    #endregion

    #region CharacterSet

    [Test]
    public void TableDef_CharacterSet_IncludedInEquality()
    {
        var t1 = new TableDef("users", null, NamingStyleKind.Exact,
            new[] { new ColumnDef("id", "int", false, ColumnKind.PrimaryKey) },
            Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>(), characterSet: "utf8mb4");
        var t2 = new TableDef("users", null, NamingStyleKind.Exact,
            new[] { new ColumnDef("id", "int", false, ColumnKind.PrimaryKey) },
            Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>(), characterSet: "utf8");

        Assert.That(t1.Equals(t2), Is.False);
    }

    [Test]
    public void SnapshotCodeGen_EmitsCharacterSet()
    {
        var snapshot = new SchemaSnapshot(1, "test", DateTimeOffset.UtcNow, null, new[]
        {
            new TableDef("users", null, NamingStyleKind.Exact, new[]
            {
                new ColumnDef("id", "int", false, ColumnKind.PrimaryKey)
            }, Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>(), characterSet: "utf8mb4")
        });

        var code = SnapshotCodeGenerator.GenerateBuildMethod(snapshot);
        Assert.That(code, Does.Contain(".CharacterSet(\"utf8mb4\")"));
    }

    #endregion

    #region Schema Move

    [Test]
    public void Diff_SchemaNameChanged_EmitsRenameTableWithSchemaTransfer()
    {
        var from = BuildSnapshot(1, new[]
        {
            new TableDef("users", "dbo", NamingStyleKind.Exact,
                new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) },
                Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
        });
        var to = BuildSnapshot(2, new[]
        {
            new TableDef("users", "audit", NamingStyleKind.Exact,
                new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) },
                Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
        });

        var steps = SchemaDiffer.Diff(from, to);

        var renameStep = steps.FirstOrDefault(s => s.StepType == MigrationStepType.RenameTable);
        Assert.That(renameStep, Is.Not.Null);
        Assert.That(renameStep!.OldSchemaName, Is.EqualTo("dbo"));
        Assert.That(renameStep.SchemaName, Is.EqualTo("audit"));
        Assert.That(renameStep.TableName, Is.EqualTo("users"));
    }

    [Test]
    public void Diff_SchemaNameNull_ToNonNull_EmitsSchemaTransfer()
    {
        var from = BuildSnapshot(1, new[]
        {
            new TableDef("users", null, NamingStyleKind.Exact,
                new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) },
                Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
        });
        var to = BuildSnapshot(2, new[]
        {
            new TableDef("users", "audit", NamingStyleKind.Exact,
                new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) },
                Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
        });

        var steps = SchemaDiffer.Diff(from, to);
        Assert.That(steps.Any(s => s.StepType == MigrationStepType.RenameTable), Is.True);
    }

    [Test]
    public void Diff_SameSchema_NoSchemaTransfer()
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
                new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) },
                Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
        });

        var steps = SchemaDiffer.Diff(from, to);
        Assert.That(steps, Is.Empty);
    }

    [Test]
    public void MigrationCodeGen_EmitsSchemaTransferInRenameTable()
    {
        var steps = new List<MigrationStep>
        {
            new MigrationStep(
                MigrationStepType.RenameTable,
                StepClassification.Cautious,
                "users", "audit", null,
                "users", "users",
                "Transfer table 'users' from schema 'dbo' to 'audit'",
                oldSchemaName: "dbo")
        };

        var snapshot = new SchemaSnapshot(1, "test", DateTimeOffset.UtcNow, null, Array.Empty<TableDef>());
        var code = MigrationCodeGenerator.GenerateMigrationClass(1, "test", steps, null, snapshot, "TestNs");
        Assert.That(code, Does.Contain("oldSchema: \"dbo\""));
        Assert.That(code, Does.Contain("newSchema: \"audit\""));
    }

    [Test]
    public void MigrationCodeGen_DowngradeReverses_SchemaTransfer()
    {
        var steps = new List<MigrationStep>
        {
            new MigrationStep(
                MigrationStepType.RenameTable,
                StepClassification.Cautious,
                "users", "audit", null,
                "users", "users",
                "Transfer table 'users' from schema 'dbo' to 'audit'",
                oldSchemaName: "dbo")
        };

        var snapshot = new SchemaSnapshot(1, "test", DateTimeOffset.UtcNow, null, Array.Empty<TableDef>());
        var code = MigrationCodeGenerator.GenerateMigrationClass(1, "test", steps, null, snapshot, "TestNs");

        // Downgrade should reverse: oldSchema=audit (was new), newSchema=dbo (was old)
        Assert.That(code, Does.Contain("oldSchema: \"audit\""));
        Assert.That(code, Does.Contain("newSchema: \"dbo\""));
    }

    #endregion

    #region DDL Rendering — Schema Transfer

    [Test]
    public void DdlRenderer_SchemaTransfer_SqlServer()
    {
        var builder = new global::Quarry.Migration.MigrationBuilder();
        builder.RenameTable("users", "users", "dbo", "audit");
        var sql = builder.BuildSql(global::Quarry.SqlDialect.SqlServer);
        Assert.That(sql, Does.Contain("ALTER SCHEMA"));
        Assert.That(sql, Does.Contain("TRANSFER"));
    }

    [Test]
    public void DdlRenderer_SchemaTransfer_PostgreSQL()
    {
        var builder = new global::Quarry.Migration.MigrationBuilder();
        builder.RenameTable("users", "users", "public", "audit");
        var sql = builder.BuildSql(global::Quarry.SqlDialect.PostgreSQL);
        Assert.That(sql, Does.Contain("SET SCHEMA"));
    }

    [Test]
    public void DdlRenderer_SchemaTransfer_MySQL()
    {
        var builder = new global::Quarry.Migration.MigrationBuilder();
        builder.RenameTable("users", "users", "old_db", "new_db");
        var sql = builder.BuildSql(global::Quarry.SqlDialect.MySQL);
        Assert.That(sql, Does.Contain("RENAME TABLE"));
    }

    [Test]
    public void DdlRenderer_SchemaTransfer_SQLite_EmitsComment()
    {
        var builder = new global::Quarry.Migration.MigrationBuilder();
        builder.RenameTable("users", "users", "old", "new");
        var sql = builder.BuildSql(global::Quarry.SqlDialect.SQLite);
        Assert.That(sql, Does.Contain("-- SQLite does not support schema namespaces"));
    }

    [Test]
    public void DdlRenderer_SchemaTransferAndRename_EmitsBothStatements()
    {
        var builder = new global::Quarry.Migration.MigrationBuilder();
        builder.RenameTable("users", "accounts", "dbo", "audit");
        var sql = builder.BuildSql(global::Quarry.SqlDialect.SqlServer);
        // Should emit schema transfer first, then rename
        Assert.That(sql, Does.Contain("ALTER SCHEMA"));
        Assert.That(sql, Does.Contain("TRANSFER"));
        Assert.That(sql, Does.Contain("sp_rename"));
    }

    #endregion

    #region Code gen — plain rename with schema (regression)

    [Test]
    public void MigrationCodeGen_PlainRenameWithSchema_EmitsThreeArgOverload()
    {
        // A plain rename (no schema transfer) that has a SchemaName should use
        // the 3-arg RenameTable overload, not the 4-arg schema transfer overload.
        var steps = new List<MigrationStep>
        {
            new MigrationStep(
                MigrationStepType.RenameTable,
                StepClassification.Cautious,
                "old_name", "dbo", null,
                "old_name", "new_name",
                "Rename table 'old_name' to 'new_name'")
        };

        var snapshot = new SchemaSnapshot(1, "test", DateTimeOffset.UtcNow, null, Array.Empty<TableDef>());
        var code = MigrationCodeGenerator.GenerateMigrationClass(1, "test", steps, null, snapshot, "TestNs");
        // Should emit 3-arg overload with schema positional parameter
        Assert.That(code, Does.Contain("builder.RenameTable(\"old_name\", \"new_name\", \"dbo\");"));
        // Should NOT emit named oldSchema/newSchema parameters
        Assert.That(code, Does.Not.Contain("oldSchema:"));
        Assert.That(code, Does.Not.Contain("newSchema:"));
    }

    #endregion

    #region DDL Rendering — Descending Columns

    [Test]
    public void DdlRenderer_AddIndex_WithDescending_RendersDesc()
    {
        var builder = new global::Quarry.Migration.MigrationBuilder();
        builder.AddIndex("IX_test", "users", new[] { "name", "date" }, descending: new[] { false, true });
        var sql = builder.BuildSql(global::Quarry.SqlDialect.PostgreSQL);
        Assert.That(sql, Does.Contain("DESC"));
        Assert.That(sql, Does.Not.Contain("[name] DESC")); // Only date should be DESC
    }

    #endregion

    #region DDL Rendering — Collation

    [Test]
    public void DdlRenderer_AlterColumn_WithCollation_SqlServer()
    {
        var builder = new global::Quarry.Migration.MigrationBuilder();
        builder.AlterColumn("users", "name", c => c.ClrType("string").Length(100).Collation("Latin1_General_CI_AS"));
        var sql = builder.BuildSql(global::Quarry.SqlDialect.SqlServer);
        Assert.That(sql, Does.Contain("COLLATE Latin1_General_CI_AS"));
    }

    [Test]
    public void DdlRenderer_AlterColumn_WithCollation_PostgreSQL()
    {
        var builder = new global::Quarry.Migration.MigrationBuilder();
        builder.AlterColumn("users", "name", c => c.ClrType("string").Length(100).Collation("en_US.utf8"));
        var sql = builder.BuildSql(global::Quarry.SqlDialect.PostgreSQL);
        Assert.That(sql, Does.Contain("COLLATE en_US.utf8"));
    }

    [Test]
    public void DdlRenderer_AlterColumn_WithCollation_MySQL()
    {
        var builder = new global::Quarry.Migration.MigrationBuilder();
        builder.AlterColumn("users", "name", c => c.ClrType("string").Length(100).Collation("utf8mb4_unicode_ci"));
        var sql = builder.BuildSql(global::Quarry.SqlDialect.MySQL);
        Assert.That(sql, Does.Contain("COLLATE utf8mb4_unicode_ci"));
    }

    #endregion

    #region Builder round-trip

    [Test]
    public void ColumnDefBuilder_ComputedExpression_RoundTrips()
    {
        var builder = new ColumnDefBuilder();
        var col = builder.Name("total").ClrType("decimal").Computed("price * qty").Build();

        Assert.That(col.IsComputed, Is.True);
        Assert.That(col.ComputedExpression, Is.EqualTo("price * qty"));
    }

    [Test]
    public void ColumnDefBuilder_Collation_RoundTrips()
    {
        var builder = new ColumnDefBuilder();
        var col = builder.Name("name").ClrType("string").Collation("Latin1_General_CI_AS").Build();

        Assert.That(col.Collation, Is.EqualTo("Latin1_General_CI_AS"));
    }

    [Test]
    public void TableDefBuilder_CharacterSet_RoundTrips()
    {
        var builder = new TableDefBuilder();
        var table = builder.Name("users").CharacterSet("utf8mb4")
            .AddColumn(c => c.Name("id").ClrType("int").PrimaryKey())
            .Build();

        Assert.That(table.CharacterSet, Is.EqualTo("utf8mb4"));
    }

    [Test]
    public void TableDefBuilder_AddIndex_DescendingColumns_RoundTrips()
    {
        var builder = new TableDefBuilder();
        var table = builder.Name("users")
            .AddColumn(c => c.Name("id").ClrType("int").PrimaryKey())
            .AddIndex("IX_test", new[] { "col_a", "col_b" }, descendingColumns: new[] { false, true })
            .Build();

        Assert.That(table.Indexes[0].DescendingColumns, Is.EqualTo(new[] { false, true }));
    }

    #endregion

    #region SchemaHasher — new fields

    [Test]
    public void SchemaHasher_CollationChange_ChangesHash()
    {
        var tables1 = new[] { new TableDef("users", null, NamingStyleKind.Exact,
            new[] { new ColumnDef("name", "string", false, ColumnKind.Standard, collation: "CI") },
            Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>()) };
        var tables2 = new[] { new TableDef("users", null, NamingStyleKind.Exact,
            new[] { new ColumnDef("name", "string", false, ColumnKind.Standard, collation: "CS") },
            Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>()) };

        Assert.That(SchemaHasher.ComputeHash(tables1), Is.Not.EqualTo(SchemaHasher.ComputeHash(tables2)));
    }

    [Test]
    public void SchemaHasher_ComputedExpressionChange_ChangesHash()
    {
        var tables1 = new[] { new TableDef("orders", null, NamingStyleKind.Exact,
            new[] { new ColumnDef("total", "decimal", false, ColumnKind.Standard, isComputed: true, computedExpression: "a*b") },
            Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>()) };
        var tables2 = new[] { new TableDef("orders", null, NamingStyleKind.Exact,
            new[] { new ColumnDef("total", "decimal", false, ColumnKind.Standard, isComputed: true, computedExpression: "a+b") },
            Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>()) };

        Assert.That(SchemaHasher.ComputeHash(tables1), Is.Not.EqualTo(SchemaHasher.ComputeHash(tables2)));
    }

    #endregion

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

    private static ColumnDef BuildColumn(string name, string clrType, ColumnKind kind,
        bool isNullable = false)
    {
        return new ColumnDef(name, clrType, isNullable, kind,
            kind == ColumnKind.PrimaryKey, false, false,
            null, null, null, false, null, null, null, null);
    }

    #endregion
}
