using Microsoft.Data.Sqlite;
using Quarry.Migration;

namespace Quarry.Tests.Migration;

public class MigrationRunnerLargeTableTests
{
    // --- GetAffectedTableNames ---

    [Test]
    public void GetAffectedTableNames_DropTable_ReturnsTableName()
    {
        var builder = new MigrationBuilder();
        builder.DropTable("users");

        var tables = MigrationRunner.GetAffectedTableNames(builder.GetOperations());

        Assert.That(tables, Is.EquivalentTo(new[] { "users" }));
    }

    [Test]
    public void GetAffectedTableNames_RenameTable_ReturnsOldName()
    {
        var builder = new MigrationBuilder();
        builder.RenameTable("users", "people");

        var tables = MigrationRunner.GetAffectedTableNames(builder.GetOperations());

        Assert.That(tables, Is.EquivalentTo(new[] { "users" }));
    }

    [Test]
    public void GetAffectedTableNames_AddColumn_ReturnsTableName()
    {
        var builder = new MigrationBuilder();
        builder.AddColumn("users", "email", c => c.ClrType("string").Length(256));

        var tables = MigrationRunner.GetAffectedTableNames(builder.GetOperations());

        Assert.That(tables, Is.EquivalentTo(new[] { "users" }));
    }

    [Test]
    public void GetAffectedTableNames_DropColumn_ReturnsTableName()
    {
        var builder = new MigrationBuilder();
        builder.DropColumn("users", "email");

        var tables = MigrationRunner.GetAffectedTableNames(builder.GetOperations());

        Assert.That(tables, Is.EquivalentTo(new[] { "users" }));
    }

    [Test]
    public void GetAffectedTableNames_RenameColumn_ReturnsTableName()
    {
        var builder = new MigrationBuilder();
        builder.RenameColumn("users", "name", "display_name");

        var tables = MigrationRunner.GetAffectedTableNames(builder.GetOperations());

        Assert.That(tables, Is.EquivalentTo(new[] { "users" }));
    }

    [Test]
    public void GetAffectedTableNames_AlterColumn_ReturnsTableName()
    {
        var builder = new MigrationBuilder();
        builder.AlterColumn("users", "name", c => c.ClrType("string").Length(200));

        var tables = MigrationRunner.GetAffectedTableNames(builder.GetOperations());

        Assert.That(tables, Is.EquivalentTo(new[] { "users" }));
    }

    [Test]
    public void GetAffectedTableNames_AddIndex_ReturnsTableName()
    {
        var builder = new MigrationBuilder();
        builder.AddIndex("IX_users_email", "users", new[] { "email" });

        var tables = MigrationRunner.GetAffectedTableNames(builder.GetOperations());

        Assert.That(tables, Is.EquivalentTo(new[] { "users" }));
    }

    [Test]
    public void GetAffectedTableNames_DropIndex_ReturnsTableName()
    {
        var builder = new MigrationBuilder();
        builder.DropIndex("IX_users_email", "users");

        var tables = MigrationRunner.GetAffectedTableNames(builder.GetOperations());

        Assert.That(tables, Is.EquivalentTo(new[] { "users" }));
    }

    [Test]
    public void GetAffectedTableNames_AddForeignKey_ReturnsTableName()
    {
        var builder = new MigrationBuilder();
        builder.AddForeignKey("FK_orders_users", "orders", "user_id", "users", "id");

        var tables = MigrationRunner.GetAffectedTableNames(builder.GetOperations());

        Assert.That(tables, Is.EquivalentTo(new[] { "orders" }));
    }

    [Test]
    public void GetAffectedTableNames_DropForeignKey_ReturnsTableName()
    {
        var builder = new MigrationBuilder();
        builder.DropForeignKey("FK_orders_users", "orders");

        var tables = MigrationRunner.GetAffectedTableNames(builder.GetOperations());

        Assert.That(tables, Is.EquivalentTo(new[] { "orders" }));
    }

