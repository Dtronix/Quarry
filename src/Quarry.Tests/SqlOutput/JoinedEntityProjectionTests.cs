using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;

/// <summary>
/// Tests for joined entity projection: .Select((u, o) => o) and .Select((u, o) => u)
/// where a bare parameter identifier selects all columns from a single joined entity.
/// </summary>
[TestFixture]
internal class JoinedEntityProjectionTests
{
    #region Select second entity

    [Test]
    public async Task Join_Select_SecondEntity_SqlAndData()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => o).Prepare();
        var pg   = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => o).Prepare();
        var my   = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => o).Prepare();
        var ss   = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => o).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t1\".\"OrderId\", \"t1\".\"UserId\", \"t1\".\"Total\", \"t1\".\"Status\", \"t1\".\"Priority\", \"t1\".\"OrderDate\", \"t1\".\"Notes\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            pg:     "SELECT \"t1\".\"OrderId\", \"t1\".\"UserId\", \"t1\".\"Total\", \"t1\".\"Status\", \"t1\".\"Priority\", \"t1\".\"OrderDate\", \"t1\".\"Notes\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            mysql:  "SELECT `t1`.`OrderId`, `t1`.`UserId`, `t1`.`Total`, `t1`.`Status`, `t1`.`Priority`, `t1`.`OrderDate`, `t1`.`Notes` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId`",
            ss:     "SELECT [t1].[OrderId], [t1].[UserId], [t1].[Total], [t1].[Status], [t1].[Priority], [t1].[OrderDate], [t1].[Notes] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId]");

        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0].OrderId, Is.EqualTo(1));
        Assert.That(results[0].Total, Is.EqualTo(250.00m));
        Assert.That(results[0].Status, Is.EqualTo("Shipped"));
        Assert.That(results[1].OrderId, Is.EqualTo(2));
        Assert.That(results[1].Total, Is.EqualTo(75.50m));
        Assert.That(results[2].OrderId, Is.EqualTo(3));
        Assert.That(results[2].Total, Is.EqualTo(150.00m));
    }

    #endregion

    #region Select first entity

    [Test]
    public async Task Join_Select_FirstEntity_SqlAndData()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => u).Prepare();
        var pg   = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => u).Prepare();
        var my   = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => u).Prepare();
        var ss   = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => u).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserId\", \"t0\".\"UserName\", \"t0\".\"Email\", \"t0\".\"IsActive\", \"t0\".\"CreatedAt\", \"t0\".\"LastLogin\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            pg:     "SELECT \"t0\".\"UserId\", \"t0\".\"UserName\", \"t0\".\"Email\", \"t0\".\"IsActive\", \"t0\".\"CreatedAt\", \"t0\".\"LastLogin\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            mysql:  "SELECT `t0`.`UserId`, `t0`.`UserName`, `t0`.`Email`, `t0`.`IsActive`, `t0`.`CreatedAt`, `t0`.`LastLogin` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId`",
            ss:     "SELECT [t0].[UserId], [t0].[UserName], [t0].[Email], [t0].[IsActive], [t0].[CreatedAt], [t0].[LastLogin] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId]");

        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        // Alice has 2 orders, Bob has 1
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
        Assert.That(results[1].UserName, Is.EqualTo("Alice"));
        Assert.That(results[2].UserName, Is.EqualTo("Bob"));
    }

    #endregion

    #region Select entity with Where

    [Test]
    public async Task Join_Select_Entity_WithWhere()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => o.Total > 100).Select((u, o) => u).Prepare();
        var pg   = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => o.Total > 100).Select((u, o) => u).Prepare();
        var my   = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => o.Total > 100).Select((u, o) => u).Prepare();
        var ss   = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => o.Total > 100).Select((u, o) => u).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserId\", \"t0\".\"UserName\", \"t0\".\"Email\", \"t0\".\"IsActive\", \"t0\".\"CreatedAt\", \"t0\".\"LastLogin\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > 100",
            pg:     "SELECT \"t0\".\"UserId\", \"t0\".\"UserName\", \"t0\".\"Email\", \"t0\".\"IsActive\", \"t0\".\"CreatedAt\", \"t0\".\"LastLogin\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > 100",
            mysql:  "SELECT `t0`.`UserId`, `t0`.`UserName`, `t0`.`Email`, `t0`.`IsActive`, `t0`.`CreatedAt`, `t0`.`LastLogin` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` WHERE `t1`.`Total` > 100",
            ss:     "SELECT [t0].[UserId], [t0].[UserName], [t0].[Email], [t0].[IsActive], [t0].[CreatedAt], [t0].[LastLogin] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] WHERE [t1].[Total] > 100");

        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
        Assert.That(results[1].UserName, Is.EqualTo("Bob"));
    }

    #endregion

    #region Three-table join

    [Test]
    public async Task ThreeTableJoin_Select_MiddleEntity()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Join<OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Select((u, o, oi) => o).Prepare();
        var pg   = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Join<Pg.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Select((u, o, oi) => o).Prepare();
        var my   = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Join<My.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Select((u, o, oi) => o).Prepare();
        var ss   = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Join<Ss.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Select((u, o, oi) => o).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t1\".\"OrderId\", \"t1\".\"UserId\", \"t1\".\"Total\", \"t1\".\"Status\", \"t1\".\"Priority\", \"t1\".\"OrderDate\", \"t1\".\"Notes\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" INNER JOIN \"order_items\" AS \"t2\" ON \"t1\".\"OrderId\" = \"t2\".\"OrderId\"",
            pg:     "SELECT \"t1\".\"OrderId\", \"t1\".\"UserId\", \"t1\".\"Total\", \"t1\".\"Status\", \"t1\".\"Priority\", \"t1\".\"OrderDate\", \"t1\".\"Notes\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" INNER JOIN \"order_items\" AS \"t2\" ON \"t1\".\"OrderId\" = \"t2\".\"OrderId\"",
            mysql:  "SELECT `t1`.`OrderId`, `t1`.`UserId`, `t1`.`Total`, `t1`.`Status`, `t1`.`Priority`, `t1`.`OrderDate`, `t1`.`Notes` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` INNER JOIN `order_items` AS `t2` ON `t1`.`OrderId` = `t2`.`OrderId`",
            ss:     "SELECT [t1].[OrderId], [t1].[UserId], [t1].[Total], [t1].[Status], [t1].[Priority], [t1].[OrderDate], [t1].[Notes] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] INNER JOIN [order_items] AS [t2] ON [t1].[OrderId] = [t2].[OrderId]");

        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0].OrderId, Is.EqualTo(1));
        Assert.That(results[1].OrderId, Is.EqualTo(2));
        Assert.That(results[2].OrderId, Is.EqualTo(3));
    }

    #endregion

    #region LeftJoin

    [Test]
    public async Task LeftJoin_Select_RightEntity()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().LeftJoin<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => o).Prepare();
        var pg   = Pg.Users().LeftJoin<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => o).Prepare();
        var my   = My.Users().LeftJoin<My.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => o).Prepare();
        var ss   = Ss.Users().LeftJoin<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => o).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t1\".\"OrderId\", \"t1\".\"UserId\", \"t1\".\"Total\", \"t1\".\"Status\", \"t1\".\"Priority\", \"t1\".\"OrderDate\", \"t1\".\"Notes\" FROM \"users\" AS \"t0\" LEFT JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            pg:     "SELECT \"t1\".\"OrderId\", \"t1\".\"UserId\", \"t1\".\"Total\", \"t1\".\"Status\", \"t1\".\"Priority\", \"t1\".\"OrderDate\", \"t1\".\"Notes\" FROM \"users\" AS \"t0\" LEFT JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            mysql:  "SELECT `t1`.`OrderId`, `t1`.`UserId`, `t1`.`Total`, `t1`.`Status`, `t1`.`Priority`, `t1`.`OrderDate`, `t1`.`Notes` FROM `users` AS `t0` LEFT JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId`",
            ss:     "SELECT [t1].[OrderId], [t1].[UserId], [t1].[Total], [t1].[Status], [t1].[Priority], [t1].[OrderDate], [t1].[Notes] FROM [users] AS [t0] LEFT JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId]");
    }

    #endregion

    #region FK, Enum, and Nullable columns

    [Test]
    public async Task Join_Select_Entity_ForeignKeyColumns()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var q = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => o).Prepare();
        var results = await q.ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(3));
        // Order.UserId is EntityRef<User, int> — verify the FK value is correctly wrapped
        Assert.That(results[0].UserId.Id, Is.EqualTo(1));
        Assert.That(results[1].UserId.Id, Is.EqualTo(1));
        Assert.That(results[2].UserId.Id, Is.EqualTo(2));
    }

    [Test]
    public async Task Join_Select_Entity_EnumColumns()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var q = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => o).Prepare();
        var results = await q.ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(3));
        // Order.Priority is OrderPriority enum
        Assert.That(results[0].Priority, Is.EqualTo(OrderPriority.High));
        Assert.That(results[1].Priority, Is.EqualTo(OrderPriority.Normal));
        Assert.That(results[2].Priority, Is.EqualTo(OrderPriority.Urgent));
    }

    [Test]
    public async Task Join_Select_Entity_NullableColumns()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var q = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => o).Prepare();
        var results = await q.ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(3));
        // Order.Notes is Col<string?> — first order has "Express", others are null
        Assert.That(results[0].Notes, Is.EqualTo("Express"));
        Assert.That(results[1].Notes, Is.Null);
        Assert.That(results[2].Notes, Is.Null);
    }

    #endregion
}
