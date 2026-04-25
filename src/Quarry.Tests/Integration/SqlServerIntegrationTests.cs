using System.Threading.Tasks;
using Quarry.Tests.Samples;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.Integration;

/// <summary>
/// End-to-end execution tests on a real Microsoft.Data.SqlClient + SQL Server
/// 2022 container covering the same generator + runtime code paths the
/// PostgreSQL counterpart exercises. These are the symmetric regression
/// guards for issue #270 — they would catch any future SqlClient binding /
/// type-inference regression on the SQL Server execution path.
/// </summary>
/// <remarks>
/// Mirrors <see cref="PostgresIntegrationTests"/>'s shape so a code reviewer
/// can diff the two files line-for-line and spot any divergence that isn't
/// inherent to the dialect. The deconstruction pattern
/// (<c>var (_, _, _, Ss) = t;</c>) anchors the generator's context resolution
/// to a local variable — property access on <c>t.Ss</c> would trigger
/// cross-context interceptor emission.
/// </remarks>
[TestFixture]
[Category("SqlServerIntegration")]
public class SqlServerIntegrationTests
{
    [Test]
    public async Task EntityInsert_OnSqlServer_ExecutesSuccessfully()
    {
        // Single-entity INSERT with OUTPUT INSERTED — the SQL Server analogue
        // of PG's RETURNING. Validates the generator's CarrierEmitter path
        // against a real SqlConnection (parameter naming, type inference,
        // Identity-column generated-id read-back).
        await using var t = await QueryTestHarness.CreateAsync();
        var (_, _, _, Ss) = t;

        var newId = await Ss.Addresses()
            .Insert(new Ss.Address { City = "Austin", Street = "500 Congress Ave", ZipCode = "78701" })
            .ExecuteScalarAsync<int>();

        Assert.That(newId, Is.GreaterThan(2), "Seed populated AddressIds 1–2; auto-generated PK must continue from there");

        // Explicit projection so the chain terminates on IQueryBuilder<T,TResult>
        // rather than IQueryBuilder<T> — the entity-terminal fallback path
        // has an unrelated interceptor signature mismatch that is out of
        // scope for this fix (tracked separately, same as the PG side).
        var city = await Ss.Addresses()
            .Where(a => a.AddressId == newId)
            .Select(a => a.City)
            .ExecuteFetchFirstOrDefaultAsync();
        Assert.That(city, Is.EqualTo("Austin"));

        var street = await Ss.Addresses()
            .Where(a => a.AddressId == newId)
            .Select(a => a.Street)
            .ExecuteFetchFirstOrDefaultAsync();
        Assert.That(street, Is.EqualTo("500 Congress Ave"));
    }

    [Test]
    public async Task InsertBatch_OnSqlServer_ExecutesSuccessfully()
    {
        // Multi-row INSERT with runtime-expanded placeholders — the
        // TerminalBodyEmitter batch-insert path. SqlClient accepts the same
        // @pN placeholder shape Quarry's existing emit produces, so this is
        // primarily a sanity check that nothing in the type-mapping or
        // identity-column dance broke between mock and real SQL Server.
        await using var t = await QueryTestHarness.CreateAsync();
        var (_, _, _, Ss) = t;

        var warehouses = new[]
        {
            new Ss.Warehouse { WarehouseName = "North Atlantic Hub", Region = "US-E" },
            new Ss.Warehouse { WarehouseName = "APAC Ring",           Region = "AP" },
            new Ss.Warehouse { WarehouseName = "LATAM Bridge",        Region = "LATAM" },
        };

        var rows = await Ss.Warehouses()
            .InsertBatch(w => (w.WarehouseName, w.Region))
            .Values(warehouses)
            .ExecuteNonQueryAsync();

        Assert.That(rows, Is.EqualTo(3));

        // Explicit projection avoids the IQueryBuilder<T>-terminal overload
        // mismatch (unrelated to this fix; see EntityInsert test).
        var insertedNames = await Ss.Warehouses()
            .Where(w => w.WarehouseName == "North Atlantic Hub"
                     || w.WarehouseName == "APAC Ring"
                     || w.WarehouseName == "LATAM Bridge")
            .Select(w => w.WarehouseName)
            .ExecuteFetchAllAsync();
        Assert.That(insertedNames, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task WhereInCollection_OnSqlServer_ExecutesSuccessfully()
    {
        // Covers TerminalEmitHelpers.EmitCollectionPartsPopulation combined
        // with CarrierEmitter's collection-parameter binding loop. The
        // collection is passed as a method argument so it is NOT
        // constant-folded to literal SQL — the generator must emit the
        // runtime-expansion path that builds __colNParts at runtime and
        // binds one DbParameter per element. SqlClient binds these the same
        // way Npgsql does once the parameter naming matches the placeholder.
        await using var t = await QueryTestHarness.CreateAsync();
        var (_, _, _, Ss) = t;

        var wantedIds = BuildWantedIds();

        var names = await Ss.Users()
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
    // the code path execution-mirror coverage actually exercises on real
    // SqlClient. A local `new[] { 1, 3 }` array would be folded to
    // `IN (1, 3)` literals.
    private static int[] BuildWantedIds() => new[] { 1, 3 };
}
