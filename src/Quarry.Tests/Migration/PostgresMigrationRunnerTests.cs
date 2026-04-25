using System;
using System.Threading.Tasks;
using Npgsql;
using Quarry.Migration;
using Quarry.Tests.Integration;

namespace Quarry.Tests.Migration;

/// <summary>
/// End-to-end regression test for <see cref="MigrationRunner"/> on a real
/// Npgsql 10 + PostgreSQL 17 container. Closes the original GH-258 bug
/// site: before this fix, <c>InsertHistoryRowAsync</c> would throw
/// <c>08P01: bind message supplies 0 parameters, but prepared statement
/// "" requires 8</c> on every run.
/// </summary>
/// <remarks>
/// Uses a fresh per-test PostgreSQL schema rather than the harness's
/// transactional-rollback path: <see cref="MigrationRunner"/> opens its
/// own transactions around each migration, which would collide with any
/// outer <c>BEGIN</c> from the harness. The schema is dropped on teardown
/// to keep the shared container tidy.
/// </remarks>
[TestFixture]
[Category("NpgsqlIntegration")]
public class PostgresMigrationRunnerTests
{
    private NpgsqlConnection _connection = null!;
    private string _schema = null!;

    [SetUp]
    public async Task SetUp()
    {
        var cs = await PostgresTestContainer.GetConnectionStringAsync();
        _connection = new NpgsqlConnection(cs);
        await _connection.OpenAsync();

        _schema = "migtest_" + Guid.NewGuid().ToString("N").Substring(0, 10);
        await using var create = _connection.CreateCommand();
        create.CommandText = $"CREATE SCHEMA \"{_schema}\"; SET search_path TO \"{_schema}\";";
        await create.ExecuteNonQueryAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_connection.State == System.Data.ConnectionState.Open)
        {
            try
            {
                await using var drop = _connection.CreateCommand();
                drop.CommandText = $"DROP SCHEMA IF EXISTS \"{_schema}\" CASCADE;";
                await drop.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                // Best-effort: we don't let teardown noise mask the test
                // result. Writing to TestContext lets a developer diagnose
                // orphan-schema accumulation in a long-running shared
                // container without failing the test run itself.
                TestContext.Out.WriteLine($"[PostgresMigrationRunnerTests] DROP SCHEMA {_schema} failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
        await _connection.DisposeAsync();
    }

    [Test]
    public async Task RunAsync_InsertsHistoryRow_OnPostgreSQL()
    {
        // This is the exact shape that triggered GH-258: MigrationRunner
        // runs its CreateTable DDL, then InsertHistoryRowAsync writes a
        // row into __quarry_migrations with 8 parameters. If the fix
        // regressed, this would fail with 08P01 here.
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

        await MigrationRunner.RunAsync(_connection, SqlDialect.PostgreSQL, migrations);

        // Verify the history row landed with all eight column values — the
        // scenario PR #261 tried (and failed) to fix.
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"SELECT version, name, status FROM __quarry_migrations ORDER BY version;";
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True, "history row must exist after the migration ran");
        Assert.That(reader.GetInt32(0), Is.EqualTo(1));
        Assert.That(reader.GetString(1), Is.EqualTo("CreateDemo"));
        Assert.That(reader.GetString(2), Is.EqualTo("applied"),
            "Status 'applied' means InsertHistoryRowAsync + UpdateHistoryStatusAsync both succeeded — that is what #258 was blocking");
    }
}