    [Test]
    public void GetAffectedTableNames_CreateTable_ExcludedFromResults()
    {
        var builder = new MigrationBuilder();
        builder.CreateTable("users", null, t =>
        {
            t.Column("id", c => c.ClrType("int").NotNull());
            t.PrimaryKey("PK_users", "id");
        });

        var tables = MigrationRunner.GetAffectedTableNames(builder.GetOperations());

        Assert.That(tables, Is.Empty);
    }

    [Test]
    public void GetAffectedTableNames_RawSql_ExcludedFromResults()
    {
        var builder = new MigrationBuilder();
        builder.Sql("UPDATE users SET active = 1;");

        var tables = MigrationRunner.GetAffectedTableNames(builder.GetOperations());

        Assert.That(tables, Is.Empty);
    }

    [Test]
    public void GetAffectedTableNames_MultipleOperations_DeduplicatesTables()
    {
        var builder = new MigrationBuilder();
        builder.AddColumn("users", "email", c => c.ClrType("string").Length(256));
        builder.AddColumn("users", "phone", c => c.ClrType("string").Length(20));
        builder.AddIndex("IX_users_email", "users", new[] { "email" });

        var tables = MigrationRunner.GetAffectedTableNames(builder.GetOperations());

        Assert.That(tables, Has.Count.EqualTo(1));
        Assert.That(tables, Is.EquivalentTo(new[] { "users" }));
    }

    [Test]
    public void GetAffectedTableNames_MixedOperations_ReturnsOnlyExistingTableTargets()
    {
        var builder = new MigrationBuilder();
        builder.CreateTable("new_table", null, t =>
        {
            t.Column("id", c => c.ClrType("int").NotNull());
            t.PrimaryKey("PK_new_table", "id");
        });
        builder.AddColumn("users", "email", c => c.ClrType("string").Length(256));
        builder.DropTable("old_table");

        var tables = MigrationRunner.GetAffectedTableNames(builder.GetOperations());

        Assert.That(tables, Is.EquivalentTo(new[] { "users", "old_table" }));
    }

    [Test]
    public void GetAffectedTableNames_EmptyOperations_ReturnsEmpty()
    {
        var builder = new MigrationBuilder();

        var tables = MigrationRunner.GetAffectedTableNames(builder.GetOperations());

        Assert.That(tables, Is.Empty);
    }

    [Test]
    public void GetAffectedTableNames_CaseInsensitiveDeduplication()
    {
        var builder = new MigrationBuilder();
        builder.AddColumn("Users", "email", c => c.ClrType("string").Length(256));
        builder.DropColumn("users", "old_field");

        var tables = MigrationRunner.GetAffectedTableNames(builder.GetOperations());

        Assert.That(tables, Has.Count.EqualTo(1));
    }

    // --- GetEstimatedRowCountSql ---

    [Test]
    public void GetEstimatedRowCountSql_SqlServer_QueriesSysPartitions()
    {
        var sql = MigrationRunner.GetEstimatedRowCountSql(SqlDialect.SqlServer);

        Assert.That(sql, Is.Not.Null);
        Assert.That(sql, Does.Contain("sys.partitions"));
        Assert.That(sql, Does.Contain("sys.tables"));
        Assert.That(sql, Does.Contain("@p0"));
    }

    [Test]
    public void GetEstimatedRowCountSql_PostgreSQL_QueriesPgClass()
    {
        var sql = MigrationRunner.GetEstimatedRowCountSql(SqlDialect.PostgreSQL);

        Assert.That(sql, Is.Not.Null);
        Assert.That(sql, Does.Contain("pg_class"));
        Assert.That(sql, Does.Contain("$1"));
    }

    [Test]
    public void GetEstimatedRowCountSql_MySQL_QueriesInformationSchema()
    {
        var sql = MigrationRunner.GetEstimatedRowCountSql(SqlDialect.MySQL);

        Assert.That(sql, Is.Not.Null);
        Assert.That(sql, Does.Contain("information_schema.tables"));
        Assert.That(sql, Does.Contain("DATABASE()"));
    }

