using Quarry.Tests.Samples;
using Cte = Quarry.Tests.Samples.Cte;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;

/// <summary>
/// End-to-end tests for lambda-form CTE chains: db.With&lt;T&gt;(orders => orders.Where(...)).
/// SQL output must match the non-lambda form (same inner SQL, same parameter binding).
/// </summary>
[TestFixture]
internal class LambdaCteTests
{
    [Test]
    public async Task LambdaCte_SimpleFilter_NoParams()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.With<Order>(orders => orders.Where(o => o.Total > 100))
            .FromCte<Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();
        var pg = Pg.With<Pg.Order>(orders => orders.Where(o => o.Total > 100))
            .FromCte<Pg.Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();
        var my = My.With<My.Order>(orders => orders.Where(o => o.Total > 100))
            .FromCte<My.Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();
        var ss = Ss.With<Ss.Order>(orders => orders.Where(o => o.Total > 100))
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

        // Seed data: orders with Total > 100 are OrderId=1 (250.00) and OrderId=3 (150.00)
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, 250.00m)));
        Assert.That(results[1], Is.EqualTo((3, 150.00m)));
    }

    [Test]
    public async Task LambdaCte_CapturedParam()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        decimal cutoff = 100m;

        var lt = Lite.With<Order>(orders => orders.Where(o => o.Total > cutoff))
            .FromCte<Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();
        var pg = Pg.With<Pg.Order>(orders => orders.Where(o => o.Total > cutoff))
            .FromCte<Pg.Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();
        var my = My.With<My.Order>(orders => orders.Where(o => o.Total > cutoff))
            .FromCte<My.Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();
        var ss = Ss.With<Ss.Order>(orders => orders.Where(o => o.Total > cutoff))
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

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, 250.00m)));
        Assert.That(results[1], Is.EqualTo((3, 150.00m)));

        // Verify captured param is live: re-create with different value
        cutoff = 200m;
        var lt2 = Lite.With<Order>(orders => orders.Where(o => o.Total > cutoff))
            .FromCte<Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();
        var results2 = await lt2.ExecuteFetchAllAsync();
        Assert.That(results2, Has.Count.EqualTo(1));
        Assert.That(results2[0], Is.EqualTo((1, 250.00m)));
    }

    [Test]
    public async Task LambdaCte_TwoChainedWiths_CapturedParams()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        decimal orderCutoff = 100m;
        bool activeFilter = true;

        var lt = Lite
            .With<Order>(orders => orders.Where(o => o.Total > orderCutoff))
            .With<User>(users => users.Where(u => u.IsActive == activeFilter))
            .FromCte<Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();
        var pg = Pg
            .With<Pg.Order>(orders => orders.Where(o => o.Total > orderCutoff))
            .With<Pg.User>(users => users.Where(u => u.IsActive == activeFilter))
            .FromCte<Pg.Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();
        var my = My
            .With<My.Order>(orders => orders.Where(o => o.Total > orderCutoff))
            .With<My.User>(users => users.Where(u => u.IsActive == activeFilter))
            .FromCte<My.Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();
        var ss = Ss
            .With<Ss.Order>(orders => orders.Where(o => o.Total > orderCutoff))
            .With<Ss.User>(users => users.Where(u => u.IsActive == activeFilter))
            .FromCte<Ss.Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "WITH \"Order\" AS (SELECT \"OrderId\", \"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\", \"Notes\" FROM \"orders\" WHERE \"Total\" > @p0), \"User\" AS (SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"IsActive\" = @p1) SELECT \"OrderId\", \"Total\" FROM \"Order\"",
            pg:     "WITH \"Order\" AS (SELECT \"OrderId\", \"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\", \"Notes\" FROM \"orders\" WHERE \"Total\" > $1), \"User\" AS (SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"IsActive\" = $2) SELECT \"OrderId\", \"Total\" FROM \"Order\"",
            mysql:  "WITH `Order` AS (SELECT `OrderId`, `UserId`, `Total`, `Status`, `Priority`, `OrderDate`, `Notes` FROM `orders` WHERE `Total` > ?), `User` AS (SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE `IsActive` = ?) SELECT `OrderId`, `Total` FROM `Order`",
            ss:     "WITH [Order] AS (SELECT [OrderId], [UserId], [Total], [Status], [Priority], [OrderDate], [Notes] FROM [orders] WHERE [Total] > @p0), [User] AS (SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE [IsActive] = @p1) SELECT [OrderId], [Total] FROM [Order]");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, 250.00m)));
        Assert.That(results[1], Is.EqualTo((3, 150.00m)));
    }

    [Test]
    public async Task LambdaCte_DedicatedDto()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.With<Order, OrderSummaryDto>(
                orders => orders.Where(o => o.Total > 100)
                    .Select(o => new OrderSummaryDto { OrderId = o.OrderId, Total = o.Total, Status = o.Status }))
            .FromCte<OrderSummaryDto>()
            .Select(d => (d.OrderId, d.Total))
            .Prepare();
        var pg = Pg.With<Pg.Order, OrderSummaryDto>(
                orders => orders.Where(o => o.Total > 100)
                    .Select(o => new OrderSummaryDto { OrderId = o.OrderId, Total = o.Total, Status = o.Status }))
            .FromCte<OrderSummaryDto>()
            .Select(d => (d.OrderId, d.Total))
            .Prepare();
        var my = My.With<My.Order, OrderSummaryDto>(
                orders => orders.Where(o => o.Total > 100)
                    .Select(o => new OrderSummaryDto { OrderId = o.OrderId, Total = o.Total, Status = o.Status }))
            .FromCte<OrderSummaryDto>()
            .Select(d => (d.OrderId, d.Total))
            .Prepare();
        var ss = Ss.With<Ss.Order, OrderSummaryDto>(
                orders => orders.Where(o => o.Total > 100)
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

    [Test]
    public async Task LambdaCte_EntityAccessor_Users_Select()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var cte = new Cte.CteDb(t.Lite.Connection);

        var q = cte.With<Cte.Order>(orders => orders.Where(o => o.Total > 100))
            .Users()
            .Select(u => (u.UserId, u.UserName))
            .Prepare();

        Assert.That(q.ToDiagnostics().Sql, Is.EqualTo(
            "WITH \"Order\" AS (SELECT \"OrderId\", \"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\", \"Notes\" FROM \"orders\" WHERE \"Total\" > 100) SELECT \"UserId\", \"UserName\" FROM \"users\""));

        var results = await q.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
    }

    [Test]
    public async Task LambdaCte_EntityAccessor_CapturedParam()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var cte = new Cte.CteDb(t.Lite.Connection);

        decimal cutoff = 100m;
        var q = cte.With<Cte.Order>(orders => orders.Where(o => o.Total > cutoff))
            .Users()
            .Where(u => u.IsActive == true)
            .Select(u => (u.UserId, u.UserName))
            .Prepare();

        Assert.That(q.ToDiagnostics().Sql, Is.EqualTo(
            "WITH \"Order\" AS (SELECT \"OrderId\", \"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\", \"Notes\" FROM \"orders\" WHERE \"Total\" > @p0) SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"IsActive\" = 1"));

        var results = await q.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
        Assert.That(results[1], Is.EqualTo((2, "Bob")));
    }
}
