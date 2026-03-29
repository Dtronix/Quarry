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
    }

    private static string GetSubquerySearchValue() => "hipp";

    #endregion
}
