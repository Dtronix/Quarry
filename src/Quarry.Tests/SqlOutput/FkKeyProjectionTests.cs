using Quarry;
using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;

/// <summary>
/// Pins behavior for projecting <c>EntityRef&lt;TEntity, TKey&gt;.Id</c> — the
/// key-only access pattern (e.g. <c>o.UserId.Id</c>). The reader must emit the
/// raw key type (<c>int</c>) without the surrounding <c>new EntityRef&lt;…&gt;(…)</c>
/// wrap that ordinary FK column projections produce, the SQL must reference the
/// FK column itself (not <c>"Id"</c>), and the projected tuple slot type must be
/// the key type. Issue #280.
/// </summary>
[TestFixture]
internal class FkKeyProjectionTests
{
    [Test]
    public async Task FromCte_FkKeyProjection_TupleProjection()
    {
        // Post-CTE Select runs through the placeholder analysis path; column metadata
        // is filled in later by BuildProjection against the registry.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.With<Order>(orders => orders.Where(o => o.Total > 100))
            .FromCte<Order>()
            .Select(o => (o.OrderId, UserKey: o.UserId.Id, o.Total))
            .Prepare();
        var pg = Pg.With<Pg.Order>(orders => orders.Where(o => o.Total > 100))
            .FromCte<Pg.Order>()
            .Select(o => (o.OrderId, UserKey: o.UserId.Id, o.Total))
            .Prepare();
        var my = My.With<My.Order>(orders => orders.Where(o => o.Total > 100))
            .FromCte<My.Order>()
            .Select(o => (o.OrderId, UserKey: o.UserId.Id, o.Total))
            .Prepare();
        var ss = Ss.With<Ss.Order>(orders => orders.Where(o => o.Total > 100))
            .FromCte<Ss.Order>()
            .Select(o => (o.OrderId, UserKey: o.UserId.Id, o.Total))
            .Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "WITH \"Order\" AS (SELECT \"OrderId\", \"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\", \"Notes\" FROM \"orders\" WHERE \"Total\" > 100) SELECT \"OrderId\", \"UserId\", \"Total\" FROM \"Order\"",
            pg:     "WITH \"Order\" AS (SELECT \"OrderId\", \"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\", \"Notes\" FROM \"orders\" WHERE \"Total\" > 100) SELECT \"OrderId\", \"UserId\", \"Total\" FROM \"Order\"",
            mysql:  "WITH `Order` AS (SELECT `OrderId`, `UserId`, `Total`, `Status`, `Priority`, `OrderDate`, `Notes` FROM `orders` WHERE `Total` > 100) SELECT `OrderId`, `UserId`, `Total` FROM `Order`",
            ss:     "WITH [Order] AS (SELECT [OrderId], [UserId], [Total], [Status], [Priority], [OrderDate], [Notes] FROM [orders] WHERE [Total] > 100) SELECT [OrderId], [UserId], [Total] FROM [Order]");

