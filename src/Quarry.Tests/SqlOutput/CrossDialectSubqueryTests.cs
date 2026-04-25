using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;


[TestFixture]
internal class CrossDialectSubqueryTests
{
    #region Any (parameterless)

    [Test]
    public async Task Where_Any_Parameterless()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.Orders.Any()).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Any()).Prepare();
        var my = My.Users().Where(u => u.Orders.Any()).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Any()).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\")",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\")",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`)",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId])");

        // Alice has 2 orders, Bob has 1 order, Charlie has none
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
        Assert.That(results[1], Is.EqualTo((2, "Bob")));

        var pg2 = Pg.Users().Where(u => u.Orders.Any()).Select(u => (u.UserId, u.UserName)).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(pgResults[1], Is.EqualTo((2, "Bob")));

        var my2 = My.Users().Where(u => u.Orders.Any()).Select(u => (u.UserId, u.UserName)).Prepare();
        var myResults = await my2.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(myResults[1], Is.EqualTo((2, "Bob")));
    }

    [Test]
    public async Task Where_NotAny_Parameterless()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => !u.Orders.Any()).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => !u.Orders.Any()).Prepare();
        var my = My.Users().Where(u => !u.Orders.Any()).Prepare();
        var ss = Ss.Users().Where(u => !u.Orders.Any()).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE NOT (EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\"))",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE NOT (EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\"))",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE NOT (EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`))",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE NOT (EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId]))");

        // Only Charlie has no orders
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo((3, "Charlie")));

        var pg2 = Pg.Users().Where(u => !u.Orders.Any()).Select(u => (u.UserId, u.UserName)).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));
        Assert.That(pgResults[0], Is.EqualTo((3, "Charlie")));

        var my2 = My.Users().Where(u => !u.Orders.Any()).Select(u => (u.UserId, u.UserName)).Prepare();
        var myResults = await my2.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(1));
        Assert.That(myResults[0], Is.EqualTo((3, "Charlie")));
    }

    #endregion

    #region Any (with predicate)

    [Test]
    public async Task Where_Any_WithPredicate()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.Orders.Any(o => o.Total > 100)).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Any(o => o.Total > 100)).Prepare();
        var my = My.Users().Where(u => u.Orders.Any(o => o.Total > 100)).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Any(o => o.Total > 100)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Total\" > 100))",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Total\" > 100))",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (`sq0`.`Total` > 100))",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND ([sq0].[Total] > 100))");

        // Alice has order 250, Bob has order 150 — both > 100
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
        Assert.That(results[1], Is.EqualTo((2, "Bob")));

        var pg2 = Pg.Users().Where(u => u.Orders.Any(o => o.Total > 100)).Select(u => (u.UserId, u.UserName)).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(pgResults[1], Is.EqualTo((2, "Bob")));

        var my2 = My.Users().Where(u => u.Orders.Any(o => o.Total > 100)).Select(u => (u.UserId, u.UserName)).Prepare();
        var myResults = await my2.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(myResults[1], Is.EqualTo((2, "Bob")));
    }

    [Test]
    public async Task Where_Any_WithStringPredicate()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.Orders.Any(o => o.Status == "paid")).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Any(o => o.Status == "paid")).Prepare();
        var my = My.Users().Where(u => u.Orders.Any(o => o.Status == "paid")).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Any(o => o.Status == "paid")).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Status\" = 'paid'))",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Status\" = 'paid'))",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (`sq0`.`Status` = 'paid'))",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND ([sq0].[Status] = 'paid'))");

        // No orders have status 'paid' (they're 'Shipped' and 'Pending')
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(0));

        var pg2 = Pg.Users().Where(u => u.Orders.Any(o => o.Status == "paid")).Select(u => (u.UserId, u.UserName)).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(0));

        var my2 = My.Users().Where(u => u.Orders.Any(o => o.Status == "paid")).Select(u => (u.UserId, u.UserName)).Prepare();
        var myResults = await my2.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(0));
    }

    #endregion

    #region All

    [Test]
    public async Task Where_All_WithPredicate()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.Orders.All(o => o.Status == "paid")).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.All(o => o.Status == "paid")).Prepare();
        var my = My.Users().Where(u => u.Orders.All(o => o.Status == "paid")).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.All(o => o.Status == "paid")).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE NOT EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND NOT (\"sq0\".\"Status\" = 'paid'))",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE NOT EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND NOT (\"sq0\".\"Status\" = 'paid'))",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE NOT EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND NOT (`sq0`.`Status` = 'paid'))",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE NOT EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND NOT ([sq0].[Status] = 'paid'))");

        // All(status=="paid") is vacuously true for users with no orders (Charlie)
        // Alice/Bob have non-paid orders, so they fail the All check
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo((3, "Charlie")));

        var pg2 = Pg.Users().Where(u => u.Orders.All(o => o.Status == "paid")).Select(u => (u.UserId, u.UserName)).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));
        Assert.That(pgResults[0], Is.EqualTo((3, "Charlie")));

        var my2 = My.Users().Where(u => u.Orders.All(o => o.Status == "paid")).Select(u => (u.UserId, u.UserName)).Prepare();
        var myResults = await my2.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(1));
        Assert.That(myResults[0], Is.EqualTo((3, "Charlie")));
    }

    #endregion

    #region Count

    [Test]
    public async Task Where_Count_GreaterThan()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.Orders.Count() > 5).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Count() > 5).Prepare();
        var my = My.Users().Where(u => u.Orders.Count() > 5).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Count() > 5).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE (SELECT COUNT(*) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") > 5",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE (SELECT COUNT(*) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") > 5",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE (SELECT COUNT(*) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`) > 5",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE (SELECT COUNT(*) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId]) > 5");

        // Max orders per user is 2 (Alice), nobody has > 5
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(0));

        var pg2 = Pg.Users().Where(u => u.Orders.Count() > 5).Select(u => (u.UserId, u.UserName)).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(0));

        var my2 = My.Users().Where(u => u.Orders.Count() > 5).Select(u => (u.UserId, u.UserName)).Prepare();
        var myResults = await my2.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(0));
    }

    [Test]
    public async Task Where_Count_EqualsZero()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.Orders.Count() == 0).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Count() == 0).Prepare();
        var my = My.Users().Where(u => u.Orders.Count() == 0).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Count() == 0).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE (SELECT COUNT(*) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") = 0",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE (SELECT COUNT(*) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") = 0",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE (SELECT COUNT(*) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`) = 0",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE (SELECT COUNT(*) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId]) = 0");

        // Only Charlie has zero orders
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo((3, "Charlie")));

        var pg2 = Pg.Users().Where(u => u.Orders.Count() == 0).Select(u => (u.UserId, u.UserName)).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));
        Assert.That(pgResults[0], Is.EqualTo((3, "Charlie")));

        var my2 = My.Users().Where(u => u.Orders.Count() == 0).Select(u => (u.UserId, u.UserName)).Prepare();
        var myResults = await my2.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(1));
        Assert.That(myResults[0], Is.EqualTo((3, "Charlie")));
    }

    [Test]
    public async Task Where_Count_WithPredicate()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.Orders.Count(o => o.Total > 100) > 2).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Count(o => o.Total > 100) > 2).Prepare();
        var my = My.Users().Where(u => u.Orders.Count(o => o.Total > 100) > 2).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Count(o => o.Total > 100) > 2).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE (SELECT COUNT(*) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Total\" > 100)) > 2",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE (SELECT COUNT(*) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Total\" > 100)) > 2",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE (SELECT COUNT(*) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (`sq0`.`Total` > 100)) > 2",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE (SELECT COUNT(*) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND ([sq0].[Total] > 100)) > 2");

        // Alice has 1 order > 100 (250), Bob has 1 (150) — neither > 2
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(0));

        var pg2 = Pg.Users().Where(u => u.Orders.Count(o => o.Total > 100) > 2).Select(u => (u.UserId, u.UserName)).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(0));

        var my2 = My.Users().Where(u => u.Orders.Count(o => o.Total > 100) > 2).Select(u => (u.UserId, u.UserName)).Prepare();
        var myResults = await my2.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(0));
    }

    [Test]
    public async Task Where_Count_WithStringPredicate()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.Orders.Count(o => o.Status == "paid") >= 1).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Count(o => o.Status == "paid") >= 1).Prepare();
        var my = My.Users().Where(u => u.Orders.Count(o => o.Status == "paid") >= 1).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Count(o => o.Status == "paid") >= 1).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE (SELECT COUNT(*) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Status\" = 'paid')) >= 1",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE (SELECT COUNT(*) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Status\" = 'paid')) >= 1",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE (SELECT COUNT(*) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (`sq0`.`Status` = 'paid')) >= 1",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE (SELECT COUNT(*) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND ([sq0].[Status] = 'paid')) >= 1");

        // No orders have status 'paid'
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(0));

        var pg2 = Pg.Users().Where(u => u.Orders.Count(o => o.Status == "paid") >= 1).Select(u => (u.UserId, u.UserName)).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(0));

        var my2 = My.Users().Where(u => u.Orders.Count(o => o.Status == "paid") >= 1).Select(u => (u.UserId, u.UserName)).Prepare();
        var myResults = await my2.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(0));
    }

    [Test]
    public async Task Where_Count_WithCapturedVariable()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var minTotal = 50m;

        var lt = Lite.Users().Where(u => u.Orders.Count(o => o.Total > minTotal) > 0).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Count(o => o.Total > minTotal) > 0).Prepare();
        var my = My.Users().Where(u => u.Orders.Count(o => o.Total > minTotal) > 0).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Count(o => o.Total > minTotal) > 0).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE (SELECT COUNT(*) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Total\" > @p0)) > 0",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE (SELECT COUNT(*) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Total\" > $1)) > 0",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE (SELECT COUNT(*) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (`sq0`.`Total` > ?)) > 0",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE (SELECT COUNT(*) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND ([sq0].[Total] > @p0)) > 0");

        // All 3 orders have Total > 50 — Alice and Bob both qualify
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
        Assert.That(results[1], Is.EqualTo((2, "Bob")));

        var pg2 = Pg.Users().Where(u => u.Orders.Count(o => o.Total > minTotal) > 0).Select(u => (u.UserId, u.UserName)).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(pgResults[1], Is.EqualTo((2, "Bob")));

        var my2 = My.Users().Where(u => u.Orders.Count(o => o.Total > minTotal) > 0).Select(u => (u.UserId, u.UserName)).Prepare();
        var myResults = await my2.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(myResults[1], Is.EqualTo((2, "Bob")));
    }

    #endregion

    #region Sum

    [Test]
    public async Task Where_Sum_GreaterThan()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.Orders.Sum(o => o.Total) > 200).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Sum(o => o.Total) > 200).Prepare();
        var my = My.Users().Where(u => u.Orders.Sum(o => o.Total) > 200).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Sum(o => o.Total) > 200).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE (SELECT SUM(\"sq0\".\"Total\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") > 200",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE (SELECT SUM(\"sq0\".\"Total\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") > 200",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE (SELECT SUM(`sq0`.`Total`) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`) > 200",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE (SELECT SUM([sq0].[Total]) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId]) > 200");

        // Alice: 250.00 + 75.50 = 325.50 > 200 ✓, Bob: 150.00 ✗
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));

        var pg2 = Pg.Users().Where(u => u.Orders.Sum(o => o.Total) > 200).Select(u => (u.UserId, u.UserName)).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));
        Assert.That(pgResults[0], Is.EqualTo((1, "Alice")));

        var my2 = My.Users().Where(u => u.Orders.Sum(o => o.Total) > 200).Select(u => (u.UserId, u.UserName)).Prepare();
        var myResults = await my2.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(1));
        Assert.That(myResults[0], Is.EqualTo((1, "Alice")));
    }

    [Test]
    public async Task Where_Sum_EqualsZero()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.Orders.Sum(o => o.Total) == 0 || !u.Orders.Any()).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Sum(o => o.Total) == 0 || !u.Orders.Any()).Prepare();
        var my = My.Users().Where(u => u.Orders.Sum(o => o.Total) == 0 || !u.Orders.Any()).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Sum(o => o.Total) == 0 || !u.Orders.Any()).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE (SELECT SUM(\"sq0\".\"Total\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") = 0 OR NOT (EXISTS (SELECT 1 FROM \"orders\" AS \"sq1\" WHERE \"sq1\".\"UserId\" = \"users\".\"UserId\"))",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE (SELECT SUM(\"sq0\".\"Total\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") = 0 OR NOT (EXISTS (SELECT 1 FROM \"orders\" AS \"sq1\" WHERE \"sq1\".\"UserId\" = \"users\".\"UserId\"))",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE (SELECT SUM(`sq0`.`Total`) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`) = 0 OR NOT (EXISTS (SELECT 1 FROM `orders` AS `sq1` WHERE `sq1`.`UserId` = `users`.`UserId`))",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE (SELECT SUM([sq0].[Total]) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId]) = 0 OR NOT (EXISTS (SELECT 1 FROM [orders] AS [sq1] WHERE [sq1].[UserId] = [users].[UserId]))");

        // Charlie has no orders (SUM is NULL, but NOT EXISTS is true)
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo((3, "Charlie")));

        var pg2 = Pg.Users().Where(u => u.Orders.Sum(o => o.Total) == 0 || !u.Orders.Any()).Select(u => (u.UserId, u.UserName)).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));
        Assert.That(pgResults[0], Is.EqualTo((3, "Charlie")));

        var my2 = My.Users().Where(u => u.Orders.Sum(o => o.Total) == 0 || !u.Orders.Any()).Select(u => (u.UserId, u.UserName)).Prepare();
        var myResults = await my2.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(1));
        Assert.That(myResults[0], Is.EqualTo((3, "Charlie")));
    }

    #endregion

    #region Min

    [Test]
    public async Task Where_Min_LessThan()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.Orders.Min(o => o.Total) < 100).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Min(o => o.Total) < 100).Prepare();
        var my = My.Users().Where(u => u.Orders.Min(o => o.Total) < 100).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Min(o => o.Total) < 100).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE (SELECT MIN(\"sq0\".\"Total\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") < 100",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE (SELECT MIN(\"sq0\".\"Total\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") < 100",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE (SELECT MIN(`sq0`.`Total`) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`) < 100",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE (SELECT MIN([sq0].[Total]) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId]) < 100");

        // Alice's min order is 75.50 < 100, Bob's min is 150.00
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));

        var pg2 = Pg.Users().Where(u => u.Orders.Min(o => o.Total) < 100).Select(u => (u.UserId, u.UserName)).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));
        Assert.That(pgResults[0], Is.EqualTo((1, "Alice")));

        var my2 = My.Users().Where(u => u.Orders.Min(o => o.Total) < 100).Select(u => (u.UserId, u.UserName)).Prepare();
        var myResults = await my2.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(1));
        Assert.That(myResults[0], Is.EqualTo((1, "Alice")));
    }

    #endregion

    #region Max

    [Test]
    public async Task Where_Max_GreaterThan()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.Orders.Max(o => o.Total) > 200).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Max(o => o.Total) > 200).Prepare();
        var my = My.Users().Where(u => u.Orders.Max(o => o.Total) > 200).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Max(o => o.Total) > 200).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE (SELECT MAX(\"sq0\".\"Total\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") > 200",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE (SELECT MAX(\"sq0\".\"Total\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") > 200",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE (SELECT MAX(`sq0`.`Total`) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`) > 200",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE (SELECT MAX([sq0].[Total]) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId]) > 200");

        // Alice max: 250.00 > 200, Bob max: 150.00 ✗
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));

        var pg2 = Pg.Users().Where(u => u.Orders.Max(o => o.Total) > 200).Select(u => (u.UserId, u.UserName)).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));
        Assert.That(pgResults[0], Is.EqualTo((1, "Alice")));

        var my2 = My.Users().Where(u => u.Orders.Max(o => o.Total) > 200).Select(u => (u.UserId, u.UserName)).Prepare();
        var myResults = await my2.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(1));
        Assert.That(myResults[0], Is.EqualTo((1, "Alice")));
    }

    [Test]
    public async Task Where_Max_DateTime_GreaterThan()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var cutoff = new DateTime(2024, 6, 30);
        var lt = Lite.Users().Where(u => u.Orders.Max(o => o.OrderDate) > cutoff).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Max(o => o.OrderDate) > cutoff).Prepare();
        var my = My.Users().Where(u => u.Orders.Max(o => o.OrderDate) > cutoff).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Max(o => o.OrderDate) > cutoff).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE (SELECT MAX(\"sq0\".\"OrderDate\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") > @p0",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE (SELECT MAX(\"sq0\".\"OrderDate\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") > $1",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE (SELECT MAX(`sq0`.`OrderDate`) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`) > ?",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE (SELECT MAX([sq0].[OrderDate]) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId]) > @p0");

        // Alice max: 2024-06-15 (< cutoff), Bob max: 2024-07-01 (> cutoff)
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo((2, "Bob")));

        var pg2 = Pg.Users().Where(u => u.Orders.Max(o => o.OrderDate) > cutoff).Select(u => (u.UserId, u.UserName)).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));
        Assert.That(pgResults[0], Is.EqualTo((2, "Bob")));

        var my2 = My.Users().Where(u => u.Orders.Max(o => o.OrderDate) > cutoff).Select(u => (u.UserId, u.UserName)).Prepare();
        var myResults = await my2.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(1));
        Assert.That(myResults[0], Is.EqualTo((2, "Bob")));
    }

    #endregion

    #region Avg

    [Test]
    public async Task Where_Avg_GreaterThan()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.Orders.Average(o => o.Total) > 160).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Average(o => o.Total) > 160).Prepare();
        var my = My.Users().Where(u => u.Orders.Average(o => o.Total) > 160).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Average(o => o.Total) > 160).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE (SELECT AVG(\"sq0\".\"Total\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") > 160",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE (SELECT AVG(\"sq0\".\"Total\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") > 160",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE (SELECT AVG(`sq0`.`Total`) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`) > 160",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE (SELECT AVG([sq0].[Total]) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId]) > 160");

        // Alice avg: (250 + 75.50) / 2 = 162.75 > 160 ✓, Bob avg: 150.00 ✗
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));

        var pg2 = Pg.Users().Where(u => u.Orders.Average(o => o.Total) > 160).Select(u => (u.UserId, u.UserName)).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));
        Assert.That(pgResults[0], Is.EqualTo((1, "Alice")));

        var my2 = My.Users().Where(u => u.Orders.Average(o => o.Total) > 160).Select(u => (u.UserId, u.UserName)).Prepare();
        var myResults = await my2.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(1));
        Assert.That(myResults[0], Is.EqualTo((1, "Alice")));
    }

    #endregion

    #region Nested Subqueries

    [Test]
    public async Task Where_Nested_Any_Any()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.Orders.Any(o => o.Items.Any(i => i.UnitPrice > 50))).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Any(o => o.Items.Any(i => i.UnitPrice > 50))).Prepare();
        var my = My.Users().Where(u => u.Orders.Any(o => o.Items.Any(i => i.UnitPrice > 50))).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Any(o => o.Items.Any(i => i.UnitPrice > 50))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (EXISTS (SELECT 1 FROM \"order_items\" AS \"sq1\" WHERE \"sq1\".\"OrderId\" = \"sq0\".\"OrderId\" AND (\"sq1\".\"UnitPrice\" > 50))))",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (EXISTS (SELECT 1 FROM \"order_items\" AS \"sq1\" WHERE \"sq1\".\"OrderId\" = \"sq0\".\"OrderId\" AND (\"sq1\".\"UnitPrice\" > 50))))",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (EXISTS (SELECT 1 FROM `order_items` AS `sq1` WHERE `sq1`.`OrderId` = `sq0`.`OrderId` AND (`sq1`.`UnitPrice` > 50))))",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND (EXISTS (SELECT 1 FROM [order_items] AS [sq1] WHERE [sq1].[OrderId] = [sq0].[OrderId] AND ([sq1].[UnitPrice] > 50))))");

        // Order1 has Widget UnitPrice=125 (>50), Order2 has Gadget UnitPrice=75.50 (>50) → Alice
        // Order3 has Widget UnitPrice=50 (not >50) → Bob does NOT qualify
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));

        var pg2 = Pg.Users().Where(u => u.Orders.Any(o => o.Items.Any(i => i.UnitPrice > 50))).Select(u => (u.UserId, u.UserName)).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));
        Assert.That(pgResults[0], Is.EqualTo((1, "Alice")));

        var my2 = My.Users().Where(u => u.Orders.Any(o => o.Items.Any(i => i.UnitPrice > 50))).Select(u => (u.UserId, u.UserName)).Prepare();
        var myResults = await my2.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(1));
        Assert.That(myResults[0], Is.EqualTo((1, "Alice")));
    }

    [Test]
    public async Task Where_Nested_Any_All()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.Orders.Any(o => o.Items.All(i => i.Quantity > 0))).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Any(o => o.Items.All(i => i.Quantity > 0))).Prepare();
        var my = My.Users().Where(u => u.Orders.Any(o => o.Items.All(i => i.Quantity > 0))).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Any(o => o.Items.All(i => i.Quantity > 0))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (NOT EXISTS (SELECT 1 FROM \"order_items\" AS \"sq1\" WHERE \"sq1\".\"OrderId\" = \"sq0\".\"OrderId\" AND NOT (\"sq1\".\"Quantity\" > 0))))",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (NOT EXISTS (SELECT 1 FROM \"order_items\" AS \"sq1\" WHERE \"sq1\".\"OrderId\" = \"sq0\".\"OrderId\" AND NOT (\"sq1\".\"Quantity\" > 0))))",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (NOT EXISTS (SELECT 1 FROM `order_items` AS `sq1` WHERE `sq1`.`OrderId` = `sq0`.`OrderId` AND NOT (`sq1`.`Quantity` > 0))))",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND (NOT EXISTS (SELECT 1 FROM [order_items] AS [sq1] WHERE [sq1].[OrderId] = [sq0].[OrderId] AND NOT ([sq1].[Quantity] > 0))))");

        // All order items have Quantity > 0 — Alice and Bob both have orders where All items have Quantity > 0
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
        Assert.That(results[1], Is.EqualTo((2, "Bob")));

        var pg2 = Pg.Users().Where(u => u.Orders.Any(o => o.Items.All(i => i.Quantity > 0))).Select(u => (u.UserId, u.UserName)).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(pgResults[1], Is.EqualTo((2, "Bob")));

        var my2 = My.Users().Where(u => u.Orders.Any(o => o.Items.All(i => i.Quantity > 0))).Select(u => (u.UserId, u.UserName)).Prepare();
        var myResults = await my2.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(myResults[1], Is.EqualTo((2, "Bob")));
    }

    #endregion

    #region Captured Variables in Subquery Predicates

    [Test]
    public async Task Where_Any_WithCapturedVariable()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var minAmount = 100m;

        var lt = Lite.Users().Where(u => u.Orders.Any(o => o.Total > minAmount)).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Any(o => o.Total > minAmount)).Prepare();
        var my = My.Users().Where(u => u.Orders.Any(o => o.Total > minAmount)).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Any(o => o.Total > minAmount)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Total\" > @p0))",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Total\" > $1))",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (`sq0`.`Total` > ?))",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND ([sq0].[Total] > @p0))");

        // Alice (250 > 100), Bob (150 > 100)
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));

        var pg2 = Pg.Users().Where(u => u.Orders.Any(o => o.Total > minAmount)).Select(u => (u.UserId, u.UserName)).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));

        var my2 = My.Users().Where(u => u.Orders.Any(o => o.Total > minAmount)).Select(u => (u.UserId, u.UserName)).Prepare();
        var myResults = await my2.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Where_All_WithCapturedVariable()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var status = "paid";

        var lt = Lite.Users().Where(u => u.Orders.All(o => o.Status == status)).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.All(o => o.Status == status)).Prepare();
        var my = My.Users().Where(u => u.Orders.All(o => o.Status == status)).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.All(o => o.Status == status)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE NOT EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND NOT (\"sq0\".\"Status\" = @p0))",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE NOT EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND NOT (\"sq0\".\"Status\" = $1))",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE NOT EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND NOT (`sq0`.`Status` = ?))",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE NOT EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND NOT ([sq0].[Status] = @p0))");

        // No orders are 'paid' — vacuously true only for Charlie (no orders)
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo((3, "Charlie")));

        var pg2 = Pg.Users().Where(u => u.Orders.All(o => o.Status == status)).Select(u => (u.UserId, u.UserName)).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));
        Assert.That(pgResults[0], Is.EqualTo((3, "Charlie")));

        var my2 = My.Users().Where(u => u.Orders.All(o => o.Status == status)).Select(u => (u.UserId, u.UserName)).Prepare();
        var myResults = await my2.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(1));
        Assert.That(myResults[0], Is.EqualTo((3, "Charlie")));
    }

    #endregion

    #region Subquery + Other Clauses

    [Test]
    public async Task Where_Any_And_Boolean()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.IsActive && u.Orders.Any()).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.IsActive && u.Orders.Any()).Prepare();
        var my = My.Users().Where(u => u.IsActive && u.Orders.Any()).Prepare();
        var ss = Ss.Users().Where(u => u.IsActive && u.Orders.Any()).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"IsActive\" = 1 AND EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\")",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"IsActive\" = TRUE AND EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\")",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE `IsActive` = 1 AND EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`)",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE [IsActive] = 1 AND EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId])");

        // Active users with orders: Alice and Bob
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
        Assert.That(results[1], Is.EqualTo((2, "Bob")));

        var pg2 = Pg.Users().Where(u => u.IsActive && u.Orders.Any()).Select(u => (u.UserId, u.UserName)).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(pgResults[1], Is.EqualTo((2, "Bob")));

        var my2 = My.Users().Where(u => u.IsActive && u.Orders.Any()).Select(u => (u.UserId, u.UserName)).Prepare();
        var myResults = await my2.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(myResults[1], Is.EqualTo((2, "Bob")));
    }

    [Test]
    public async Task Where_Any_ThenSelect_Tuple()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.Orders.Any()).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Any()).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => u.Orders.Any()).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Any()).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\")",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\")",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`)",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId])");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
        Assert.That(results[1], Is.EqualTo((2, "Bob")));

        var pg2 = Pg.Users().Where(u => u.Orders.Any()).Select(u => (u.UserId, u.UserName)).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(pgResults[1], Is.EqualTo((2, "Bob")));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(myResults[1], Is.EqualTo((2, "Bob")));
    }

    #endregion

    #region Additional coverage

    [Test]
    public async Task Where_Any_Or_Boolean()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.Orders.Any() || u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Any() || u.IsActive).Prepare();
        var my = My.Users().Where(u => u.Orders.Any() || u.IsActive).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Any() || u.IsActive).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") OR \"IsActive\" = 1",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") OR \"IsActive\" = TRUE",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`) OR `IsActive` = 1",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId]) OR [IsActive] = 1");

        // Has orders OR is active — all 3 users (Alice/Bob have orders, Alice/Bob are active, Charlie is neither but... wait Charlie is inactive and has no orders, so she fails)
        // Alice: orders=yes → true. Bob: orders=yes → true. Charlie: orders=no, active=no → false.
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));

        var pg2 = Pg.Users().Where(u => u.Orders.Any() || u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));

        var my2 = My.Users().Where(u => u.Orders.Any() || u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();
        var myResults = await my2.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Where_Multiple_Subqueries_Alias_Monotonicity()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.Orders.Any() && u.Orders.Any(o => o.Total > 100)).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Any() && u.Orders.Any(o => o.Total > 100)).Prepare();
        var my = My.Users().Where(u => u.Orders.Any() && u.Orders.Any(o => o.Total > 100)).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Any() && u.Orders.Any(o => o.Total > 100)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AND EXISTS (SELECT 1 FROM \"orders\" AS \"sq1\" WHERE \"sq1\".\"UserId\" = \"users\".\"UserId\" AND (\"sq1\".\"Total\" > 100))",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AND EXISTS (SELECT 1 FROM \"orders\" AS \"sq1\" WHERE \"sq1\".\"UserId\" = \"users\".\"UserId\" AND (\"sq1\".\"Total\" > 100))",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`) AND EXISTS (SELECT 1 FROM `orders` AS `sq1` WHERE `sq1`.`UserId` = `users`.`UserId` AND (`sq1`.`Total` > 100))",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId]) AND EXISTS (SELECT 1 FROM [orders] AS [sq1] WHERE [sq1].[UserId] = [users].[UserId] AND ([sq1].[Total] > 100))");

        // Has any order AND has an order > 100: Alice (250), Bob (150)
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));

        var pg2 = Pg.Users().Where(u => u.Orders.Any() && u.Orders.Any(o => o.Total > 100)).Select(u => (u.UserId, u.UserName)).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));

        var my2 = My.Users().Where(u => u.Orders.Any() && u.Orders.Any(o => o.Total > 100)).Select(u => (u.UserId, u.UserName)).Prepare();
        var myResults = await my2.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Where_Three_Level_Nesting()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.Orders.Any(o => o.Items.Any())).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Any(o => o.Items.Any())).Prepare();
        var my = My.Users().Where(u => u.Orders.Any(o => o.Items.Any())).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Any(o => o.Items.Any())).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (EXISTS (SELECT 1 FROM \"order_items\" AS \"sq1\" WHERE \"sq1\".\"OrderId\" = \"sq0\".\"OrderId\")))",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (EXISTS (SELECT 1 FROM \"order_items\" AS \"sq1\" WHERE \"sq1\".\"OrderId\" = \"sq0\".\"OrderId\")))",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (EXISTS (SELECT 1 FROM `order_items` AS `sq1` WHERE `sq1`.`OrderId` = `sq0`.`OrderId`)))",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND (EXISTS (SELECT 1 FROM [order_items] AS [sq1] WHERE [sq1].[OrderId] = [sq0].[OrderId])))");

        // All orders have at least one item — Alice and Bob
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
        Assert.That(results[1], Is.EqualTo((2, "Bob")));

        var pg2 = Pg.Users().Where(u => u.Orders.Any(o => o.Items.Any())).Select(u => (u.UserId, u.UserName)).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(pgResults[1], Is.EqualTo((2, "Bob")));

        var my2 = My.Users().Where(u => u.Orders.Any(o => o.Items.Any())).Select(u => (u.UserId, u.UserName)).Prepare();
        var myResults = await my2.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(myResults[1], Is.EqualTo((2, "Bob")));
    }

    [Test]
    public async Task Where_Any_StringPredicate_Contains()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.Orders.Any(o => o.Status.Contains("hipp"))).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Any(o => o.Status.Contains("paid"))).Prepare();
        var my = My.Users().Where(u => u.Orders.Any(o => o.Status.Contains("paid"))).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Any(o => o.Status.Contains("paid"))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Status\" LIKE '%hipp%'))",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Status\" LIKE '%paid%'))",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (`sq0`.`Status` LIKE '%paid%'))",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND ([sq0].[Status] LIKE '%paid%'))");

        Assert.That(lt.ToDiagnostics().Parameters, Has.Count.EqualTo(0));

        // 'Shipped' contains 'hipp' — Alice (order 1) and Bob (order 3) have Shipped orders
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));

        var pg2 = Pg.Users().Where(u => u.Orders.Any(o => o.Status.Contains("hipp"))).Select(u => (u.UserId, u.UserName)).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));

        var my2 = My.Users().Where(u => u.Orders.Any(o => o.Status.Contains("hipp"))).Select(u => (u.UserId, u.UserName)).Prepare();
        var myResults = await my2.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Where_Any_StringPredicate_StartsWith()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.Orders.Any(o => o.Status.StartsWith("P"))).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Any(o => o.Status.StartsWith("p"))).Prepare();
        var my = My.Users().Where(u => u.Orders.Any(o => o.Status.StartsWith("p"))).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Any(o => o.Status.StartsWith("p"))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Status\" LIKE 'P%'))",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Status\" LIKE 'p%'))",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (`sq0`.`Status` LIKE 'p%'))",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND ([sq0].[Status] LIKE 'p%'))");

        // 'Pending' starts with 'P' — only Alice has a Pending order (order 2)
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));

        var pg2 = Pg.Users().Where(u => u.Orders.Any(o => o.Status.StartsWith("P"))).Select(u => (u.UserId, u.UserName)).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));
        Assert.That(pgResults[0], Is.EqualTo((1, "Alice")));

        var my2 = My.Users().Where(u => u.Orders.Any(o => o.Status.StartsWith("P"))).Select(u => (u.UserId, u.UserName)).Prepare();
        var myResults = await my2.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(1));
        Assert.That(myResults[0], Is.EqualTo((1, "Alice")));
    }

    [Test]
    public async Task Where_Any_StringPredicate_EndsWith()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.Orders.Any(o => o.Status.EndsWith("ped"))).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Any(o => o.Status.EndsWith("ped"))).Prepare();
        var my = My.Users().Where(u => u.Orders.Any(o => o.Status.EndsWith("ped"))).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Any(o => o.Status.EndsWith("ped"))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Status\" LIKE '%ped'))",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Status\" LIKE '%ped'))",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (`sq0`.`Status` LIKE '%ped'))",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND ([sq0].[Status] LIKE '%ped'))");

        Assert.That(lt.ToDiagnostics().Parameters, Has.Count.EqualTo(0));

        // 'Shipped' ends with 'ped' — Alice (order 1) and Bob (order 3) have Shipped orders
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));

        var pg2 = Pg.Users().Where(u => u.Orders.Any(o => o.Status.EndsWith("ped"))).Select(u => (u.UserId, u.UserName)).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));

        var my2 = My.Users().Where(u => u.Orders.Any(o => o.Status.EndsWith("ped"))).Select(u => (u.UserId, u.UserName)).Prepare();
        var myResults = await my2.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Where_Any_StringPredicate_Contains_CapturedVariable_RemainsParameterized()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Mutable captured variable — must stay parameterized, not inlined
        var search = GetSubquerySearchValue();

        var lt = Lite.Users().Where(u => u.Orders.Any(o => o.Status.Contains(search))).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Any(o => o.Status.Contains(search))).Prepare();
        var my = My.Users().Where(u => u.Orders.Any(o => o.Status.Contains(search))).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Any(o => o.Status.Contains(search))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Status\" LIKE '%' || @p0 || '%'))",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Status\" LIKE '%' || $1 || '%'))",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (`sq0`.`Status` LIKE CONCAT('%', ?, '%')))",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND ([sq0].[Status] LIKE '%' + @p0 + '%'))");

        Assert.That(lt.ToDiagnostics().Parameters, Has.Count.EqualTo(1));

        // 'Shipped' contains 'hipp' — Alice (order 1) and Bob (order 3) have Shipped orders
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));

        var pg2 = Pg.Users().Where(u => u.Orders.Any(o => o.Status.Contains(search))).Select(u => (u.UserId, u.UserName)).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));

        var my2 = My.Users().Where(u => u.Orders.Any(o => o.Status.Contains(search))).Select(u => (u.UserId, u.UserName)).Prepare();
        var myResults = await my2.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
    }

    private static string GetSubquerySearchValue() => "hipp";

    #endregion

    #region Null checks in subquery predicates

    [Test]
    public async Task Where_Any_NullCheck_IsNull()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Orders with NULL Notes: Order 2 (Alice), Order 3 (Bob)
        var lt = Lite.Users().Where(u => u.Orders.Any(o => o.Notes == null)).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Any(o => o.Notes == null)).Prepare();
        var my = My.Users().Where(u => u.Orders.Any(o => o.Notes == null)).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Any(o => o.Notes == null)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Notes\" IS NULL))",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Notes\" IS NULL))",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (`sq0`.`Notes` IS NULL))",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND ([sq0].[Notes] IS NULL))");

        // Alice has Order 2 (Notes=NULL), Bob has Order 3 (Notes=NULL)
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
        Assert.That(results[1], Is.EqualTo((2, "Bob")));

        var pg2 = Pg.Users().Where(u => u.Orders.Any(o => o.Notes == null)).Select(u => (u.UserId, u.UserName)).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(pgResults[1], Is.EqualTo((2, "Bob")));

        var my2 = My.Users().Where(u => u.Orders.Any(o => o.Notes == null)).Select(u => (u.UserId, u.UserName)).Prepare();
        var myResults = await my2.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(myResults[1], Is.EqualTo((2, "Bob")));
    }

    [Test]
    public async Task Where_Any_NullCheck_IsNotNull()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Orders with non-NULL Notes: Order 1 (Alice, Notes='Express')
        var lt = Lite.Users().Where(u => u.Orders.Any(o => o.Notes != null)).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Any(o => o.Notes != null)).Prepare();
        var my = My.Users().Where(u => u.Orders.Any(o => o.Notes != null)).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Any(o => o.Notes != null)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Notes\" IS NOT NULL))",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Notes\" IS NOT NULL))",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (`sq0`.`Notes` IS NOT NULL))",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND ([sq0].[Notes] IS NOT NULL))");

        // Only Alice has Order 1 with Notes='Express'
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));

        var pg2 = Pg.Users().Where(u => u.Orders.Any(o => o.Notes != null)).Select(u => (u.UserId, u.UserName)).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));
        Assert.That(pgResults[0], Is.EqualTo((1, "Alice")));

        var my2 = My.Users().Where(u => u.Orders.Any(o => o.Notes != null)).Select(u => (u.UserId, u.UserName)).Prepare();
        var myResults = await my2.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(1));
        Assert.That(myResults[0], Is.EqualTo((1, "Alice")));
    }

    [Test]
    public async Task Where_Any_NullCheck_WithCapturedVariable()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Combine null check with captured variable comparison
        var minTotal = 100m;
        var lt = Lite.Users().Where(u => u.Orders.Any(o => o.Notes != null && o.Total > minTotal)).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Any(o => o.Notes != null && o.Total > minTotal)).Prepare();
        var my = My.Users().Where(u => u.Orders.Any(o => o.Notes != null && o.Total > minTotal)).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Any(o => o.Notes != null && o.Total > minTotal)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Notes\" IS NOT NULL AND (\"sq0\".\"Total\" > @p0)))",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Notes\" IS NOT NULL AND (\"sq0\".\"Total\" > $1)))",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (`sq0`.`Notes` IS NOT NULL AND (`sq0`.`Total` > ?)))",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND ([sq0].[Notes] IS NOT NULL AND ([sq0].[Total] > @p0)))");

        Assert.That(lt.ToDiagnostics().Parameters, Has.Count.EqualTo(1));

        // Only Order 1 (Alice) has Notes='Express' AND Total=250 > 100
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));

        var pg2 = Pg.Users().Where(u => u.Orders.Any(o => o.Notes != null && o.Total > minTotal)).Select(u => (u.UserId, u.UserName)).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));
        Assert.That(pgResults[0], Is.EqualTo((1, "Alice")));

        var my2 = My.Users().Where(u => u.Orders.Any(o => o.Notes != null && o.Total > minTotal)).Select(u => (u.UserId, u.UserName)).Prepare();
        var myResults = await my2.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(1));
        Assert.That(myResults[0], Is.EqualTo((1, "Alice")));
    }

    #endregion

    #region Issue #257 — Many<T>.Sum/Min/Max/Avg/Count in Select projection

    [Test]
    public async Task Select_Many_Count_InTuple()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Select(u => (u.UserName, OrderCount: u.Orders.Count())).Prepare();
        var pg = Pg.Users().Select(u => (u.UserName, OrderCount: u.Orders.Count())).Prepare();
        var my = My.Users().Select(u => (u.UserName, OrderCount: u.Orders.Count())).Prepare();
        var ss = Ss.Users().Select(u => (u.UserName, OrderCount: u.Orders.Count())).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserName\", (SELECT COUNT(*) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"OrderCount\" FROM \"users\"",
            pg:     "SELECT \"UserName\", (SELECT COUNT(*) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"OrderCount\" FROM \"users\"",
            mysql:  "SELECT `UserName`, (SELECT COUNT(*) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`) AS `OrderCount` FROM `users`",
            ss:     "SELECT [UserName], (SELECT COUNT(*) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId]) AS [OrderCount] FROM [users]");

        // Alice 2, Bob 1, Charlie 0 — Count is safe to read even on empty sets (returns 0, never NULL).
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0], Is.EqualTo(("Alice", 2)));
        Assert.That(results[1], Is.EqualTo(("Bob", 1)));
        Assert.That(results[2], Is.EqualTo(("Charlie", 0)));

        var pg2 = Pg.Users().Select(u => (u.UserName, OrderCount: u.Orders.Count())).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));
        Assert.That(pgResults[0], Is.EqualTo(("Alice", 2)));
        Assert.That(pgResults[1], Is.EqualTo(("Bob", 1)));
        Assert.That(pgResults[2], Is.EqualTo(("Charlie", 0)));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));
        Assert.That(myResults[0], Is.EqualTo(("Alice", 2)));
        Assert.That(myResults[1], Is.EqualTo(("Bob", 1)));
        Assert.That(myResults[2], Is.EqualTo(("Charlie", 0)));
    }

    [Test]
    public async Task Select_Many_Sum_InTuple()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Filter Charlie out — Sum on empty sets returns NULL which can't read into non-nullable decimal.
        var lt = Lite.Users().Where(u => u.Orders.Any()).Select(u => (u.UserName, OrderTotal: u.Orders.Sum(o => o.Total))).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Any()).Select(u => (u.UserName, OrderTotal: u.Orders.Sum(o => o.Total))).Prepare();
        var my = My.Users().Where(u => u.Orders.Any()).Select(u => (u.UserName, OrderTotal: u.Orders.Sum(o => o.Total))).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Any()).Select(u => (u.UserName, OrderTotal: u.Orders.Sum(o => o.Total))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserName\", (SELECT SUM(\"sq0\".\"Total\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"OrderTotal\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\")",
            pg:     "SELECT \"UserName\", (SELECT SUM(\"sq0\".\"Total\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"OrderTotal\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\")",
            mysql:  "SELECT `UserName`, (SELECT SUM(`sq0`.`Total`) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`) AS `OrderTotal` FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`)",
            ss:     "SELECT [UserName], (SELECT SUM([sq0].[Total]) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId]) AS [OrderTotal] FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId])");

        // Alice: 250.00 + 75.50 = 325.50; Bob: 150.00.
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo(("Alice", 325.50m)));
        Assert.That(results[1], Is.EqualTo(("Bob", 150.00m)));

        var pg2 = Pg.Users().Where(u => u.Orders.Any()).Select(u => (u.UserName, OrderTotal: u.Orders.Sum(o => o.Total))).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0], Is.EqualTo(("Alice", 325.50m)));
        Assert.That(pgResults[1], Is.EqualTo(("Bob", 150.00m)));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0], Is.EqualTo(("Alice", 325.50m)));
        Assert.That(myResults[1], Is.EqualTo(("Bob", 150.00m)));
    }

    [Test]
    public async Task Select_Many_Min_InTuple()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.Orders.Any()).Select(u => (u.UserName, MinOrder: u.Orders.Min(o => o.Total))).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Any()).Select(u => (u.UserName, MinOrder: u.Orders.Min(o => o.Total))).Prepare();
        var my = My.Users().Where(u => u.Orders.Any()).Select(u => (u.UserName, MinOrder: u.Orders.Min(o => o.Total))).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Any()).Select(u => (u.UserName, MinOrder: u.Orders.Min(o => o.Total))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserName\", (SELECT MIN(\"sq0\".\"Total\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"MinOrder\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\")",
            pg:     "SELECT \"UserName\", (SELECT MIN(\"sq0\".\"Total\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"MinOrder\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\")",
            mysql:  "SELECT `UserName`, (SELECT MIN(`sq0`.`Total`) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`) AS `MinOrder` FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`)",
            ss:     "SELECT [UserName], (SELECT MIN([sq0].[Total]) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId]) AS [MinOrder] FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId])");

        // Alice min: 75.50; Bob min: 150.00.
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo(("Alice", 75.50m)));
        Assert.That(results[1], Is.EqualTo(("Bob", 150.00m)));

        var pg2 = Pg.Users().Where(u => u.Orders.Any()).Select(u => (u.UserName, MinOrder: u.Orders.Min(o => o.Total))).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0], Is.EqualTo(("Alice", 75.50m)));
        Assert.That(pgResults[1], Is.EqualTo(("Bob", 150.00m)));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0], Is.EqualTo(("Alice", 75.50m)));
        Assert.That(myResults[1], Is.EqualTo(("Bob", 150.00m)));
    }

    [Test]
    public async Task Select_Many_Max_InTuple()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.Orders.Any()).Select(u => (u.UserName, MaxOrder: u.Orders.Max(o => o.Total))).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Any()).Select(u => (u.UserName, MaxOrder: u.Orders.Max(o => o.Total))).Prepare();
        var my = My.Users().Where(u => u.Orders.Any()).Select(u => (u.UserName, MaxOrder: u.Orders.Max(o => o.Total))).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Any()).Select(u => (u.UserName, MaxOrder: u.Orders.Max(o => o.Total))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserName\", (SELECT MAX(\"sq0\".\"Total\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"MaxOrder\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\")",
            pg:     "SELECT \"UserName\", (SELECT MAX(\"sq0\".\"Total\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"MaxOrder\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\")",
            mysql:  "SELECT `UserName`, (SELECT MAX(`sq0`.`Total`) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`) AS `MaxOrder` FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`)",
            ss:     "SELECT [UserName], (SELECT MAX([sq0].[Total]) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId]) AS [MaxOrder] FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId])");

        // Alice max: 250.00; Bob max: 150.00.
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo(("Alice", 250.00m)));
        Assert.That(results[1], Is.EqualTo(("Bob", 150.00m)));

        var pg2 = Pg.Users().Where(u => u.Orders.Any()).Select(u => (u.UserName, MaxOrder: u.Orders.Max(o => o.Total))).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0], Is.EqualTo(("Alice", 250.00m)));
        Assert.That(pgResults[1], Is.EqualTo(("Bob", 150.00m)));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0], Is.EqualTo(("Alice", 250.00m)));
        Assert.That(myResults[1], Is.EqualTo(("Bob", 150.00m)));
    }

    [Test]
    public async Task Select_Many_Average_InTuple()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.Orders.Any()).Select(u => (u.UserName, AvgOrder: u.Orders.Average(o => o.Total))).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Any()).Select(u => (u.UserName, AvgOrder: u.Orders.Average(o => o.Total))).Prepare();
        var my = My.Users().Where(u => u.Orders.Any()).Select(u => (u.UserName, AvgOrder: u.Orders.Average(o => o.Total))).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Any()).Select(u => (u.UserName, AvgOrder: u.Orders.Average(o => o.Total))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserName\", (SELECT AVG(\"sq0\".\"Total\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"AvgOrder\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\")",
            pg:     "SELECT \"UserName\", (SELECT AVG(\"sq0\".\"Total\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"AvgOrder\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\")",
            mysql:  "SELECT `UserName`, (SELECT AVG(`sq0`.`Total`) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`) AS `AvgOrder` FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`)",
            ss:     "SELECT [UserName], (SELECT AVG([sq0].[Total]) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId]) AS [AvgOrder] FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId])");

        // Alice avg: (250.00 + 75.50) / 2 = 162.75; Bob avg: 150.00.
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo(("Alice", 162.75m)));
        Assert.That(results[1], Is.EqualTo(("Bob", 150.00m)));

        var pg2 = Pg.Users().Where(u => u.Orders.Any()).Select(u => (u.UserName, AvgOrder: u.Orders.Average(o => o.Total))).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0], Is.EqualTo(("Alice", 162.75m)));
        Assert.That(pgResults[1], Is.EqualTo(("Bob", 150.00m)));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0], Is.EqualTo(("Alice", 162.75m)));
        Assert.That(myResults[1], Is.EqualTo(("Bob", 150.00m)));
    }

    /// <summary>
    /// Issue #257 exact repro: multiple navigation aggregates in one tuple Select.
    /// Prior to the fix this emitted SELECT "UserName", "", "", "" with reader code
    /// containing (?)r.GetValue(N) that didn't compile (CS0246 cascade).
    /// </summary>
    [Test]
    public async Task Select_Many_MultipleAggregates_InTuple_Repro()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.Orders.Any())
            .Select(u => (
                u.UserName,
                OrderTotal:   u.Orders.Sum(o => o.Total),
                BiggestOrder: u.Orders.Max(o => o.Total),
                AverageOrder: u.Orders.Average(o => o.Total)))
            .Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Any())
            .Select(u => (
                u.UserName,
                OrderTotal:   u.Orders.Sum(o => o.Total),
                BiggestOrder: u.Orders.Max(o => o.Total),
                AverageOrder: u.Orders.Average(o => o.Total)))
            .Prepare();
        var my = My.Users().Where(u => u.Orders.Any())
            .Select(u => (
                u.UserName,
                OrderTotal:   u.Orders.Sum(o => o.Total),
                BiggestOrder: u.Orders.Max(o => o.Total),
                AverageOrder: u.Orders.Average(o => o.Total)))
            .Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Any())
            .Select(u => (
                u.UserName,
                OrderTotal:   u.Orders.Sum(o => o.Total),
                BiggestOrder: u.Orders.Max(o => o.Total),
                AverageOrder: u.Orders.Average(o => o.Total)))
            .Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserName\", (SELECT SUM(\"sq0\".\"Total\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"OrderTotal\", (SELECT MAX(\"sq0\".\"Total\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"BiggestOrder\", (SELECT AVG(\"sq0\".\"Total\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"AverageOrder\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\")",
            pg:     "SELECT \"UserName\", (SELECT SUM(\"sq0\".\"Total\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"OrderTotal\", (SELECT MAX(\"sq0\".\"Total\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"BiggestOrder\", (SELECT AVG(\"sq0\".\"Total\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"AverageOrder\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\")",
            mysql:  "SELECT `UserName`, (SELECT SUM(`sq0`.`Total`) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`) AS `OrderTotal`, (SELECT MAX(`sq0`.`Total`) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`) AS `BiggestOrder`, (SELECT AVG(`sq0`.`Total`) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`) AS `AverageOrder` FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`)",
            ss:     "SELECT [UserName], (SELECT SUM([sq0].[Total]) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId]) AS [OrderTotal], (SELECT MAX([sq0].[Total]) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId]) AS [BiggestOrder], (SELECT AVG([sq0].[Total]) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId]) AS [AverageOrder] FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId])");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo(("Alice", 325.50m, 250.00m, 162.75m)));
        Assert.That(results[1], Is.EqualTo(("Bob", 150.00m, 150.00m, 150.00m)));

        var pg2 = Pg.Users().Where(u => u.Orders.Any())
            .Select(u => (u.UserName,
                OrderTotal: u.Orders.Sum(o => o.Total),
                BiggestOrder: u.Orders.Max(o => o.Total),
                AverageOrder: u.Orders.Average(o => o.Total)))
            .Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0], Is.EqualTo(("Alice", 325.50m, 250.00m, 162.75m)));
        Assert.That(pgResults[1], Is.EqualTo(("Bob", 150.00m, 150.00m, 150.00m)));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0], Is.EqualTo(("Alice", 325.50m, 250.00m, 162.75m)));
        Assert.That(myResults[1], Is.EqualTo(("Bob", 150.00m, 150.00m, 150.00m)));
    }

    [Test]
    public async Task Select_Many_Sum_InDtoInitializer()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.Orders.Any())
            .Select(u => new UserOrderTotalDto { Name = u.UserName, OrderTotal = u.Orders.Sum(o => o.Total) }).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Any())
            .Select(u => new UserOrderTotalDto { Name = u.UserName, OrderTotal = u.Orders.Sum(o => o.Total) }).Prepare();
        var my = My.Users().Where(u => u.Orders.Any())
            .Select(u => new UserOrderTotalDto { Name = u.UserName, OrderTotal = u.Orders.Sum(o => o.Total) }).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Any())
            .Select(u => new UserOrderTotalDto { Name = u.UserName, OrderTotal = u.Orders.Sum(o => o.Total) }).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            ss.ToDiagnostics(),
            sqlite: "SELECT \"UserName\", (SELECT SUM(\"sq0\".\"Total\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"OrderTotal\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\")",
            pg:     "SELECT \"UserName\", (SELECT SUM(\"sq0\".\"Total\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"OrderTotal\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\")",
            mysql:  "SELECT `UserName`, (SELECT SUM(`sq0`.`Total`) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`) AS `OrderTotal` FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`)",
            ss:     "SELECT [UserName], (SELECT SUM([sq0].[Total]) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId]) AS [OrderTotal] FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId])");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].Name, Is.EqualTo("Alice"));
        Assert.That(results[0].OrderTotal, Is.EqualTo(325.50m));
        Assert.That(results[1].Name, Is.EqualTo("Bob"));
        Assert.That(results[1].OrderTotal, Is.EqualTo(150.00m));

        var pg2 = Pg.Users().Where(u => u.Orders.Any())
            .Select(u => new UserOrderTotalDto { Name = u.UserName, OrderTotal = u.Orders.Sum(o => o.Total) }).Prepare();
        var pgResults = await pg2.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0].Name, Is.EqualTo("Alice"));
        Assert.That(pgResults[0].OrderTotal, Is.EqualTo(325.50m));
        Assert.That(pgResults[1].Name, Is.EqualTo("Bob"));
        Assert.That(pgResults[1].OrderTotal, Is.EqualTo(150.00m));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0].Name, Is.EqualTo("Alice"));
        Assert.That(myResults[0].OrderTotal, Is.EqualTo(325.50m));
        Assert.That(myResults[1].Name, Is.EqualTo("Bob"));
        Assert.That(myResults[1].OrderTotal, Is.EqualTo(150.00m));
    }

    /// <summary>DTO carrier for <see cref="Select_Many_Sum_InDtoInitializer"/>.</summary>
    public class UserOrderTotalDto
    {
        public string Name { get; set; } = "";
        public decimal OrderTotal { get; set; }
    }

    /// <summary>
    /// Locks in current behavior: Many&lt;T&gt;.Sum/Min/Max/Avg in Select projects to a
    /// non-nullable CLR type matching the existing Sql.* aggregate convention. Empty
    /// nav collections (e.g., Charlie has no orders) emit SQL NULL which fails to read
    /// into the non-nullable carrier — same trade-off the existing Where path takes.
    /// Users wanting empty-safe semantics should filter the empty case
    /// (e.g., <c>.Where(u =&gt; u.Orders.Any())</c>) before projecting.
    /// </summary>
    [Test]
    public async Task Select_Many_Sum_OnEmptyNavigation_ThrowsAtRead()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, _) = t;

        var lt = Lite.Users()
            .Select(u => (u.UserName, OrderTotal: u.Orders.Sum(o => o.Total)))
            .Prepare();

        // SQL renders correctly (no .Where filter); only the read fails on Charlie's NULL.
        Assert.That(
            lt.ToDiagnostics().Sql,
            Is.EqualTo("SELECT \"UserName\", (SELECT SUM(\"sq0\".\"Total\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"OrderTotal\" FROM \"users\""));

        // Reading SQL NULL into non-nullable decimal must throw — confirms empty-set caveat.
        // We assert on exception *type* (QuarryQueryException wrapping a provider exception,
        // or the raw provider exception) rather than message wording, so the test doesn't
        // break when a driver changes its error text.
        Exception? caught = null;
        try { await lt.ExecuteFetchAllAsync(); }
        catch (Exception ex) { caught = ex; }
        Assert.That(caught, Is.Not.Null, "Expected an exception reading SQL NULL into non-nullable decimal");
        Assert.That(caught, Is.InstanceOf<QuarryQueryException>()
            .Or.InstanceOf<InvalidOperationException>()
            .Or.InstanceOf<InvalidCastException>(),
            $"Expected a read-time exception wrapping the NULL-into-non-nullable failure. Got: {caught!.GetType().FullName}");

        var pg2 = Pg.Users()
            .Select(u => (u.UserName, OrderTotal: u.Orders.Sum(o => o.Total)))
            .Prepare();
        Exception? pgCaught = null;
        try { await pg2.ExecuteFetchAllAsync(); }
        catch (Exception ex) { pgCaught = ex; }
        Assert.That(pgCaught, Is.Not.Null);
        Assert.That(pgCaught, Is.InstanceOf<QuarryQueryException>()
            .Or.InstanceOf<InvalidOperationException>()
            .Or.InstanceOf<InvalidCastException>());

        var my2 = My.Users()
            .Select(u => (u.UserName, OrderTotal: u.Orders.Sum(o => o.Total)))
            .Prepare();
        Exception? myCaught = null;
        try { await my2.ExecuteFetchAllAsync(); }
        catch (Exception ex) { myCaught = ex; }
        Assert.That(myCaught, Is.Not.Null);
        Assert.That(myCaught, Is.InstanceOf<QuarryQueryException>()
            .Or.InstanceOf<InvalidOperationException>()
            .Or.InstanceOf<InvalidCastException>());
    }

    #endregion
}
