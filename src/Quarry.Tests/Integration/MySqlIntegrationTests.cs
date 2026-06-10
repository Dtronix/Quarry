using System.Threading.Tasks;
using MySqlConnector;
using Quarry;
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

    [Test]
    public async Task WindowFunctionProjectionParams_OnMySQL_PreservesBindingAlignment()
    {
        // Audit surface #1 from issue #303: parameterized window-function arguments in
        // the SELECT projection render textually BEFORE the WHERE clause, but their
        // chain-call order (Select after Where) gives them HIGHER global slots than the
        // WHERE parameter. SQL text: `LAG(Total, ?, ?) OVER ... WHERE Total > ?` is
        // slots [1, 2, 0]; chain-order binding would feed `threshold` into the LAG
        // offset. The captured locals (not literals) force parameterization.
        await using var t = await QueryTestHarness.CreateAsync();
        var (_, _, My, _) = t;

        decimal threshold = 100.00m;
        int lagOffset = 1;
        decimal lagDefault = 0.00m;

        var rows = await My.Orders()
            .Where(o => o.Total > threshold)
            .Select(o => (o.Total, Prev: Sql.Lag(o.Total, lagOffset, lagDefault, over => over.OrderBy(o.OrderId))))
            .ExecuteFetchAllAsync();

        // Orders 1 (250.00) and 3 (150.00) survive `Total > 100`; LAG over OrderId
        // gives order 1 the default (0.00) and order 3 the previous Total (250.00).
        Assert.That(rows, Has.Count.EqualTo(2),
            "Expected orders 1 and 3 — a different count means a window-function arg " +
            "and the WHERE threshold swapped `?` slots.");
        Assert.That(rows, Does.Contain((250.00m, 0.00m)),
            "Order 1 must carry the LAG default (0.00) — anything else means lagDefault " +
            "did not reach the LAG default `?` slot.");
        Assert.That(rows, Does.Contain((150.00m, 250.00m)),
            "Order 3 must carry order 1's Total (250.00) as its LAG value — anything else " +
            "means lagOffset/lagDefault bound to the wrong `?` slots.");
    }

    [Test]
    public async Task WindowFunctionParamsWithParameterizedLimit_OnMySQL_PreservesBindingAlignment()
    {
        // Review finding #7/#13 for issue #303: parameterized pagination combined with
        // projection parameters. The LIMIT `?` is textually LAST but its global slot is
        // allocated after every chain param, while the projection params (LAG args)
        // render FIRST with mid-range slots: text [1, 2, 0, 3] vs chain [0, 1, 2, 3].
        // Marker emission must use the pagination param's true global slot (not the
        // clause-level running index, which excludes projection params) for the
        // extraction to validate and reorder this shape.
        await using var t = await QueryTestHarness.CreateAsync();
        var (_, _, My, _) = t;

        decimal threshold = 100.00m;
        int lagOffset = 1;
        decimal lagDefault = 0.00m;
        int take = 1;

        var rows = await My.Orders()
            .Where(o => o.Total > threshold)
            .OrderBy(o => o.OrderId)
            .Select(o => (o.Total, Prev: Sql.Lag(o.Total, lagOffset, lagDefault, over => over.OrderBy(o.OrderId))))
            .Limit(take)
            .ExecuteFetchAllAsync();

        // Orders 1 (250.00) and 3 (150.00) survive the filter; OrderId ASC puts order 1
        // first; LIMIT 1 keeps only it, carrying the LAG default.
        Assert.That(rows, Has.Count.EqualTo(1),
            "Expected exactly 1 row — a different count means `take` and another value " +
            "swapped `?` slots (e.g. threshold bound to LIMIT).");
        Assert.That(rows[0], Is.EqualTo((250.00m, 0.00m)),
            "Order 1 with the LAG default — anything else means a LAG arg, the WHERE " +
            "threshold, or the LIMIT param landed in the wrong `?` slot.");
    }

    [Test]
    public async Task ConditionalMaskWithDistinctOrderByWrap_OnMySQL_PreservesBindingAlignmentPerVariant()
    {
        // Audit surface #2 from issue #303: conditional clauses interact with the wrap's
        // hoisted ORDER BY param. The chain ranking is shared across mask variants —
        // masking the conditional WHERE off removes its `?` from the text but must not
        // disturb the surviving slots' relative order. Both variants execute here.
        await using var t = await QueryTestHarness.CreateAsync();
        var (_, _, My, _) = t;

        decimal threshold = 100.00m;
        decimal bias = 10000.00m;
        int minId = 3;

        // Variant: conditional filter OFF (mask 0) — slots [bias(2), threshold(0)] in text.
        bool applyMinOff = false;
        IQueryBuilder<My.Order> qOff = My.Orders().Where(o => o.Total > threshold);
        if (applyMinOff) { qOff = qOff.Where(o => o.OrderId >= minId); }
        var totalsOff = await qOff
            .OrderBy(o => o.Total + bias)
            .Distinct()
            .Select(o => o.Total)
            .ExecuteFetchAllAsync();

        Assert.That(totalsOff, Is.EqualTo(new[] { 150.00m, 250.00m }),
            "Mask-off variant must order by (Total + bias) ASC over orders 3 and 1 — " +
            "zero rows means bias landed in the WHERE `?`; wrong order means the hoisted " +
            "ORDER BY slot misbound.");

        // Variant: conditional filter ON (mask 1) — slots [bias(2), threshold(0), minId(1)] in text.
        bool applyMinOn = true;
        IQueryBuilder<My.Order> qOn = My.Orders().Where(o => o.Total > threshold);
        if (applyMinOn) { qOn = qOn.Where(o => o.OrderId >= minId); }
        var totalsOn = await qOn
            .OrderBy(o => o.Total + bias)
            .Distinct()
            .Select(o => o.Total)
            .ExecuteFetchAllAsync();

        Assert.That(totalsOn, Is.EqualTo(new[] { 150.00m }),
            "Mask-on variant must keep only order 3 (OrderId >= 3, Total 150.00) — " +
            "a different set means the conditional WHERE param and another slot swapped.");
    }

    [Test]
    public async Task CollectionExpansionWithDistinctOrderByWrap_OnMySQL_PreservesBindingAlignment()
    {
        // Audit surface #3 from issue #303: runtime collection expansion interleaved
        // with the wrap's hoisted ORDER BY param. Text order is [bias, id, id] (the
        // hoisted `?` first, then one `?` per expanded element inside IN (...)), while
        // chain order is [collection(0), bias(1)]. The method-call indirection keeps
        // the array from being constant-folded, forcing the runtime expansion path.
        await using var t = await QueryTestHarness.CreateAsync();
        var (_, _, My, _) = t;

        var wantedIds = BuildWantedIds();
        decimal bias = 10000.00m;

        var totals = await My.Orders()
            .Where(o => wantedIds.Contains(o.OrderId))
            .OrderBy(o => o.Total + bias)
            .Distinct()
            .Select(o => o.Total)
            .ExecuteFetchAllAsync();

        Assert.That(totals, Is.EqualTo(new[] { 150.00m, 250.00m }),
            "Orders 1 and 3 ordered by (Total + bias) ASC — zero rows means bias landed " +
            "in an IN-list `?` slot; wrong membership means an id reached the ORDER BY `?`.");
    }
}
