using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;


[TestFixture]
internal class CrossDialectCteTests
{
    #region CTE FromCte (simple)

    [Test]
    public async Task Cte_FromCte_SimpleFilter()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.With<Order>(Lite.Orders().Where(o => o.Total > 100))
            .FromCte<Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();
        var pg = Pg.With<Pg.Order>(Pg.Orders().Where(o => o.Total > 100))
            .FromCte<Pg.Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();
        var my = My.With<My.Order>(My.Orders().Where(o => o.Total > 100))
            .FromCte<My.Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();
        var ss = Ss.With<Ss.Order>(Ss.Orders().Where(o => o.Total > 100))
            .FromCte<Ss.Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "WITH \"Order\" AS (SELECT \"OrderId\", \"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\", \"Notes\" FROM \"orders\" WHERE \"Total\" > 100) SELECT \"OrderId\", \"Total\" FROM \"Order\"",
            pg:     "WITH \"Order\" AS (SELECT \"OrderId\", \"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\", \"Notes\" FROM \"orders\" WHERE \"Total\" > 100) SELECT \"OrderId\", \"Total\" FROM \"Order\"",
            mysql:  "WITH `Order` AS (SELECT `OrderId`, `UserId`, `Total`, `Status`, `Priority`, `OrderDate`, `Notes` FROM `orders` WHERE `Total` > 100) SELECT `OrderId`, `Total` FROM `Order`",
            ss:     "WITH [Order] AS (SELECT [OrderId], [UserId], [Total], [Status], [Priority], [OrderDate], [Notes] FROM [orders] WHERE [Total] > 100) SELECT [OrderId], [Total] FROM [Order]");

        // Seed data: OrderId=1 Total=250.00, OrderId=2 Total=75.50, OrderId=3 Total=150.00
        // Orders with Total > 100: OrderId=1 (250.00), OrderId=3 (150.00)
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, 250.00m)));
        Assert.That(results[1], Is.EqualTo((3, 150.00m)));
    }

    #endregion

    #region CTE FromCte (captured-variable inner parameter)

    /// <summary>
    /// Regression test for the runtime bug where CTE inner-query captured parameters
    /// were never copied from the inner carrier into the outer carrier, silently binding
    /// default values at execution time. The original simple-filter test masked this
    /// because it inlined the WHERE comparand as a literal.
    /// </summary>
    [Test]
    public async Task Cte_FromCte_CapturedParam()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        decimal cutoff = 100m;

        var lt = Lite.With<Order>(Lite.Orders().Where(o => o.Total > cutoff))
            .FromCte<Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();
        var pg = Pg.With<Pg.Order>(Pg.Orders().Where(o => o.Total > cutoff))
            .FromCte<Pg.Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();
        var my = My.With<My.Order>(My.Orders().Where(o => o.Total > cutoff))
            .FromCte<My.Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();
        var ss = Ss.With<Ss.Order>(Ss.Orders().Where(o => o.Total > cutoff))
            .FromCte<Ss.Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "WITH \"Order\" AS (SELECT \"OrderId\", \"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\", \"Notes\" FROM \"orders\" WHERE \"Total\" > @p0) SELECT \"OrderId\", \"Total\" FROM \"Order\"",
            pg:     "WITH \"Order\" AS (SELECT \"OrderId\", \"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\", \"Notes\" FROM \"orders\" WHERE \"Total\" > $1) SELECT \"OrderId\", \"Total\" FROM \"Order\"",
            mysql:  "WITH `Order` AS (SELECT `OrderId`, `UserId`, `Total`, `Status`, `Priority`, `OrderDate`, `Notes` FROM `orders` WHERE `Total` > ?) SELECT `OrderId`, `Total` FROM `Order`",
            ss:     "WITH [Order] AS (SELECT [OrderId], [UserId], [Total], [Status], [Priority], [OrderDate], [Notes] FROM [orders] WHERE [Total] > @p0) SELECT [OrderId], [Total] FROM [Order]");

        // Seed data: OrderId=1 Total=250.00, OrderId=2 Total=75.50, OrderId=3 Total=150.00
        // Orders with Total > 100: OrderId=1 (250.00), OrderId=3 (150.00)
        // If the captured cutoff is dropped to default(decimal) = 0, all three rows would match.
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, 250.00m)));
        Assert.That(results[1], Is.EqualTo((3, 150.00m)));

        // Re-execute with a different captured value to confirm the parameter is not pinned.
        cutoff = 200m;
        var lt2 = Lite.With<Order>(Lite.Orders().Where(o => o.Total > cutoff))
            .FromCte<Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();
        var results2 = await lt2.ExecuteFetchAllAsync();
        Assert.That(results2, Has.Count.EqualTo(1));
        Assert.That(results2[0], Is.EqualTo((1, 250.00m)));
    }

    #endregion

    #region CTE FromCte (dedicated DTO via projection)

    /// <summary>
    /// Coverage for the dedicated-DTO path: the CTE name is derived from a DTO class
    /// that is not a schema entity. The 2-arg <c>With&lt;TEntity, TDto&gt;</c> overload
    /// composes a Select projection into a DTO type, and FromCte&lt;TDto&gt; uses that
    /// DTO as the primary FROM source. Verifies that <c>CteDtoResolver.ResolveColumns</c>
    /// produces the right column metadata for a non-entity class.
    /// </summary>
    [Test]
    public async Task Cte_FromCte_DedicatedDto()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.With<Order, OrderSummaryDto>(
                Lite.Orders().Where(o => o.Total > 100)
                    .Select(o => new OrderSummaryDto { OrderId = o.OrderId, Total = o.Total, Status = o.Status }))
            .FromCte<OrderSummaryDto>()
            .Select(d => (d.OrderId, d.Total))
            .Prepare();
        var pg = Pg.With<Pg.Order, OrderSummaryDto>(
                Pg.Orders().Where(o => o.Total > 100)
                    .Select(o => new OrderSummaryDto { OrderId = o.OrderId, Total = o.Total, Status = o.Status }))
            .FromCte<OrderSummaryDto>()
            .Select(d => (d.OrderId, d.Total))
            .Prepare();
        var my = My.With<My.Order, OrderSummaryDto>(
                My.Orders().Where(o => o.Total > 100)
                    .Select(o => new OrderSummaryDto { OrderId = o.OrderId, Total = o.Total, Status = o.Status }))
            .FromCte<OrderSummaryDto>()
            .Select(d => (d.OrderId, d.Total))
            .Prepare();
        var ss = Ss.With<Ss.Order, OrderSummaryDto>(
                Ss.Orders().Where(o => o.Total > 100)
                    .Select(o => new OrderSummaryDto { OrderId = o.OrderId, Total = o.Total, Status = o.Status }))
            .FromCte<OrderSummaryDto>()
            .Select(d => (d.OrderId, d.Total))
            .Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "WITH \"OrderSummaryDto\" AS (SELECT \"OrderId\", \"Total\", \"Status\" FROM \"orders\" WHERE \"Total\" > 100) SELECT \"OrderId\", \"Total\" FROM \"OrderSummaryDto\"",
            pg:     "WITH \"OrderSummaryDto\" AS (SELECT \"OrderId\", \"Total\", \"Status\" FROM \"orders\" WHERE \"Total\" > 100) SELECT \"OrderId\", \"Total\" FROM \"OrderSummaryDto\"",
            mysql:  "WITH `OrderSummaryDto` AS (SELECT `OrderId`, `Total`, `Status` FROM `orders` WHERE `Total` > 100) SELECT `OrderId`, `Total` FROM `OrderSummaryDto`",
            ss:     "WITH [OrderSummaryDto] AS (SELECT [OrderId], [Total], [Status] FROM [orders] WHERE [Total] > 100) SELECT [OrderId], [Total] FROM [OrderSummaryDto]");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, 250.00m)));
        Assert.That(results[1], Is.EqualTo((3, 150.00m)));
    }

    #endregion
}
