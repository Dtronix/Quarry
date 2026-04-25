using System.Threading.Tasks;
using MySqlConnector;
using Quarry.Tests.Samples;
using My = Quarry.Tests.Samples.My;

namespace Quarry.Tests.Integration;

/// <summary>
/// End-to-end execution tests on a real MySqlConnector + MySQL 8.4 container
/// covering the same generator + runtime code paths PR #266 verified for
/// PostgreSQL. The MySQL parallel of <see cref="PostgresIntegrationTests"/>.
/// </summary>
/// <remarks>
/// Tests use simple tables without DateTime columns to keep the scope
/// focused on parameter-binding correctness: if MySqlConnector ever drops
/// parameters in a future MySql.Data-style regression, any non-trivial
/// INSERT will throw before the scenario completes. Broader execution
/// coverage lives in the cross-dialect mirror; this fixture is the focused
/// regression guard.
///
/// The deconstruction pattern (<c>var (_, _, My, _) = t;</c>) matches the
/// rest of the test suite and anchors the generator's context resolution
/// to a local variable — property access on <c>t.My</c> triggers
/// cross-context interceptor emission.
/// </remarks>
[TestFixture]
[Category("MySqlIntegration")]
public class MySqlIntegrationTests
{
    [Test]
    public async Task ContainerBootstraps_OnMySQL()
    {
        // Bootstrap probe: prove the Testcontainers.MySql + MySqlConnector
        // wiring is reachable in this test process and on CI. If Docker is
        // unavailable the harness routes to Assert.Ignore with a clear
        // message; otherwise we expect a "8.4.x" version string.
        var cs = await MySqlTestContainer.GetConnectionStringAsync();
        await using var conn = new MySqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT VERSION()";
        var v = (string?)await cmd.ExecuteScalarAsync();
        Assert.That(v, Does.StartWith("8.4"));
    }

    [Test]
    public async Task EntityInsert_OnMySQL_ExecutesSuccessfully()
    {
        // Covers the single-entity INSERT path through MySqlConnector. The
        // generator emits `INSERT ... ; SELECT LAST_INSERT_ID()` for MySQL
        // (a different shape than PG's `RETURNING` clause), so this verifies
        // the multi-statement command + scalar read works end-to-end.
        await using var t = await QueryTestHarness.CreateAsync();
        var (_, _, My, _) = t;

        var newId = await My.Addresses()
            .Insert(new My.Address { City = "Austin", Street = "500 Congress Ave", ZipCode = "78701" })
            .ExecuteScalarAsync<int>();

        Assert.That(newId, Is.GreaterThan(2), "Seed populated AddressIds 1–2; auto-generated PK must continue from there");

        // Explicit projection so the chain terminates on IQueryBuilder<T,TResult>
        // rather than IQueryBuilder<T> — the entity-terminal fallback path
        // has an unrelated interceptor signature mismatch that is out of
        // scope for this fix (tracked separately).
        var city = await My.Addresses()
            .Where(a => a.AddressId == newId)
            .Select(a => a.City)
            .ExecuteFetchFirstOrDefaultAsync();
        Assert.That(city, Is.EqualTo("Austin"));

        var street = await My.Addresses()
            .Where(a => a.AddressId == newId)
            .Select(a => a.Street)
            .ExecuteFetchFirstOrDefaultAsync();
        Assert.That(street, Is.EqualTo("500 Congress Ave"));
    }

    [Test]
    public async Task InsertBatch_OnMySQL_ExecutesSuccessfully()
    {
        // Covers TerminalBodyEmitter batch-insert path on MySQL: multi-row
        // INSERT with runtime-expanded `?` placeholders. Each parameter
        // binds positionally — if a generator change ever breaks the
        // expansion shape, this fails immediately.
        await using var t = await QueryTestHarness.CreateAsync();
        var (_, _, My, _) = t;

        var warehouses = new[]
        {
            new My.Warehouse { WarehouseName = "North Atlantic Hub", Region = "US-E" },
            new My.Warehouse { WarehouseName = "APAC Ring",           Region = "AP" },
            new My.Warehouse { WarehouseName = "LATAM Bridge",        Region = "LATAM" },
        };

        var rows = await My.Warehouses()
            .InsertBatch(w => (w.WarehouseName, w.Region))
            .Values(warehouses)
            .ExecuteNonQueryAsync();

        Assert.That(rows, Is.EqualTo(3));

        // Explicit projection avoids the IQueryBuilder<T>-terminal overload
        // mismatch (unrelated to this fix; see EntityInsert test).
        var insertedNames = await My.Warehouses()
            .Where(w => w.WarehouseName == "North Atlantic Hub"
                     || w.WarehouseName == "APAC Ring"
                     || w.WarehouseName == "LATAM Bridge")
            .Select(w => w.WarehouseName)
            .ExecuteFetchAllAsync();
        Assert.That(insertedNames, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task WhereInCollection_OnMySQL_ExecutesSuccessfully()
    {
        // Covers TerminalEmitHelpers.EmitCollectionPartsPopulation combined
        // with CarrierEmitter's collection-parameter binding loop. The
        // collection is passed as a method argument so it is NOT
        // constant-folded to literal SQL — the generator must emit the
        // runtime-expansion path that builds __colNParts at runtime and
        // binds one DbParameter per element.
        await using var t = await QueryTestHarness.CreateAsync();
        var (_, _, My, _) = t;

        var wantedIds = BuildWantedIds();

        var names = await My.Users()
            .Where(u => wantedIds.Contains(u.UserId))
            .Select(u => u.UserName)
            .ExecuteFetchAllAsync();

        Assert.That(names, Has.Count.EqualTo(2));
        Assert.That(names, Does.Contain("Alice"));
        Assert.That(names, Does.Contain("Charlie"));
    }

    // Returning the array through a method call prevents the SqlExprAnnotator
    // constant-inlining pass from recognising the array initialiser — the
    // generator emits the runtime collection-expansion code path, which is
    // the code path GH-258 actually surfaces on real Npgsql, and we want
    // the same path exercised on MySqlConnector.
    private static int[] BuildWantedIds() => new[] { 1, 3 };
}
