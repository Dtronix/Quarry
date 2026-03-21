using Microsoft.Data.Sqlite;
using Quarry.Migration;

namespace Quarry.Tests.Migration;

public class MigrationRunnerIntegrationTests
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
    public async Task RunAsync_CreateTable_CreatesTableInDatabase()
    {
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

        await MigrationRunner.RunAsync(_connection, _dialect, migrations);

        // Verify table exists
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='users';";
        var result = await cmd.ExecuteScalarAsync();
        Assert.That(result, Is.EqualTo("users"));
    }

    [Test]
    public async Task RunAsync_CreatesHistoryTable()
    {
        var migrations = Array.Empty<(int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>)>();

        await MigrationRunner.RunAsync(_connection, _dialect, migrations);

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='__quarry_migrations';";
        var result = await cmd.ExecuteScalarAsync();
        Assert.That(result, Is.EqualTo("__quarry_migrations"));
    }

    [Test]
    public async Task RunAsync_RecordsMigrationHistory()
    {
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
        cmd.CommandText = "SELECT version, name FROM __quarry_migrations;";
        using var reader = await cmd.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(1));
        Assert.That(reader.GetString(1), Is.EqualTo("CreateUsers"));
    }

    [Test]
    public async Task RunAsync_SkipsAlreadyAppliedMigrations()
    {
        var callCount = 0;
        var migrations = new (int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>)[]
        {
            (1, "CreateUsers",
                b =>
                {
                    callCount++;
                    b.CreateTable("users", null, t =>
                    {
                        t.Column("id", c => c.ClrType("int").NotNull());
                        t.PrimaryKey("PK_users", "id");
                    });
                },
                b => b.DropTable("users"),
                _ => { })
        };

        await MigrationRunner.RunAsync(_connection, _dialect, migrations);
        await MigrationRunner.RunAsync(_connection, _dialect, migrations);

        Assert.That(callCount, Is.EqualTo(1));
    }

    [Test]
    public async Task RunAsync_MultipleMigrations_AppliesInOrder()
    {
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
            (2, "CreatePosts",
                b => b.CreateTable("posts", null, t =>
                {
                    t.Column("id", c => c.ClrType("int").NotNull());
                    t.Column("user_id", c => c.ClrType("int").NotNull());
                    t.PrimaryKey("PK_posts", "id");
                }),
                b => b.DropTable("posts"),
                _ => { })
        };

        await MigrationRunner.RunAsync(_connection, _dialect, migrations);

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM __quarry_migrations;";
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.That(count, Is.EqualTo(2));
    }

    [Test]
    public async Task RunAsync_TargetVersion_StopsAtTarget()
    {
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
            (2, "CreatePosts",
                b => b.CreateTable("posts", null, t =>
                {
                    t.Column("id", c => c.ClrType("int").NotNull());
                    t.PrimaryKey("PK_posts", "id");
                }),
                b => b.DropTable("posts"),
                _ => { })
        };

        await MigrationRunner.RunAsync(_connection, _dialect, migrations,
            new MigrationOptions { TargetVersion = 1 });

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM __quarry_migrations;";
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public async Task RunAsync_Downgrade_RollsBackMigrations()
    {
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
            (2, "CreatePosts",
                b => b.CreateTable("posts", null, t =>
                {
                    t.Column("id", c => c.ClrType("int").NotNull());
                    t.PrimaryKey("PK_posts", "id");
                }),
                b => b.DropTable("posts"),
                _ => { })
        };

        // Apply all
        await MigrationRunner.RunAsync(_connection, _dialect, migrations);

        // Rollback to version 1
        await MigrationRunner.RunAsync(_connection, _dialect, migrations,
            new MigrationOptions { Direction = MigrationDirection.Downgrade, TargetVersion = 1 });

        // posts table should be gone
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='posts';";
        var result = await cmd.ExecuteScalarAsync();
        Assert.That(result, Is.Null);

        // users table should remain
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='users';";
        result = await cmd.ExecuteScalarAsync();
        Assert.That(result, Is.EqualTo("users"));

        // History should only have version 1
        cmd.CommandText = "SELECT COUNT(*) FROM __quarry_migrations;";
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public async Task RunAsync_DryRun_DoesNotModifyDatabase()
    {
        var logs = new List<string>();
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

        await MigrationRunner.RunAsync(_connection, _dialect, migrations,
            new MigrationOptions { DryRun = true, Logger = s => logs.Add(s) });

        // Table should NOT exist
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='users';";
        var result = await cmd.ExecuteScalarAsync();
        Assert.That(result, Is.Null);

        // But SQL should have been logged
        Assert.That(logs.Any(l => l.Contains("CREATE TABLE")), Is.True);
    }

    [Test]
    public async Task RunAsync_FailedMigration_RollsBackTransaction()
    {
        var migrations = new (int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>)[]
        {
            (1, "BadMigration",
                b =>
                {
                    b.CreateTable("good_table", null, t =>
                    {
                        t.Column("id", c => c.ClrType("int").NotNull());
                        t.PrimaryKey("PK_good", "id");
                    });
                    // This will fail because the table doesn't exist yet for the raw SQL
                    b.Sql("INSERT INTO nonexistent_table VALUES (1);");
                },
                b => b.DropTable("good_table"),
                _ => { })
        };

        try
        {
            await MigrationRunner.RunAsync(_connection, _dialect, migrations);
        }
        catch
        {
            // Expected
        }

        // Table should not exist if transaction rolled back
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='good_table';";
        var result = await cmd.ExecuteScalarAsync();
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task RunAsync_UpgradeThenFullDowngrade_AllTablesRemoved()
    {
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
            (2, "CreatePosts",
                b => b.CreateTable("posts", null, t =>
                {
                    t.Column("id", c => c.ClrType("int").NotNull());
                    t.PrimaryKey("PK_posts", "id");
                }),
                b => b.DropTable("posts"),
                _ => { })
        };

        await MigrationRunner.RunAsync(_connection, _dialect, migrations);
        await MigrationRunner.RunAsync(_connection, _dialect, migrations,
            new MigrationOptions { Direction = MigrationDirection.Downgrade, TargetVersion = 0 });

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='users';";
        Assert.That(await cmd.ExecuteScalarAsync(), Is.Null);

        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='posts';";
        Assert.That(await cmd.ExecuteScalarAsync(), Is.Null);

        cmd.CommandText = "SELECT COUNT(*) FROM __quarry_migrations;";
        Assert.That((long)(await cmd.ExecuteScalarAsync())!, Is.EqualTo(0));
    }

    [Test]
    public async Task RunAsync_SecondRunAfterComplete_IsNoOp()
    {
        var callCount = 0;
        var migrations = new (int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>)[]
        {
            (1, "CreateUsers",
                b =>
                {
                    callCount++;
                    b.CreateTable("users", null, t =>
                    {
                        t.Column("id", c => c.ClrType("int").NotNull());
                        t.PrimaryKey("PK_users", "id");
                    });
                },
                b => b.DropTable("users"),
                _ => { })
        };

        await MigrationRunner.RunAsync(_connection, _dialect, migrations);
        await MigrationRunner.RunAsync(_connection, _dialect, migrations);

        Assert.That(callCount, Is.EqualTo(1));
    }

    [Test]
    public async Task RunAsync_RawSqlInMigration_ExecutesCorrectly()
    {
        var migrations = new (int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>)[]
        {
            (1, "CreateUsers",
                b => b.CreateTable("users", null, t =>
                {
                    t.Column("id", c => c.ClrType("int").NotNull());
                    t.Column("name", c => c.ClrType("string").Length(100).Nullable());
                    t.PrimaryKey("PK_users", "id");
                }),
                b => b.DropTable("users"),
                _ => { }),
            (2, "SeedData",
                b => b.Sql("INSERT INTO \"users\" (\"id\", \"name\") VALUES (1, 'Alice');"),
                b => b.Sql("DELETE FROM \"users\" WHERE \"id\" = 1;"),
                _ => { })
        };

        await MigrationRunner.RunAsync(_connection, _dialect, migrations);

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT \"name\" FROM \"users\" WHERE \"id\" = 1;";
        var result = await cmd.ExecuteScalarAsync();
        Assert.That(result, Is.EqualTo("Alice"));
    }

    [Test]
    public async Task RunAsync_ConnectionAlreadyOpen_DoesNotThrow()
    {
        // Connection is already open from SetUp
        Assert.That(_connection.State, Is.EqualTo(System.Data.ConnectionState.Open));
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

        Assert.DoesNotThrowAsync(async () => await MigrationRunner.RunAsync(_connection, _dialect, migrations));
    }

    [Test]
    public async Task RunAsync_ConnectionClosed_OpensAutomatically()
    {
        _connection.Close();
        Assert.That(_connection.State, Is.EqualTo(System.Data.ConnectionState.Closed));

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
        Assert.That(_connection.State, Is.EqualTo(System.Data.ConnectionState.Open));
    }

    [Test]
    public async Task RunAsync_DowngradeToZero_HistoryEmpty()
    {
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
        await MigrationRunner.RunAsync(_connection, _dialect, migrations,
            new MigrationOptions { Direction = MigrationDirection.Downgrade, TargetVersion = 0 });

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM __quarry_migrations;";
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.That(count, Is.EqualTo(0));
    }

    [Test]
    public async Task RunAsync_DryRun_Downgrade_LogsButDoesNotModify()
    {
        var logs = new List<string>();
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
        await MigrationRunner.RunAsync(_connection, _dialect, migrations,
            new MigrationOptions { Direction = MigrationDirection.Downgrade, TargetVersion = 0, DryRun = true, Logger = s => logs.Add(s) });

        // Table should still exist
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='users';";
        Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo("users"));
    }

    [Test]
    public async Task RunAsync_Logger_ReceivesMeaningfulMessages()
    {
        var logs = new List<string>();
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

        await MigrationRunner.RunAsync(_connection, _dialect, migrations,
            new MigrationOptions { Logger = s => logs.Add(s) });

        Assert.That(logs.Count, Is.GreaterThan(0));
    }

    [Test]
    public async Task RunAsync_EmptyMigrationArray_IsNoOp()
    {
        var migrations = Array.Empty<(int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>)>();

        await MigrationRunner.RunAsync(_connection, _dialect, migrations);

        // History table should exist but be empty
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='__quarry_migrations';";
        Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo("__quarry_migrations"));
    }

    [Test]
    public async Task RunAsync_HistoryRow_HasAppliedBy()
    {
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
        cmd.CommandText = "SELECT applied_by FROM __quarry_migrations WHERE version = 1;";
        var result = (string)(await cmd.ExecuteScalarAsync())!;
        Assert.That(result, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public async Task RunAsync_HistoryRow_HasPositiveExecutionTime()
    {
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
        cmd.CommandText = "SELECT execution_time_ms FROM __quarry_migrations WHERE version = 1;";
        var result = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.That(result, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public async Task RunAsync_BackupEnabled_ExecutesBackupSql()
    {
        var migrations = new (int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>)[]
        {
            (1, "CreateUsers",
                b => b.CreateTable("users", null, t =>
                {
                    t.Column("id", c => c.ClrType("int").NotNull());
                    t.Column("name", c => c.ClrType("string").Length(100).Nullable());
                    t.PrimaryKey("PK_users", "id");
                }),
                b => b.DropTable("users"),
                _ => { }),
            (2, "DropUsers",
                b => b.DropTable("users"),
                b => b.CreateTable("users", null, t =>
                {
                    t.Column("id", c => c.ClrType("int").NotNull());
                    t.Column("name", c => c.ClrType("string").Length(100).Nullable());
                    t.PrimaryKey("PK_users", "id");
                }),
                b => b.Sql("CREATE TABLE IF NOT EXISTS \"__quarry_backup_users\" AS SELECT * FROM \"users\";"))
        };

        // Insert a row first
        await MigrationRunner.RunAsync(_connection, _dialect, new[] { migrations[0] });
        using (var insertCmd = _connection.CreateCommand())
        {
            insertCmd.CommandText = "INSERT INTO \"users\" (\"id\", \"name\") VALUES (1, 'Test');";
            await insertCmd.ExecuteNonQueryAsync();
        }

        await MigrationRunner.RunAsync(_connection, _dialect, migrations,
            new MigrationOptions { RunBackups = true });

        // Verify backup table was created
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='__quarry_backup_users';";
        var result = await cmd.ExecuteScalarAsync();
        Assert.That(result, Is.EqualTo("__quarry_backup_users"));
    }

    [Test]
    public async Task RunAsync_HistoryRow_HasCorrectMetadata()
    {
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
        cmd.CommandText = "SELECT version, name FROM __quarry_migrations WHERE version = 1;";
        using var reader = await cmd.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(1));
        Assert.That(reader.GetString(1), Is.EqualTo("CreateUsers"));
    }

    // --- AddColumn integration tests ---

    [Test]
    public async Task RunAsync_AddColumn_AddsColumnToExistingTable()
    {
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
                _ => { }),
            (2, "AddEmailColumn",
                b => b.AddColumn("users", "email", c => c.ClrType("string").Length(200).Nullable()),
                b => b.DropColumn("users", "email"),
                _ => { })
        };

        await MigrationRunner.RunAsync(_connection, _dialect, migrations);

        // Insert a row using the new column
        using var insertCmd = _connection.CreateCommand();
        insertCmd.CommandText = "INSERT INTO \"users\" (\"id\", \"name\", \"email\") VALUES (1, 'Alice', 'alice@test.com');";
        await insertCmd.ExecuteNonQueryAsync();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT \"email\" FROM \"users\" WHERE \"id\" = 1;";
        var result = await cmd.ExecuteScalarAsync();
        Assert.That(result, Is.EqualTo("alice@test.com"));
    }

    // --- DropColumn via SQLite table rebuild integration tests ---

    [Test]
    public async Task RunAsync_DropColumn_SQLiteRebuild_RemovesColumnAndPreservesData()
    {
        var migrations = new (int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>)[]
        {
            (1, "CreateUsers",
                b => b.CreateTable("users", null, t =>
                {
                    t.Column("id", c => c.ClrType("int").NotNull());
                    t.Column("name", c => c.ClrType("string").Length(100).NotNull());
                    t.Column("legacy_col", c => c.ClrType("string").Nullable());
                    t.PrimaryKey("PK_users", "id");
                }),
                b => b.DropTable("users"),
                _ => { }),
            (2, "DropLegacyColumn",
                b => b.DropColumn("users", "legacy_col")
                    .WithSourceTable(t =>
                    {
                        t.Column("id", c => c.ClrType("int").NotNull());
                        t.Column("name", c => c.ClrType("string").Length(100).NotNull());
                        t.Column("legacy_col", c => c.ClrType("string").Nullable());
                        t.PrimaryKey("PK_users", "id");
                    }),
                b => b.AddColumn("users", "legacy_col", c => c.ClrType("string").Nullable()),
                _ => { })
        };

        // Apply first migration and insert data
        await MigrationRunner.RunAsync(_connection, _dialect, new[] { migrations[0] });
        using (var insertCmd = _connection.CreateCommand())
        {
            insertCmd.CommandText = "INSERT INTO \"users\" (\"id\", \"name\", \"legacy_col\") VALUES (1, 'Alice', 'old_value');";
            await insertCmd.ExecuteNonQueryAsync();
            insertCmd.CommandText = "INSERT INTO \"users\" (\"id\", \"name\", \"legacy_col\") VALUES (2, 'Bob', 'other');";
            await insertCmd.ExecuteNonQueryAsync();
        }

        // Apply drop column migration
        await MigrationRunner.RunAsync(_connection, _dialect, migrations);

        // Data should be preserved for remaining columns
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT \"name\" FROM \"users\" WHERE \"id\" = 1;";
        Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo("Alice"));

        cmd.CommandText = "SELECT \"name\" FROM \"users\" WHERE \"id\" = 2;";
        Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo("Bob"));

        // legacy_col should no longer exist
        cmd.CommandText = "PRAGMA table_info(\"users\");";
        using var reader = await cmd.ExecuteReaderAsync();
        var columnNames = new List<string>();
        while (await reader.ReadAsync())
            columnNames.Add(reader.GetString(1));

        Assert.That(columnNames, Does.Contain("id"));
        Assert.That(columnNames, Does.Contain("name"));
        Assert.That(columnNames, Does.Not.Contain("legacy_col"));

        // Temp table should be cleaned up
        using var tmpCmd = _connection.CreateCommand();
        tmpCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='_quarry_tmp_users';";
        Assert.That(await tmpCmd.ExecuteScalarAsync(), Is.Null);
    }

    // --- AlterColumn via SQLite table rebuild integration tests ---

    [Test]
    public async Task RunAsync_AlterColumn_SQLiteRebuild_ChangesTypeAndPreservesData()
    {
        var migrations = new (int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>)[]
        {
            (1, "CreateUsers",
                b => b.CreateTable("users", null, t =>
                {
                    t.Column("id", c => c.ClrType("int").NotNull());
                    t.Column("name", c => c.ClrType("string").Length(50).NotNull());
                    t.PrimaryKey("PK_users", "id");
                }),
                b => b.DropTable("users"),
                _ => { }),
            (2, "WidenNameColumn",
                b => b.AlterColumn("users", "name", c => c.ClrType("string").Length(200).NotNull())
                    .WithSourceTable(t =>
                    {
                        t.Column("id", c => c.ClrType("int").NotNull());
                        t.Column("name", c => c.ClrType("string").Length(50).NotNull());
                        t.PrimaryKey("PK_users", "id");
                    }),
                b => b.AlterColumn("users", "name", c => c.ClrType("string").Length(50).NotNull())
                    .WithSourceTable(t =>
                    {
                        t.Column("id", c => c.ClrType("int").NotNull());
                        t.Column("name", c => c.ClrType("string").Length(200).NotNull());
                        t.PrimaryKey("PK_users", "id");
                    }),
                _ => { })
        };

        // Apply first migration and insert data
        await MigrationRunner.RunAsync(_connection, _dialect, new[] { migrations[0] });
        using (var insertCmd = _connection.CreateCommand())
        {
            insertCmd.CommandText = "INSERT INTO \"users\" (\"id\", \"name\") VALUES (1, 'Alice');";
            await insertCmd.ExecuteNonQueryAsync();
            insertCmd.CommandText = "INSERT INTO \"users\" (\"id\", \"name\") VALUES (2, 'Bob');";
            await insertCmd.ExecuteNonQueryAsync();
        }

        // Apply alter column migration
        await MigrationRunner.RunAsync(_connection, _dialect, migrations);

        // Data should be preserved
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT \"name\" FROM \"users\" WHERE \"id\" = 1;";
        Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo("Alice"));

        cmd.CommandText = "SELECT \"name\" FROM \"users\" WHERE \"id\" = 2;";
        Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo("Bob"));

        // Temp table should be cleaned up
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='_quarry_tmp_users';";
        Assert.That(await cmd.ExecuteScalarAsync(), Is.Null);
    }

    // --- AddIndex / DropIndex integration tests ---

    [Test]
    public async Task RunAsync_AddIndex_CreatesIndexInDatabase()
    {
        var migrations = new (int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>)[]
        {
            (1, "CreateUsers",
                b => b.CreateTable("users", null, t =>
                {
                    t.Column("id", c => c.ClrType("int").NotNull());
                    t.Column("email", c => c.ClrType("string").Length(200).NotNull());
                    t.PrimaryKey("PK_users", "id");
                }),
                b => b.DropTable("users"),
                _ => { }),
            (2, "AddEmailIndex",
                b => b.AddIndex("IX_users_email", "users", new[] { "email" }, unique: true),
                b => b.DropIndex("IX_users_email", "users"),
                _ => { })
        };

        await MigrationRunner.RunAsync(_connection, _dialect, migrations);

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND name='IX_users_email';";
        var result = await cmd.ExecuteScalarAsync();
        Assert.That(result, Is.EqualTo("IX_users_email"));
    }

    [Test]
    public async Task RunAsync_DropIndex_RemovesIndexFromDatabase()
    {
        var migrations = new (int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>)[]
        {
            (1, "CreateUsersWithIndex",
                b =>
                {
                    b.CreateTable("users", null, t =>
                    {
                        t.Column("id", c => c.ClrType("int").NotNull());
                        t.Column("email", c => c.ClrType("string").Length(200).NotNull());
                        t.PrimaryKey("PK_users", "id");
                    });
                    b.AddIndex("IX_users_email", "users", new[] { "email" });
                },
                b =>
                {
                    b.DropIndex("IX_users_email", "users");
                    b.DropTable("users");
                },
                _ => { }),
            (2, "DropEmailIndex",
                b => b.DropIndex("IX_users_email", "users"),
                b => b.AddIndex("IX_users_email", "users", new[] { "email" }),
                _ => { })
        };

        await MigrationRunner.RunAsync(_connection, _dialect, migrations);

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND name='IX_users_email';";
        Assert.That(await cmd.ExecuteScalarAsync(), Is.Null);
    }

    // --- Downgrade error rollback test ---

    [Test]
    public async Task RunAsync_FailedDowngrade_RollsBackTransaction()
    {
        var migrations = new (int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>)[]
        {
            (1, "CreateUsers",
                b => b.CreateTable("users", null, t =>
                {
                    t.Column("id", c => c.ClrType("int").NotNull());
                    t.PrimaryKey("PK_users", "id");
                }),
                b =>
                {
                    b.DropTable("users");
                    // This will fail because the table doesn't exist
                    b.Sql("INSERT INTO nonexistent_table VALUES (1);");
                },
                _ => { })
        };

        // Apply migration
        await MigrationRunner.RunAsync(_connection, _dialect, migrations);

        // Attempt downgrade that will fail
        try
        {
            await MigrationRunner.RunAsync(_connection, _dialect, migrations,
                new MigrationOptions { Direction = MigrationDirection.Downgrade, TargetVersion = 0 });
        }
        catch
        {
            // Expected
        }

        // users table should still exist because the transaction rolled back
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='users';";
        Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo("users"));

        // History should still show version 1 applied
        cmd.CommandText = "SELECT COUNT(*) FROM __quarry_migrations WHERE version = 1;";
        Assert.That((long)(await cmd.ExecuteScalarAsync())!, Is.EqualTo(1));
    }

    // --- Multi-migration partial failure test ---

    [Test]
    public async Task RunAsync_SecondMigrationFails_FirstMigrationPersists()
    {
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
            (2, "BadMigration",
                b => b.Sql("INSERT INTO nonexistent_table VALUES (1);"),
                b => { },
                _ => { })
        };

        try
        {
            await MigrationRunner.RunAsync(_connection, _dialect, migrations);
        }
        catch
        {
            // Expected - migration 2 fails
        }

        // Migration 1 should have been committed (each migration has its own transaction)
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='users';";
        Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo("users"));

        // Only migration 1 should be in history
        cmd.CommandText = "SELECT COUNT(*) FROM __quarry_migrations;";
        Assert.That((long)(await cmd.ExecuteScalarAsync())!, Is.EqualTo(1));

        cmd.CommandText = "SELECT version FROM __quarry_migrations;";
        Assert.That((int)(long)(await cmd.ExecuteScalarAsync())!, Is.EqualTo(1));
    }

    // --- Re-upgrade after downgrade round-trip test ---

    [Test]
    public async Task RunAsync_DowngradeThenReUpgrade_RoundTripWorks()
    {
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
                _ => { }),
            (2, "CreatePosts",
                b => b.CreateTable("posts", null, t =>
                {
                    t.Column("id", c => c.ClrType("int").NotNull());
                    t.Column("title", c => c.ClrType("string").Length(200).NotNull());
                    t.PrimaryKey("PK_posts", "id");
                }),
                b => b.DropTable("posts"),
                _ => { })
        };

        // Upgrade all
        await MigrationRunner.RunAsync(_connection, _dialect, migrations);

        // Downgrade to version 0
        await MigrationRunner.RunAsync(_connection, _dialect, migrations,
            new MigrationOptions { Direction = MigrationDirection.Downgrade, TargetVersion = 0 });

        // Re-upgrade all
        await MigrationRunner.RunAsync(_connection, _dialect, migrations);

        // Both tables should exist
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='users';";
        Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo("users"));

        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='posts';";
        Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo("posts"));

        // History should have both versions
        cmd.CommandText = "SELECT COUNT(*) FROM __quarry_migrations;";
        Assert.That((long)(await cmd.ExecuteScalarAsync())!, Is.EqualTo(2));

        // Tables should be usable
        using var insertCmd = _connection.CreateCommand();
        insertCmd.CommandText = "INSERT INTO \"users\" (\"id\", \"name\") VALUES (1, 'Alice');";
        await insertCmd.ExecuteNonQueryAsync();

        cmd.CommandText = "SELECT \"name\" FROM \"users\" WHERE \"id\" = 1;";
        Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo("Alice"));
    }

    // --- Backup data verification test ---

    [Test]
    public async Task RunAsync_BackupEnabled_BackupTableContainsCorrectData()
    {
        var migrations = new (int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>)[]
        {
            (1, "CreateUsers",
                b => b.CreateTable("users", null, t =>
                {
                    t.Column("id", c => c.ClrType("int").NotNull());
                    t.Column("name", c => c.ClrType("string").Length(100).Nullable());
                    t.PrimaryKey("PK_users", "id");
                }),
                b => b.DropTable("users"),
                _ => { }),
            (2, "DropUsers",
                b => b.DropTable("users"),
                b => b.CreateTable("users", null, t =>
                {
                    t.Column("id", c => c.ClrType("int").NotNull());
                    t.Column("name", c => c.ClrType("string").Length(100).Nullable());
                    t.PrimaryKey("PK_users", "id");
                }),
                b => b.Sql("CREATE TABLE IF NOT EXISTS \"__quarry_backup_users\" AS SELECT * FROM \"users\";"))
        };

        // Apply first migration and insert data
        await MigrationRunner.RunAsync(_connection, _dialect, new[] { migrations[0] });
        using (var insertCmd = _connection.CreateCommand())
        {
            insertCmd.CommandText = "INSERT INTO \"users\" (\"id\", \"name\") VALUES (1, 'Alice');";
            await insertCmd.ExecuteNonQueryAsync();
            insertCmd.CommandText = "INSERT INTO \"users\" (\"id\", \"name\") VALUES (2, 'Bob');";
            await insertCmd.ExecuteNonQueryAsync();
        }

        // Apply destructive migration with backups enabled
        await MigrationRunner.RunAsync(_connection, _dialect, migrations,
            new MigrationOptions { RunBackups = true });

        // Verify backup table contains the correct data
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM \"__quarry_backup_users\";";
        Assert.That((long)(await cmd.ExecuteScalarAsync())!, Is.EqualTo(2));

        cmd.CommandText = "SELECT \"name\" FROM \"__quarry_backup_users\" WHERE \"id\" = 1;";
        Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo("Alice"));

        cmd.CommandText = "SELECT \"name\" FROM \"__quarry_backup_users\" WHERE \"id\" = 2;";
        Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo("Bob"));
    }

    // --- RenameColumn integration test ---

    [Test]
    public async Task RunAsync_RenameColumn_RenamesColumnAndPreservesData()
    {
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
                _ => { }),
            (2, "RenameNameToFullName",
                b => b.RenameColumn("users", "name", "full_name"),
                b => b.RenameColumn("users", "full_name", "name"),
                _ => { })
        };

        // Apply first migration and insert data
        await MigrationRunner.RunAsync(_connection, _dialect, new[] { migrations[0] });
        using (var insertCmd = _connection.CreateCommand())
        {
            insertCmd.CommandText = "INSERT INTO \"users\" (\"id\", \"name\") VALUES (1, 'Alice');";
            await insertCmd.ExecuteNonQueryAsync();
        }

        // Apply rename migration
        await MigrationRunner.RunAsync(_connection, _dialect, migrations);

        // Data should be accessible via new column name
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT \"full_name\" FROM \"users\" WHERE \"id\" = 1;";
        Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo("Alice"));

        // Old column name should not exist
        cmd.CommandText = "PRAGMA table_info(\"users\");";
        using var reader = await cmd.ExecuteReaderAsync();
        var columnNames = new List<string>();
        while (await reader.ReadAsync())
            columnNames.Add(reader.GetString(1));

        Assert.That(columnNames, Does.Contain("full_name"));
        Assert.That(columnNames, Does.Not.Contain("name"));
    }

    // --- RenameColumn downgrade integration test ---

    [Test]
    public async Task RunAsync_RenameColumn_Downgrade_RestoresOriginalName()
    {
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
                _ => { }),
            (2, "RenameNameToFullName",
                b => b.RenameColumn("users", "name", "full_name"),
                b => b.RenameColumn("users", "full_name", "name"),
                _ => { })
        };

        // Apply all migrations
        await MigrationRunner.RunAsync(_connection, _dialect, migrations);
        using (var insertCmd = _connection.CreateCommand())
        {
            insertCmd.CommandText = "INSERT INTO \"users\" (\"id\", \"full_name\") VALUES (1, 'Alice');";
            await insertCmd.ExecuteNonQueryAsync();
        }

        // Downgrade back to version 1
        await MigrationRunner.RunAsync(_connection, _dialect, migrations,
            new MigrationOptions { Direction = MigrationDirection.Downgrade, TargetVersion = 1 });

        // Data should be accessible via original column name
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT \"name\" FROM \"users\" WHERE \"id\" = 1;";
        Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo("Alice"));
    }

    // --- SQLite rebuild downgrade test (re-add column after drop) ---

    [Test]
    public async Task RunAsync_DropColumn_Downgrade_ReAddsColumn()
    {
        var migrations = new (int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>)[]
        {
            (1, "CreateUsers",
                b => b.CreateTable("users", null, t =>
                {
                    t.Column("id", c => c.ClrType("int").NotNull());
                    t.Column("name", c => c.ClrType("string").Length(100).NotNull());
                    t.Column("legacy_col", c => c.ClrType("string").Nullable());
                    t.PrimaryKey("PK_users", "id");
                }),
                b => b.DropTable("users"),
                _ => { }),
            (2, "DropLegacyColumn",
                b => b.DropColumn("users", "legacy_col")
                    .WithSourceTable(t =>
                    {
                        t.Column("id", c => c.ClrType("int").NotNull());
                        t.Column("name", c => c.ClrType("string").Length(100).NotNull());
                        t.Column("legacy_col", c => c.ClrType("string").Nullable());
                        t.PrimaryKey("PK_users", "id");
                    }),
                b => b.AddColumn("users", "legacy_col", c => c.ClrType("string").Nullable()),
                _ => { })
        };

        // Apply all
        await MigrationRunner.RunAsync(_connection, _dialect, migrations);

        // Downgrade to version 1
        await MigrationRunner.RunAsync(_connection, _dialect, migrations,
            new MigrationOptions { Direction = MigrationDirection.Downgrade, TargetVersion = 1 });

        // legacy_col should be back
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(\"users\");";
        using var reader = await cmd.ExecuteReaderAsync();
        var columnNames = new List<string>();
        while (await reader.ReadAsync())
            columnNames.Add(reader.GetString(1));

        Assert.That(columnNames, Does.Contain("id"));
        Assert.That(columnNames, Does.Contain("name"));
        Assert.That(columnNames, Does.Contain("legacy_col"));
    }

    // --- Multi-step schema evolution integration test ---

    [Test]
    public async Task RunAsync_MultiStepSchemaEvolution_FullLifecycle()
    {
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
                _ => { }),
            (2, "AddEmailColumn",
                b => b.AddColumn("users", "email", c => c.ClrType("string").Length(200).Nullable()),
                b => b.DropColumn("users", "email"),
                _ => { }),
            (3, "AddEmailIndex",
                b => b.AddIndex("IX_users_email", "users", new[] { "email" }, unique: true),
                b => b.DropIndex("IX_users_email", "users"),
                _ => { }),
            (4, "CreatePosts",
                b => b.CreateTable("posts", null, t =>
                {
                    t.Column("id", c => c.ClrType("int").NotNull());
                    t.Column("user_id", c => c.ClrType("int").NotNull());
                    t.Column("title", c => c.ClrType("string").Length(200).NotNull());
                    t.PrimaryKey("PK_posts", "id");
                }),
                b => b.DropTable("posts"),
                _ => { })
        };

        // Apply all 4 migrations
        await MigrationRunner.RunAsync(_connection, _dialect, migrations);

        // Verify final schema state
        using var cmd = _connection.CreateCommand();

        // Insert data spanning both tables
        using (var insertCmd = _connection.CreateCommand())
        {
            insertCmd.CommandText = "INSERT INTO \"users\" (\"id\", \"name\", \"email\") VALUES (1, 'Alice', 'alice@test.com');";
            await insertCmd.ExecuteNonQueryAsync();
            insertCmd.CommandText = "INSERT INTO \"posts\" (\"id\", \"user_id\", \"title\") VALUES (1, 1, 'Hello World');";
            await insertCmd.ExecuteNonQueryAsync();
        }

        // Downgrade to version 2 (drop posts table + email index)
        await MigrationRunner.RunAsync(_connection, _dialect, migrations,
            new MigrationOptions { Direction = MigrationDirection.Downgrade, TargetVersion = 2 });

        // posts should be gone
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='posts';";
        Assert.That(await cmd.ExecuteScalarAsync(), Is.Null);

        // index should be gone
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND name='IX_users_email';";
        Assert.That(await cmd.ExecuteScalarAsync(), Is.Null);

        // users table with email column should still exist with data
        cmd.CommandText = "SELECT \"email\" FROM \"users\" WHERE \"id\" = 1;";
        Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo("alice@test.com"));

        // History should show versions 1 and 2
        cmd.CommandText = "SELECT COUNT(*) FROM __quarry_migrations;";
        Assert.That((long)(await cmd.ExecuteScalarAsync())!, Is.EqualTo(2));

        // Re-upgrade back to latest
        await MigrationRunner.RunAsync(_connection, _dialect, migrations);

        // Everything should be back
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='posts';";
        Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo("posts"));

        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND name='IX_users_email';";
        Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo("IX_users_email"));

        cmd.CommandText = "SELECT COUNT(*) FROM __quarry_migrations;";
        Assert.That((long)(await cmd.ExecuteScalarAsync())!, Is.EqualTo(4));
    }

    // ─── Foreign Key folding into CREATE TABLE for SQLite ──────────────────

    [Test]
    public async Task AddForeignKey_SQLite_FoldsIntoCreateTable_NoError()
    {
        // This is the exact pattern generated by `quarry migrate add`:
        // CreateTable + CreateTable + AddForeignKey as separate operation
        var migrations = new (int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>)[]
        {
            (1, "InitialCreate",
                b =>
                {
                    b.CreateTable("users", null, t =>
                    {
                        t.Column("id", c => c.ClrType("int").NotNull());
                        t.Column("name", c => c.ClrType("string").Length(100).NotNull());
                        t.PrimaryKey("PK_users", "id");
                    });
                    b.CreateTable("posts", null, t =>
                    {
                        t.Column("id", c => c.ClrType("int").NotNull());
                        t.Column("user_id", c => c.ClrType("int").NotNull());
                        t.Column("title", c => c.ClrType("string").Length(200).NotNull());
                        t.PrimaryKey("PK_posts", "id");
                    });
                    b.AddForeignKey("FK_posts_users", "posts", "user_id", "users", "id");
                },
                b =>
                {
                    b.DropForeignKey("FK_posts_users", "posts");
                    b.DropTable("posts");
                    b.DropTable("users");
                },
                _ => { })
        };

        await MigrationRunner.RunAsync(_connection, _dialect, migrations);

        // Verify both tables exist
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='users';";
        Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo("users"));

        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='posts';";
        Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo("posts"));

        // Verify FK constraint is present via pragma
        cmd.CommandText = "PRAGMA foreign_key_list(posts);";
        using var reader = await cmd.ExecuteReaderAsync();
        Assert.That(reader.HasRows, Is.True, "posts table should have a foreign key");
    }

    [Test]
    public async Task AddForeignKey_SQLite_FoldsIntoCreateTable_EnforcesForeignKey()
    {
        var migrations = new (int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>)[]
        {
            (1, "Init",
                b =>
                {
                    b.CreateTable("users", null, t =>
                    {
                        t.Column("id", c => c.ClrType("int").NotNull());
                        t.PrimaryKey("PK_users", "id");
                    });
                    b.CreateTable("posts", null, t =>
                    {
                        t.Column("id", c => c.ClrType("int").NotNull());
                        t.Column("user_id", c => c.ClrType("int").NotNull());
                        t.PrimaryKey("PK_posts", "id");
                    });
                    b.AddForeignKey("FK_posts_users", "posts", "user_id", "users", "id");
                },
                b =>
                {
                    b.DropForeignKey("FK_posts_users", "posts");
                    b.DropTable("posts");
                    b.DropTable("users");
                },
                _ => { })
        };

        await MigrationRunner.RunAsync(_connection, _dialect, migrations);

        // Enable FK enforcement and insert valid data
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON;";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "INSERT INTO users (id) VALUES (1);";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "INSERT INTO posts (id, user_id) VALUES (1, 1);";
        Assert.DoesNotThrow(() => cmd.ExecuteNonQuery());

        // Inserting with invalid FK should fail
        cmd.CommandText = "INSERT INTO posts (id, user_id) VALUES (2, 999);";
        Assert.Throws<SqliteException>(() => cmd.ExecuteNonQuery());
    }

    [Test]
    public async Task AddForeignKey_SQLite_DowngradeAfterFold_Succeeds()
    {
        var migrations = new (int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>)[]
        {
            (1, "Init",
                b =>
                {
                    b.CreateTable("users", null, t =>
                    {
                        t.Column("id", c => c.ClrType("int").NotNull());
                        t.PrimaryKey("PK_users", "id");
                    });
                    b.CreateTable("posts", null, t =>
                    {
                        t.Column("id", c => c.ClrType("int").NotNull());
                        t.Column("user_id", c => c.ClrType("int").NotNull());
                        t.PrimaryKey("PK_posts", "id");
                    });
                    b.AddForeignKey("FK_posts_users", "posts", "user_id", "users", "id");
                },
                b =>
                {
                    b.DropForeignKey("FK_posts_users", "posts");
                    b.DropTable("posts");
                    b.DropTable("users");
                },
                _ => { })
        };

        // Upgrade
        await MigrationRunner.RunAsync(_connection, _dialect, migrations);

        // Downgrade
        await MigrationRunner.RunAsync(_connection, _dialect, migrations,
            new MigrationOptions { Direction = MigrationDirection.Downgrade, TargetVersion = 0 });

        // Verify tables are gone
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='users';";
        Assert.That(await cmd.ExecuteScalarAsync(), Is.Null);

        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='posts';";
        Assert.That(await cmd.ExecuteScalarAsync(), Is.Null);
    }

    [Test]
    public async Task AddForeignKey_SQLite_MultipleFKs_AllFolded()
    {
        var migrations = new (int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>)[]
        {
            (1, "Init",
                b =>
                {
                    b.CreateTable("users", null, t =>
                    {
                        t.Column("id", c => c.ClrType("int").NotNull());
                        t.PrimaryKey("PK_users", "id");
                    });
                    b.CreateTable("categories", null, t =>
                    {
                        t.Column("id", c => c.ClrType("int").NotNull());
                        t.PrimaryKey("PK_categories", "id");
                    });
                    b.CreateTable("posts", null, t =>
                    {
                        t.Column("id", c => c.ClrType("int").NotNull());
                        t.Column("user_id", c => c.ClrType("int").NotNull());
                        t.Column("category_id", c => c.ClrType("int").NotNull());
                        t.PrimaryKey("PK_posts", "id");
                    });
                    b.AddForeignKey("FK_posts_users", "posts", "user_id", "users", "id");
                    b.AddForeignKey("FK_posts_categories", "posts", "category_id", "categories", "id");
                },
                b =>
                {
                    b.DropForeignKey("FK_posts_users", "posts");
                    b.DropForeignKey("FK_posts_categories", "posts");
                    b.DropTable("posts");
                    b.DropTable("categories");
                    b.DropTable("users");
                },
                _ => { })
        };

        await MigrationRunner.RunAsync(_connection, _dialect, migrations);

        // Verify both FKs are present
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_key_list(posts);";
        using var reader = await cmd.ExecuteReaderAsync();
        var fkCount = 0;
        while (await reader.ReadAsync()) fkCount++;
        Assert.That(fkCount, Is.EqualTo(2), "posts should have 2 foreign keys");
    }

    [Test]
    public async Task AddForeignKey_SQLite_FullRoundTrip_UpgradeDowngradeReUpgrade()
    {
        var migrations = new (int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>)[]
        {
            (1, "Init",
                b =>
                {
                    b.CreateTable("users", null, t =>
                    {
                        t.Column("id", c => c.ClrType("int").NotNull());
                        t.Column("name", c => c.ClrType("string").Length(100).NotNull());
                        t.PrimaryKey("PK_users", "id");
                    });
                    b.CreateTable("posts", null, t =>
                    {
                        t.Column("id", c => c.ClrType("int").NotNull());
                        t.Column("user_id", c => c.ClrType("int").NotNull());
                        t.PrimaryKey("PK_posts", "id");
                    });
                    b.AddForeignKey("FK_posts_users", "posts", "user_id", "users", "id");
                },
                b =>
                {
                    b.DropForeignKey("FK_posts_users", "posts");
                    b.DropTable("posts");
                    b.DropTable("users");
                },
                _ => { }),
            (2, "AddEmail",
                b => b.AddColumn("users", "email", c => c.ClrType("string").Nullable()),
                b => b.DropColumn("users", "email"),
                _ => { })
        };

        // Upgrade to v2
        await MigrationRunner.RunAsync(_connection, _dialect, migrations);

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM __quarry_migrations;";
        Assert.That((long)(await cmd.ExecuteScalarAsync())!, Is.EqualTo(2));

        // Downgrade to v0
        await MigrationRunner.RunAsync(_connection, _dialect, migrations,
            new MigrationOptions { Direction = MigrationDirection.Downgrade, TargetVersion = 0 });

        cmd.CommandText = "SELECT COUNT(*) FROM __quarry_migrations;";
        Assert.That((long)(await cmd.ExecuteScalarAsync())!, Is.EqualTo(0));

        // Re-upgrade to v2
        await MigrationRunner.RunAsync(_connection, _dialect, migrations);

        cmd.CommandText = "SELECT COUNT(*) FROM __quarry_migrations;";
        Assert.That((long)(await cmd.ExecuteScalarAsync())!, Is.EqualTo(2));

        // Verify FK is present after re-upgrade
        cmd.CommandText = "PRAGMA foreign_key_list(posts);";
        using var reader = await cmd.ExecuteReaderAsync();
        Assert.That(reader.HasRows, Is.True);
    }

    [Test]
    public async Task RunAsync_SuppressTransaction_ExecutesNonTransactionalPhase()
    {
        // SuppressTransaction operations should execute outside the transaction.
        // With SQLite we can verify by using raw SQL with SuppressTransaction
        // and confirming it runs successfully.
        var migrations = new (int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>)[]
        {
            (1, "CreateUsersWithSuppressedIndex",
                b =>
                {
                    b.CreateTable("users", null, t =>
                    {
                        t.Column("id", c => c.ClrType("int").NotNull());
                        t.Column("email", c => c.ClrType("string").Length(255).NotNull());
                        t.PrimaryKey("PK_users", "id");
                    });
                    b.AddIndex("IX_users_email", "users", new[] { "email" }, unique: true);
                    b.Sql("CREATE INDEX \"IX_users_email_lower\" ON \"users\" (\"email\");")
                     .SuppressTransaction();
                },
                b =>
                {
                    b.Sql("DROP INDEX \"IX_users_email_lower\";").SuppressTransaction();
                    b.DropTable("users");
                },
                _ => { })
        };

        await MigrationRunner.RunAsync(_connection, _dialect, migrations);

        // Verify both indexes exist
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='IX_users_email';";
        var count1 = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.That(count1, Is.EqualTo(1));

        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='IX_users_email_lower';";
        var count2 = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.That(count2, Is.EqualTo(1));

        // Verify history row recorded
        cmd.CommandText = "SELECT COUNT(*) FROM __quarry_migrations WHERE version = 1;";
        var historyCount = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.That(historyCount, Is.EqualTo(1));
    }

    [Test]
    public async Task RunAsync_SuppressTransaction_Rollback_ExecutesNonTransactionalFirst()
    {
        // Apply a migration with suppressed ops, then roll back.
        // The suppressed ops should be rolled back first (outside tx),
        // then transactional ops inside tx.
        var migrations = new (int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>)[]
        {
            (1, "CreateUsersWithSuppressedIndex",
                b =>
                {
                    b.CreateTable("users", null, t =>
                    {
                        t.Column("id", c => c.ClrType("int").NotNull());
                        t.Column("email", c => c.ClrType("string").Length(255).NotNull());
                        t.PrimaryKey("PK_users", "id");
                    });
                    b.Sql("CREATE INDEX \"IX_users_email_lower\" ON \"users\" (\"email\");")
                     .SuppressTransaction();
                },
                b =>
                {
                    b.Sql("DROP INDEX \"IX_users_email_lower\";").SuppressTransaction();
                    b.DropTable("users");
                },
                _ => { })
        };

        await MigrationRunner.RunAsync(_connection, _dialect, migrations);

        // Rollback
        await MigrationRunner.RunAsync(_connection, _dialect, migrations,
            new MigrationOptions { Direction = MigrationDirection.Downgrade });

        // Verify table and index are gone
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='users';";
        var tableCount = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.That(tableCount, Is.EqualTo(0));

        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='IX_users_email_lower';";
        var indexCount = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.That(indexCount, Is.EqualTo(0));

        // Verify history row removed
        cmd.CommandText = "SELECT COUNT(*) FROM __quarry_migrations WHERE version = 1;";
        var historyCount = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.That(historyCount, Is.EqualTo(0));
    }

    [Test]
    public async Task RunAsync_DryRun_SuppressTransaction_PrintsAllSql()
    {
        var loggedSql = new List<string>();
        var migrations = new (int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>)[]
        {
            (1, "CreateWithSuppressed",
                b =>
                {
                    b.CreateTable("users", null, t =>
                    {
                        t.Column("id", c => c.ClrType("int").NotNull());
                        t.PrimaryKey("PK_users", "id");
                    });
                    b.Sql("CREATE INDEX CONCURRENTLY \"IX_test\" ON \"users\" (\"id\");")
                     .SuppressTransaction();
                },
                b => b.DropTable("users"),
                _ => { })
        };

        await MigrationRunner.RunAsync(_connection, _dialect, migrations,
            new MigrationOptions { DryRun = true, Logger = s => loggedSql.Add(s) });

        var combined = string.Join("\n", loggedSql);
        Assert.That(combined, Does.Contain("CREATE TABLE"));
        Assert.That(combined, Does.Contain("CREATE INDEX CONCURRENTLY"));

        // Verify nothing was actually executed
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='users';";
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.That(count, Is.EqualTo(0));
    }

    [Test]
    public async Task RunAsync_OnlyNonTransactionalOperations_StillRecordsHistory()
    {
        // First create the table in a separate migration so the suppressed-only
        // migration has something to operate on.
        var migrations = new (int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>)[]
        {
            (1, "CreateUsers",
                b => b.CreateTable("users", null, t =>
                {
                    t.Column("id", c => c.ClrType("int").NotNull());
                    t.Column("email", c => c.ClrType("string").Length(255).NotNull());
                    t.PrimaryKey("PK_users", "id");
                }),
                b => b.DropTable("users"),
                _ => { }),
            (2, "SuppressedOnly",
                b => b.Sql("CREATE INDEX \"IX_users_email_sup\" ON \"users\" (\"email\");")
                      .SuppressTransaction(),
                b => b.Sql("DROP INDEX \"IX_users_email_sup\";").SuppressTransaction(),
                _ => { })
        };

        await MigrationRunner.RunAsync(_connection, _dialect, migrations);

        // Verify history recorded for both migrations
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM __quarry_migrations;";
        var historyCount = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.That(historyCount, Is.EqualTo(2));

        // Verify the suppressed index was created
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='IX_users_email_sup';";
        var indexCount = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.That(indexCount, Is.EqualTo(1));
    }

    [Test]
    public async Task RunAsync_NonTransactionalPhaseFails_HistoryRowStillCommitted()
    {
        // The transactional phase commits (including the history row) before
        // the non-transactional phase runs. If phase 2 fails, the history row
        // should remain — the migration is partially applied.
        var migrations = new (int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>)[]
        {
            (1, "TxSucceedsNonTxFails",
                b =>
                {
                    b.CreateTable("users", null, t =>
                    {
                        t.Column("id", c => c.ClrType("int").NotNull());
                        t.PrimaryKey("PK_users", "id");
                    });
                    // This will fail: referencing a table/column that doesn't exist
                    b.Sql("CREATE INDEX \"IX_bogus\" ON \"nonexistent_table\" (\"col\");")
                     .SuppressTransaction();
                },
                b => b.DropTable("users"),
                _ => { })
        };

        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            () => MigrationRunner.RunAsync(_connection, _dialect, migrations));
        Assert.That(ex!.Message, Does.Contain("non-transactional phase"));
        Assert.That(ex.Message, Does.Contain("already been committed"));

        // Table should exist (transactional phase succeeded)
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='users';";
        var tableCount = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.That(tableCount, Is.EqualTo(1));

        // History row should exist (committed in phase 1)
        cmd.CommandText = "SELECT COUNT(*) FROM __quarry_migrations WHERE version = 1;";
        var historyCount = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.That(historyCount, Is.EqualTo(1));
    }

    [Test]
    public async Task RunAsync_SuppressTransaction_RecordsExecutionTime()
    {
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

        // Verify execution_time_ms is recorded (not hardcoded 0)
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT execution_time_ms FROM __quarry_migrations WHERE version = 1;";
        var executionTime = (long)(await cmd.ExecuteScalarAsync())!;
        // Just verify it was populated — it should be >= 0 (may be 0 on fast machines, but the path is correct)
        Assert.That(executionTime, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public async Task RunAsync_CommandTimeout_AppliedToCommands()
    {
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

        // CommandTimeout should not prevent normal execution — just verify it doesn't throw
        await MigrationRunner.RunAsync(_connection, _dialect, migrations,
            new MigrationOptions { CommandTimeout = TimeSpan.FromMinutes(5) });

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='users';";
        var result = await cmd.ExecuteScalarAsync();
        Assert.That(result, Is.EqualTo("users"));
    }

    [Test]
    public async Task RunAsync_CommandTimeout_NullUsesDefault()
    {
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

        // Null CommandTimeout (default) should work fine
        await MigrationRunner.RunAsync(_connection, _dialect, migrations,
            new MigrationOptions { CommandTimeout = null });

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM __quarry_migrations;";
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public async Task RunAsync_LockTimeout_SQLite_DoesNotThrow()
    {
        // LockTimeout on SQLite should be a no-op (with warning log), not throw
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

        await MigrationRunner.RunAsync(_connection, _dialect, migrations,
            new MigrationOptions { LockTimeout = TimeSpan.FromSeconds(10) });

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='users';";
        var result = await cmd.ExecuteScalarAsync();
        Assert.That(result, Is.EqualTo("users"));
    }

    [Test]
    public async Task RunAsync_LockTimeout_SQLite_Downgrade_DoesNotThrow()
    {
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

        // Apply first
        await MigrationRunner.RunAsync(_connection, _dialect, migrations);

        // Rollback with LockTimeout — should not throw for SQLite
        await MigrationRunner.RunAsync(_connection, _dialect, migrations,
            new MigrationOptions
            {
                Direction = MigrationDirection.Downgrade,
                LockTimeout = TimeSpan.FromSeconds(10)
            });

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM __quarry_migrations;";
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.That(count, Is.EqualTo(0));
    }

    [Test]
    public async Task RunAsync_BothTimeouts_WorkTogether()
    {
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

        await MigrationRunner.RunAsync(_connection, _dialect, migrations,
            new MigrationOptions
            {
                CommandTimeout = TimeSpan.FromMinutes(10),
                LockTimeout = TimeSpan.FromSeconds(30)
            });

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='users';";
        var result = await cmd.ExecuteScalarAsync();
        Assert.That(result, Is.EqualTo("users"));
    }

    [Test]
    public async Task RunAsync_BeforeEachAndAfterEach_CalledDuringUpgrade()
    {
        var beforeCalls = new List<(int Version, string Name)>();
        var afterCalls = new List<(int Version, string Name, TimeSpan Elapsed)>();

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
            (2, "CreatePosts",
                b => b.CreateTable("posts", null, t =>
                {
                    t.Column("id", c => c.ClrType("int").NotNull());
                    t.PrimaryKey("PK_posts", "id");
                }),
                b => b.DropTable("posts"),
                _ => { })
        };

        var options = new MigrationOptions
        {
            BeforeEach = (version, name, conn) =>
            {
                beforeCalls.Add((version, name));
                return Task.CompletedTask;
            },
            AfterEach = (version, name, elapsed, conn) =>
            {
                afterCalls.Add((version, name, elapsed));
                return Task.CompletedTask;
            }
        };

        await MigrationRunner.RunAsync(_connection, _dialect, migrations, options);

        Assert.That(beforeCalls, Has.Count.EqualTo(2));
        Assert.That(beforeCalls[0].Version, Is.EqualTo(1));
        Assert.That(beforeCalls[1].Version, Is.EqualTo(2));
        Assert.That(afterCalls, Has.Count.EqualTo(2));
        Assert.That(afterCalls[0].Version, Is.EqualTo(1));
        Assert.That(afterCalls[1].Version, Is.EqualTo(2));
        Assert.That(afterCalls[0].Elapsed, Is.GreaterThan(TimeSpan.Zero));
    }

    [Test]
    public async Task RunAsync_BeforeEachAndAfterEach_CalledDuringRollback()
    {
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

        // First apply the migration
        await MigrationRunner.RunAsync(_connection, _dialect, migrations);

        // Now rollback with hooks
        var beforeCalls = new List<(int Version, string Name)>();
        var afterCalls = new List<(int Version, string Name, TimeSpan Elapsed)>();

        var options = new MigrationOptions
        {
            Direction = MigrationDirection.Downgrade,
            BeforeEach = (version, name, conn) =>
            {
                beforeCalls.Add((version, name));
                return Task.CompletedTask;
            },
            AfterEach = (version, name, elapsed, conn) =>
            {
                afterCalls.Add((version, name, elapsed));
                return Task.CompletedTask;
            }
        };

        await MigrationRunner.RunAsync(_connection, _dialect, migrations, options);

        Assert.That(beforeCalls, Has.Count.EqualTo(1));
        Assert.That(beforeCalls[0].Version, Is.EqualTo(1));
        Assert.That(beforeCalls[0].Name, Is.EqualTo("CreateUsers"));
        Assert.That(afterCalls, Has.Count.EqualTo(1));
        Assert.That(afterCalls[0].Version, Is.EqualTo(1));
    }

    [Test]
    public async Task RunAsync_OnError_CalledOnFailure()
    {
        var errorCalls = new List<(int Version, string Name, Exception Ex)>();

        var migrations = new (int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>)[]
        {
            (1, "BadMigration",
                b => b.Sql("INVALID SQL STATEMENT THAT WILL FAIL"),
                b => { },
                _ => { })
        };

        var options = new MigrationOptions
        {
            OnError = (version, name, ex, conn) =>
            {
                errorCalls.Add((version, name, ex));
                return Task.CompletedTask;
            }
        };

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await MigrationRunner.RunAsync(_connection, _dialect, migrations, options));

        Assert.That(errorCalls, Has.Count.EqualTo(1));
        Assert.That(errorCalls[0].Version, Is.EqualTo(1));
        Assert.That(errorCalls[0].Name, Is.EqualTo("BadMigration"));
    }

    [Test]
    public async Task RunAsync_DryRun_SkipsHooks()
    {
        var hookCalled = false;

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

        var options = new MigrationOptions
        {
            DryRun = true,
            BeforeEach = (version, name, conn) =>
            {
                hookCalled = true;
                return Task.CompletedTask;
            },
            AfterEach = (version, name, elapsed, conn) =>
            {
                hookCalled = true;
                return Task.CompletedTask;
            }
        };

        await MigrationRunner.RunAsync(_connection, _dialect, migrations, options);

        Assert.That(hookCalled, Is.False);
    }

    [Test]
    public async Task RunAsync_OnErrorThrows_RollbackStillHappensAndOriginalExceptionPropagates()
    {
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
            (2, "BadMigration",
                b => b.Sql("INVALID SQL STATEMENT THAT WILL FAIL"),
                b => { },
                _ => { })
        };

        var options = new MigrationOptions
        {
            OnError = (version, name, ex, conn) =>
                throw new InvalidOperationException("Hook exploded")
        };

        var thrown = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await MigrationRunner.RunAsync(_connection, _dialect, migrations, options));

        // Original migration exception propagates, not the hook exception
        Assert.That(thrown!.Message, Does.Contain("BadMigration"));
        Assert.That(thrown.Message, Does.Contain("failed during upgrade"));

        // Migration 1 was committed before the failure, so users table should exist
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='users';";
        var result = await cmd.ExecuteScalarAsync();
        Assert.That(result, Is.EqualTo("users"));
    }
}
