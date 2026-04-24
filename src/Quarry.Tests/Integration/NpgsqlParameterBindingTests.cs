using System;
using System.Data.Common;
using System.Threading.Tasks;
using Npgsql;

namespace Quarry.Tests.Integration;

/// <summary>
/// Regression documentation for GH-258 redux: encodes, against a real
/// Npgsql 10 + PostgreSQL 17 container, the four configurations that
/// matter for how Quarry assigns parameter names and why only one is
/// correct.
/// </summary>
/// <remarks>
/// <para>
/// The empirical result is that Npgsql 10 switches between named and
/// positional binding modes based on whether any <c>DbParameter</c> has a
/// <c>ParameterName</c> set — <em>not</em> based on what placeholder form
/// appears in <c>CommandText</c>. When any parameter has a name, Npgsql
/// looks for <c>@name</c>/<c>:name</c> markers in the SQL; if the SQL uses
/// native <c>$N</c> positional form there are no matches, and Npgsql sends
/// the Bind frame with zero parameter values — producing
/// <c>08P01: bind message supplies 0 parameters, but prepared statement
/// "" requires N</c>.
/// </para>
/// <para>
/// Five variants probed:
/// <list type="bullet">
///   <item><description><c>A</c>: <c>@pN</c> SQL + <c>@pN</c> name — works via Npgsql's <c>@name</c>→<c>$N</c> rewrite.</description></item>
///   <item><description><c>B</c>: <c>$N</c> SQL + <c>@pN</c> name — fails with 08P01 (the v0.3.0 state, original #258).</description></item>
///   <item><description><c>C</c>: <c>$N</c> SQL + <c>$N</c> name — fails with 08P01 (the v0.3.1/v0.3.2 state after PR #261).</description></item>
///   <item><description><c>D</c>: <c>$N</c> SQL + empty name — works via native positional binding.</description></item>
///   <item><description><c>E</c>: <c>$N</c> SQL + unset name — works via native positional binding.</description></item>
/// </list>
/// Quarry emits configuration D for PostgreSQL.
/// </para>
/// </remarks>
[TestFixture]
[Category("NpgsqlIntegration")]
public class NpgsqlParameterBindingTests
{
    private string _connectionString = null!;

    [OneTimeSetUp]
    public async Task SetUp()
    {
        _connectionString = await PostgresTestContainer.GetConnectionStringAsync();

        // Use a dedicated schema for the probe so it can't conflict with
        // baseline data the upgraded QueryTestHarness will add in Phase 4.
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var create = conn.CreateCommand();
        create.CommandText = @"
            CREATE SCHEMA IF NOT EXISTS probe;
            CREATE TABLE IF NOT EXISTS probe.history (
                version INTEGER NOT NULL,
                name TEXT NOT NULL,
                applied_at TEXT NOT NULL,
                checksum TEXT NOT NULL,
                execution_time_ms INTEGER NOT NULL,
                applied_by TEXT NOT NULL,
                started_at TEXT NOT NULL,
                status TEXT NOT NULL
            );";
        await create.ExecuteNonQueryAsync();
    }

    private async Task TruncateAsync()
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "TRUNCATE probe.history;";
        await cmd.ExecuteNonQueryAsync();
    }

    private static void AddParams(DbCommand cmd, Func<int, string?> nameFor)
    {
        object[] values =
        {
            /* version */           1,
            /* name */              "probe",
            /* applied_at */        DateTime.UtcNow.ToString("o"),
            /* checksum */          "abc",
            /* execution_time_ms */ 0,
            /* applied_by */        "tester",
            /* started_at */        DateTime.UtcNow.ToString("o"),
            /* status */            "running",
        };

        for (int i = 0; i < values.Length; i++)
        {
            var p = cmd.CreateParameter();
            var name = nameFor(i);
            if (name is not null)
                p.ParameterName = name;
            p.Value = values[i];
            cmd.Parameters.Add(p);
        }
    }

    private async Task<int> ExecuteInsertAsync(string sqlWithPlaceholders, Func<int, string?> nameFor)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sqlWithPlaceholders;
        AddParams(cmd, nameFor);
        return await cmd.ExecuteNonQueryAsync();
    }

    private static string BuildSql(Func<int, string> placeholderFor)
    {
        var values = string.Join(", ", Enumerable.Range(0, 8).Select(placeholderFor));
        return $@"INSERT INTO probe.history
            (version, name, applied_at, checksum, execution_time_ms, applied_by, started_at, status)
            VALUES ({values});";
    }

    [Test]
    public async Task A_AtPn_Sql_With_AtPn_Names_Works()
    {
        await TruncateAsync();
        var sql = BuildSql(i => $"@p{i}");
        var rows = await ExecuteInsertAsync(sql, i => $"@p{i}");
        Assert.That(rows, Is.EqualTo(1), "A: @pN SQL + @pN names — Npgsql's @name→$N rewrite should accept this");
    }

    [Test]
    public async Task B_DollarN_Sql_With_AtPn_Names_v030_State_Fails()
    {
        await TruncateAsync();
        var sql = BuildSql(i => $"${i + 1}");
        var ex = Assert.ThrowsAsync<PostgresException>(async () =>
            await ExecuteInsertAsync(sql, i => $"@p{i}"));
        Assert.That(ex!.SqlState, Is.EqualTo("08P01"),
            "B (v0.3.0 state): $N SQL with @pN names should reproduce the bind-count mismatch — this is why #258 was filed originally");
    }

    [Test]
    public async Task C_DollarN_Sql_With_DollarN_Names_v032_State_Fails()
    {
        await TruncateAsync();
        var sql = BuildSql(i => $"${i + 1}");
        var ex = Assert.ThrowsAsync<PostgresException>(async () =>
            await ExecuteInsertAsync(sql, i => $"${i + 1}"));
        Assert.That(ex!.SqlState, Is.EqualTo("08P01"),
            "C (v0.3.2 state): $N SQL with $N names still fails — PR #261's theory (matching name to placeholder) does not work; Npgsql still enters named mode as soon as any parameter has a name");
    }

    [Test]
    public async Task D_DollarN_Sql_With_Empty_Names_Works()
    {
        await TruncateAsync();
        var sql = BuildSql(i => $"${i + 1}");
        var rows = await ExecuteInsertAsync(sql, _ => "");
        Assert.That(rows, Is.EqualTo(1),
            "D: $N SQL + empty ParameterName — Npgsql uses native positional binding; this is the configuration Quarry emits for PostgreSQL");
    }

    [Test]
    public async Task E_DollarN_Sql_With_Unset_Names_Works()
    {
        await TruncateAsync();
        var sql = BuildSql(i => $"${i + 1}");
        var rows = await ExecuteInsertAsync(sql, _ => null);
        Assert.That(rows, Is.EqualTo(1),
            "E: $N SQL + unset ParameterName — equivalent to D; documents that leaving the name unset is also valid");
    }
}
