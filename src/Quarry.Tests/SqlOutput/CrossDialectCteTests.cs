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
}