    [Test]
    public void GetEstimatedRowCountSql_SQLite_ReturnsNull()
    {
        var sql = MigrationRunner.GetEstimatedRowCountSql(SqlDialect.SQLite);

        Assert.That(sql, Is.Null);
    }

    // --- Integration: WarnOnLargeTable with SQLite (no-op) ---

    [Test]
    public async Task RunAsync_WarnOnLargeTable_SQLite_SkipsWithoutError()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var migrations = new (int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>)[]
        {
            (1, "CreateUsers",
                b => b.CreateTable("users", null, t =>
                {
                    t.Column("id", c => c.ClrType("int").NotNull());
                    t.Column("name", c => c.ClrType("string").Length(100).NotNull());
                    t.PrimaryKey("PK_users", "id");
                }),
                b => b.DropTable("users"),
                _ => { })
        };

        var options = new MigrationOptions
        {
            WarnOnLargeTable = true,
            LargeTableThreshold = 100
        };

        // Should not throw — SQLite large table warning is silently skipped
        await MigrationRunner.RunAsync(connection, SqlDialect.SQLite, migrations, options);

        // Verify the migration still applied successfully
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='users';";
        var result = await cmd.ExecuteScalarAsync();
        Assert.That(result, Is.EqualTo("users"));
    }

    [Test]
    public async Task RunAsync_WarnOnLargeTable_SQLite_AlterTable_SkipsWithoutError()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var migrations = new (int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>)[]
        {
            (1, "CreateUsers",
                b => b.CreateTable("users", null, t =>
                {
                    t.Column("id", c => c.ClrType("int").NotNull());
                    t.PrimaryKey("PK_users", "id");
                }),
                b => b.DropTable("users"),
                _ => { }),
            (2, "AddEmail",
                b => b.AddColumn("users", "email", c => c.ClrType("string").Length(256)),
                b => b.DropColumn("users", "email"),
                _ => { })
        };

        var options = new MigrationOptions
        {
            WarnOnLargeTable = true,
            LargeTableThreshold = 0 // Even with threshold 0, SQLite should skip without error
        };

        await MigrationRunner.RunAsync(connection, SqlDialect.SQLite, migrations, options);

        // Verify both migrations applied
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM __quarry_migrations WHERE status = 'applied';";
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        Assert.That(count, Is.EqualTo(2));
    }

    [Test]
    public async Task RunAsync_WarnOnLargeTable_Disabled_DoesNotQuery()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var logOutput = new List<string>();
        var migrations = new (int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>)[]
        {
            (1, "CreateUsers",
                b => b.CreateTable("users", null, t =>
                {
                    t.Column("id", c => c.ClrType("int").NotNull());
                    t.PrimaryKey("PK_users", "id");
                }),
                b => b.DropTable("users"),
                _ => { }),
            (2, "AddEmail",
                b => b.AddColumn("users", "email", c => c.ClrType("string").Length(256)),
                b => b.DropColumn("users", "email"),
                _ => { })
        };

        var options = new MigrationOptions
        {
            WarnOnLargeTable = false, // Disabled (default)
            Logger = msg => logOutput.Add(msg)
        };

        await MigrationRunner.RunAsync(connection, SqlDialect.SQLite, migrations, options);

        // No large table warnings should appear in log
        Assert.That(logOutput, Has.None.Contain("WARNING: Table"));
    }

    [Test]
    public async Task RunAsync_WarnOnLargeTable_DryRun_StillChecks()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var migrations = new (int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>)[]
        {
            (1, "AddEmail",
                b => b.AddColumn("users", "email", c => c.ClrType("string").Length(256)),
                b => b.DropColumn("users", "email"),
                _ => { })
        };

        var options = new MigrationOptions
        {
            WarnOnLargeTable = true,
            DryRun = true
        };

        // Should not throw — dry run + large table warning (SQLite skipped) works fine
        await MigrationRunner.RunAsync(connection, SqlDialect.SQLite, migrations, options);
    }
}
