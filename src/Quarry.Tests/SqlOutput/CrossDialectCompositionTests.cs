using Quarry;
using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;


/// <summary>
/// Cross-dialect feature composition tests that validate complex, multi-clause
/// SQL generation by exercising combinations of features that are individually
/// tested but not yet tested together. See issue #23.
/// </summary>
[TestFixture]
internal class CrossDialectCompositionTests
{
    #region 1. Join + WHERE + OrderBy(DESC) + LIMIT/OFFSET (join pagination)

    [Test]
    public async Task Join_Where_OrderBy_Limit_Offset()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => o.Total > 100 && u.IsActive)
                .OrderBy((u, o) => o.Total, Direction.Descending)
                .Limit(10).Offset(0)
                .Select((u, o) => (u.UserName, o.Total, o.Status))
                .Prepare();
        var pg = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => o.Total > 100 && u.IsActive)
                .OrderBy((u, o) => o.Total, Direction.Descending)
                .Limit(10).Offset(0)
                .Select((u, o) => (u.UserName, o.Total, o.Status))
                .Prepare();
        var my = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => o.Total > 100 && u.IsActive)
                .OrderBy((u, o) => o.Total, Direction.Descending)
                .Limit(10).Offset(0)
                .Select((u, o) => (u.UserName, o.Total, o.Status))
                .Prepare();
        var ss = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => o.Total > 100 && u.IsActive)
                .OrderBy((u, o) => o.Total, Direction.Descending)
                .Limit(10).Offset(0)
                .Select((u, o) => (u.UserName, o.Total, o.Status))
                .Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\", \"t1\".\"Status\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > 100 AND \"t0\".\"IsActive\" ORDER BY \"t1\".\"Total\" DESC LIMIT 10",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\", \"t1\".\"Status\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > 100 AND \"t0\".\"IsActive\" ORDER BY \"t1\".\"Total\" DESC LIMIT 10",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total`, `t1`.`Status` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` WHERE `t1`.`Total` > 100 AND `t0`.`IsActive` ORDER BY `t1`.`Total` DESC LIMIT 10",
            ss:     "SELECT [t0].[UserName], [t1].[Total], [t1].[Status] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] WHERE [t1].[Total] > 100 AND [t0].[IsActive] ORDER BY [t1].[Total] DESC OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY");

        // Seed: Alice has orders 250 (Shipped) and 75.50 (Pending). Only 250 > 100. Bob has 150 (Shipped) > 100. — 2 results
        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo(("Alice", 250.00m, "Shipped")));
        Assert.That(results[1], Is.EqualTo(("Bob", 150.00m, "Shipped")));
    }

    #endregion

    #region 2. Subquery + boolean WHERE + OrderBy + Select

    [Test]
    public async Task Where_Boolean_Subquery_OrderBy_Select()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users()
                .Where(u => u.IsActive && u.Orders.Any(o => o.Total > 500))
                .OrderBy(u => u.UserName)
                .Select(u => (u.UserName, u.Email))
                .Prepare();
        var pg = Pg.Users()
                .Where(u => u.IsActive && u.Orders.Any(o => o.Total > 500))
                .OrderBy(u => u.UserName)
                .Select(u => (u.UserName, u.Email))
                .Prepare();
        var my = My.Users()
                .Where(u => u.IsActive && u.Orders.Any(o => o.Total > 500))
                .OrderBy(u => u.UserName)
                .Select(u => (u.UserName, u.Email))
                .Prepare();
        var ss = Ss.Users()
                .Where(u => u.IsActive && u.Orders.Any(o => o.Total > 500))
                .OrderBy(u => u.UserName)
                .Select(u => (u.UserName, u.Email))
                .Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserName\", \"Email\" FROM \"users\" WHERE \"IsActive\" AND EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Total\" > 500)) ORDER BY \"UserName\" ASC",
            pg:     "SELECT \"UserName\", \"Email\" FROM \"users\" WHERE \"IsActive\" AND EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Total\" > 500)) ORDER BY \"UserName\" ASC",
            mysql:  "SELECT `UserName`, `Email` FROM `users` WHERE `IsActive` AND EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (`sq0`.`Total` > 500)) ORDER BY `UserName` ASC",
            ss:     "SELECT [UserName], [Email] FROM [users] WHERE [IsActive] AND EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND ([sq0].[Total] > 500)) ORDER BY [UserName] ASC");

        // No users have orders > 500 in seed data — 0 results
        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(0));
    }

    #endregion

    #region 3. Three-table join with WHERE on deepest table

    [Test]
    public async Task ThreeTableJoin_Where_DeepestTable()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id)
                .Join<OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id)
                .Where((u, o, oi) => oi.UnitPrice > 50.00m)
                .Select((u, o, oi) => (u.UserName, o.Status, oi.ProductName, oi.Quantity))
                .Prepare();
        var pg = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id)
                .Join<Pg.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id)
                .Where((u, o, oi) => oi.UnitPrice > 50.00m)
                .Select((u, o, oi) => (u.UserName, o.Status, oi.ProductName, oi.Quantity))
                .Prepare();
        var my = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id)
                .Join<My.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id)
                .Where((u, o, oi) => oi.UnitPrice > 50.00m)
                .Select((u, o, oi) => (u.UserName, o.Status, oi.ProductName, oi.Quantity))
                .Prepare();
        var ss = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id)
                .Join<Ss.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id)
                .Where((u, o, oi) => oi.UnitPrice > 50.00m)
                .Select((u, o, oi) => (u.UserName, o.Status, oi.ProductName, oi.Quantity))
                .Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Status\", \"t2\".\"ProductName\", \"t2\".\"Quantity\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" INNER JOIN \"order_items\" AS \"t2\" ON \"t1\".\"OrderId\" = \"t2\".\"OrderId\" WHERE \"t2\".\"UnitPrice\" > 50.00",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Status\", \"t2\".\"ProductName\", \"t2\".\"Quantity\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" INNER JOIN \"order_items\" AS \"t2\" ON \"t1\".\"OrderId\" = \"t2\".\"OrderId\" WHERE \"t2\".\"UnitPrice\" > 50.00",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Status`, `t2`.`ProductName`, `t2`.`Quantity` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` INNER JOIN `order_items` AS `t2` ON `t1`.`OrderId` = `t2`.`OrderId` WHERE `t2`.`UnitPrice` > 50.00",
            ss:     "SELECT [t0].[UserName], [t1].[Status], [t2].[ProductName], [t2].[Quantity] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] INNER JOIN [order_items] AS [t2] ON [t1].[OrderId] = [t2].[OrderId] WHERE [t2].[UnitPrice] > 50.00");

        // Seed: Widget UnitPrice=125 (>50) and Gadget UnitPrice=75.50 (>50), Widget UnitPrice=50 (NOT >50) — 2 results
        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
    }

    #endregion

    #region 4. Multiple subqueries in WHERE (Any + All together)

    [Test]
    public async Task Where_Any_And_All_MultipleSubqueries()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users()
                .Where(u => u.Orders.Any(o => o.Status == "shipped")
                         && u.Orders.All(o => o.Status != "cancelled"))
                .Select(u => (u.UserId, u.UserName))
                .Prepare();
        var pg = Pg.Users()
                .Where(u => u.Orders.Any(o => o.Status == "shipped")
                         && u.Orders.All(o => o.Status != "cancelled"))
                .Select(u => (u.UserId, u.UserName))
                .Prepare();
        var my = My.Users()
                .Where(u => u.Orders.Any(o => o.Status == "shipped")
                         && u.Orders.All(o => o.Status != "cancelled"))
                .Select(u => (u.UserId, u.UserName))
                .Prepare();
        var ss = Ss.Users()
                .Where(u => u.Orders.Any(o => o.Status == "shipped")
                         && u.Orders.All(o => o.Status != "cancelled"))
                .Select(u => (u.UserId, u.UserName))
                .Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Status\" = 'shipped')) AND NOT EXISTS (SELECT 1 FROM \"orders\" AS \"sq1\" WHERE \"sq1\".\"UserId\" = \"users\".\"UserId\" AND NOT (\"sq1\".\"Status\" <> 'cancelled'))",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Status\" = 'shipped')) AND NOT EXISTS (SELECT 1 FROM \"orders\" AS \"sq1\" WHERE \"sq1\".\"UserId\" = \"users\".\"UserId\" AND NOT (\"sq1\".\"Status\" <> 'cancelled'))",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (`sq0`.`Status` = 'shipped')) AND NOT EXISTS (SELECT 1 FROM `orders` AS `sq1` WHERE `sq1`.`UserId` = `users`.`UserId` AND NOT (`sq1`.`Status` <> 'cancelled'))",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND ([sq0].[Status] = 'shipped')) AND NOT EXISTS (SELECT 1 FROM [orders] AS [sq1] WHERE [sq1].[UserId] = [users].[UserId] AND NOT ([sq1].[Status] <> 'cancelled'))");

        // Seed: Alice has Shipped+Pending, Bob has Shipped — case-sensitive "shipped" vs "Shipped" means 0 matches
        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(0));
    }

    #endregion

    #region 5. String operation + null check + OrderBy(DESC) + LIMIT

    [Test]
    public async Task Where_NullCheck_Contains_OrderBy_Limit()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users()
                .Where(u => u.Email != null && u.UserName.Contains("john"))
                .OrderBy(u => u.UserName, Direction.Descending)
                .Limit(5)
                .Select(u => (u.UserName, u.Email))
                .Prepare();
        var pg = Pg.Users()
                .Where(u => u.Email != null && u.UserName.Contains("john"))
                .OrderBy(u => u.UserName, Direction.Descending)
                .Limit(5)
                .Select(u => (u.UserName, u.Email))
                .Prepare();
        var my = My.Users()
                .Where(u => u.Email != null && u.UserName.Contains("john"))
                .OrderBy(u => u.UserName, Direction.Descending)
                .Limit(5)
                .Select(u => (u.UserName, u.Email))
                .Prepare();
        var ss = Ss.Users()
                .Where(u => u.Email != null && u.UserName.Contains("john"))
                .OrderBy(u => u.UserName, Direction.Descending)
                .Limit(5)
                .Select(u => (u.UserName, u.Email))
                .Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserName\", \"Email\" FROM \"users\" WHERE \"Email\" IS NOT NULL AND \"UserName\" LIKE '%' || @p0 || '%' ORDER BY \"UserName\" DESC LIMIT 5",
            pg:     "SELECT \"UserName\", \"Email\" FROM \"users\" WHERE \"Email\" IS NOT NULL AND \"UserName\" LIKE '%' || $1 || '%' ORDER BY \"UserName\" DESC LIMIT 5",
            mysql:  "SELECT `UserName`, `Email` FROM `users` WHERE `Email` IS NOT NULL AND `UserName` LIKE CONCAT('%', ?, '%') ORDER BY `UserName` DESC LIMIT 5",
            ss:     "SELECT [UserName], [Email] FROM [users] WHERE [Email] IS NOT NULL AND [UserName] LIKE '%' + @p0 + '%' ORDER BY [UserName] DESC OFFSET 0 ROWS FETCH NEXT 5 ROWS ONLY");

        // No seed users have "john" in UserName — 0 results
        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(0));
    }

    #endregion

    #region 6. IN clause within a join

    [Test]
    public async Task Join_Where_InClause()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var statuses = new[] { "pending", "processing", "shipped" };
        var lite = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => statuses.Contains(o.Status))
                .Select((u, o) => (u.UserName, o.Total))
                .Prepare();
        var pg = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => statuses.Contains(o.Status))
                .Select((u, o) => (u.UserName, o.Total))
                .Prepare();
        var my = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => statuses.Contains(o.Status))
                .Select((u, o) => (u.UserName, o.Total))
                .Prepare();
        var ss = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => statuses.Contains(o.Status))
                .Select((u, o) => (u.UserName, o.Total))
                .Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Status\" IN ('pending', 'processing', 'shipped')",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Status\" IN ('pending', 'processing', 'shipped')",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` WHERE `t1`.`Status` IN ('pending', 'processing', 'shipped')",
            ss:     "SELECT [t0].[UserName], [t1].[Total] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] WHERE [t1].[Status] IN ('pending', 'processing', 'shipped')");

        // Case-sensitive: seed has "Shipped", "Pending" — lowercase "shipped"/"pending" match 0
        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(0));
    }

    #endregion

    #region 7. Enum comparison with COUNT subquery

    [Test]
    public async Task Where_CountSubquery_WithEnumPredicate()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users()
                .Where(u => u.Orders.Count(o => o.Priority == OrderPriority.Urgent) > 2)
                .Select(u => (u.UserId, u.UserName))
                .Prepare();
        var pg = Pg.Users()
                .Where(u => u.Orders.Count(o => o.Priority == OrderPriority.Urgent) > 2)
                .Select(u => (u.UserId, u.UserName))
                .Prepare();
        var my = My.Users()
                .Where(u => u.Orders.Count(o => o.Priority == OrderPriority.Urgent) > 2)
                .Select(u => (u.UserId, u.UserName))
                .Prepare();
        var ss = Ss.Users()
                .Where(u => u.Orders.Count(o => o.Priority == OrderPriority.Urgent) > 2)
                .Select(u => (u.UserId, u.UserName))
                .Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE (SELECT COUNT(*) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Priority\" = 3)) > 2",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE (SELECT COUNT(*) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Priority\" = 3)) > 2",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE (SELECT COUNT(*) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (`sq0`.`Priority` = 3)) > 2",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE (SELECT COUNT(*) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND ([sq0].[Priority] = 3)) > 2");

        // Seed: Bob has 1 Urgent order, nobody has >2 — 0 results
        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(0));
    }

    #endregion

    #region 8. Aggregate with GROUP BY + HAVING

    [Test]
    public async Task GroupBy_Having_Aggregates()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Orders()
                .Where(o => true)
                .GroupBy(o => o.Status)
                .Having(o => Sql.Count() > 5)
                .Select(o => (o.Status, Sql.Count(), Sql.Sum(o.Total)))
                .Prepare();
        var pg = Pg.Orders()
                .Where(o => true)
                .GroupBy(o => o.Status)
                .Having(o => Sql.Count() > 5)
                .Select(o => (o.Status, Sql.Count(), Sql.Sum(o.Total)))
                .Prepare();
        var my = My.Orders()
                .Where(o => true)
                .GroupBy(o => o.Status)
                .Having(o => Sql.Count() > 5)
                .Select(o => (o.Status, Sql.Count(), Sql.Sum(o.Total)))
                .Prepare();
        var ss = Ss.Orders()
                .Where(o => true)
                .GroupBy(o => o.Status)
                .Having(o => Sql.Count() > 5)
                .Select(o => (o.Status, Sql.Count(), Sql.Sum(o.Total)))
                .Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"Status\", COUNT(*) AS \"Item2\", SUM(\"Total\") AS \"Item3\" FROM \"orders\" GROUP BY \"Status\" HAVING COUNT(*) > 5",
            pg:     "SELECT \"Status\", COUNT(*) AS \"Item2\", SUM(\"Total\") AS \"Item3\" FROM \"orders\" GROUP BY \"Status\" HAVING COUNT(*) > 5",
            mysql:  "SELECT `Status`, COUNT(*) AS `Item2`, SUM(\"Total\") AS `Item3` FROM `orders` GROUP BY `Status` HAVING COUNT(*) > 5",
            ss:     "SELECT [Status], COUNT(*) AS [Item2], SUM(\"Total\") AS [Item3] FROM [orders] GROUP BY [Status] HAVING COUNT(*) > 5");

        // Seed: only 3 orders total, no status group has >5 — 0 results
        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(0));
    }

    #endregion

    #region 9. DISTINCT + Join + OrderBy + LIMIT

    [Test]
    public async Task Join_Distinct_OrderBy_Limit()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => o.Total > 0)
                .OrderBy((u, o) => o.Total)
                .Distinct()
                .Limit(20)
                .Select((u, o) => u.UserName)
                .Prepare();
        var pg = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => o.Total > 0)
                .OrderBy((u, o) => o.Total)
                .Distinct()
                .Limit(20)
                .Select((u, o) => u.UserName)
                .Prepare();
        var my = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => o.Total > 0)
                .OrderBy((u, o) => o.Total)
                .Distinct()
                .Limit(20)
                .Select((u, o) => u.UserName)
                .Prepare();
        var ss = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => o.Total > 0)
                .OrderBy((u, o) => o.Total)
                .Distinct()
                .Limit(20)
                .Select((u, o) => u.UserName)
                .Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT DISTINCT \"t0\".\"UserName\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > 0 ORDER BY \"t1\".\"Total\" ASC LIMIT 20",
            pg:     "SELECT DISTINCT \"t0\".\"UserName\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > 0 ORDER BY \"t1\".\"Total\" ASC LIMIT 20",
            mysql:  "SELECT DISTINCT `t0`.`UserName` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` WHERE `t1`.`Total` > 0 ORDER BY `t1`.`Total` ASC LIMIT 20",
            ss:     "SELECT DISTINCT [t0].[UserName] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] WHERE [t1].[Total] > 0 ORDER BY [t1].[Total] ASC OFFSET 0 ROWS FETCH NEXT 20 ROWS ONLY");

        // Seed: Alice has 2 orders, Bob has 1 — DISTINCT UserName gives Alice, Bob — 2 results
        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
    }

    #endregion

    #region 10. Nested 3-level subquery (Users -> Orders -> OrderItems)

    [Test]
    public async Task Where_NestedThreeLevelSubquery()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users()
                .Where(u => u.Orders.Any(o => o.Items.Any(i => i.UnitPrice > 100)))
                .Select(u => (u.UserId, u.UserName))
                .Prepare();
        var pg = Pg.Users()
                .Where(u => u.Orders.Any(o => o.Items.Any(i => i.UnitPrice > 100)))
                .Select(u => (u.UserId, u.UserName))
                .Prepare();
        var my = My.Users()
                .Where(u => u.Orders.Any(o => o.Items.Any(i => i.UnitPrice > 100)))
                .Select(u => (u.UserId, u.UserName))
                .Prepare();
        var ss = Ss.Users()
                .Where(u => u.Orders.Any(o => o.Items.Any(i => i.UnitPrice > 100)))
                .Select(u => (u.UserId, u.UserName))
                .Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (EXISTS (SELECT 1 FROM \"order_items\" AS \"sq1\" WHERE \"sq1\".\"OrderId\" = \"sq0\".\"OrderId\" AND (\"sq1\".\"UnitPrice\" > 100))))",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (EXISTS (SELECT 1 FROM \"order_items\" AS \"sq1\" WHERE \"sq1\".\"OrderId\" = \"sq0\".\"OrderId\" AND (\"sq1\".\"UnitPrice\" > 100))))",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (EXISTS (SELECT 1 FROM `order_items` AS `sq1` WHERE `sq1`.`OrderId` = `sq0`.`OrderId` AND (`sq1`.`UnitPrice` > 100))))",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND (EXISTS (SELECT 1 FROM [order_items] AS [sq1] WHERE [sq1].[OrderId] = [sq0].[OrderId] AND ([sq1].[UnitPrice] > 100))))");

        // Seed: Widget UnitPrice=125 (>100) in order 1 (Alice) — Alice matches
        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
    }

    #endregion

    #region 12. Aggregate AVG/MIN/MAX in tuple projection (Issue #49)

    [Test]
    public async Task GroupBy_Select_WithAvg()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Avg(o.Total))).Prepare();
        var pg   = Pg.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Avg(o.Total))).Prepare();
        var my   = My.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Avg(o.Total))).Prepare();
        var ss   = Ss.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Avg(o.Total))).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"Status\", AVG(\"Total\") AS \"Item2\" FROM \"orders\" GROUP BY \"Status\"",
            pg:     "SELECT \"Status\", AVG(\"Total\") AS \"Item2\" FROM \"orders\" GROUP BY \"Status\"",
            mysql:  "SELECT `Status`, AVG(\"Total\") AS `Item2` FROM `orders` GROUP BY `Status`",
            ss:     "SELECT [Status], AVG(\"Total\") AS [Item2] FROM [orders] GROUP BY [Status]");

        // Seed: Shipped=(250+150)/2=200, Pending=75.50 — 2 groups
        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GroupBy_Select_WithMin()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Min(o.Total))).Prepare();
        var pg   = Pg.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Min(o.Total))).Prepare();
        var my   = My.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Min(o.Total))).Prepare();
        var ss   = Ss.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Min(o.Total))).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"Status\", MIN(\"Total\") AS \"Item2\" FROM \"orders\" GROUP BY \"Status\"",
            pg:     "SELECT \"Status\", MIN(\"Total\") AS \"Item2\" FROM \"orders\" GROUP BY \"Status\"",
            mysql:  "SELECT `Status`, MIN(\"Total\") AS `Item2` FROM `orders` GROUP BY `Status`",
            ss:     "SELECT [Status], MIN(\"Total\") AS [Item2] FROM [orders] GROUP BY [Status]");

        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GroupBy_Select_WithMax()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Max(o.Total))).Prepare();
        var pg   = Pg.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Max(o.Total))).Prepare();
        var my   = My.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Max(o.Total))).Prepare();
        var ss   = Ss.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Max(o.Total))).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"Status\", MAX(\"Total\") AS \"Item2\" FROM \"orders\" GROUP BY \"Status\"",
            pg:     "SELECT \"Status\", MAX(\"Total\") AS \"Item2\" FROM \"orders\" GROUP BY \"Status\"",
            mysql:  "SELECT `Status`, MAX(\"Total\") AS `Item2` FROM `orders` GROUP BY `Status`",
            ss:     "SELECT [Status], MAX(\"Total\") AS [Item2] FROM [orders] GROUP BY [Status]");

        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GroupBy_Having_Select_AllAggregates()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Orders().Where(o => true).GroupBy(o => o.Status).Having(o => Sql.Count() > 1).Select(o => (o.Status, Sql.Count(), Sql.Sum(o.Total), Sql.Avg(o.Total), Sql.Min(o.Total), Sql.Max(o.Total))).Prepare();
        var pg   = Pg.Orders().Where(o => true).GroupBy(o => o.Status).Having(o => Sql.Count() > 1).Select(o => (o.Status, Sql.Count(), Sql.Sum(o.Total), Sql.Avg(o.Total), Sql.Min(o.Total), Sql.Max(o.Total))).Prepare();
        var my   = My.Orders().Where(o => true).GroupBy(o => o.Status).Having(o => Sql.Count() > 1).Select(o => (o.Status, Sql.Count(), Sql.Sum(o.Total), Sql.Avg(o.Total), Sql.Min(o.Total), Sql.Max(o.Total))).Prepare();
        var ss   = Ss.Orders().Where(o => true).GroupBy(o => o.Status).Having(o => Sql.Count() > 1).Select(o => (o.Status, Sql.Count(), Sql.Sum(o.Total), Sql.Avg(o.Total), Sql.Min(o.Total), Sql.Max(o.Total))).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"Status\", COUNT(*) AS \"Item2\", SUM(\"Total\") AS \"Item3\", AVG(\"Total\") AS \"Item4\", MIN(\"Total\") AS \"Item5\", MAX(\"Total\") AS \"Item6\" FROM \"orders\" GROUP BY \"Status\" HAVING COUNT(*) > 1",
            pg:     "SELECT \"Status\", COUNT(*) AS \"Item2\", SUM(\"Total\") AS \"Item3\", AVG(\"Total\") AS \"Item4\", MIN(\"Total\") AS \"Item5\", MAX(\"Total\") AS \"Item6\" FROM \"orders\" GROUP BY \"Status\" HAVING COUNT(*) > 1",
            mysql:  "SELECT `Status`, COUNT(*) AS `Item2`, SUM(\"Total\") AS `Item3`, AVG(\"Total\") AS `Item4`, MIN(\"Total\") AS `Item5`, MAX(\"Total\") AS `Item6` FROM `orders` GROUP BY `Status` HAVING COUNT(*) > 1",
            ss:     "SELECT [Status], COUNT(*) AS [Item2], SUM(\"Total\") AS [Item3], AVG(\"Total\") AS [Item4], MIN(\"Total\") AS [Item5], MAX(\"Total\") AS [Item6] FROM [orders] GROUP BY [Status] HAVING COUNT(*) > 1");

        // Seed: Shipped has 2 orders (>1), Pending has 1 (not >1) — 1 group
        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Item1, Is.EqualTo("Shipped"));
        Assert.That(results[0].Item2, Is.EqualTo(2));
    }

    #endregion

    #region 14. Static readonly collection (IN clause with inlineable field)

    // Static readonly field with constant initializer - new pipeline inlines these
    private static readonly string[] _runtimeStatuses = new[] { "pending", "processing", "shipped" };

    [Test]
    public async Task Where_ContainsRuntimeCollection()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Orders().Where(o => _runtimeStatuses.Contains(o.Status)).Select(o => (o.OrderId, o.Status)).Prepare();
        var pg   = Pg.Orders().Where(o => _runtimeStatuses.Contains(o.Status)).Select(o => (o.OrderId, o.Status)).Prepare();
        var my   = My.Orders().Where(o => _runtimeStatuses.Contains(o.Status)).Select(o => (o.OrderId, o.Status)).Prepare();
        var ss   = Ss.Orders().Where(o => _runtimeStatuses.Contains(o.Status)).Select(o => (o.OrderId, o.Status)).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", \"Status\" FROM \"orders\" WHERE \"Status\" IN ('pending', 'processing', 'shipped')",
            pg:     "SELECT \"OrderId\", \"Status\" FROM \"orders\" WHERE \"Status\" IN ('pending', 'processing', 'shipped')",
            mysql:  "SELECT `OrderId`, `Status` FROM `orders` WHERE `Status` IN ('pending', 'processing', 'shipped')",
            ss:     "SELECT [OrderId], [Status] FROM [orders] WHERE [Status] IN ('pending', 'processing', 'shipped')");

        // Case-sensitive: seed has "Shipped", "Pending" — lowercase values match 0
        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(0));
    }

    [Test]
    public async Task Where_ContainsRuntimeCollection_DiagnosticParameters()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var prepared = Lite.Orders().Where(o => _runtimeStatuses.Contains(o.Status))
            .Select(o => (o.OrderId, o.Status))
            .Prepare();

        var diag = prepared.ToDiagnostics();

        // Static readonly field with constant initializer is inlined - no runtime parameters
        Assert.That(diag.Parameters, Has.Count.EqualTo(0));

        // Verify the Where clause has the inlined IN values
        var whereClause = diag.Clauses.First(c => c.ClauseType == "Where");
        Assert.That(whereClause.Parameters, Has.Count.EqualTo(0));
        Assert.That(whereClause.SqlFragment, Does.Contain("IN ('pending'"));
    }

    #endregion

    #region 10. Joined OrderBy carrier diagnostics

    [Test]
    public async Task Join_Where_OrderBy_CarrierDiagnostics()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var prepared = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Where((u, o) => o.Total > 100 && u.IsActive)
            .OrderBy((u, o) => o.Total, Direction.Descending)
            .Limit(10)
            .Select((u, o) => (u.UserName, o.Total))
            .Prepare();

        var diag = prepared.ToDiagnostics();

        Assert.That(diag.Sql, Does.Contain("ORDER BY"));
        Assert.That(diag.Sql, Does.Contain("DESC"));
        Assert.That(diag.IsCarrierOptimized, Is.True);
        Assert.That(diag.Tier, Is.EqualTo(DiagnosticOptimizationTier.PrebuiltDispatch));

        // Limit 10 is a literal constant — inlined directly into SQL, no runtime parameter
        Assert.That(diag.Sql, Does.Contain("LIMIT 10"));

        // Verify per-clause diagnostics include OrderBy
        var orderByClause = diag.Clauses.First(c => c.ClauseType == "OrderBy");
        Assert.That(orderByClause.SqlFragment, Does.Contain("Total"));

        // Verify execution
        var results = await prepared.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
    }

    #endregion
}