        // Total > 100 keeps Order 1 (Alice/UserId=1) and Order 3 (Bob/UserId=2).
        // Sort client-side: post-CTE chain doesn't expose OrderBy.
        var results = (await lt.ExecuteFetchAllAsync()).OrderBy(r => r.OrderId).ToList();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].OrderId, Is.EqualTo(1));
        Assert.That(results[0].UserKey, Is.EqualTo(1));   // verifies key value, not EntityRef
        Assert.That(results[0].Total, Is.EqualTo(250.00m));
        Assert.That(results[1].OrderId, Is.EqualTo(3));
        Assert.That(results[1].UserKey, Is.EqualTo(2));

        var pgResults = (await pg.ExecuteFetchAllAsync()).OrderBy(r => r.OrderId).ToList();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0].UserKey, Is.EqualTo(1));
        Assert.That(pgResults[1].UserKey, Is.EqualTo(2));

        var myResults = (await my.ExecuteFetchAllAsync()).OrderBy(r => r.OrderId).ToList();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0].UserKey, Is.EqualTo(1));
        Assert.That(myResults[1].UserKey, Is.EqualTo(2));

        var ssResults = (await ss.ExecuteFetchAllAsync()).OrderBy(r => r.OrderId).ToList();
        Assert.That(ssResults, Has.Count.EqualTo(2));
        Assert.That(ssResults[0].UserKey, Is.EqualTo(1));
        Assert.That(ssResults[1].UserKey, Is.EqualTo(2));
    }

    [Test]
    public async Task Single_FkKeyProjection_TupleProjection()
    {
        // Non-CTE single-entity Select runs through AnalyzeFromTypeSymbol — the
        // semantic-model path. Columns come back fully resolved; no enrichment hop.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Orders()
            .Select(o => (o.OrderId, UserKey: o.UserId.Id, o.Total))
            .Prepare();
        var pg = Pg.Orders()
            .Select(o => (o.OrderId, UserKey: o.UserId.Id, o.Total))
            .Prepare();
        var my = My.Orders()
            .Select(o => (o.OrderId, UserKey: o.UserId.Id, o.Total))
            .Prepare();
        var ss = Ss.Orders()
            .Select(o => (o.OrderId, UserKey: o.UserId.Id, o.Total))
            .Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", \"UserId\", \"Total\" FROM \"orders\"",
            pg:     "SELECT \"OrderId\", \"UserId\", \"Total\" FROM \"orders\"",
            mysql:  "SELECT `OrderId`, `UserId`, `Total` FROM `orders`",
            ss:     "SELECT [OrderId], [UserId], [Total] FROM [orders]");

        // 3 orders. Sort client-side to keep the assertion stable.
        // Order 1/Alice/1, Order 2/Alice/1, Order 3/Bob/2.
        var results = (await lt.ExecuteFetchAllAsync()).OrderBy(r => r.OrderId).ToList();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0].UserKey, Is.EqualTo(1));
        Assert.That(results[1].UserKey, Is.EqualTo(1));
        Assert.That(results[2].UserKey, Is.EqualTo(2));

        var pgResults = (await pg.ExecuteFetchAllAsync()).OrderBy(r => r.OrderId).ToList();
        Assert.That(pgResults, Has.Count.EqualTo(3));
        Assert.That(pgResults[2].UserKey, Is.EqualTo(2));

        var myResults = (await my.ExecuteFetchAllAsync()).OrderBy(r => r.OrderId).ToList();
        Assert.That(myResults, Has.Count.EqualTo(3));
        Assert.That(myResults[2].UserKey, Is.EqualTo(2));

        var ssResults = (await ss.ExecuteFetchAllAsync()).OrderBy(r => r.OrderId).ToList();
        Assert.That(ssResults, Has.Count.EqualTo(3));
        Assert.That(ssResults[2].UserKey, Is.EqualTo(2));
    }

    [Test]
    public async Task Joined_FkKeyProjection_TupleProjection()
    {
        // Joined Select runs through the placeholder path with per-table-alias lookups.
        // The FK key access lives on the joined-side entity (t1).
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .OrderBy((u, o) => o.OrderId)
            .Select((u, o) => (u.UserName, UserKey: o.UserId.Id, o.Total))
            .Prepare();
        var pg = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id)
            .OrderBy((u, o) => o.OrderId)
            .Select((u, o) => (u.UserName, UserKey: o.UserId.Id, o.Total))
            .Prepare();
        var my = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id)
            .OrderBy((u, o) => o.OrderId)
            .Select((u, o) => (u.UserName, UserKey: o.UserId.Id, o.Total))
            .Prepare();
        var ss = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id)
            .OrderBy((u, o) => o.OrderId)
            .Select((u, o) => (u.UserName, UserKey: o.UserId.Id, o.Total))
            .Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"UserId\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" ORDER BY \"t1\".\"OrderId\" ASC",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"UserId\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" ORDER BY \"t1\".\"OrderId\" ASC",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`UserId`, `t1`.`Total` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` ORDER BY `t1`.`OrderId` ASC",
            ss:     "SELECT [t0].[UserName], [t1].[UserId], [t1].[Total] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] ORDER BY [t1].[OrderId] ASC");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
        Assert.That(results[0].UserKey, Is.EqualTo(1));
        Assert.That(results[2].UserName, Is.EqualTo("Bob"));
        Assert.That(results[2].UserKey, Is.EqualTo(2));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));
        Assert.That(pgResults[2].UserKey, Is.EqualTo(2));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));
        Assert.That(myResults[2].UserKey, Is.EqualTo(2));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(3));
        Assert.That(ssResults[2].UserKey, Is.EqualTo(2));
    }

    [Test]
    public async Task FkKeyProjection_DiagnosticsShape()
    {
        // Fast SQL/diagnostics regression check: confirms the FK key column comes
        // through with the right ColumnName and ClrType. Single dialect is enough —
        // diagnostics shape is dialect-independent.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var lt = Lite.Orders()
            .Select(o => (o.OrderId, UserKey: o.UserId.Id, o.Total))
            .Prepare();

        var diag = lt.ToDiagnostics();
        var projCols = diag.ProjectionColumns!;
        Assert.That(projCols, Has.Count.EqualTo(3));

        // Element 1 is UserKey: o.UserId.Id — the FK key access.
        Assert.That(projCols[1].PropertyName, Is.EqualTo("UserKey"));
        Assert.That(projCols[1].ColumnName, Is.EqualTo("UserId"),
            "FK key access must reference the FK column, not 'Id'");
        Assert.That(projCols[1].ClrType, Is.EqualTo("int"),
            "FK key access must project the key type (int), not EntityRef<…>");
        Assert.That(projCols[1].IsForeignKey, Is.False,
            "FK key access must not be flagged as a foreign-key column — would cause the reader to wrap in new EntityRef<…>(…)");
        Assert.That(projCols[1].ForeignKeyEntityName, Is.Null,
            "FK key access has no referenced entity name in its diagnostic shape — only the FK column projection does");
    }
}
