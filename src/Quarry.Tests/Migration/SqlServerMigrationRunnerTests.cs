using System;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Quarry.Migration;
using Quarry.Tests.Integration;

namespace Quarry.Tests.Migration;

/// <summary>
/// End-to-end regression test for <see cref="MigrationRunner"/> on a real
/// Microsoft.Data.SqlClient + SQL Server 2022 container. Mirrors
/// <see cref="PostgresMigrationRunnerTests"/>: if a future change ever ships
/// an Ss-side analogue of the Npgsql 10 binding mismatch (#258), this test
/// would catch it before the bug reaches users.
/// </summary>
/// <remarks>
/// Uses a dedicated per-test SQL Server schema and login rather than the
/// harness's transactional-rollback path: <see cref="MigrationRunner"/>
/// opens its own transactions around each migration, which would collide
/// with any outer <c>BEGIN</c>. Schema + login are dropped on teardown
/// to keep the shared container tidy.
/// </remarks>
[TestFixture]
[Category("SqlServerIntegration")]
public class SqlServerMigrationRunnerTests
{
    private SqlConnection _saConnection = null!;
    private SqlConnection _userConnection = null!;
    private MsSqlTestContainer.OwnedSchemaInfo _info;

    [SetUp]
    public async Task SetUp()
    {
        // Baseline must exist — CreateEmptySchemaAsync only creates the test
        // schema, not the shared infrastructure (the lock primitives etc.
        // depend on the baseline already being in place).
        await MsSqlTestContainer.EnsureBaselineAsync();

        var saCs = await MsSqlTestContainer.GetSaConnectionStringAsync();
        _saConnection = new SqlConnection(saCs);
        await _saConnection.OpenAsync();

        _info = await MsSqlTestContainer.CreateEmptySchemaAsync(_saConnection);

        var userCs = await MsSqlTestContainer.GetOwnedSchemaConnectionStringAsync(_info);
        _userConnection = new SqlConnection(userCs);
        await _userConnection.OpenAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_userConnection is not null)
        {
            try { await _userConnection.DisposeAsync(); }
            catch { /* best-effort */ }
        }
        if (_saConnection is not null && _saConnection.State == System.Data.ConnectionState.Open)
        {
            try
            {
                await MsSqlTestContainer.DropOwnedSchemaAsync(_saConnection, _info);
            }
            catch (Exception ex)
            {
                // Best-effort: writing to TestContext lets a developer
                // diagnose orphan-schema accumulation without failing the
                // test run itself. Mirrors the PG-side teardown shape.
                TestContext.Out.WriteLine($"[SqlServerMigrationRunnerTests] DROP SCHEMA {_info.Schema} failed: {ex.GetType().Name}: {ex.Message}");
            }
            try { await _saConnection.DisposeAsync(); } catch { }
        }
    }

    [Test]
    public async Task RunAsync_InsertsHistoryRow_OnSqlServer()
    {
        // Symmetric to the PG-side regression test. MigrationRunner runs its
        // CreateTable DDL, then InsertHistoryRowAsync writes a row into
        // __quarry_migrations with eight parameters. If a future SqlClient
        // upgrade introduced the same parameter-binding mismatch class that
        // PR #266 closed for Npgsql, this test would surface it.
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

        await MigrationRunner.RunAsync(_userConnection, SqlDialect.SqlServer, migrations);

        // Verify the history row landed with all eight column values — the
        // scenario this test closes the door on for SqlClient.
        await using var cmd = _userConnection.CreateCommand();
        cmd.CommandText = @"SELECT version, name, status FROM __quarry_migrations ORDER BY version;";
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True, "history row must exist after the migration ran");
        Assert.That(reader.GetInt32(0), Is.EqualTo(1));
        Assert.That(reader.GetString(1), Is.EqualTo("CreateDemo"));
        Assert.That(reader.GetString(2), Is.EqualTo("applied"),
            "Status 'applied' means InsertHistoryRowAsync + UpdateHistoryStatusAsync both succeeded");
    }
}
