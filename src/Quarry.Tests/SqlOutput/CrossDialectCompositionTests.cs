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
internal class CrossDialectCompositionTests : CrossDialectTestBase
{
    #region 1. Join + WHERE + OrderBy(DESC) + LIMIT/OFFSET (join pagination)

    [Test]
    public void Join_Where_OrderBy_Limit_Offset()
    {
        AssertDialects(
            Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => o.Total > 100 && u.IsActive)
                .OrderBy((u, o) => o.Total, Direction.Descending)
                .Limit(10).Offset(0)
                .Select((u, o) => (u.UserName, o.Total, o.Status))
                .ToDiagnostics(),
            Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => o.Total > 100 && u.IsActive)
                .OrderBy((u, o) => o.Total, Direction.Descending)
                .Limit(10).Offset(0)
                .Select((u, o) => (u.UserName, o.Total, o.Status))
                .ToDiagnostics(),
            My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => o.Total > 100 && u.IsActive)
                .OrderBy((u, o) => o.Total, Direction.Descending)
                .Limit(10).Offset(0)
                .Select((u, o) => (u.UserName, o.Total, o.Status))
                .ToDiagnostics(),
            Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => o.Total > 100 && u.IsActive)
                .OrderBy((u, o) => o.Total, Direction.Descending)
                .Limit(10).Offset(0)
                .Select((u, o) => (u.UserName, o.Total, o.Status))
                .ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\", \"t1\".\"Status\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > 100 AND \"t0\".\"IsActive\" ORDER BY \"t1\".\"Total\" DESC LIMIT 10",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\", \"t1\".\"Status\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > 100 AND \"t0\".\"IsActive\" ORDER BY \"t1\".\"Total\" DESC LIMIT 10",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total`, `t1`.`Status` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` WHERE `t1`.`Total` > 100 AND `t0`.`IsActive` ORDER BY `t1`.`Total` DESC LIMIT 10",
            ss:     "SELECT [t0].[UserName], [t1].[Total], [t1].[Status] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] WHERE [t1].[Total] > 100 AND [t0].[IsActive] ORDER BY [t1].[Total] DESC OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY");
    }

    #endregion

    #region 2. Subquery + boolean WHERE + OrderBy + Select

    [Test]
    public void Where_Boolean_Subquery_OrderBy_Select()
    {
        AssertDialects(
            Lite.Users()
                .Where(u => u.IsActive && u.Orders.Any(o => o.Total > 500))
                .OrderBy(u => u.UserName)
                .Select(u => (u.UserName, u.Email))
                .ToDiagnostics(),
            Pg.Users()
                .Where(u => u.IsActive && u.Orders.Any(o => o.Total > 500))
                .OrderBy(u => u.UserName)
                .Select(u => (u.UserName, u.Email))
                .ToDiagnostics(),
            My.Users()
                .Where(u => u.IsActive && u.Orders.Any(o => o.Total > 500))
                .OrderBy(u => u.UserName)
                .Select(u => (u.UserName, u.Email))
                .ToDiagnostics(),
            Ss.Users()
                .Where(u => u.IsActive && u.Orders.Any(o => o.Total > 500))
                .OrderBy(u => u.UserName)
                .Select(u => (u.UserName, u.Email))
                .ToDiagnostics(),
            sqlite: "SELECT \"UserName\", \"Email\" FROM \"users\" WHERE \"IsActive\" AND EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Total\" > 500)) ORDER BY \"UserName\" ASC",
            pg:     "SELECT \"UserName\", \"Email\" FROM \"users\" WHERE \"IsActive\" AND EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Total\" > 500)) ORDER BY \"UserName\" ASC",
            mysql:  "SELECT `UserName`, `Email` FROM `users` WHERE `IsActive` AND EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (`sq0`.`Total` > 500)) ORDER BY `UserName` ASC",
            ss:     "SELECT [UserName], [Email] FROM [users] WHERE [IsActive] AND EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND ([sq0].[Total] > 500)) ORDER BY [UserName] ASC");
    }

    #endregion

    #region 3. Three-table join with WHERE on deepest table

    [Test]
    public void ThreeTableJoin_Where_DeepestTable()
    {
        AssertDialects(
            Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id)
                .Join<OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id)
                .Where((u, o, oi) => oi.UnitPrice > 50.00m)
                .Select((u, o, oi) => (u.UserName, o.Status, oi.ProductName, oi.Quantity))
                .ToDiagnostics(),
            Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id)
                .Join<Pg.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id)
                .Where((u, o, oi) => oi.UnitPrice > 50.00m)
                .Select((u, o, oi) => (u.UserName, o.Status, oi.ProductName, oi.Quantity))
                .ToDiagnostics(),
            My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id)
                .Join<My.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id)
                .Where((u, o, oi) => oi.UnitPrice > 50.00m)
                .Select((u, o, oi) => (u.UserName, o.Status, oi.ProductName, oi.Quantity))
                .ToDiagnostics(),
            Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id)
                .Join<Ss.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id)
                .Where((u, o, oi) => oi.UnitPrice > 50.00m)
                .Select((u, o, oi) => (u.UserName, o.Status, oi.ProductName, oi.Quantity))
                .ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Status\", \"t2\".\"ProductName\", \"t2\".\"Quantity\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" INNER JOIN \"order_items\" AS \"t2\" ON \"t1\".\"OrderId\" = \"t2\".\"OrderId\" WHERE \"t2\".\"UnitPrice\" > 50.00",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Status\", \"t2\".\"ProductName\", \"t2\".\"Quantity\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" INNER JOIN \"order_items\" AS \"t2\" ON \"t1\".\"OrderId\" = \"t2\".\"OrderId\" WHERE \"t2\".\"UnitPrice\" > 50.00",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Status`, `t2`.`ProductName`, `t2`.`Quantity` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` INNER JOIN `order_items` AS `t2` ON `t1`.`OrderId` = `t2`.`OrderId` WHERE `t2`.`UnitPrice` > 50.00",
            ss:     "SELECT [t0].[UserName], [t1].[Status], [t2].[ProductName], [t2].[Quantity] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] INNER JOIN [order_items] AS [t2] ON [t1].[OrderId] = [t2].[OrderId] WHERE [t2].[UnitPrice] > 50.00");
    }

    #endregion

    #region 4. Multiple subqueries in WHERE (Any + All together)

    [Test]
    public void Where_Any_And_All_MultipleSubqueries()
    {
        AssertDialects(
            Lite.Users()
                .Where(u => u.Orders.Any(o => o.Status == "shipped")
                         && u.Orders.All(o => o.Status != "cancelled"))
                .Select(u => (u.UserId, u.UserName))
                .ToDiagnostics(),
            Pg.Users()
                .Where(u => u.Orders.Any(o => o.Status == "shipped")
                         && u.Orders.All(o => o.Status != "cancelled"))
                .Select(u => (u.UserId, u.UserName))
                .ToDiagnostics(),
            My.Users()
                .Where(u => u.Orders.Any(o => o.Status == "shipped")
                         && u.Orders.All(o => o.Status != "cancelled"))
                .Select(u => (u.UserId, u.UserName))
                .ToDiagnostics(),
            Ss.Users()
                .Where(u => u.Orders.Any(o => o.Status == "shipped")
                         && u.Orders.All(o => o.Status != "cancelled"))
                .Select(u => (u.UserId, u.UserName))
                .ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Status\" = 'shipped')) AND NOT EXISTS (SELECT 1 FROM \"orders\" AS \"sq1\" WHERE \"sq1\".\"UserId\" = \"users\".\"UserId\" AND NOT (\"sq1\".\"Status\" <> 'cancelled'))",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Status\" = 'shipped')) AND NOT EXISTS (SELECT 1 FROM \"orders\" AS \"sq1\" WHERE \"sq1\".\"UserId\" = \"users\".\"UserId\" AND NOT (\"sq1\".\"Status\" <> 'cancelled'))",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (`sq0`.`Status` = 'shipped')) AND NOT EXISTS (SELECT 1 FROM `orders` AS `sq1` WHERE `sq1`.`UserId` = `users`.`UserId` AND NOT (`sq1`.`Status` <> 'cancelled'))",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND ([sq0].[Status] = 'shipped')) AND NOT EXISTS (SELECT 1 FROM [orders] AS [sq1] WHERE [sq1].[UserId] = [users].[UserId] AND NOT ([sq1].[Status] <> 'cancelled'))");
    }

    #endregion

    #region 5. String operation + null check + OrderBy(DESC) + LIMIT

    [Test]
    public void Where_NullCheck_Contains_OrderBy_Limit()
    {
        AssertDialects(
            Lite.Users()
                .Where(u => u.Email != null && u.UserName.Contains("john"))
                .OrderBy(u => u.UserName, Direction.Descending)
                .Limit(5)
                .Select(u => (u.UserName, u.Email))
                .ToDiagnostics(),
            Pg.Users()
                .Where(u => u.Email != null && u.UserName.Contains("john"))
                .OrderBy(u => u.UserName, Direction.Descending)
                .Limit(5)
                .Select(u => (u.UserName, u.Email))
                .ToDiagnostics(),
            My.Users()
                .Where(u => u.Email != null && u.UserName.Contains("john"))
                .OrderBy(u => u.UserName, Direction.Descending)
                .Limit(5)
                .Select(u => (u.UserName, u.Email))
                .ToDiagnostics(),
            Ss.Users()
                .Where(u => u.Email != null && u.UserName.Contains("john"))
                .OrderBy(u => u.UserName, Direction.Descending)
                .Limit(5)
                .Select(u => (u.UserName, u.Email))
                .ToDiagnostics(),
            sqlite: "SELECT \"UserName\", \"Email\" FROM \"users\" WHERE \"Email\" IS NOT NULL AND \"UserName\" LIKE '%' || @p0 || '%' ORDER BY \"UserName\" DESC LIMIT 5",
            pg:     "SELECT \"UserName\", \"Email\" FROM \"users\" WHERE \"Email\" IS NOT NULL AND \"UserName\" LIKE '%' || $1 || '%' ORDER BY \"UserName\" DESC LIMIT 5",
            mysql:  "SELECT `UserName`, `Email` FROM `users` WHERE `Email` IS NOT NULL AND `UserName` LIKE CONCAT('%', ?, '%') ORDER BY `UserName` DESC LIMIT 5",
            ss:     "SELECT [UserName], [Email] FROM [users] WHERE [Email] IS NOT NULL AND [UserName] LIKE '%' + @p0 + '%' ORDER BY [UserName] DESC OFFSET 0 ROWS FETCH NEXT 5 ROWS ONLY");
    }

    #endregion

    #region 6. IN clause within a join

    [Test]
    public void Join_Where_InClause()
    {
        var statuses = new[] { "pending", "processing", "shipped" };
        AssertDialects(
            Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => statuses.Contains(o.Status))
                .Select((u, o) => (u.UserName, o.Total))
                .ToDiagnostics(),
            Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => statuses.Contains(o.Status))
                .Select((u, o) => (u.UserName, o.Total))
                .ToDiagnostics(),
            My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => statuses.Contains(o.Status))
                .Select((u, o) => (u.UserName, o.Total))
                .ToDiagnostics(),
            Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => statuses.Contains(o.Status))
                .Select((u, o) => (u.UserName, o.Total))
                .ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Status\" IN ('pending', 'processing', 'shipped')",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Status\" IN ('pending', 'processing', 'shipped')",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` WHERE `t1`.`Status` IN ('pending', 'processing', 'shipped')",
            ss:     "SELECT [t0].[UserName], [t1].[Total] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] WHERE [t1].[Status] IN ('pending', 'processing', 'shipped')");
    }

    #endregion

    #region 7. Enum comparison with COUNT subquery

    [Test]
    public void Where_CountSubquery_WithEnumPredicate()
    {
        AssertDialects(
            Lite.Users()
                .Where(u => u.Orders.Count(o => o.Priority == OrderPriority.Urgent) > 2)
                .Select(u => (u.UserId, u.UserName))
                .ToDiagnostics(),
            Pg.Users()
                .Where(u => u.Orders.Count(o => o.Priority == OrderPriority.Urgent) > 2)
                .Select(u => (u.UserId, u.UserName))
                .ToDiagnostics(),
            My.Users()
                .Where(u => u.Orders.Count(o => o.Priority == OrderPriority.Urgent) > 2)
                .Select(u => (u.UserId, u.UserName))
                .ToDiagnostics(),
            Ss.Users()
                .Where(u => u.Orders.Count(o => o.Priority == OrderPriority.Urgent) > 2)
                .Select(u => (u.UserId, u.UserName))
                .ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE (SELECT COUNT(*) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Priority\" = 3)) > 2",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE (SELECT COUNT(*) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Priority\" = 3)) > 2",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE (SELECT COUNT(*) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (`sq0`.`Priority` = 3)) > 2",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE (SELECT COUNT(*) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND ([sq0].[Priority] = 3)) > 2");
    }

    #endregion

    #region 8. Aggregate with GROUP BY + HAVING

    [Test]
    public void GroupBy_Having_Aggregates()
    {
        AssertDialects(
            Lite.Orders()
                .Where(o => true)
                .GroupBy(o => o.Status)
                .Having(o => Sql.Count() > 5)
                .Select(o => (o.Status, Sql.Count(), Sql.Sum(o.Total)))
                .ToDiagnostics(),
            Pg.Orders()
                .Where(o => true)
                .GroupBy(o => o.Status)
                .Having(o => Sql.Count() > 5)
                .Select(o => (o.Status, Sql.Count(), Sql.Sum(o.Total)))
                .ToDiagnostics(),
            My.Orders()
                .Where(o => true)
                .GroupBy(o => o.Status)
                .Having(o => Sql.Count() > 5)
                .Select(o => (o.Status, Sql.Count(), Sql.Sum(o.Total)))
                .ToDiagnostics(),
            Ss.Orders()
                .Where(o => true)
                .GroupBy(o => o.Status)
                .Having(o => Sql.Count() > 5)
                .Select(o => (o.Status, Sql.Count(), Sql.Sum(o.Total)))
                .ToDiagnostics(),
            sqlite: "SELECT \"Status\", COUNT(*) AS \"Item2\", SUM(\"Total\") AS \"Item3\" FROM \"orders\" GROUP BY \"Status\" HAVING COUNT(*) > 5",
            pg:     "SELECT \"Status\", COUNT(*) AS \"Item2\", SUM(\"Total\") AS \"Item3\" FROM \"orders\" GROUP BY \"Status\" HAVING COUNT(*) > 5",
            mysql:  "SELECT `Status`, COUNT(*) AS `Item2`, SUM(\"Total\") AS `Item3` FROM `orders` GROUP BY `Status` HAVING COUNT(*) > 5",
            ss:     "SELECT [Status], COUNT(*) AS [Item2], SUM(\"Total\") AS [Item3] FROM [orders] GROUP BY [Status] HAVING COUNT(*) > 5");
    }

    #endregion

    #region 9. DISTINCT + Join + OrderBy + LIMIT

    [Test]
    public void Join_Distinct_OrderBy_Limit()
    {
        AssertDialects(
            Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => o.Total > 0)
                .OrderBy((u, o) => o.Total)
                .Distinct()
                .Limit(20)
                .Select((u, o) => u.UserName)
                .ToDiagnostics(),
            Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => o.Total > 0)
                .OrderBy((u, o) => o.Total)
                .Distinct()
                .Limit(20)
                .Select((u, o) => u.UserName)
                .ToDiagnostics(),
            My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => o.Total > 0)
                .OrderBy((u, o) => o.Total)
                .Distinct()
                .Limit(20)
                .Select((u, o) => u.UserName)
                .ToDiagnostics(),
            Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => o.Total > 0)
                .OrderBy((u, o) => o.Total)
                .Distinct()
                .Limit(20)
                .Select((u, o) => u.UserName)
                .ToDiagnostics(),
            sqlite: "SELECT DISTINCT \"t0\".\"UserName\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > 0 ORDER BY \"t1\".\"Total\" ASC LIMIT 20",
            pg:     "SELECT DISTINCT \"t0\".\"UserName\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > 0 ORDER BY \"t1\".\"Total\" ASC LIMIT 20",
            mysql:  "SELECT DISTINCT `t0`.`UserName` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` WHERE `t1`.`Total` > 0 ORDER BY `t1`.`Total` ASC LIMIT 20",
            ss:     "SELECT DISTINCT [t0].[UserName] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] WHERE [t1].[Total] > 0 ORDER BY [t1].[Total] ASC OFFSET 0 ROWS FETCH NEXT 20 ROWS ONLY");
    }

    #endregion

    #region 10. Nested 3-level subquery (Users -> Orders -> OrderItems)

    [Test]
    public void Where_NestedThreeLevelSubquery()
    {
        AssertDialects(
            Lite.Users()
                .Where(u => u.Orders.Any(o => o.Items.Any(i => i.UnitPrice > 100)))
                .Select(u => (u.UserId, u.UserName))
                .ToDiagnostics(),
            Pg.Users()
                .Where(u => u.Orders.Any(o => o.Items.Any(i => i.UnitPrice > 100)))
                .Select(u => (u.UserId, u.UserName))
                .ToDiagnostics(),
            My.Users()
                .Where(u => u.Orders.Any(o => o.Items.Any(i => i.UnitPrice > 100)))
                .Select(u => (u.UserId, u.UserName))
                .ToDiagnostics(),
            Ss.Users()
                .Where(u => u.Orders.Any(o => o.Items.Any(i => i.UnitPrice > 100)))
                .Select(u => (u.UserId, u.UserName))
                .ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (EXISTS (SELECT 1 FROM \"order_items\" AS \"sq1\" WHERE \"sq1\".\"OrderId\" = \"sq0\".\"OrderId\" AND (\"sq1\".\"UnitPrice\" > 100))))",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (EXISTS (SELECT 1 FROM \"order_items\" AS \"sq1\" WHERE \"sq1\".\"OrderId\" = \"sq0\".\"OrderId\" AND (\"sq1\".\"UnitPrice\" > 100))))",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (EXISTS (SELECT 1 FROM `order_items` AS `sq1` WHERE `sq1`.`OrderId` = `sq0`.`OrderId` AND (`sq1`.`UnitPrice` > 100))))",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND (EXISTS (SELECT 1 FROM [order_items] AS [sq1] WHERE [sq1].[OrderId] = [sq0].[OrderId] AND ([sq1].[UnitPrice] > 100))))");
    }

    #endregion

    #region 12. Aggregate AVG/MIN/MAX in tuple projection (Issue #49)

    [Test]
    public void GroupBy_Select_WithAvg()
    {
        AssertDialects(
            Lite.Orders()
                .Where(o => true)
                .GroupBy(o => o.Status)
                .Select(o => (o.Status, Sql.Avg(o.Total)))
                .ToDiagnostics(),
            Pg.Orders()
                .Where(o => true)
                .GroupBy(o => o.Status)
                .Select(o => (o.Status, Sql.Avg(o.Total)))
                .ToDiagnostics(),
            My.Orders()
                .Where(o => true)
                .GroupBy(o => o.Status)
                .Select(o => (o.Status, Sql.Avg(o.Total)))
                .ToDiagnostics(),
            Ss.Orders()
                .Where(o => true)
                .GroupBy(o => o.Status)
                .Select(o => (o.Status, Sql.Avg(o.Total)))
                .ToDiagnostics(),
            sqlite: "SELECT \"Status\", AVG(\"Total\") AS \"Item2\" FROM \"orders\" GROUP BY \"Status\"",
            pg:     "SELECT \"Status\", AVG(\"Total\") AS \"Item2\" FROM \"orders\" GROUP BY \"Status\"",
            mysql:  "SELECT `Status`, AVG(\"Total\") AS `Item2` FROM `orders` GROUP BY `Status`",
            ss:     "SELECT [Status], AVG(\"Total\") AS [Item2] FROM [orders] GROUP BY [Status]");
    }

    [Test]
    public void GroupBy_Select_WithMin()
    {
        AssertDialects(
            Lite.Orders()
                .Where(o => true)
                .GroupBy(o => o.Status)
                .Select(o => (o.Status, Sql.Min(o.Total)))
                .ToDiagnostics(),
            Pg.Orders()
                .Where(o => true)
                .GroupBy(o => o.Status)
                .Select(o => (o.Status, Sql.Min(o.Total)))
                .ToDiagnostics(),
            My.Orders()
                .Where(o => true)
                .GroupBy(o => o.Status)
                .Select(o => (o.Status, Sql.Min(o.Total)))
                .ToDiagnostics(),
            Ss.Orders()
                .Where(o => true)
                .GroupBy(o => o.Status)
                .Select(o => (o.Status, Sql.Min(o.Total)))
                .ToDiagnostics(),
            sqlite: "SELECT \"Status\", MIN(\"Total\") AS \"Item2\" FROM \"orders\" GROUP BY \"Status\"",
            pg:     "SELECT \"Status\", MIN(\"Total\") AS \"Item2\" FROM \"orders\" GROUP BY \"Status\"",
            mysql:  "SELECT `Status`, MIN(\"Total\") AS `Item2` FROM `orders` GROUP BY `Status`",
            ss:     "SELECT [Status], MIN(\"Total\") AS [Item2] FROM [orders] GROUP BY [Status]");
    }

    [Test]
    public void GroupBy_Select_WithMax()
    {
        AssertDialects(
            Lite.Orders()
                .Where(o => true)
                .GroupBy(o => o.Status)
                .Select(o => (o.Status, Sql.Max(o.Total)))
                .ToDiagnostics(),
            Pg.Orders()
                .Where(o => true)
                .GroupBy(o => o.Status)
                .Select(o => (o.Status, Sql.Max(o.Total)))
                .ToDiagnostics(),
            My.Orders()
                .Where(o => true)
                .GroupBy(o => o.Status)
                .Select(o => (o.Status, Sql.Max(o.Total)))
                .ToDiagnostics(),
            Ss.Orders()
                .Where(o => true)
                .GroupBy(o => o.Status)
                .Select(o => (o.Status, Sql.Max(o.Total)))
                .ToDiagnostics(),
            sqlite: "SELECT \"Status\", MAX(\"Total\") AS \"Item2\" FROM \"orders\" GROUP BY \"Status\"",
            pg:     "SELECT \"Status\", MAX(\"Total\") AS \"Item2\" FROM \"orders\" GROUP BY \"Status\"",
            mysql:  "SELECT `Status`, MAX(\"Total\") AS `Item2` FROM `orders` GROUP BY `Status`",
            ss:     "SELECT [Status], MAX(\"Total\") AS [Item2] FROM [orders] GROUP BY [Status]");
    }

    [Test]
    public void GroupBy_Having_Select_AllAggregates()
    {
        AssertDialects(
            Lite.Orders()
                .Where(o => true)
                .GroupBy(o => o.Status)
                .Having(o => Sql.Count() > 1)
                .Select(o => (o.Status, Sql.Count(), Sql.Sum(o.Total), Sql.Avg(o.Total), Sql.Min(o.Total), Sql.Max(o.Total)))
                .ToDiagnostics(),
            Pg.Orders()
                .Where(o => true)
                .GroupBy(o => o.Status)
                .Having(o => Sql.Count() > 1)
                .Select(o => (o.Status, Sql.Count(), Sql.Sum(o.Total), Sql.Avg(o.Total), Sql.Min(o.Total), Sql.Max(o.Total)))
                .ToDiagnostics(),
            My.Orders()
                .Where(o => true)
                .GroupBy(o => o.Status)
                .Having(o => Sql.Count() > 1)
                .Select(o => (o.Status, Sql.Count(), Sql.Sum(o.Total), Sql.Avg(o.Total), Sql.Min(o.Total), Sql.Max(o.Total)))
                .ToDiagnostics(),
            Ss.Orders()
                .Where(o => true)
                .GroupBy(o => o.Status)
                .Having(o => Sql.Count() > 1)
                .Select(o => (o.Status, Sql.Count(), Sql.Sum(o.Total), Sql.Avg(o.Total), Sql.Min(o.Total), Sql.Max(o.Total)))
                .ToDiagnostics(),
            sqlite: "SELECT \"Status\", COUNT(*) AS \"Item2\", SUM(\"Total\") AS \"Item3\", AVG(\"Total\") AS \"Item4\", MIN(\"Total\") AS \"Item5\", MAX(\"Total\") AS \"Item6\" FROM \"orders\" GROUP BY \"Status\" HAVING COUNT(*) > 1",
            pg:     "SELECT \"Status\", COUNT(*) AS \"Item2\", SUM(\"Total\") AS \"Item3\", AVG(\"Total\") AS \"Item4\", MIN(\"Total\") AS \"Item5\", MAX(\"Total\") AS \"Item6\" FROM \"orders\" GROUP BY \"Status\" HAVING COUNT(*) > 1",
            mysql:  "SELECT `Status`, COUNT(*) AS `Item2`, SUM(\"Total\") AS `Item3`, AVG(\"Total\") AS `Item4`, MIN(\"Total\") AS `Item5`, MAX(\"Total\") AS `Item6` FROM `orders` GROUP BY `Status` HAVING COUNT(*) > 1",
            ss:     "SELECT [Status], COUNT(*) AS [Item2], SUM(\"Total\") AS [Item3], AVG(\"Total\") AS [Item4], MIN(\"Total\") AS [Item5], MAX(\"Total\") AS [Item6] FROM [orders] GROUP BY [Status] HAVING COUNT(*) > 1");
    }

    #endregion

    #region 14. Runtime collection parameter (IN clause with non-inlineable collection)

    // Static field — TryResolveVariableCollectionLiterals can't trace field initializers
    private static readonly string[] _runtimeStatuses = new[] { "pending", "processing", "shipped" };

    [Test]
    public void Where_ContainsRuntimeCollection()
    {
        AssertDialects(
            Lite.Orders().Where(o => _runtimeStatuses.Contains(o.Status))
                .Select(o => (o.OrderId, o.Status))
                .ToDiagnostics(),
            Pg.Orders().Where(o => _runtimeStatuses.Contains(o.Status))
                .Select(o => (o.OrderId, o.Status))
                .ToDiagnostics(),
            My.Orders().Where(o => _runtimeStatuses.Contains(o.Status))
                .Select(o => (o.OrderId, o.Status))
                .ToDiagnostics(),
            Ss.Orders().Where(o => _runtimeStatuses.Contains(o.Status))
                .Select(o => (o.OrderId, o.Status))
                .ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", \"Status\" FROM \"orders\" WHERE \"Status\" IN (@p0, @p1, @p2)",
            pg:     "SELECT \"OrderId\", \"Status\" FROM \"orders\" WHERE \"Status\" IN ($1, $2, $3)",
            mysql:  "SELECT `OrderId`, `Status` FROM `orders` WHERE `Status` IN (?, ?, ?)",
            ss:     "SELECT [OrderId], [Status] FROM [orders] WHERE [Status] IN (@p0, @p1, @p2)");
    }

    [Test]
    public void Where_ContainsRuntimeCollection_DiagnosticParameters()
    {
        var diag = Lite.Orders().Where(o => _runtimeStatuses.Contains(o.Status))
            .Select(o => (o.OrderId, o.Status))
            .ToDiagnostics();

        // Verify top-level parameters include expanded collection values
        Assert.That(diag.Parameters, Has.Count.EqualTo(3));
        Assert.That(diag.Parameters[0].Value, Is.EqualTo("pending"));
        Assert.That(diag.Parameters[1].Value, Is.EqualTo("processing"));
        Assert.That(diag.Parameters[2].Value, Is.EqualTo("shipped"));

        // Verify per-clause parameters on the Where clause
        var whereClause = diag.Clauses.First(c => c.ClauseType == "Where");
        Assert.That(whereClause.Parameters, Has.Count.EqualTo(3));
        Assert.That(whereClause.Parameters[0].Value, Is.EqualTo("pending"));
        Assert.That(whereClause.SqlFragment, Does.Contain("@p0"));
    }

    #endregion

    #region 10. Joined OrderBy carrier diagnostics

    [Test]
    public void Join_Where_OrderBy_CarrierDiagnostics()
    {
        var diag = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Where((u, o) => o.Total > 100 && u.IsActive)
            .OrderBy((u, o) => o.Total, Direction.Descending)
            .Limit(10)
            .Select((u, o) => (u.UserName, o.Total))
            .ToDiagnostics();

        Assert.That(diag.Sql, Does.Contain("ORDER BY"));
        Assert.That(diag.Sql, Does.Contain("DESC"));
        Assert.That(diag.IsCarrierOptimized, Is.True);
        Assert.That(diag.Tier, Is.EqualTo(DiagnosticOptimizationTier.PrebuiltDispatch));

        // Verify Limit parameter is present
        Assert.That(diag.Parameters, Has.Count.EqualTo(1));
        Assert.That(diag.Parameters[0].Value, Is.EqualTo(10));

        // Verify per-clause diagnostics include OrderBy
        var orderByClause = diag.Clauses.First(c => c.ClauseType == "OrderBy");
        Assert.That(orderByClause.SqlFragment, Does.Contain("Total"));
    }

    #endregion
}
