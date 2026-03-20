using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;


[TestFixture]
internal class CrossDialectSubqueryTests : CrossDialectTestBase
{
    #region Any (parameterless)

    [Test]
    public void Where_Any_Parameterless()
    {
        AssertDialects(
            Lite.Users().Where(u => u.Orders.Any()).ToDiagnostics(),
            Pg.Users().Where(u => u.Orders.Any()).ToDiagnostics(),
            My.Users().Where(u => u.Orders.Any()).ToDiagnostics(),
            Ss.Users().Where(u => u.Orders.Any()).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\")",
            pg:     "SELECT * FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\")",
            mysql:  "SELECT * FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`)",
            ss:     "SELECT * FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId])");
    }

    [Test]
    public void Where_NotAny_Parameterless()
    {
        AssertDialects(
            Lite.Users().Where(u => !u.Orders.Any()).ToDiagnostics(),
            Pg.Users().Where(u => !u.Orders.Any()).ToDiagnostics(),
            My.Users().Where(u => !u.Orders.Any()).ToDiagnostics(),
            Ss.Users().Where(u => !u.Orders.Any()).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE NOT (EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\"))",
            pg:     "SELECT * FROM \"users\" WHERE NOT (EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\"))",
            mysql:  "SELECT * FROM `users` WHERE NOT (EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`))",
            ss:     "SELECT * FROM [users] WHERE NOT (EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId]))");
    }

    #endregion

    #region Any (with predicate)

    [Test]
    public void Where_Any_WithPredicate()
    {
        AssertDialects(
            Lite.Users().Where(u => u.Orders.Any(o => o.Total > 100)).ToDiagnostics(),
            Pg.Users().Where(u => u.Orders.Any(o => o.Total > 100)).ToDiagnostics(),
            My.Users().Where(u => u.Orders.Any(o => o.Total > 100)).ToDiagnostics(),
            Ss.Users().Where(u => u.Orders.Any(o => o.Total > 100)).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Total\" > 100))",
            pg:     "SELECT * FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Total\" > 100))",
            mysql:  "SELECT * FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (`sq0`.`Total` > 100))",
            ss:     "SELECT * FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND ([sq0].[Total] > 100))");
    }

    [Test]
    public void Where_Any_WithStringPredicate()
    {
        AssertDialects(
            Lite.Users().Where(u => u.Orders.Any(o => o.Status == "paid")).ToDiagnostics(),
            Pg.Users().Where(u => u.Orders.Any(o => o.Status == "paid")).ToDiagnostics(),
            My.Users().Where(u => u.Orders.Any(o => o.Status == "paid")).ToDiagnostics(),
            Ss.Users().Where(u => u.Orders.Any(o => o.Status == "paid")).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Status\" = 'paid'))",
            pg:     "SELECT * FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Status\" = 'paid'))",
            mysql:  "SELECT * FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (`sq0`.`Status` = 'paid'))",
            ss:     "SELECT * FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND ([sq0].[Status] = 'paid'))");
    }

    #endregion

    #region All

    [Test]
    public void Where_All_WithPredicate()
    {
        AssertDialects(
            Lite.Users().Where(u => u.Orders.All(o => o.Status == "paid")).ToDiagnostics(),
            Pg.Users().Where(u => u.Orders.All(o => o.Status == "paid")).ToDiagnostics(),
            My.Users().Where(u => u.Orders.All(o => o.Status == "paid")).ToDiagnostics(),
            Ss.Users().Where(u => u.Orders.All(o => o.Status == "paid")).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE NOT EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND NOT (\"sq0\".\"Status\" = 'paid'))",
            pg:     "SELECT * FROM \"users\" WHERE NOT EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND NOT (\"sq0\".\"Status\" = 'paid'))",
            mysql:  "SELECT * FROM `users` WHERE NOT EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND NOT (`sq0`.`Status` = 'paid'))",
            ss:     "SELECT * FROM [users] WHERE NOT EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND NOT ([sq0].[Status] = 'paid'))");
    }

    #endregion

    #region Count

    [Test]
    public void Where_Count_GreaterThan()
    {
        AssertDialects(
            Lite.Users().Where(u => u.Orders.Count() > 5).ToDiagnostics(),
            Pg.Users().Where(u => u.Orders.Count() > 5).ToDiagnostics(),
            My.Users().Where(u => u.Orders.Count() > 5).ToDiagnostics(),
            Ss.Users().Where(u => u.Orders.Count() > 5).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE (SELECT COUNT(*) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") > 5",
            pg:     "SELECT * FROM \"users\" WHERE (SELECT COUNT(*) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") > 5",
            mysql:  "SELECT * FROM `users` WHERE (SELECT COUNT(*) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`) > 5",
            ss:     "SELECT * FROM [users] WHERE (SELECT COUNT(*) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId]) > 5");
    }

    [Test]
    public void Where_Count_EqualsZero()
    {
        AssertDialects(
            Lite.Users().Where(u => u.Orders.Count() == 0).ToDiagnostics(),
            Pg.Users().Where(u => u.Orders.Count() == 0).ToDiagnostics(),
            My.Users().Where(u => u.Orders.Count() == 0).ToDiagnostics(),
            Ss.Users().Where(u => u.Orders.Count() == 0).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE (SELECT COUNT(*) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") = 0",
            pg:     "SELECT * FROM \"users\" WHERE (SELECT COUNT(*) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") = 0",
            mysql:  "SELECT * FROM `users` WHERE (SELECT COUNT(*) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`) = 0",
            ss:     "SELECT * FROM [users] WHERE (SELECT COUNT(*) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId]) = 0");
    }

    [Test]
    public void Where_Count_WithPredicate()
    {
        AssertDialects(
            Lite.Users().Where(u => u.Orders.Count(o => o.Total > 100) > 2).ToDiagnostics(),
            Pg.Users().Where(u => u.Orders.Count(o => o.Total > 100) > 2).ToDiagnostics(),
            My.Users().Where(u => u.Orders.Count(o => o.Total > 100) > 2).ToDiagnostics(),
            Ss.Users().Where(u => u.Orders.Count(o => o.Total > 100) > 2).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE (SELECT COUNT(*) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Total\" > 100)) > 2",
            pg:     "SELECT * FROM \"users\" WHERE (SELECT COUNT(*) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Total\" > 100)) > 2",
            mysql:  "SELECT * FROM `users` WHERE (SELECT COUNT(*) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (`sq0`.`Total` > 100)) > 2",
            ss:     "SELECT * FROM [users] WHERE (SELECT COUNT(*) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND ([sq0].[Total] > 100)) > 2");
    }

    [Test]
    public void Where_Count_WithStringPredicate()
    {
        AssertDialects(
            Lite.Users().Where(u => u.Orders.Count(o => o.Status == "paid") >= 1).ToDiagnostics(),
            Pg.Users().Where(u => u.Orders.Count(o => o.Status == "paid") >= 1).ToDiagnostics(),
            My.Users().Where(u => u.Orders.Count(o => o.Status == "paid") >= 1).ToDiagnostics(),
            Ss.Users().Where(u => u.Orders.Count(o => o.Status == "paid") >= 1).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE (SELECT COUNT(*) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Status\" = 'paid')) >= 1",
            pg:     "SELECT * FROM \"users\" WHERE (SELECT COUNT(*) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Status\" = 'paid')) >= 1",
            mysql:  "SELECT * FROM `users` WHERE (SELECT COUNT(*) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (`sq0`.`Status` = 'paid')) >= 1",
            ss:     "SELECT * FROM [users] WHERE (SELECT COUNT(*) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND ([sq0].[Status] = 'paid')) >= 1");
    }

    [Test]
    public void Where_Count_WithCapturedVariable()
    {
        var minTotal = 50m;
        AssertDialects(
            Lite.Users().Where(u => u.Orders.Count(o => o.Total > minTotal) > 0).ToDiagnostics(),
            Pg.Users().Where(u => u.Orders.Count(o => o.Total > minTotal) > 0).ToDiagnostics(),
            My.Users().Where(u => u.Orders.Count(o => o.Total > minTotal) > 0).ToDiagnostics(),
            Ss.Users().Where(u => u.Orders.Count(o => o.Total > minTotal) > 0).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE (SELECT COUNT(*) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Total\" > @p0)) > 0",
            pg:     "SELECT * FROM \"users\" WHERE (SELECT COUNT(*) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Total\" > @p0)) > 0",
            mysql:  "SELECT * FROM `users` WHERE (SELECT COUNT(*) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (`sq0`.`Total` > @p0)) > 0",
            ss:     "SELECT * FROM [users] WHERE (SELECT COUNT(*) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND ([sq0].[Total] > @p0)) > 0");
    }

    #endregion

    #region Nested Subqueries

    [Test]
    public void Where_Nested_Any_Any()
    {
        AssertDialects(
            Lite.Users().Where(u => u.Orders.Any(o => o.Items.Any(i => i.UnitPrice > 50))).ToDiagnostics(),
            Pg.Users().Where(u => u.Orders.Any(o => o.Items.Any(i => i.UnitPrice > 50))).ToDiagnostics(),
            My.Users().Where(u => u.Orders.Any(o => o.Items.Any(i => i.UnitPrice > 50))).ToDiagnostics(),
            Ss.Users().Where(u => u.Orders.Any(o => o.Items.Any(i => i.UnitPrice > 50))).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (EXISTS (SELECT 1 FROM \"order_items\" AS \"sq1\" WHERE \"sq1\".\"OrderId\" = \"sq0\".\"OrderId\" AND (\"sq1\".\"UnitPrice\" > 50))))",
            pg:     "SELECT * FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (EXISTS (SELECT 1 FROM \"order_items\" AS \"sq1\" WHERE \"sq1\".\"OrderId\" = \"sq0\".\"OrderId\" AND (\"sq1\".\"UnitPrice\" > 50))))",
            mysql:  "SELECT * FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (EXISTS (SELECT 1 FROM `order_items` AS `sq1` WHERE `sq1`.`OrderId` = `sq0`.`OrderId` AND (`sq1`.`UnitPrice` > 50))))",
            ss:     "SELECT * FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND (EXISTS (SELECT 1 FROM [order_items] AS [sq1] WHERE [sq1].[OrderId] = [sq0].[OrderId] AND ([sq1].[UnitPrice] > 50))))");
    }

    [Test]
    public void Where_Nested_Any_All()
    {
        AssertDialects(
            Lite.Users().Where(u => u.Orders.Any(o => o.Items.All(i => i.Quantity > 0))).ToDiagnostics(),
            Pg.Users().Where(u => u.Orders.Any(o => o.Items.All(i => i.Quantity > 0))).ToDiagnostics(),
            My.Users().Where(u => u.Orders.Any(o => o.Items.All(i => i.Quantity > 0))).ToDiagnostics(),
            Ss.Users().Where(u => u.Orders.Any(o => o.Items.All(i => i.Quantity > 0))).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (NOT EXISTS (SELECT 1 FROM \"order_items\" AS \"sq1\" WHERE \"sq1\".\"OrderId\" = \"sq0\".\"OrderId\" AND NOT (\"sq1\".\"Quantity\" > 0))))",
            pg:     "SELECT * FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (NOT EXISTS (SELECT 1 FROM \"order_items\" AS \"sq1\" WHERE \"sq1\".\"OrderId\" = \"sq0\".\"OrderId\" AND NOT (\"sq1\".\"Quantity\" > 0))))",
            mysql:  "SELECT * FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (NOT EXISTS (SELECT 1 FROM `order_items` AS `sq1` WHERE `sq1`.`OrderId` = `sq0`.`OrderId` AND NOT (`sq1`.`Quantity` > 0))))",
            ss:     "SELECT * FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND (NOT EXISTS (SELECT 1 FROM [order_items] AS [sq1] WHERE [sq1].[OrderId] = [sq0].[OrderId] AND NOT ([sq1].[Quantity] > 0))))");
    }

    #endregion

    #region Captured Variables in Subquery Predicates

    [Test]
    public void Where_Any_WithCapturedVariable()
    {
        var minAmount = 100m;
        AssertDialects(
            Lite.Users().Where(u => u.Orders.Any(o => o.Total > minAmount)).ToDiagnostics(),
            Pg.Users().Where(u => u.Orders.Any(o => o.Total > minAmount)).ToDiagnostics(),
            My.Users().Where(u => u.Orders.Any(o => o.Total > minAmount)).ToDiagnostics(),
            Ss.Users().Where(u => u.Orders.Any(o => o.Total > minAmount)).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Total\" > @p0))",
            pg:     "SELECT * FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Total\" > @p0))",
            mysql:  "SELECT * FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (`sq0`.`Total` > @p0))",
            ss:     "SELECT * FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND ([sq0].[Total] > @p0))");
    }

    [Test]
    public void Where_All_WithCapturedVariable()
    {
        var status = "paid";
        AssertDialects(
            Lite.Users().Where(u => u.Orders.All(o => o.Status == status)).ToDiagnostics(),
            Pg.Users().Where(u => u.Orders.All(o => o.Status == status)).ToDiagnostics(),
            My.Users().Where(u => u.Orders.All(o => o.Status == status)).ToDiagnostics(),
            Ss.Users().Where(u => u.Orders.All(o => o.Status == status)).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE NOT EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND NOT (\"sq0\".\"Status\" = @p0))",
            pg:     "SELECT * FROM \"users\" WHERE NOT EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND NOT (\"sq0\".\"Status\" = @p0))",
            mysql:  "SELECT * FROM `users` WHERE NOT EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND NOT (`sq0`.`Status` = @p0))",
            ss:     "SELECT * FROM [users] WHERE NOT EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND NOT ([sq0].[Status] = @p0))");
    }

    #endregion

    #region Subquery + Other Clauses

    [Test]
    public void Where_Any_And_Boolean()
    {
        AssertDialects(
            Lite.Users().Where(u => u.IsActive && u.Orders.Any()).ToDiagnostics(),
            Pg.Users().Where(u => u.IsActive && u.Orders.Any()).ToDiagnostics(),
            My.Users().Where(u => u.IsActive && u.Orders.Any()).ToDiagnostics(),
            Ss.Users().Where(u => u.IsActive && u.Orders.Any()).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE \"IsActive\" AND EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\")",
            pg:     "SELECT * FROM \"users\" WHERE \"IsActive\" AND EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\")",
            mysql:  "SELECT * FROM `users` WHERE `IsActive` AND EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`)",
            ss:     "SELECT * FROM [users] WHERE [IsActive] AND EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId])");
    }

    [Test]
    public void Where_Any_ThenSelect_Tuple()
    {
        AssertDialects(
            Lite.Users().Where(u => u.Orders.Any()).Select(u => (u.UserId, u.UserName)).ToDiagnostics(),
            Pg.Users().Where(u => u.Orders.Any()).Select(u => (u.UserId, u.UserName)).ToDiagnostics(),
            My.Users().Where(u => u.Orders.Any()).Select(u => (u.UserId, u.UserName)).ToDiagnostics(),
            Ss.Users().Where(u => u.Orders.Any()).Select(u => (u.UserId, u.UserName)).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\")",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\")",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`)",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId])");
    }

    #endregion

    #region Additional coverage

    [Test]
    public void Where_Any_Or_Boolean()
    {
        AssertDialects(
            Lite.Users().Where(u => u.Orders.Any() || u.IsActive).ToDiagnostics(),
            Pg.Users().Where(u => u.Orders.Any() || u.IsActive).ToDiagnostics(),
            My.Users().Where(u => u.Orders.Any() || u.IsActive).ToDiagnostics(),
            Ss.Users().Where(u => u.Orders.Any() || u.IsActive).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") OR \"IsActive\"",
            pg:     "SELECT * FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") OR \"IsActive\"",
            mysql:  "SELECT * FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`) OR `IsActive`",
            ss:     "SELECT * FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId]) OR [IsActive]");
    }

    [Test]
    public void Where_Multiple_Subqueries_Alias_Monotonicity()
    {
        AssertDialects(
            Lite.Users().Where(u => u.Orders.Any() && u.Orders.Any(o => o.Total > 100)).ToDiagnostics(),
            Pg.Users().Where(u => u.Orders.Any() && u.Orders.Any(o => o.Total > 100)).ToDiagnostics(),
            My.Users().Where(u => u.Orders.Any() && u.Orders.Any(o => o.Total > 100)).ToDiagnostics(),
            Ss.Users().Where(u => u.Orders.Any() && u.Orders.Any(o => o.Total > 100)).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AND EXISTS (SELECT 1 FROM \"orders\" AS \"sq1\" WHERE \"sq1\".\"UserId\" = \"users\".\"UserId\" AND (\"sq1\".\"Total\" > 100))",
            pg:     "SELECT * FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AND EXISTS (SELECT 1 FROM \"orders\" AS \"sq1\" WHERE \"sq1\".\"UserId\" = \"users\".\"UserId\" AND (\"sq1\".\"Total\" > 100))",
            mysql:  "SELECT * FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`) AND EXISTS (SELECT 1 FROM `orders` AS `sq1` WHERE `sq1`.`UserId` = `users`.`UserId` AND (`sq1`.`Total` > 100))",
            ss:     "SELECT * FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId]) AND EXISTS (SELECT 1 FROM [orders] AS [sq1] WHERE [sq1].[UserId] = [users].[UserId] AND ([sq1].[Total] > 100))");
    }

    [Test]
    public void Where_Three_Level_Nesting()
    {
        AssertDialects(
            Lite.Users().Where(u => u.Orders.Any(o => o.Items.Any())).ToDiagnostics(),
            Pg.Users().Where(u => u.Orders.Any(o => o.Items.Any())).ToDiagnostics(),
            My.Users().Where(u => u.Orders.Any(o => o.Items.Any())).ToDiagnostics(),
            Ss.Users().Where(u => u.Orders.Any(o => o.Items.Any())).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (EXISTS (SELECT 1 FROM \"order_items\" AS \"sq1\" WHERE \"sq1\".\"OrderId\" = \"sq0\".\"OrderId\")))",
            pg:     "SELECT * FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (EXISTS (SELECT 1 FROM \"order_items\" AS \"sq1\" WHERE \"sq1\".\"OrderId\" = \"sq0\".\"OrderId\")))",
            mysql:  "SELECT * FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (EXISTS (SELECT 1 FROM `order_items` AS `sq1` WHERE `sq1`.`OrderId` = `sq0`.`OrderId`)))",
            ss:     "SELECT * FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND (EXISTS (SELECT 1 FROM [order_items] AS [sq1] WHERE [sq1].[OrderId] = [sq0].[OrderId])))");
    }

    [Test]
    public void Where_Any_StringPredicate_Contains()
    {
        AssertDialects(
            Lite.Users().Where(u => u.Orders.Any(o => o.Status.Contains("paid"))).ToDiagnostics(),
            Pg.Users().Where(u => u.Orders.Any(o => o.Status.Contains("paid"))).ToDiagnostics(),
            My.Users().Where(u => u.Orders.Any(o => o.Status.Contains("paid"))).ToDiagnostics(),
            Ss.Users().Where(u => u.Orders.Any(o => o.Status.Contains("paid"))).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Status\" LIKE '%' || @p0 || '%'))",
            pg:     "SELECT * FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Status\" LIKE '%' || $1 || '%'))",
            mysql:  "SELECT * FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (`sq0`.`Status` LIKE CONCAT('%', ?, '%')))",
            ss:     "SELECT * FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND ([sq0].[Status] LIKE '%' + @p0 + '%'))");
    }

    [Test]
    public void Where_Any_StringPredicate_StartsWith()
    {
        AssertDialects(
            Lite.Users().Where(u => u.Orders.Any(o => o.Status.StartsWith("p"))).ToDiagnostics(),
            Pg.Users().Where(u => u.Orders.Any(o => o.Status.StartsWith("p"))).ToDiagnostics(),
            My.Users().Where(u => u.Orders.Any(o => o.Status.StartsWith("p"))).ToDiagnostics(),
            Ss.Users().Where(u => u.Orders.Any(o => o.Status.StartsWith("p"))).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Status\" LIKE @p0 || '%'))",
            pg:     "SELECT * FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Status\" LIKE $1 || '%'))",
            mysql:  "SELECT * FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (`sq0`.`Status` LIKE CONCAT(?, '%')))",
            ss:     "SELECT * FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND ([sq0].[Status] LIKE @p0 + '%'))");
    }

    #endregion
}
