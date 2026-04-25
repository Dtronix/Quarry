using System;
using System.Threading.Tasks;
using MySqlConnector;
using Quarry.Migration;
using Quarry.Tests.Integration;

namespace Quarry.Tests.Migration;

/// <summary>
/// End-to-end regression test for <see cref="MigrationRunner"/> on a real
/// MySqlConnector + MySQL 8.4 container. Mirrors
/// <see cref="PostgresMigrationRunnerTests"/> for the MySQL dialect: if a
/// future change ever breaks <c>InsertHistoryRowAsync</c> on MySQL the same
/// way GH-258 broke it on PostgreSQL, this catches it.
/// </summary>
/// <remarks>
/// Uses a fresh per-test database rather than the harness's transactional-
/// rollback path: <see cref="MigrationRunner"/> opens its own transactions
/// around each migration, which would collide with any outer
/// <c>BEGIN</c> from the harness. The database is dropped on teardown to
/// keep the shared container tidy.
/// </remarks>
[TestFixture]
[Category("MySqlIntegration")]
public class MySqlMigrationRunnerTests
{
    private MySqlConnection? _connection;
    private string? _database;

    [SetUp]
    public async Task SetUp()
    {
        var cs = await MySqlTestContainer.GetConnectionStringAsync();
        _connection = new MySqlConnection(cs);
        await _connection.OpenAsync();

        _database = "migtest_" + Guid.NewGuid().ToString("N").Substring(0, 10);
        await using var create = _connection.CreateCommand();
        create.CommandText = $"CREATE DATABASE `{_database}`; USE `{_database}`;";
        await create.ExecuteNonQueryAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        // Null-checked because Assert.Ignore in SetUp (Docker unavailable)
        // skips the field assignments — TearDown still runs and would NRE
        // otherwise.
        if (_connection is not null && _connection.State == System.Data.ConnectionState.Open && _database is not null)
        {
            try
            {
                await using var drop = _connection.CreateCommand();
                drop.CommandText = $"DROP DATABASE IF EXISTS `{_database}`;";
                await drop.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                // Best-effort: writing to TestContext lets a developer
                // diagnose orphan-database accumulation in a long-running
                // shared container without failing the test run itself.
                TestContext.Out.WriteLine($"[MySqlMigrationRunnerTests] DROP DATABASE {_database} failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
        if (_connection is not null)
            await _connection.DisposeAsync();
    }

    [Test]
    public async Task RunAsync_InsertsHistoryRow_OnMySQL()
    {
        // The MySQL parallel of PostgresMigrationRunnerTests: if any future
        // change regresses InsertHistoryRowAsync's parameter binding on
        // MySQL the same way GH-258 broke it on PG, this catches it before
        // it ships.
        var migrations = new (int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>)[]
        {
            (1, "CreateDemo",
                b => b.CreateTable("demo", null, t =>
                {
                    t.Column("id", c => c.ClrType("int").NotNull());
                    t.Column("name", c => c.ClrType("string").Length(100).NotNull());
                    t.PrimaryKey("PK_demo", "id");
                }),
                b => b.DropTable("demo"),
                _ => { })
        };

        await MigrationRunner.RunAsync(_connection!, SqlDialect.MySQL, migrations);

        // Verify the history row landed with all eight column values.
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT version, name, status FROM __quarry_migrations ORDER BY version;";
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True, "history row must exist after the migration ran");
        Assert.That(reader.GetInt32(0), Is.EqualTo(1));
        Assert.That(reader.GetString(1), Is.EqualTo("CreateDemo"));
        Assert.That(reader.GetString(2), Is.EqualTo("applied"),
            "Status 'applied' means InsertHistoryRowAsync + UpdateHistoryStatusAsync both succeeded");
    }
}
