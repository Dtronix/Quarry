using Quarry.Shared.Migration;

namespace Quarry.Tests.Migration;

public class MigrationCodeGeneratorTests
{
    [Test]
    public void GenerateMigrationClass_CreateTable_EmitsUpgradeAndDowngrade()
    {
        var newSnapshot = new SchemaSnapshot(1, "Init", DateTimeOffset.UtcNow, null,
            new[]
            {
                new TableDef("users", null, NamingStyleKind.Exact,
                    new[]
                    {
                        new ColumnDef("id", "int", false, ColumnKind.PrimaryKey, isIdentity: true),
                        new ColumnDef("name", "string", false, ColumnKind.Standard, maxLength: 100)
                    },
                    Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
            });

        var steps = SchemaDiffer.Diff(null, newSnapshot);

        var code = MigrationCodeGenerator.GenerateMigrationClass(
            1, "InitialCreate", steps, null, newSnapshot, "MyApp.Migrations");

        Assert.That(code, Does.Contain("[Migration("));
        Assert.That(code, Does.Contain("Version = 1"));
        Assert.That(code, Does.Contain("Upgrade"));
        Assert.That(code, Does.Contain("Downgrade"));
        Assert.That(code, Does.Contain("CreateTable"));
        Assert.That(code, Does.Contain("DropTable"));
    }

    [Test]
    public void GenerateMigrationClass_AddColumn_EmitsCorrectSteps()
    {
        var from = new SchemaSnapshot(1, "v1", DateTimeOffset.UtcNow, null,
            new[]
            {
                new TableDef("users", null, NamingStyleKind.Exact,
                    new[] { new ColumnDef("id", "int", false, ColumnKind.PrimaryKey) },
                    Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
            });
        var to = new SchemaSnapshot(2, "v2", DateTimeOffset.UtcNow, 1,
            new[]
            {
                new TableDef("users", null, NamingStyleKind.Exact,
                    new[]
                    {
                        new ColumnDef("id", "int", false, ColumnKind.PrimaryKey),
                        new ColumnDef("email", "string", true, ColumnKind.Standard, maxLength: 255)
                    },
                    Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
            });

        var steps = SchemaDiffer.Diff(from, to);
        var code = MigrationCodeGenerator.GenerateMigrationClass(
            2, "AddEmail", steps, from, to, "MyApp.Migrations");

        Assert.That(code, Does.Contain("AddColumn"));
        Assert.That(code, Does.Contain("email"));
    }

    [Test]
    public void GenerateMigrationClass_DestructiveStep_EmitsWarningComment()
    {
        var from = new SchemaSnapshot(1, "v1", DateTimeOffset.UtcNow, null,
            new[]
            {
                new TableDef("users", null, NamingStyleKind.Exact,
                    new[]
                    {
                        new ColumnDef("id", "int", false, ColumnKind.PrimaryKey),
                        new ColumnDef("old_field", "string", true, ColumnKind.Standard)
                    },
                    Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
            });
        var to = new SchemaSnapshot(2, "v2", DateTimeOffset.UtcNow, 1,
            new[]
            {
                new TableDef("users", null, NamingStyleKind.Exact,
                    new[] { new ColumnDef("id", "int", false, ColumnKind.PrimaryKey) },
                    Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
            });

        var steps = SchemaDiffer.Diff(from, to);
        var code = MigrationCodeGenerator.GenerateMigrationClass(
            2, "RemoveOldField", steps, from, to, "MyApp.Migrations");

        Assert.That(code, Does.Contain("DropColumn"));
    }

    [Test]
    public void GenerateMigrationClass_DropTable_DowngradeHasCreateTable()
    {
        var from = new SchemaSnapshot(1, "v1", DateTimeOffset.UtcNow, null,
            new[]
            {
                new TableDef("users", null, NamingStyleKind.Exact,
                    new[] { new ColumnDef("id", "int", false, ColumnKind.PrimaryKey, isIdentity: true) },
                    Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
            });
        var to = new SchemaSnapshot(2, "v2", DateTimeOffset.UtcNow, 1, Array.Empty<TableDef>());

        var steps = SchemaDiffer.Diff(from, to);
        var code = MigrationCodeGenerator.GenerateMigrationClass(
            2, "DropUsers", steps, from, to, "MyApp.Migrations");

        Assert.That(code, Does.Contain("DropTable"));
        // Downgrade should re-create the table with original schema
        Assert.That(code, Does.Contain("CreateTable(\"users\""));
        Assert.That(code, Does.Contain(".ClrType(\"int\")"));
        Assert.That(code, Does.Contain(".Identity()"));
        Assert.That(code, Does.Contain("PK_users"));
    }

    [Test]
    public void GenerateMigrationClass_DropTable_DowngradePreservesAllColumns()
    {
        var from = new SchemaSnapshot(1, "v1", DateTimeOffset.UtcNow, null,
            new[]
            {
                new TableDef("products", null, NamingStyleKind.Exact,
                    new[]
                    {
                        new ColumnDef("id", "int", false, ColumnKind.PrimaryKey, isIdentity: true),
                        new ColumnDef("name", "string", false, ColumnKind.Standard, maxLength: 200),
                        new ColumnDef("price", "decimal", false, ColumnKind.Standard, precision: 10, scale: 2),
                        new ColumnDef("description", "string", true, ColumnKind.Standard)
                    },
                    Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
            });
        var to = new SchemaSnapshot(2, "v2", DateTimeOffset.UtcNow, 1, Array.Empty<TableDef>());

        var steps = SchemaDiffer.Diff(from, to);
        var code = MigrationCodeGenerator.GenerateMigrationClass(
            2, "DropProducts", steps, from, to, "MyApp.Migrations");

        // Upgrade drops
        Assert.That(code, Does.Contain("DropTable(\"products\""));
        // Downgrade re-creates with all columns
        Assert.That(code, Does.Contain("CreateTable(\"products\""));
        Assert.That(code, Does.Contain(".ClrType(\"int\")"));
        Assert.That(code, Does.Contain(".Identity()"));
        Assert.That(code, Does.Contain(".ClrType(\"string\")"));
        Assert.That(code, Does.Contain(".Length(200)"));
        Assert.That(code, Does.Contain(".ClrType(\"decimal\")"));
        Assert.That(code, Does.Contain(".Precision(10, 2)"));
        Assert.That(code, Does.Contain(".Nullable()"));
        Assert.That(code, Does.Contain("PK_products"));
    }

    [Test]
    public void GenerateMigrationClass_DropTable_WithSchema_DowngradePreservesSchema()
    {
        var table = new TableDef("users", "dbo", NamingStyleKind.Exact,
            new[] { new ColumnDef("id", "int", false, ColumnKind.PrimaryKey) },
            Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>());

        var steps = new[]
        {
            new MigrationStep(MigrationStepType.DropTable, StepClassification.Destructive,
                "users", "dbo", null, table, null, "Drop users")
        };
        var to = new SchemaSnapshot(2, "v2", DateTimeOffset.UtcNow, 1, Array.Empty<TableDef>());
        var code = MigrationCodeGenerator.GenerateMigrationClass(2, "DropUsers", steps, null, to, "Test");

        Assert.That(code, Does.Contain("CreateTable(\"users\", \"dbo\""));
    }

    [Test]
    public void GenerateMigrationClass_AlterColumn_DowngradeHasReverseAlter()
    {
        var from = new SchemaSnapshot(1, "v1", DateTimeOffset.UtcNow, null,
            new[]
            {
                new TableDef("users", null, NamingStyleKind.Exact,
                    new[]
                    {
                        new ColumnDef("id", "int", false, ColumnKind.PrimaryKey),
                        new ColumnDef("age", "string", true, ColumnKind.Standard)
                    },
                    Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
            });
        var to = new SchemaSnapshot(2, "v2", DateTimeOffset.UtcNow, 1,
            new[]
            {
                new TableDef("users", null, NamingStyleKind.Exact,
                    new[]
                    {
                        new ColumnDef("id", "int", false, ColumnKind.PrimaryKey),
                        new ColumnDef("age", "int", false, ColumnKind.Standard)
                    },
                    Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
            });

        var steps = SchemaDiffer.Diff(from, to);
        var code = MigrationCodeGenerator.GenerateMigrationClass(
            2, "ChangeAge", steps, from, to, "MyApp.Migrations");

        Assert.That(code, Does.Contain("AlterColumn"));
    }

    [Test]
    public void GenerateMigrationClass_MultipleTableChanges_AllRepresented()
    {
        var to = new SchemaSnapshot(1, "Init", DateTimeOffset.UtcNow, null,
            new[]
            {
                new TableDef("users", null, NamingStyleKind.Exact,
                    new[] { new ColumnDef("id", "int", false, ColumnKind.PrimaryKey) },
                    Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>()),
                new TableDef("posts", null, NamingStyleKind.Exact,
                    new[] { new ColumnDef("id", "int", false, ColumnKind.PrimaryKey) },
                    Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
            });

        var steps = SchemaDiffer.Diff(null, to);
        var code = MigrationCodeGenerator.GenerateMigrationClass(
            1, "Init", steps, null, to, "MyApp.Migrations");

        Assert.That(code, Does.Contain("users"));
        Assert.That(code, Does.Contain("posts"));
    }

    [Test]
    public void GenerateMigrationClass_EmptySteps_EmptyBodies()
    {
        var to = new SchemaSnapshot(1, "Empty", DateTimeOffset.UtcNow, null, Array.Empty<TableDef>());

        var code = MigrationCodeGenerator.GenerateMigrationClass(
            1, "EmptyMigration", Array.Empty<MigrationStep>(), null, to, "MyApp.Migrations");

        Assert.That(code, Does.Contain("Upgrade"));
        Assert.That(code, Does.Contain("Downgrade"));
    }

    #region Downgrade branches

    [Test]
    public void Downgrade_RenameTable_EmitsReverseRename()
    {
        var steps = new[]
        {
            new MigrationStep(MigrationStepType.RenameTable, StepClassification.Cautious,
                "users", null, null, "users", "accounts", "Rename users to accounts")
        };
        var to = new SchemaSnapshot(2, "v2", DateTimeOffset.UtcNow, 1, Array.Empty<TableDef>());
        var code = MigrationCodeGenerator.GenerateMigrationClass(2, "Rename", steps, null, to, "Test");
        Assert.That(code, Does.Contain("RenameTable"));
        Assert.That(code, Does.Contain("accounts"));
        Assert.That(code, Does.Contain("users"));
    }

    [Test]
    public void Downgrade_RenameColumn_EmitsReverseRename()
    {
        var steps = new[]
        {
            new MigrationStep(MigrationStepType.RenameColumn, StepClassification.Cautious,
                "users", null, "user_name", "user_name", "username", "Rename column")
        };
        var to = new SchemaSnapshot(2, "v2", DateTimeOffset.UtcNow, 1, Array.Empty<TableDef>());
        var code = MigrationCodeGenerator.GenerateMigrationClass(2, "Rename", steps, null, to, "Test");
        Assert.That(code, Does.Contain("RenameColumn"));
        Assert.That(code, Does.Contain("username"));
        Assert.That(code, Does.Contain("user_name"));
    }

    [Test]
    public void Downgrade_AddForeignKey_EmitsDropForeignKey()
    {
        var fk = new ForeignKeyDef("FK_posts_users", "user_id", "users", "id");
        var steps = new[]
        {
            new MigrationStep(MigrationStepType.AddForeignKey, StepClassification.Safe,
                "posts", null, null, null, fk, "Add FK")
        };
        var to = new SchemaSnapshot(2, "v2", DateTimeOffset.UtcNow, 1, Array.Empty<TableDef>());
        var code = MigrationCodeGenerator.GenerateMigrationClass(2, "AddFk", steps, null, to, "Test");
        Assert.That(code, Does.Contain("DropForeignKey"));
        Assert.That(code, Does.Contain("FK_posts_users"));
    }

    [Test]
    public void Downgrade_DropForeignKey_EmitsAddForeignKey()
    {
        var fk = new ForeignKeyDef("FK_posts_users", "user_id", "users", "id");
        var steps = new[]
        {
            new MigrationStep(MigrationStepType.DropForeignKey, StepClassification.Destructive,
                "posts", null, null, fk, null, "Drop FK")
        };
        var to = new SchemaSnapshot(2, "v2", DateTimeOffset.UtcNow, 1, Array.Empty<TableDef>());
        var code = MigrationCodeGenerator.GenerateMigrationClass(2, "DropFk", steps, null, to, "Test");
        Assert.That(code, Does.Contain("AddForeignKey"));
        Assert.That(code, Does.Contain("FK_posts_users"));
    }

    [Test]
    public void Downgrade_AddIndex_EmitsDropIndex()
    {
        var idx = new IndexDef("IX_users_email", new[] { "email" }, true);
        var steps = new[]
        {
            new MigrationStep(MigrationStepType.AddIndex, StepClassification.Safe,
                "users", null, null, null, idx, "Add index")
        };
        var to = new SchemaSnapshot(2, "v2", DateTimeOffset.UtcNow, 1, Array.Empty<TableDef>());
        var code = MigrationCodeGenerator.GenerateMigrationClass(2, "AddIdx", steps, null, to, "Test");
        Assert.That(code, Does.Contain("DropIndex"));
        Assert.That(code, Does.Contain("IX_users_email"));
    }

    [Test]
    public void Downgrade_DropIndex_EmitsAddIndex()
    {
        var idx = new IndexDef("IX_users_email", new[] { "email" }, true);
        var steps = new[]
        {
            new MigrationStep(MigrationStepType.DropIndex, StepClassification.Destructive,
                "users", null, null, idx, null, "Drop index")
        };
        var to = new SchemaSnapshot(2, "v2", DateTimeOffset.UtcNow, 1, Array.Empty<TableDef>());
        var code = MigrationCodeGenerator.GenerateMigrationClass(2, "DropIdx", steps, null, to, "Test");
        Assert.That(code, Does.Contain("AddIndex"));
        Assert.That(code, Does.Contain("IX_users_email"));
        Assert.That(code, Does.Contain("unique: true"));
    }

    [Test]
    public void Downgrade_AlterColumn_EmitsReverseAlter()
    {
        var oldCol = new ColumnDef("age", "string", true, ColumnKind.Standard);
        var newCol = new ColumnDef("age", "int", false, ColumnKind.Standard);
        var steps = new[]
        {
            new MigrationStep(MigrationStepType.AlterColumn, StepClassification.Cautious,
                "users", null, "age", oldCol, newCol, "Alter column")
        };
        var to = new SchemaSnapshot(2, "v2", DateTimeOffset.UtcNow, 1, Array.Empty<TableDef>());
        var code = MigrationCodeGenerator.GenerateMigrationClass(2, "Alter", steps, null, to, "Test");
        // Downgrade should reverse to old type
        Assert.That(code, Does.Contain("AlterColumn"));
        Assert.That(code, Does.Contain(".ClrType(\"string\")"));
        Assert.That(code, Does.Contain(".Nullable()"));
    }

    #endregion

    #region Warnings

    [Test]
    public void Warnings_NullableToNonNull_EmitsWarning()
    {
        var oldCol = new ColumnDef("status", "string", true, ColumnKind.Standard);
        var newCol = new ColumnDef("status", "string", false, ColumnKind.Standard);
        var steps = new[]
        {
            new MigrationStep(MigrationStepType.AlterColumn, StepClassification.Cautious,
                "users", null, "status", oldCol, newCol, "Alter column")
        };
        var to = new SchemaSnapshot(2, "v2", DateTimeOffset.UtcNow, 1, Array.Empty<TableDef>());
        var code = MigrationCodeGenerator.GenerateMigrationClass(2, "Warn", steps, null, to, "Test");
        Assert.That(code, Does.Contain("WARNING"));
        Assert.That(code, Does.Contain("nullable").IgnoreCase);
    }

    [Test]
    public void Warnings_TypeChange_EmitsWarning()
    {
        var oldCol = new ColumnDef("age", "string", false, ColumnKind.Standard);
        var newCol = new ColumnDef("age", "int", false, ColumnKind.Standard);
        var steps = new[]
        {
            new MigrationStep(MigrationStepType.AlterColumn, StepClassification.Cautious,
                "users", null, "age", oldCol, newCol, "Alter column")
        };
        var to = new SchemaSnapshot(2, "v2", DateTimeOffset.UtcNow, 1, Array.Empty<TableDef>());
        var code = MigrationCodeGenerator.GenerateMigrationClass(2, "Warn", steps, null, to, "Test");
        Assert.That(code, Does.Contain("WARNING"));
        Assert.That(code, Does.Contain("type changed").IgnoreCase);
    }

    #endregion
}
