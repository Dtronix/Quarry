using Quarry.Tests.Samples;

namespace Quarry.Tests.SqlOutput;


[TestFixture]
internal class CrossDialectCteTests
{
    #region CTE FromCte (simple)

    [Test]
    public async Task Cte_FromCte_SimpleFilter()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var lt = Lite.With<Order>(Lite.Orders().Where(o => o.Total > 100))
            .FromCte<Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();

        var ltSql = lt.ToDiagnostics().Sql;
        Assert.That(ltSql, Does.StartWith("WITH"), $"SQLite SQL: {ltSql}");

        // Seed data: OrderId=1 Total=250.00, OrderId=2 Total=75.50, OrderId=3 Total=150.00
        // Orders with Total > 100: OrderId=1 (250.00), OrderId=3 (150.00)
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, 250.00m)));
        Assert.That(results[1], Is.EqualTo((3, 150.00m)));
    }

    #endregion
}
