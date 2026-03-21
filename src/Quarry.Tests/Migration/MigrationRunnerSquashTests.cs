using Microsoft.Data.Sqlite;
using Quarry.Migration;

namespace Quarry.Tests.Migration;

/// <summary>
/// Tests for squash-aware migration runner behavior.
/// </summary>
public class MigrationRunnerSquashTests
{
    private SqliteConnection _connection = null!;
    private SqlDialect _dialect;

    [SetUp]
    public void SetUp()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _dialect = SqlDialect.SQLite;
    }

    [TearDown]
    public void TearDown()
    {
        _connection.Dispose();
    }

    [Test]
    public async Task HistoryTable_HasSquashFromColumn()
    {
        var migrations = Array.Empty<(int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>)>();
        await MigrationRunner.RunAsync(_connection, _dialect, migrations);

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(__quarry_migrations);";
        using var reader = await cmd.ExecuteReaderAsync();

        var columns = new List<string>();
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(1)); // column name

        Assert.That(columns, Does.Contain("squash_from"));
    }

    [Test]
    public async Task Extended_RunAsync_AppliesSquashBaseline_OnFreshDb()
    {
        var migrations = new (int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>, int)[]
        {
            (1, "Baseline",
                b => b.CreateTable("users", null, t =>
                {
                    t.Column("id", c => c.ClrType("int").NotNull());
                    t.PrimaryKey("PK_users", "id");
                }),
                b => b.DropTable("users"),
                _ => { },
                5) // SquashedFrom = 5: replaces migrations 1-5
        };

        await MigrationRunner.RunAsync(_connection, _dialect, migrations);

        // Verify baseline was applied
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='users';";
        var result = await cmd.ExecuteScalarAsync();
        Assert.That(result, Is.EqualTo("users"));

        // Verify history row exists
        using var histCmd = _connection.CreateCommand();
        histCmd.CommandText = "SELECT version, name FROM __quarry_migrations WHERE status='applied';";
        using var reader = await histCmd.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(1));
        Assert.That(reader.GetString(1), Is.EqualTo("Baseline"));
    }

    [Test]
    public async Task Extended_RunAsync_SkipsSquashBaseline_WhenDbHasSquashedVersions()
    {
        // First, apply the original migrations
        var originalMigrations = new (int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>)[]
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
                b => b.AddColumn("users", "email", c => c.ClrType("string").Length(255).Nullable()),
                b => b.DropColumn("users", "email"),
                _ => { }),
            (3, "AddTimestamp",
                b => b.AddColumn("users", "created_at", c => c.ClrType("string").Nullable()),
                b => b.DropColumn("users", "created_at"),
                _ => { })
        };

        await MigrationRunner.RunAsync(_connection, _dialect, originalMigrations);

        // Now run with a squash baseline — it should be skipped because
        // the DB already has version 1 applied (ContainsKey check)
        var squashedMigrations = new (int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>, int)[]
        {
            (1, "Baseline",
                b => b.CreateTable("users", null, t =>
                {
                    t.Column("id", c => c.ClrType("int").NotNull());
                    t.Column("email", c => c.ClrType("string").Length(255).Nullable());
                    t.Column("created_at", c => c.ClrType("string").Nullable());
                    t.PrimaryKey("PK_users", "id");
                }),
                b => b.DropTable("users"),
                _ => { },
                3), // SquashedFrom = 3
            (4, "AddStatus",
                b => b.AddColumn("users", "status", c => c.ClrType("string").Length(50).Nullable()),
                b => b.DropColumn("users", "status"),
                _ => { },
                0) // Not a squash baseline
        };

        await MigrationRunner.RunAsync(_connection, _dialect, squashedMigrations);

        // Version 1 (baseline) should NOT have been re-applied — still just 1 row for version 1
        using var histCmd = _connection.CreateCommand();
        histCmd.CommandText = "SELECT COUNT(*) FROM __quarry_migrations WHERE version = 1;";
        var v1Count = (long)(await histCmd.ExecuteScalarAsync())!;
        Assert.That(v1Count, Is.EqualTo(1));

        // But version 4 should have been applied
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(users);";
        using var reader = await cmd.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(1));
        Assert.That(columns, Does.Contain("status"));
    }

    [Test]
    public async Task Extended_RunAsync_CompatibleWithLegacyOverload()
    {
        // Legacy 5-tuple overload still works
        var migrations = new (int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>)[]
        {
            (1, "CreateUsers",
                b => b.CreateTable("users", null, t =>
                {
                    t.Column("id", c => c.ClrType("int").NotNull());
                    t.PrimaryKey("PK_users", "id");
                }),
                b => b.DropTable("users"),
                _ => { })
        };

        await MigrationRunner.RunAsync(_connection, _dialect, migrations);

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM __quarry_migrations WHERE status='applied';";
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public async Task Extended_RunAsync_AppliesBaseline_WhenDbIsEmpty()
    {
        // On a fresh DB with no applied migrations, the baseline should be applied
        var squashedMigrations = new (int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>, int)[]
        {
            (1, "Baseline",
                b => b.CreateTable("users", null, t =>
                {
                    t.Column("id", c => c.ClrType("int").NotNull());
                    t.Column("email", c => c.ClrType("string").Length(255).Nullable());
                    t.PrimaryKey("PK_users", "id");
                }),
                b => b.DropTable("users"),
                _ => { },
                3), // SquashedFrom = 3
            (4, "AddStatus",
                b => b.AddColumn("users", "status", c => c.ClrType("string").Length(50).Nullable()),
                b => b.DropColumn("users", "status"),
                _ => { },
                0)
        };

        await MigrationRunner.RunAsync(_connection, _dialect, squashedMigrations);

        // Both should be applied
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM __quarry_migrations WHERE status='applied';";
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.That(count, Is.EqualTo(2));

        // Verify table structure
        using var colCmd = _connection.CreateCommand();
        colCmd.CommandText = "PRAGMA table_info(users);";
        using var reader = await colCmd.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(1));
        Assert.That(columns, Does.Contain("id"));
        Assert.That(columns, Does.Contain("email"));
        Assert.That(columns, Does.Contain("status"));
    }

    [Test]
    public async Task Extended_RunAsync_SkipsSquashBaseline_WhenDbHasHigherVersionInSquashedRange()
    {
        // Scenario: DB was at version 3 of 5 original migrations.
        // Squash creates a baseline at version 1 (SquashedFrom=5).
        // But we present the baseline as a NEW version (say 1) that hasn't been applied.
        // The DB has version 3 applied, which is in the squashed range [1,5].
        // The baseline should be skipped.

        // Simulate a DB that has only version 3 applied (partial application scenario)
        // First, run an empty migration set to create the history table
        await MigrationRunner.RunAsync(_connection, _dialect,
            Array.Empty<(int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>)>());

        // Manually insert a version 3 row to simulate partial application
        using (var insertCmd = _connection.CreateCommand())
        {
            insertCmd.CommandText = @"INSERT INTO __quarry_migrations (version, name, applied_at, checksum, execution_time_ms, applied_by, status)
                VALUES (3, 'AddTimestamp', '2026-01-01', 'abc123', 0, 'test', 'applied');";
            await insertCmd.ExecuteNonQueryAsync();
        }

        // Now run with squash baseline at version 1 (SquashedFrom=5)
        var squashedMigrations = new (int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>, int)[]
        {
            (1, "Baseline",
                b => b.CreateTable("should_not_exist", null, t =>
                {
                    t.Column("id", c => c.ClrType("int").NotNull());
                    t.PrimaryKey("PK_test", "id");
                }),
                b => b.DropTable("should_not_exist"),
                _ => { },
                5) // SquashedFrom = 5
        };

        await MigrationRunner.RunAsync(_connection, _dialect, squashedMigrations);

        // Baseline should be skipped — DB has version 3 which is in [1,5]
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='should_not_exist';";
        var result = await cmd.ExecuteScalarAsync();
        Assert.That(result, Is.Null, "Squash baseline should have been skipped because DB has version 3 in squashed range [1,5]");
    }

    [Test]
    public async Task MigrationAttribute_SquashedFrom_DefaultsToZero()
    {
        var attr = new MigrationAttribute { Version = 1, Name = "Test" };
        Assert.That(attr.SquashedFrom, Is.EqualTo(0));
    }

    [Test]
    public async Task MigrationAttribute_SquashedFrom_CanBeSet()
    {
        var attr = new MigrationAttribute { Version = 1, Name = "Baseline", SquashedFrom = 5 };
        Assert.That(attr.SquashedFrom, Is.EqualTo(5));
    }
}
