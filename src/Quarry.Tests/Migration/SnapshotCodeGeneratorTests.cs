using Quarry.Shared.Migration;

namespace Quarry.Tests.Migration;

public class SnapshotCodeGeneratorTests
{
    [Test]
    public void GenerateSnapshotClass_EmitsValidCSharp()
    {
        var snapshot = new SchemaSnapshot(1, "InitialCreate", DateTimeOffset.UtcNow, null,
            new[]
            {
                new TableDef("users", null, NamingStyleKind.SnakeCase,
                    new[]
                    {
                        new ColumnDef("user_id", "int", false, ColumnKind.PrimaryKey, isIdentity: true),
                        new ColumnDef("user_name", "string", false, ColumnKind.Standard, maxLength: 100)
                    },
                    Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
            });

        var code = SnapshotCodeGenerator.GenerateSnapshotClass(snapshot, "MyApp.Migrations");

        Assert.That(code, Does.Contain("namespace MyApp.Migrations;"));
        Assert.That(code, Does.Contain("[MigrationSnapshot("));
        Assert.That(code, Does.Contain("Version = 1"));
        Assert.That(code, Does.Contain("InitialCreate"));
        Assert.That(code, Does.Contain("SchemaHash = "));
        Assert.That(code, Does.Contain(".Name(\"users\")"));
        Assert.That(code, Does.Contain(".Name(\"user_id\")"));
        Assert.That(code, Does.Contain(".PrimaryKey()"));
        Assert.That(code, Does.Contain(".Identity()"));
        Assert.That(code, Does.Contain(".Length(100)"));
    }

    [Test]
    public void GenerateSnapshotClass_EmitsNamingStyle()
    {
        var snapshot = new SchemaSnapshot(1, "Test", DateTimeOffset.UtcNow, null,
            new[]
            {
                new TableDef("users", null, NamingStyleKind.SnakeCase,
                    new[] { new ColumnDef("id", "int", false, ColumnKind.PrimaryKey) },
                    Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
            });

        var code = SnapshotCodeGenerator.GenerateSnapshotClass(snapshot, "Test");

        Assert.That(code, Does.Contain("NamingStyleKind.SnakeCase"));
    }

    [Test]
    public void GenerateSnapshotClass_EmitsForeignKeys()
    {
        var snapshot = new SchemaSnapshot(1, "WithFk", DateTimeOffset.UtcNow, null,
            new[]
            {
                new TableDef("posts", null, NamingStyleKind.Exact,
                    new[] { new ColumnDef("id", "int", false, ColumnKind.PrimaryKey) },
                    new[] { new ForeignKeyDef("FK_posts_users", "user_id", "users", "id", ForeignKeyAction.Cascade, ForeignKeyAction.NoAction) },
                    Array.Empty<IndexDef>())
            });

        var code = SnapshotCodeGenerator.GenerateSnapshotClass(snapshot, "Test");

        Assert.That(code, Does.Contain("FK_posts_users"));
        Assert.That(code, Does.Contain("ForeignKeyAction.Cascade"));
    }

    [Test]
    public void GenerateSnapshotClass_EmitsParentVersion()
    {
        var snapshot = new SchemaSnapshot(2, "AddPosts", DateTimeOffset.UtcNow, 1,
            Array.Empty<TableDef>());

        var code = SnapshotCodeGenerator.GenerateSnapshotClass(snapshot, "Test");

        Assert.That(code, Does.Contain("ParentVersion = 1"));
        Assert.That(code, Does.Contain(".SetParentVersion(1)"));
    }

    [Test]
    public void GenerateSnapshotFromSchema_IncludesAllTables()
    {
        var tables = new[]
        {
            new TableDef("users", null, NamingStyleKind.Exact,
                new[] { new ColumnDef("id", "int", false, ColumnKind.PrimaryKey) },
                Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>()),
            new TableDef("posts", null, NamingStyleKind.Exact,
                new[] { new ColumnDef("id", "int", false, ColumnKind.PrimaryKey) },
                Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
        };

        var code = SnapshotCodeGenerator.GenerateSnapshotFromSchema(tables, 1, "Init", null, "Test");

        Assert.That(code, Does.Contain(".Name(\"users\")"));
        Assert.That(code, Does.Contain(".Name(\"posts\")"));
    }

    [Test]
    public void GenerateSnapshotClass_NullableColumn_EmitsNullable()
    {
        var snapshot = new SchemaSnapshot(1, "Test", DateTimeOffset.UtcNow, null,
            new[]
            {
                new TableDef("users", null, NamingStyleKind.Exact,
                    new[]
                    {
                        new ColumnDef("id", "int", false, ColumnKind.PrimaryKey),
                        new ColumnDef("bio", "string", true, ColumnKind.Standard)
                    },
                    Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
            });

        var code = SnapshotCodeGenerator.GenerateSnapshotClass(snapshot, "Test");

        Assert.That(code, Does.Contain(".Nullable()"));
    }

    [Test]
    public void GenerateSnapshotClass_ClientGeneratedColumn_EmitsClientGenerated()
    {
        var snapshot = new SchemaSnapshot(1, "Test", DateTimeOffset.UtcNow, null,
            new[]
            {
                new TableDef("users", null, NamingStyleKind.Exact,
                    new[]
                    {
                        new ColumnDef("id", "Guid", false, ColumnKind.PrimaryKey, isClientGenerated: true)
                    },
                    Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
            });

        var code = SnapshotCodeGenerator.GenerateSnapshotClass(snapshot, "Test");

        Assert.That(code, Does.Contain("ClientGenerated"));
    }

    [Test]
    public void GenerateSnapshotClass_ComputedColumn_EmitsComputed()
    {
        var snapshot = new SchemaSnapshot(1, "Test", DateTimeOffset.UtcNow, null,
            new[]
            {
                new TableDef("users", null, NamingStyleKind.Exact,
                    new[]
                    {
                        new ColumnDef("id", "int", false, ColumnKind.PrimaryKey),
                        new ColumnDef("full_name", "string", true, ColumnKind.Standard, isComputed: true)
                    },
                    Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
            });

        var code = SnapshotCodeGenerator.GenerateSnapshotClass(snapshot, "Test");

        Assert.That(code, Does.Contain("Computed"));
    }

    [Test]
    public void GenerateSnapshotClass_CustomTypeMapping_EmitsCustomTypeMapping()
    {
        var snapshot = new SchemaSnapshot(1, "Test", DateTimeOffset.UtcNow, null,
            new[]
            {
                new TableDef("users", null, NamingStyleKind.Exact,
                    new[]
                    {
                        new ColumnDef("id", "int", false, ColumnKind.PrimaryKey),
                        new ColumnDef("data", "string", true, ColumnKind.Standard, customTypeMapping: "jsonb")
                    },
                    Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
            });

        var code = SnapshotCodeGenerator.GenerateSnapshotClass(snapshot, "Test");

        Assert.That(code, Does.Contain("jsonb"));
    }

    [Test]
    public void GenerateSnapshotClass_IndexWithFilter_EmitsFilter()
    {
        var snapshot = new SchemaSnapshot(1, "Test", DateTimeOffset.UtcNow, null,
            new[]
            {
                new TableDef("users", null, NamingStyleKind.Exact,
                    new[] { new ColumnDef("id", "int", false, ColumnKind.PrimaryKey), new ColumnDef("email", "string", true, ColumnKind.Standard) },
                    Array.Empty<ForeignKeyDef>(),
                    new[] { new IndexDef("IX_users_email", new[] { "email" }, true, "email IS NOT NULL") })
            });

        var code = SnapshotCodeGenerator.GenerateSnapshotClass(snapshot, "Test");

        Assert.That(code, Does.Contain("IX_users_email"));
        Assert.That(code, Does.Contain("email IS NOT NULL"));
    }

    #region Flag Combinations

    [Test]
    public void GenerateColumn_HasDefaultWithoutExpression_EmitsHasDefault()
    {
        var snapshot = new SchemaSnapshot(1, "Test", DateTimeOffset.UtcNow, null,
            new[]
            {
                new TableDef("users", null, NamingStyleKind.Exact,
                    new[] { new ColumnDef("status", "string", false, ColumnKind.Standard, hasDefault: true) },
                    Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
            });
        var code = SnapshotCodeGenerator.GenerateSnapshotClass(snapshot, "Test");
        Assert.That(code, Does.Contain(".HasDefault()"));
    }

    [Test]
    public void GenerateColumn_WithMappedName_EmitsMapTo()
    {
        var snapshot = new SchemaSnapshot(1, "Test", DateTimeOffset.UtcNow, null,
            new[]
            {
                new TableDef("users", null, NamingStyleKind.Exact,
                    new[] { new ColumnDef("name", "string", false, ColumnKind.Standard, mappedName: "user_name") },
                    Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
            });
        var code = SnapshotCodeGenerator.GenerateSnapshotClass(snapshot, "Test");
        Assert.That(code, Does.Contain(".MapTo(\"user_name\")"));
    }

    [Test]
    public void GenerateColumn_WithPrecisionAndScale_EmitsPrecision()
    {
        var snapshot = new SchemaSnapshot(1, "Test", DateTimeOffset.UtcNow, null,
            new[]
            {
                new TableDef("products", null, NamingStyleKind.Exact,
                    new[] { new ColumnDef("price", "decimal", false, ColumnKind.Standard, precision: 18, scale: 2) },
                    Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
            });
        var code = SnapshotCodeGenerator.GenerateSnapshotClass(snapshot, "Test");
        Assert.That(code, Does.Contain(".Precision(18, 2)"));
    }

    [Test]
    public void GenerateColumn_PrimaryKeyAndIdentity_EmitsBoth()
    {
        var snapshot = new SchemaSnapshot(1, "Test", DateTimeOffset.UtcNow, null,
            new[]
            {
                new TableDef("users", null, NamingStyleKind.Exact,
                    new[] { new ColumnDef("id", "int", false, ColumnKind.PrimaryKey, isIdentity: true) },
                    Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
            });
        var code = SnapshotCodeGenerator.GenerateSnapshotClass(snapshot, "Test");
        Assert.That(code, Does.Contain(".PrimaryKey()"));
        Assert.That(code, Does.Contain(".Identity()"));
    }

    [Test]
    public void GenerateColumn_ForeignKeyWithReferencedEntity_EmitsForeignKey()
    {
        var snapshot = new SchemaSnapshot(1, "Test", DateTimeOffset.UtcNow, null,
            new[]
            {
                new TableDef("posts", null, NamingStyleKind.Exact,
                    new[] { new ColumnDef("user_id", "int", false, ColumnKind.ForeignKey, referencedEntityName: "User") },
                    Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
            });
        var code = SnapshotCodeGenerator.GenerateSnapshotClass(snapshot, "Test");
        Assert.That(code, Does.Contain(".ForeignKey(\"User\")"));
    }

    [Test]
    public void GenerateForeignKey_BothActions_EmitsBoth()
    {
        var snapshot = new SchemaSnapshot(1, "Test", DateTimeOffset.UtcNow, null,
            new[]
            {
                new TableDef("posts", null, NamingStyleKind.Exact,
                    new[] { new ColumnDef("id", "int", false, ColumnKind.PrimaryKey) },
                    new[] { new ForeignKeyDef("FK_posts_users", "user_id", "users", "id",
                        ForeignKeyAction.Cascade, ForeignKeyAction.SetNull) },
                    Array.Empty<IndexDef>())
            });
        var code = SnapshotCodeGenerator.GenerateSnapshotClass(snapshot, "Test");
        Assert.That(code, Does.Contain("ForeignKeyAction.Cascade"));
        Assert.That(code, Does.Contain("ForeignKeyAction.SetNull"));
    }

    [Test]
    public void GenerateForeignKey_NoActionActions_OmitsBoth()
    {
        var snapshot = new SchemaSnapshot(1, "Test", DateTimeOffset.UtcNow, null,
            new[]
            {
                new TableDef("posts", null, NamingStyleKind.Exact,
                    new[] { new ColumnDef("id", "int", false, ColumnKind.PrimaryKey) },
                    new[] { new ForeignKeyDef("FK_posts_users", "user_id", "users", "id",
                        ForeignKeyAction.NoAction, ForeignKeyAction.NoAction) },
                    Array.Empty<IndexDef>())
            });
        var code = SnapshotCodeGenerator.GenerateSnapshotClass(snapshot, "Test");
        Assert.That(code, Does.Not.Contain("OnDelete"));
        Assert.That(code, Does.Not.Contain("OnUpdate"));
    }

    [Test]
    public void GenerateIndex_WithMethod_EmitsMethod()
    {
        var snapshot = new SchemaSnapshot(1, "Test", DateTimeOffset.UtcNow, null,
            new[]
            {
                new TableDef("users", null, NamingStyleKind.Exact,
                    new[] { new ColumnDef("id", "int", false, ColumnKind.PrimaryKey) },
                    Array.Empty<ForeignKeyDef>(),
                    new[] { new IndexDef("IX_users_id", new[] { "id" }, method: "btree") })
            });
        var code = SnapshotCodeGenerator.GenerateSnapshotClass(snapshot, "Test");
        Assert.That(code, Does.Contain("btree"));
    }

    [Test]
    public void GenerateSnapshot_MultipleTables_DifferentNamingStyles()
    {
        var snapshot = new SchemaSnapshot(1, "Test", DateTimeOffset.UtcNow, null,
            new[]
            {
                new TableDef("users", null, NamingStyleKind.SnakeCase,
                    new[] { new ColumnDef("id", "int", false, ColumnKind.PrimaryKey) },
                    Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>()),
                new TableDef("Posts", null, NamingStyleKind.CamelCase,
                    new[] { new ColumnDef("id", "int", false, ColumnKind.PrimaryKey) },
                    Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
            });
        var code = SnapshotCodeGenerator.GenerateSnapshotClass(snapshot, "Test");
        Assert.That(code, Does.Contain("NamingStyleKind.SnakeCase"));
        Assert.That(code, Does.Contain("NamingStyleKind.CamelCase"));
    }

    #endregion
}
