using System.Threading.Tasks;
using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;

namespace Quarry.Tests.Integration;

/// <summary>
/// End-to-end execution tests on a real Npgsql 10 + PostgreSQL 17 container
/// covering every generator + runtime code path PR #261 touched. These are
/// the tests that would have caught the original GH-258 bug if they'd
/// existed — and would catch any future regression in the same surface.
/// </summary>
/// <remarks>
/// Tests use simple tables without DateTime columns to keep the scope
/// focused on parameter-binding correctness: if Npgsql silently drops
/// parameters (the #258 failure mode), any non-trivial INSERT will throw
/// 08P01 before the scenario completes. Broader execution coverage can be
/// added in follow-up work; this file is the regression guard.
///
/// The deconstruction pattern (<c>var (_, Pg, _, _) = t;</c>) matches the
/// rest of the test suite and anchors the generator's context resolution
/// to a local variable — property access on <c>t.Pg</c> triggers
/// cross-context interceptor emission.
/// </remarks>
[TestFixture]
[Category("NpgsqlIntegration")]
internal class PostgresIntegrationTests
{
    [Test]
    public async Task EntityInsert_OnPostgreSQL_ExecutesSuccessfully()
    {
        // Covers CarrierEmitter.EmitCarrierInsertTerminal: single-entity
        // INSERT with RETURNING. PR #261 emitted ParameterName = "$N" here,
        // which Npgsql rejects with 08P01. This fix emits "" so Npgsql
        // stays on its native positional-binding path.
        await using var t = await QueryTestHarness.CreateAsync();
        var (_, Pg, _, _) = t;

        var newId = await Pg.Addresses()
            .Insert(new Pg.Address { City = "Austin", Street = "500 Congress Ave", ZipCode = "78701" })
            .ExecuteScalarAsync<int>();

        Assert.That(newId, Is.GreaterThan(2), "Seed populated AddressIds 1–2; auto-generated PK must continue from there");

        // Explicit projection so the chain terminates on IQueryBuilder<T,TResult>
        // rather than IQueryBuilder<T> — the entity-terminal fallback path
        // has an unrelated interceptor signature mismatch that is out of
        // scope for this fix (tracked separately).
        var city = await Pg.Addresses()
            .Where(a => a.AddressId == newId)
            .Select(a => a.City)
            .ExecuteFetchFirstOrDefaultAsync();
        Assert.That(city, Is.EqualTo("Austin"));

        var street = await Pg.Addresses()
            .Where(a => a.AddressId == newId)
            .Select(a => a.Street)
            .ExecuteFetchFirstOrDefaultAsync();
        Assert.That(street, Is.EqualTo("500 Congress Ave"));
    }

    [Test]
    public async Task InsertBatch_OnPostgreSQL_ExecutesSuccessfully()
    {
        // Covers TerminalBodyEmitter batch-insert path: multi-row INSERT with
        // runtime-expanded placeholders. PR #261 emitted
        // ParameterNames.Dollar(__paramIdx) here, which Npgsql rejects. This
        // fix emits "" so each parameter is positional-bound.
        await using var t = await QueryTestHarness.CreateAsync();
        var (_, Pg, _, _) = t;

        var warehouses = new[]
        {
            new Pg.Warehouse { WarehouseName = "North Atlantic Hub", Region = "US-E" },
            new Pg.Warehouse { WarehouseName = "APAC Ring",           Region = "AP" },
            new Pg.Warehouse { WarehouseName = "LATAM Bridge",        Region = "LATAM" },
        };

        var rows = await Pg.Warehouses()
            .InsertBatch(w => (w.WarehouseName, w.Region))
            .Values(warehouses)
            .ExecuteNonQueryAsync();

        Assert.That(rows, Is.EqualTo(3));

        // Explicit projection avoids the IQueryBuilder<T>-terminal overload
        // mismatch (unrelated to this fix; see EntityInsert test).
        var insertedNames = await Pg.Warehouses()
            .Where(w => w.WarehouseName == "North Atlantic Hub"
                     || w.WarehouseName == "APAC Ring"
                     || w.WarehouseName == "LATAM Bridge")
            .Select(w => w.WarehouseName)
            .ExecuteFetchAllAsync();
        Assert.That(insertedNames, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task WhereInCollection_OnPostgreSQL_ExecutesSuccessfully()
    {
        // Covers TerminalEmitHelpers.EmitCollectionPartsPopulation: expands
        // a .NET collection into N `$N` placeholders at runtime. Each
        // generated parameter goes through the same "ParameterName = \"\""
        // path the generator emits for PostgreSQL; if that regressed back
        // to Dollar() or AtP(), Npgsql would drop the parameters and the
        // query would return zero rows (or throw 08P01).
        await using var t = await QueryTestHarness.CreateAsync();
        var (_, Pg, _, _) = t;

        var wantedIds = new[] { 1, 3 };

        var names = await Pg.Users()
            .Where(u => wantedIds.Contains(u.UserId))
            .Select(u => u.UserName)
            .ExecuteFetchAllAsync();

        Assert.That(names, Has.Count.EqualTo(2));
        Assert.That(names, Does.Contain("Alice"));
        Assert.That(names, Does.Contain("Charlie"));
    }
}
