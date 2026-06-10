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

    [Test]
    public async Task DistinctOrderByWrap_ParameterizedWhereAndOrderBy_OnMySQL_PreservesBindingAlignment()
    {
        // Regression guard for MySQL positional `?` placeholder binding under the
        // DistinctOrderBy wrap path.
        //
        // ChainAnalyzer assigns global parameter indices in chain-call order, and
        // CarrierEmitter binds DbParameters in that same order. For SQLite and
        // SqlServer the placeholders are named (@pN — bound by ParameterName) and
        // for PostgreSQL they are explicitly numbered (`$N` — Npgsql positional
        // mode indexes the Bind frame by N), so out-of-order placeholder texts
        // bind correctly on those three. MySQL is the lone dialect where the
        // placeholder is opaque `?` and the Nth `?` in the SQL text is bound to
        // the Nth DbParameter added — if SQL text order ever diverges from the
        // chain-order add sequence, MySQL silently swaps values.
        //
        // The DistinctOrderBy wrap (SqlAssembler.RenderSelectSqlWithDistinctOrderByWrap)
        // is the documented divergence point: it hoists the OrderBy expression into
        // the inner SELECT (textually BEFORE the WHERE) while keeping the OrderBy
        // capture at its chain-order global slot (after the WHERE capture). Quarry
        // chains: `.Where(p0).OrderBy(p1).Distinct()` → MySQL SQL becomes
        // `... (col + ?) AS _o0 ... WHERE col > ?` — first `?` is bias-slot, second
        // is threshold-slot, but cmd.Parameters has threshold (P0) before bias (P1).
        //
        // Pre-fix observable: with threshold=100 / bias=10000 the executed query
        // collapses to `WHERE Total > 10000` (bias bound to the WHERE `?`) and
        // returns zero rows instead of the two distinct totals (250.00, 150.00)
        // from orders 1 and 3. The non-overlapping value ranges make the failure
        // mode unambiguous: a swap produces empty results, not a subtly wrong set.
        await using var t = await QueryTestHarness.CreateAsync();
        var (_, _, My, _) = t;

        decimal threshold = 100.00m;
        decimal bias = 10000.00m;

        var totals = await My.Orders()
            .Where(o => o.Total > threshold)
            .OrderBy(o => o.Total + bias)
            .Distinct()
            .Select(o => o.Total)
            .ExecuteFetchAllAsync();

        Assert.That(totals, Has.Count.EqualTo(2),
            "Expected 2 distinct Totals (250.00 from order 1, 150.00 from order 3). " +
            "Zero rows = MySQL bound `bias` to the WHERE `?` slot (positional ? misalignment " +
            "in the DistinctOrderBy wrap path).");
        Assert.That(totals, Does.Contain(250.00m),
            "Order 1 Total (250.00) must survive `Total > 100` filter — its absence " +
            "indicates the threshold parameter did not reach the WHERE `?`.");
        Assert.That(totals, Does.Contain(150.00m),
            "Order 3 Total (150.00) must survive `Total > 100` filter — its absence " +
            "indicates the threshold parameter did not reach the WHERE `?`.");
    }
}
