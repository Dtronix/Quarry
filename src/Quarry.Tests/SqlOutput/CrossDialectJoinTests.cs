using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;


[TestFixture]
internal class CrossDialectJoinTests
{
    #region Inner Join

    [Test]
    public async Task Join_InnerJoin_OnClause()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var pg = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var my = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var ss = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId`",
            ss:     "SELECT [t0].[UserName], [t1].[Total] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId]");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0], Is.EqualTo(("Alice", 250.00m)));
        Assert.That(results[1], Is.EqualTo(("Alice", 75.50m)));
        Assert.That(results[2], Is.EqualTo(("Bob", 150.00m)));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));
        Assert.That(pgResults[0], Is.EqualTo(("Alice", 250.00m)));
        Assert.That(pgResults[1], Is.EqualTo(("Alice", 75.50m)));
        Assert.That(pgResults[2], Is.EqualTo(("Bob", 150.00m)));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));
        Assert.That(myResults[0], Is.EqualTo(("Alice", 250.00m)));
        Assert.That(myResults[1], Is.EqualTo(("Alice", 75.50m)));
        Assert.That(myResults[2], Is.EqualTo(("Bob", 150.00m)));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(3));
        Assert.That(ssResults[0], Is.EqualTo(("Alice", 250.00m)));
        Assert.That(ssResults[1], Is.EqualTo(("Alice", 75.50m)));
        Assert.That(ssResults[2], Is.EqualTo(("Bob", 150.00m)));
    }

    #endregion

    #region Join + Where

    [Test]
    public async Task Join_WithWhere_OnLeftTable()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => u.IsActive).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var pg = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => u.IsActive).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var my = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => u.IsActive).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var ss = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => u.IsActive).Select((u, o) => (u.UserName, o.Total)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t0\".\"IsActive\" = 1",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t0\".\"IsActive\" = TRUE",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` WHERE `t0`.`IsActive` = 1",
            ss:     "SELECT [t0].[UserName], [t1].[Total] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] WHERE [t0].[IsActive] = 1");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0], Is.EqualTo(("Alice", 250.00m)));
        Assert.That(results[1], Is.EqualTo(("Alice", 75.50m)));
        Assert.That(results[2], Is.EqualTo(("Bob", 150.00m)));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));
        Assert.That(pgResults[0], Is.EqualTo(("Alice", 250.00m)));
        Assert.That(pgResults[1], Is.EqualTo(("Alice", 75.50m)));
        Assert.That(pgResults[2], Is.EqualTo(("Bob", 150.00m)));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));
        Assert.That(myResults[0], Is.EqualTo(("Alice", 250.00m)));
        Assert.That(myResults[1], Is.EqualTo(("Alice", 75.50m)));
        Assert.That(myResults[2], Is.EqualTo(("Bob", 150.00m)));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(3));
        Assert.That(ssResults[0], Is.EqualTo(("Alice", 250.00m)));
        Assert.That(ssResults[1], Is.EqualTo(("Alice", 75.50m)));
        Assert.That(ssResults[2], Is.EqualTo(("Bob", 150.00m)));
    }

    [Test]
    public async Task Join_WithWhere_OnRightTable()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => o.Total > 100).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var pg = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => o.Total > 100).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var my = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => o.Total > 100).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var ss = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => o.Total > 100).Select((u, o) => (u.UserName, o.Total)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > 100",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > 100",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` WHERE `t1`.`Total` > 100",
            ss:     "SELECT [t0].[UserName], [t1].[Total] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] WHERE [t1].[Total] > 100");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo(("Alice", 250.00m)));
        Assert.That(results[1], Is.EqualTo(("Bob", 150.00m)));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0], Is.EqualTo(("Alice", 250.00m)));
        Assert.That(pgResults[1], Is.EqualTo(("Bob", 150.00m)));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0], Is.EqualTo(("Alice", 250.00m)));
        Assert.That(myResults[1], Is.EqualTo(("Bob", 150.00m)));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(2));
        Assert.That(ssResults[0], Is.EqualTo(("Alice", 250.00m)));
        Assert.That(ssResults[1], Is.EqualTo(("Bob", 150.00m)));
    }

    #endregion

    #region Join + Tuple Projection Columns

    [Test]
    public async Task Join_Select_Tuple_ColumnQuoting()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var pg = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var my = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var ss = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId`",
            ss:     "SELECT [t0].[UserName], [t1].[Total] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId]");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(3));
    }

    #endregion

    #region Inner Join + Named Tuples

    [Test]
    public async Task Join_InnerJoin_NamedTupleProjection()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (Name: u.UserName, Amount: o.Total)).Prepare();
        var pg = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (Name: u.UserName, Amount: o.Total)).Prepare();
        var my = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (Name: u.UserName, Amount: o.Total)).Prepare();
        var ss = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (Name: u.UserName, Amount: o.Total)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId`",
            ss:     "SELECT [t0].[UserName], [t1].[Total] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId]");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        // Verify named element access works across join boundaries
        Assert.That(results[0].Name, Is.EqualTo("Alice"));
        Assert.That(results[0].Amount, Is.EqualTo(250.00m));
        Assert.That(results[1].Name, Is.EqualTo("Alice"));
        Assert.That(results[1].Amount, Is.EqualTo(75.50m));
        Assert.That(results[2].Name, Is.EqualTo("Bob"));
        Assert.That(results[2].Amount, Is.EqualTo(150.00m));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));
        Assert.That(pgResults[0].Name, Is.EqualTo("Alice"));
        Assert.That(pgResults[0].Amount, Is.EqualTo(250.00m));
        Assert.That(pgResults[1].Name, Is.EqualTo("Alice"));
        Assert.That(pgResults[1].Amount, Is.EqualTo(75.50m));
        Assert.That(pgResults[2].Name, Is.EqualTo("Bob"));
        Assert.That(pgResults[2].Amount, Is.EqualTo(150.00m));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));
        Assert.That(myResults[0].Name, Is.EqualTo("Alice"));
        Assert.That(myResults[0].Amount, Is.EqualTo(250.00m));
        Assert.That(myResults[1].Name, Is.EqualTo("Alice"));
        Assert.That(myResults[1].Amount, Is.EqualTo(75.50m));
        Assert.That(myResults[2].Name, Is.EqualTo("Bob"));
        Assert.That(myResults[2].Amount, Is.EqualTo(150.00m));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(3));
        Assert.That(ssResults[0].Name, Is.EqualTo("Alice"));
        Assert.That(ssResults[0].Amount, Is.EqualTo(250.00m));
        Assert.That(ssResults[1].Name, Is.EqualTo("Alice"));
        Assert.That(ssResults[1].Amount, Is.EqualTo(75.50m));
        Assert.That(ssResults[2].Name, Is.EqualTo("Bob"));
        Assert.That(ssResults[2].Amount, Is.EqualTo(150.00m));
    }

    [Test]
    public async Task Join_ThreeTable_NamedTupleProjection()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Join<OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Select((u, o, oi) => (User: u.UserName, Amount: o.Total, Product: oi.ProductName)).Prepare();
        var pg = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Join<Pg.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Select((u, o, oi) => (User: u.UserName, Amount: o.Total, Product: oi.ProductName)).Prepare();
        var my = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Join<My.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Select((u, o, oi) => (User: u.UserName, Amount: o.Total, Product: oi.ProductName)).Prepare();
        var ss = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Join<Ss.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Select((u, o, oi) => (User: u.UserName, Amount: o.Total, Product: oi.ProductName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\", \"t2\".\"ProductName\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" INNER JOIN \"order_items\" AS \"t2\" ON \"t1\".\"OrderId\" = \"t2\".\"OrderId\"",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\", \"t2\".\"ProductName\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" INNER JOIN \"order_items\" AS \"t2\" ON \"t1\".\"OrderId\" = \"t2\".\"OrderId\"",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total`, `t2`.`ProductName` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` INNER JOIN `order_items` AS `t2` ON `t1`.`OrderId` = `t2`.`OrderId`",
            ss:     "SELECT [t0].[UserName], [t1].[Total], [t2].[ProductName] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] INNER JOIN [order_items] AS [t2] ON [t1].[OrderId] = [t2].[OrderId]");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0].User, Is.EqualTo("Alice"));
        Assert.That(results[0].Amount, Is.EqualTo(250.00m));
        Assert.That(results[0].Product, Is.Not.Null);

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));
        Assert.That(pgResults[0].User, Is.EqualTo("Alice"));
        Assert.That(pgResults[0].Amount, Is.EqualTo(250.00m));
        Assert.That(pgResults[0].Product, Is.Not.Null);

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));
        Assert.That(myResults[0].User, Is.EqualTo("Alice"));
        Assert.That(myResults[0].Amount, Is.EqualTo(250.00m));
        Assert.That(myResults[0].Product, Is.Not.Null);

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(3));
        Assert.That(ssResults[0].User, Is.EqualTo("Alice"));
        Assert.That(ssResults[0].Amount, Is.EqualTo(250.00m));
        Assert.That(ssResults[0].Product, Is.Not.Null);
    }

    #endregion

    #region Left Join

    [Test]
    public async Task LeftJoin_Select()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().LeftJoin<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => u.UserName).Prepare();
        var pg = Pg.Users().LeftJoin<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => u.UserName).Prepare();
        var my = My.Users().LeftJoin<My.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => u.UserName).Prepare();
        var ss = Ss.Users().LeftJoin<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => u.UserName).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\" FROM \"users\" AS \"t0\" LEFT JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            pg:     "SELECT \"t0\".\"UserName\" FROM \"users\" AS \"t0\" LEFT JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            mysql:  "SELECT `t0`.`UserName` FROM `users` AS `t0` LEFT JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId`",
            ss:     "SELECT [t0].[UserName] FROM [users] AS [t0] LEFT JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId]");

        // Alice has 2 orders, Bob has 1 order, Charlie has 0 orders (NULL row) — 4 rows
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(4));
        Assert.That(results.Count(r => r == "Alice"), Is.EqualTo(2));
        Assert.That(results.Count(r => r == "Charlie"), Is.EqualTo(1));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(4));
        Assert.That(pgResults.Count(r => r == "Alice"), Is.EqualTo(2));
        Assert.That(pgResults.Count(r => r == "Charlie"), Is.EqualTo(1));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(4));
        Assert.That(myResults.Count(r => r == "Alice"), Is.EqualTo(2));
        Assert.That(myResults.Count(r => r == "Charlie"), Is.EqualTo(1));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(4));
        Assert.That(ssResults.Count(r => r == "Alice"), Is.EqualTo(2));
        Assert.That(ssResults.Count(r => r == "Charlie"), Is.EqualTo(1));
    }

    [Test]
    public async Task LeftJoin_WithWhere_OnLeftTable()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().LeftJoin<Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => u.IsActive).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var pg = Pg.Users().LeftJoin<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => u.IsActive).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var my = My.Users().LeftJoin<My.Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => u.IsActive).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var ss = Ss.Users().LeftJoin<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => u.IsActive).Select((u, o) => (u.UserName, o.Total)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" LEFT JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t0\".\"IsActive\" = 1",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" LEFT JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t0\".\"IsActive\" = TRUE",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total` FROM `users` AS `t0` LEFT JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` WHERE `t0`.`IsActive` = 1",
            ss:     "SELECT [t0].[UserName], [t1].[Total] FROM [users] AS [t0] LEFT JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] WHERE [t0].[IsActive] = 1");

        // Alice (active, 2 orders) + Bob (active, 1 order) = 3 rows; Charlie (inactive) excluded
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results.Any(r => r.Item1 == "Charlie"), Is.False);

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));
        Assert.That(pgResults.Any(r => r.Item1 == "Charlie"), Is.False);

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));
        Assert.That(myResults.Any(r => r.Item1 == "Charlie"), Is.False);

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(3));
        Assert.That(ssResults.Any(r => r.Item1 == "Charlie"), Is.False);
    }

    #endregion

    #region Right Join

    [Test]
    public async Task RightJoin_Select()
    {
        // SQL-only — SQLite doesn't support RIGHT JOIN
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().RightJoin<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var pg = Pg.Users().RightJoin<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var my = My.Users().RightJoin<My.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var ss = Ss.Users().RightJoin<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" RIGHT JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" RIGHT JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total` FROM `users` AS `t0` RIGHT JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId`",
            ss:     "SELECT [t0].[UserName], [t1].[Total] FROM [users] AS [t0] RIGHT JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId]");
    }

    #endregion

    #region 3-Table Join

    [Test]
    public async Task Join_ThreeTable_Select()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Join<OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Select((u, o, oi) => (u.UserName, o.Total, oi.ProductName)).Prepare();
        var pg = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Join<Pg.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Select((u, o, oi) => (u.UserName, o.Total, oi.ProductName)).Prepare();
        var my = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Join<My.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Select((u, o, oi) => (u.UserName, o.Total, oi.ProductName)).Prepare();
        var ss = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Join<Ss.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Select((u, o, oi) => (u.UserName, o.Total, oi.ProductName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\", \"t2\".\"ProductName\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" INNER JOIN \"order_items\" AS \"t2\" ON \"t1\".\"OrderId\" = \"t2\".\"OrderId\"",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\", \"t2\".\"ProductName\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" INNER JOIN \"order_items\" AS \"t2\" ON \"t1\".\"OrderId\" = \"t2\".\"OrderId\"",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total`, `t2`.`ProductName` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` INNER JOIN `order_items` AS `t2` ON `t1`.`OrderId` = `t2`.`OrderId`",
            ss:     "SELECT [t0].[UserName], [t1].[Total], [t2].[ProductName] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] INNER JOIN [order_items] AS [t2] ON [t1].[OrderId] = [t2].[OrderId]");

        // Seed: 3 order items, each in a different order
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(3));
    }

    #endregion

    #region 4-Table Join

    [Test]
    public async Task Join_FourTable_Select()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Join<OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Join<Account>((u, o, oi, a) => u.UserId == a.UserId.Id).Select((u, o, oi, a) => (u.UserName, o.Total, oi.ProductName, a.AccountName)).Prepare();
        var pg = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Join<Pg.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Join<Pg.Account>((u, o, oi, a) => u.UserId == a.UserId.Id).Select((u, o, oi, a) => (u.UserName, o.Total, oi.ProductName, a.AccountName)).Prepare();
        var my = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Join<My.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Join<My.Account>((u, o, oi, a) => u.UserId == a.UserId.Id).Select((u, o, oi, a) => (u.UserName, o.Total, oi.ProductName, a.AccountName)).Prepare();
        var ss = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Join<Ss.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Join<Ss.Account>((u, o, oi, a) => u.UserId == a.UserId.Id).Select((u, o, oi, a) => (u.UserName, o.Total, oi.ProductName, a.AccountName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\", \"t2\".\"ProductName\", \"t3\".\"AccountName\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" INNER JOIN \"order_items\" AS \"t2\" ON \"t1\".\"OrderId\" = \"t2\".\"OrderId\" INNER JOIN \"accounts\" AS \"t3\" ON \"t0\".\"UserId\" = \"t3\".\"UserId\"",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\", \"t2\".\"ProductName\", \"t3\".\"AccountName\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" INNER JOIN \"order_items\" AS \"t2\" ON \"t1\".\"OrderId\" = \"t2\".\"OrderId\" INNER JOIN \"accounts\" AS \"t3\" ON \"t0\".\"UserId\" = \"t3\".\"UserId\"",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total`, `t2`.`ProductName`, `t3`.`AccountName` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` INNER JOIN `order_items` AS `t2` ON `t1`.`OrderId` = `t2`.`OrderId` INNER JOIN `accounts` AS `t3` ON `t0`.`UserId` = `t3`.`UserId`",
            ss:     "SELECT [t0].[UserName], [t1].[Total], [t2].[ProductName], [t3].[AccountName] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] INNER JOIN [order_items] AS [t2] ON [t1].[OrderId] = [t2].[OrderId] INNER JOIN [accounts] AS [t3] ON [t0].[UserId] = [t3].[UserId]");

        // Seed: Alice has 2 orders × 2 accounts, Bob has 1 order × 1 account. Each order has 1 item.
        // Alice: order1(Widget) × Savings,Checking = 2 rows, order2(Gadget) × Savings,Checking = 2 rows — 4
        // Bob: order3(Widget) × Savings = 1 row — 1
        // Total = 5
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(5));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(5));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(5));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(5));
    }

    #endregion

    #region Join + Where with multi-param and boolean columns

    [Test]
    public async Task Join_WithWhere_MultiParamAndBoolColumn_SequentialParamIndices()
    {
        // Regression test: WHERE with 2 captured params + a bare boolean column must produce
        // sequential parameter indices (@p0, @p1) — not @p0, @p2 (skipping a slot for the bool).
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var minTotal = 100m;
        var lt = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Where((u, o) => o.Total > minTotal && u.IsActive)
            .Select((u, o) => (u.UserName, o.Total)).Prepare();
        var pg = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id)
            .Where((u, o) => o.Total > minTotal && u.IsActive)
            .Select((u, o) => (u.UserName, o.Total)).Prepare();
        var my = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id)
            .Where((u, o) => o.Total > minTotal && u.IsActive)
            .Select((u, o) => (u.UserName, o.Total)).Prepare();
        var ss = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id)
            .Where((u, o) => o.Total > minTotal && u.IsActive)
            .Select((u, o) => (u.UserName, o.Total)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > @p0 AND \"t0\".\"IsActive\" = 1",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > $1 AND \"t0\".\"IsActive\" = TRUE",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` WHERE `t1`.`Total` > ? AND `t0`.`IsActive` = 1",
            ss:     "SELECT [t0].[UserName], [t1].[Total] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] WHERE [t1].[Total] > @p0 AND [t0].[IsActive] = 1");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo(("Alice", 250.00m)));
        Assert.That(results[1], Is.EqualTo(("Bob", 150.00m)));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0], Is.EqualTo(("Alice", 250.00m)));
        Assert.That(pgResults[1], Is.EqualTo(("Bob", 150.00m)));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0], Is.EqualTo(("Alice", 250.00m)));
        Assert.That(myResults[1], Is.EqualTo(("Bob", 150.00m)));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(2));
        Assert.That(ssResults[0], Is.EqualTo(("Alice", 250.00m)));
        Assert.That(ssResults[1], Is.EqualTo(("Bob", 150.00m)));
    }

    [Test]
    public async Task Join_WithWhere_TwoCapturedParams_BooleanBetween_SequentialIndices()
    {
        // Regression test: param AND bool AND param — must produce @p0 and @p1, not @p0 and @p2.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var userName = "Alice";
        var minTotal = 50m;
        var lt = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Where((u, o) => u.UserName == userName && u.IsActive && o.Total > minTotal)
            .Select((u, o) => (u.UserName, o.Total)).Prepare();
        var pg = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id)
            .Where((u, o) => u.UserName == userName && u.IsActive && o.Total > minTotal)
            .Select((u, o) => (u.UserName, o.Total)).Prepare();
        var my = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id)
            .Where((u, o) => u.UserName == userName && u.IsActive && o.Total > minTotal)
            .Select((u, o) => (u.UserName, o.Total)).Prepare();
        var ss = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id)
            .Where((u, o) => u.UserName == userName && u.IsActive && o.Total > minTotal)
            .Select((u, o) => (u.UserName, o.Total)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t0\".\"UserName\" = @p0 AND \"t0\".\"IsActive\" = 1 AND \"t1\".\"Total\" > @p1",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t0\".\"UserName\" = $1 AND \"t0\".\"IsActive\" = TRUE AND \"t1\".\"Total\" > $2",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` WHERE `t0`.`UserName` = ? AND `t0`.`IsActive` = 1 AND `t1`.`Total` > ?",
            ss:     "SELECT [t0].[UserName], [t1].[Total] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] WHERE [t0].[UserName] = @p0 AND [t0].[IsActive] = 1 AND [t1].[Total] > @p1");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo(("Alice", 250.00m)));
        Assert.That(results[1], Is.EqualTo(("Alice", 75.50m)));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0], Is.EqualTo(("Alice", 250.00m)));
        Assert.That(pgResults[1], Is.EqualTo(("Alice", 75.50m)));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0], Is.EqualTo(("Alice", 250.00m)));
        Assert.That(myResults[1], Is.EqualTo(("Alice", 75.50m)));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(2));
        Assert.That(ssResults[0], Is.EqualTo(("Alice", 250.00m)));
        Assert.That(ssResults[1], Is.EqualTo(("Alice", 75.50m)));
    }

    #endregion

    #region Pre-Join Where (retranslation with join context)

    [Test]
    public async Task Where_BeforeJoin_GetsTableAliasQualification()
    {
        // Pre-join WHERE clauses are retranslated with join context to add table alias
        // qualification (t0. prefix). The retranslation guard at ChainAnalyzer must
        // preserve the original clause if retranslation fails (isSuccess check).
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.IsActive).Join<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var pg = Pg.Users().Where(u => u.IsActive).Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var my = My.Users().Where(u => u.IsActive).Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var ss = Ss.Users().Where(u => u.IsActive).Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();

        // WHERE must have t0. qualification — proves retranslation succeeded
        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t0\".\"IsActive\" = 1",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t0\".\"IsActive\" = TRUE",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` WHERE `t0`.`IsActive` = 1",
            ss:     "SELECT [t0].[UserName], [t1].[Total] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] WHERE [t0].[IsActive] = 1");

        // Execution: Alice (active, 2 orders) + Bob (active, 1 order) = 3 rows
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0], Is.EqualTo(("Alice", 250.00m)));
        Assert.That(results[1], Is.EqualTo(("Alice", 75.50m)));
        Assert.That(results[2], Is.EqualTo(("Bob", 150.00m)));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));
        Assert.That(pgResults[0], Is.EqualTo(("Alice", 250.00m)));
        Assert.That(pgResults[1], Is.EqualTo(("Alice", 75.50m)));
        Assert.That(pgResults[2], Is.EqualTo(("Bob", 150.00m)));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));
        Assert.That(myResults[0], Is.EqualTo(("Alice", 250.00m)));
        Assert.That(myResults[1], Is.EqualTo(("Alice", 75.50m)));
        Assert.That(myResults[2], Is.EqualTo(("Bob", 150.00m)));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(3));
        Assert.That(ssResults[0], Is.EqualTo(("Alice", 250.00m)));
        Assert.That(ssResults[1], Is.EqualTo(("Alice", 75.50m)));
        Assert.That(ssResults[2], Is.EqualTo(("Bob", 150.00m)));
    }

    [Test]
    public async Task Where_WithParam_BeforeJoin_GetsTableAliasQualification()
    {
        // Same retranslation path but with a captured parameter in the WHERE clause
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var minId = 1;
        var lt = Lite.Users().Where(u => u.UserId >= minId).Join<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var pg = Pg.Users().Where(u => u.UserId >= minId).Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var my = My.Users().Where(u => u.UserId >= minId).Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var ss = Ss.Users().Where(u => u.UserId >= minId).Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t0\".\"UserId\" >= @p0",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t0\".\"UserId\" >= $1",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` WHERE `t0`.`UserId` >= ?",
            ss:     "SELECT [t0].[UserName], [t1].[Total] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] WHERE [t0].[UserId] >= @p0");

        // Execution: all 3 users have UserId >= 1, but Charlie has no orders → 3 rows
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(3));
    }

    #endregion

    #region Full Outer Join

    [Test]
    public async Task FullOuterJoin_OnClause()
    {
        // MySQL is intentionally excluded: it has no FULL OUTER JOIN support, and the
        // analyzer (QRA503) makes a `My.…FullOuterJoin(…)` call site fail compilation —
        // see DialectRuleTests.QRA503_MysqlFullOuterJoin_Reports.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, _, Ss) = t;

        var lt = Lite.Users().FullOuterJoin<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var pg = Pg.Users().FullOuterJoin<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var ss = Ss.Users().FullOuterJoin<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();

        var litDiag = lt.ToDiagnostics();
        var pgDiag = pg.ToDiagnostics();
        var ssDiag = ss.ToDiagnostics();
        Assert.That(litDiag.Sql, Is.EqualTo(
            "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" FULL OUTER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\""));
        Assert.That(pgDiag.Sql, Is.EqualTo(
            "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" FULL OUTER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\""));
        Assert.That(ssDiag.Sql, Is.EqualTo(
            "SELECT [t0].[UserName], [t1].[Total] FROM [users] AS [t0] FULL OUTER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId]"));
    }

    #endregion

    #region Cross Join

    [Test]
    public async Task CrossJoin_NoOnClause()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().CrossJoin<Order>().Select((u, o) => (u.UserName, o.Total)).Prepare();
        var pg = Pg.Users().CrossJoin<Pg.Order>().Select((u, o) => (u.UserName, o.Total)).Prepare();
        var my = My.Users().CrossJoin<My.Order>().Select((u, o) => (u.UserName, o.Total)).Prepare();
        var ss = Ss.Users().CrossJoin<Ss.Order>().Select((u, o) => (u.UserName, o.Total)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" CROSS JOIN \"orders\" AS \"t1\"",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" CROSS JOIN \"orders\" AS \"t1\"",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total` FROM `users` AS `t0` CROSS JOIN `orders` AS `t1`",
            ss:     "SELECT [t0].[UserName], [t1].[Total] FROM [users] AS [t0] CROSS JOIN [orders] AS [t1]");

        // Cross join: 3 users × 3 orders = 9 rows
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(9));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(9));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(9));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(9));
    }

    [Test]
    public async Task CrossJoin_WithWhere()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().CrossJoin<Order>().Where((u, o) => o.Total > 100).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var pg = Pg.Users().CrossJoin<Pg.Order>().Where((u, o) => o.Total > 100).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var my = My.Users().CrossJoin<My.Order>().Where((u, o) => o.Total > 100).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var ss = Ss.Users().CrossJoin<Ss.Order>().Where((u, o) => o.Total > 100).Select((u, o) => (u.UserName, o.Total)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" CROSS JOIN \"orders\" AS \"t1\" WHERE \"t1\".\"Total\" > 100",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" CROSS JOIN \"orders\" AS \"t1\" WHERE \"t1\".\"Total\" > 100",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total` FROM `users` AS `t0` CROSS JOIN `orders` AS `t1` WHERE `t1`.`Total` > 100",
            ss:     "SELECT [t0].[UserName], [t1].[Total] FROM [users] AS [t0] CROSS JOIN [orders] AS [t1] WHERE [t1].[Total] > 100");

        // 3 users × 2 orders with Total > 100 = 6 rows
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(6));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(6));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(6));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(6));
    }

    #endregion

    #region Issue #257 — navigation aggregates in joined Select projection

    [Test]
    public async Task Select_Joined_Many_Sum_OnLeftTable()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Join filters out Charlie automatically (no orders → no inner-join match), so Sum is safe.
        var lt = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total, OrderTotal: u.Orders.Sum(x => x.Total))).Prepare();
        var pg = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total, OrderTotal: u.Orders.Sum(x => x.Total))).Prepare();
        var my = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total, OrderTotal: u.Orders.Sum(x => x.Total))).Prepare();
        var ss = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total, OrderTotal: u.Orders.Sum(x => x.Total))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\", (SELECT SUM(\"sq0\".\"Total\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"t0\".\"UserId\") AS \"OrderTotal\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\", (SELECT SUM(\"sq0\".\"Total\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"t0\".\"UserId\") AS \"OrderTotal\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total`, (SELECT SUM(`sq0`.`Total`) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `t0`.`UserId`) AS `OrderTotal` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId`",
            ss:     "SELECT [t0].[UserName], [t1].[Total], (SELECT SUM([sq0].[Total]) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [t0].[UserId]) AS [OrderTotal] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId]");

        // Inner join: Alice/250 (sum 325.50), Alice/75.50 (sum 325.50), Bob/150 (sum 150).
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0], Is.EqualTo(("Alice", 250.00m, 325.50m)));
        Assert.That(results[1], Is.EqualTo(("Alice", 75.50m, 325.50m)));
        Assert.That(results[2], Is.EqualTo(("Bob", 150.00m, 150.00m)));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));
        Assert.That(pgResults[0], Is.EqualTo(("Alice", 250.00m, 325.50m)));
        Assert.That(pgResults[1], Is.EqualTo(("Alice", 75.50m, 325.50m)));
        Assert.That(pgResults[2], Is.EqualTo(("Bob", 150.00m, 150.00m)));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));
        Assert.That(myResults[0], Is.EqualTo(("Alice", 250.00m, 325.50m)));
        Assert.That(myResults[1], Is.EqualTo(("Alice", 75.50m, 325.50m)));
        Assert.That(myResults[2], Is.EqualTo(("Bob", 150.00m, 150.00m)));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(3));
        Assert.That(ssResults[0], Is.EqualTo(("Alice", 250.00m, 325.50m)));
        Assert.That(ssResults[1], Is.EqualTo(("Alice", 75.50m, 325.50m)));
        Assert.That(ssResults[2], Is.EqualTo(("Bob", 150.00m, 150.00m)));
    }

    [Test]
    public async Task Select_Joined_Many_Count_OnLeftTable()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total, OrderCount: u.Orders.Count())).Prepare();
        var pg = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total, OrderCount: u.Orders.Count())).Prepare();
        var my = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total, OrderCount: u.Orders.Count())).Prepare();
        var ss = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total, OrderCount: u.Orders.Count())).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\", (SELECT COUNT(*) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"t0\".\"UserId\") AS \"OrderCount\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\", (SELECT COUNT(*) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"t0\".\"UserId\") AS \"OrderCount\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total`, (SELECT COUNT(*) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `t0`.`UserId`) AS `OrderCount` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId`",
            ss:     "SELECT [t0].[UserName], [t1].[Total], (SELECT COUNT(*) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [t0].[UserId]) AS [OrderCount] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId]");

        // Inner join yields 3 rows: Alice/250 (count 2), Alice/75.50 (count 2), Bob/150 (count 1).
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0], Is.EqualTo(("Alice", 250.00m, 2)));
        Assert.That(results[1], Is.EqualTo(("Alice", 75.50m, 2)));
        Assert.That(results[2], Is.EqualTo(("Bob", 150.00m, 1)));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));
        Assert.That(pgResults[0], Is.EqualTo(("Alice", 250.00m, 2)));
        Assert.That(pgResults[1], Is.EqualTo(("Alice", 75.50m, 2)));
        Assert.That(pgResults[2], Is.EqualTo(("Bob", 150.00m, 1)));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));
        Assert.That(myResults[0], Is.EqualTo(("Alice", 250.00m, 2)));
        Assert.That(myResults[1], Is.EqualTo(("Alice", 75.50m, 2)));
        Assert.That(myResults[2], Is.EqualTo(("Bob", 150.00m, 1)));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(3));
        Assert.That(ssResults[0], Is.EqualTo(("Alice", 250.00m, 2)));
        Assert.That(ssResults[1], Is.EqualTo(("Alice", 75.50m, 2)));
        Assert.That(ssResults[2], Is.EqualTo(("Bob", 150.00m, 1)));
    }

    /// <summary>
    /// Fills the matrix gap flagged in #257 review (row 10): HasManyThrough aggregate in a
    /// joined-select context. Exercises <c>ResolveSubqueryTargetEntity</c>'s ThroughNavigation
    /// branch under the joined `t0` alias path — previously only covered by HasMany+joined
    /// and HasManyThrough+single-entity.
    /// </summary>
    [Test]
    public async Task Select_Joined_HasManyThrough_Max_OnLeftTable()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Alice→{Addr 1,2}, Bob→{Addr 1}, Charlie→{} (filtered by the inner join on orders).
        var lt = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total, MaxAddrId: u.Addresses.Max(a => a.AddressId))).Prepare();
        var pg = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total, MaxAddrId: u.Addresses.Max(a => a.AddressId))).Prepare();
        var my = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total, MaxAddrId: u.Addresses.Max(a => a.AddressId))).Prepare();
        var ss = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total, MaxAddrId: u.Addresses.Max(a => a.AddressId))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\", (SELECT MAX(\"j0\".\"AddressId\") FROM \"user_addresses\" AS \"sq0\" INNER JOIN \"addresses\" AS \"j0\" ON \"sq0\".\"AddressId\" = \"j0\".\"AddressId\" WHERE \"sq0\".\"UserId\" = \"t0\".\"UserId\") AS \"MaxAddrId\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\", (SELECT MAX(\"j0\".\"AddressId\") FROM \"user_addresses\" AS \"sq0\" INNER JOIN \"addresses\" AS \"j0\" ON \"sq0\".\"AddressId\" = \"j0\".\"AddressId\" WHERE \"sq0\".\"UserId\" = \"t0\".\"UserId\") AS \"MaxAddrId\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total`, (SELECT MAX(`j0`.`AddressId`) FROM `user_addresses` AS `sq0` INNER JOIN `addresses` AS `j0` ON `sq0`.`AddressId` = `j0`.`AddressId` WHERE `sq0`.`UserId` = `t0`.`UserId`) AS `MaxAddrId` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId`",
            ss:     "SELECT [t0].[UserName], [t1].[Total], (SELECT MAX([j0].[AddressId]) FROM [user_addresses] AS [sq0] INNER JOIN [addresses] AS [j0] ON [sq0].[AddressId] = [j0].[AddressId] WHERE [sq0].[UserId] = [t0].[UserId]) AS [MaxAddrId] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId]");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0], Is.EqualTo(("Alice", 250.00m, 2)));
        Assert.That(results[1], Is.EqualTo(("Alice", 75.50m, 2)));
        Assert.That(results[2], Is.EqualTo(("Bob", 150.00m, 1)));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));
        Assert.That(pgResults[0], Is.EqualTo(("Alice", 250.00m, 2)));
        Assert.That(pgResults[1], Is.EqualTo(("Alice", 75.50m, 2)));
        Assert.That(pgResults[2], Is.EqualTo(("Bob", 150.00m, 1)));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));
        Assert.That(myResults[0], Is.EqualTo(("Alice", 250.00m, 2)));
        Assert.That(myResults[1], Is.EqualTo(("Alice", 75.50m, 2)));
        Assert.That(myResults[2], Is.EqualTo(("Bob", 150.00m, 1)));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(3));
        Assert.That(ssResults[0], Is.EqualTo(("Alice", 250.00m, 2)));
        Assert.That(ssResults[1], Is.EqualTo(("Alice", 75.50m, 2)));
        Assert.That(ssResults[2], Is.EqualTo(("Bob", 150.00m, 1)));
    }

    #endregion
}
