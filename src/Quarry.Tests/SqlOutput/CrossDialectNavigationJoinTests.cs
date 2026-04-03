using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;

[TestFixture]
internal class CrossDialectNavigationJoinTests
{
    #region One<T> navigation in Where

    [Test]
    public async Task NavigationJoin_Where_SingleNavigation()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Orders().Where(o => o.User!.IsActive).Select(o => o.Total).ToDiagnostics();
        var pg = Pg.Orders().Where(o => o.User!.IsActive).Select(o => o.Total).ToDiagnostics();
        var my = My.Orders().Where(o => o.User!.IsActive).Select(o => o.Total).ToDiagnostics();
        var ss = Ss.Orders().Where(o => o.User!.IsActive).Select(o => o.Total).ToDiagnostics();

        QueryTestHarness.AssertDialects(
            lt, pg, my, ss,
            sqlite: "SELECT \"Total\" FROM \"orders\" AS \"t0\" INNER JOIN \"users\" AS \"j0\" ON \"t0\".\"UserId\" = \"j0\".\"UserId\" WHERE \"j0\".\"IsActive\" = 1",
            pg:     "SELECT \"Total\" FROM \"orders\" AS \"t0\" INNER JOIN \"users\" AS \"j0\" ON \"t0\".\"UserId\" = \"j0\".\"UserId\" WHERE \"j0\".\"IsActive\" = TRUE",
            mysql:  "SELECT `Total` FROM `orders` AS `t0` INNER JOIN `users` AS `j0` ON `t0`.`UserId` = `j0`.`UserId` WHERE `j0`.`IsActive` = 1",
            ss:     "SELECT [Total] FROM [orders] AS [t0] INNER JOIN [users] AS [j0] ON [t0].[UserId] = [j0].[UserId] WHERE [j0].[IsActive] = 1");
    }

    [Test]
    public async Task NavigationJoin_Where_WithParameter()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var name = "Alice";
        var lt = Lite.Orders().Where(o => o.User!.UserName == name).Select(o => o.Total).ToDiagnostics();
        var pg = Pg.Orders().Where(o => o.User!.UserName == name).Select(o => o.Total).ToDiagnostics();
        var my = My.Orders().Where(o => o.User!.UserName == name).Select(o => o.Total).ToDiagnostics();
        var ss = Ss.Orders().Where(o => o.User!.UserName == name).Select(o => o.Total).ToDiagnostics();

        QueryTestHarness.AssertDialects(
            lt, pg, my, ss,
            sqlite: "SELECT \"Total\" FROM \"orders\" AS \"t0\" INNER JOIN \"users\" AS \"j0\" ON \"t0\".\"UserId\" = \"j0\".\"UserId\" WHERE \"j0\".\"UserName\" = @p0",
            pg:     "SELECT \"Total\" FROM \"orders\" AS \"t0\" INNER JOIN \"users\" AS \"j0\" ON \"t0\".\"UserId\" = \"j0\".\"UserId\" WHERE \"j0\".\"UserName\" = $1",
            mysql:  "SELECT `Total` FROM `orders` AS `t0` INNER JOIN `users` AS `j0` ON `t0`.`UserId` = `j0`.`UserId` WHERE `j0`.`UserName` = ?",
            ss:     "SELECT [Total] FROM [orders] AS [t0] INNER JOIN [users] AS [j0] ON [t0].[UserId] = [j0].[UserId] WHERE [j0].[UserName] = @p0");
    }

    #endregion

    #region One<T> navigation in OrderBy

    [Test]
    public async Task NavigationJoin_OrderBy_SingleNavigation()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Orders().Select(o => o.Total).OrderBy(o => o.User!.UserName).ToDiagnostics();
        var pg = Pg.Orders().Select(o => o.Total).OrderBy(o => o.User!.UserName).ToDiagnostics();
        var my = My.Orders().Select(o => o.Total).OrderBy(o => o.User!.UserName).ToDiagnostics();
        var ss = Ss.Orders().Select(o => o.Total).OrderBy(o => o.User!.UserName).ToDiagnostics();

        QueryTestHarness.AssertDialects(
            lt, pg, my, ss,
            sqlite: "SELECT \"Total\" FROM \"orders\" AS \"t0\" INNER JOIN \"users\" AS \"j0\" ON \"t0\".\"UserId\" = \"j0\".\"UserId\" ORDER BY \"j0\".\"UserName\" ASC",
            pg:     "SELECT \"Total\" FROM \"orders\" AS \"t0\" INNER JOIN \"users\" AS \"j0\" ON \"t0\".\"UserId\" = \"j0\".\"UserId\" ORDER BY \"j0\".\"UserName\" ASC",
            mysql:  "SELECT `Total` FROM `orders` AS `t0` INNER JOIN `users` AS `j0` ON `t0`.`UserId` = `j0`.`UserId` ORDER BY `j0`.`UserName` ASC",
            ss:     "SELECT [Total] FROM [orders] AS [t0] INNER JOIN [users] AS [j0] ON [t0].[UserId] = [j0].[UserId] ORDER BY [j0].[UserName] ASC");
    }

    #endregion

    #region Deep navigation chain (two levels)

    [Test]
    public async Task NavigationJoin_DeepChain_TwoLevels()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // OrderItem -> Order -> User (two hops) in Where
        var lt = Lite.OrderItems().Where(i => i.Order!.User!.IsActive).Select(i => i.ProductName).ToDiagnostics();
        var pg = Pg.OrderItems().Where(i => i.Order!.User!.IsActive).Select(i => i.ProductName).ToDiagnostics();
        var my = My.OrderItems().Where(i => i.Order!.User!.IsActive).Select(i => i.ProductName).ToDiagnostics();
        var ss = Ss.OrderItems().Where(i => i.Order!.User!.IsActive).Select(i => i.ProductName).ToDiagnostics();

        QueryTestHarness.AssertDialects(
            lt, pg, my, ss,
            sqlite: "SELECT \"ProductName\" FROM \"order_items\" AS \"t0\" INNER JOIN \"orders\" AS \"j0\" ON \"t0\".\"OrderId\" = \"j0\".\"OrderId\" INNER JOIN \"users\" AS \"j1\" ON \"j0\".\"UserId\" = \"j1\".\"UserId\" WHERE \"j1\".\"IsActive\" = 1",
            pg:     "SELECT \"ProductName\" FROM \"order_items\" AS \"t0\" INNER JOIN \"orders\" AS \"j0\" ON \"t0\".\"OrderId\" = \"j0\".\"OrderId\" INNER JOIN \"users\" AS \"j1\" ON \"j0\".\"UserId\" = \"j1\".\"UserId\" WHERE \"j1\".\"IsActive\" = TRUE",
            mysql:  "SELECT `ProductName` FROM `order_items` AS `t0` INNER JOIN `orders` AS `j0` ON `t0`.`OrderId` = `j0`.`OrderId` INNER JOIN `users` AS `j1` ON `j0`.`UserId` = `j1`.`UserId` WHERE `j1`.`IsActive` = 1",
            ss:     "SELECT [ProductName] FROM [order_items] AS [t0] INNER JOIN [orders] AS [j0] ON [t0].[OrderId] = [j0].[OrderId] INNER JOIN [users] AS [j1] ON [j0].[UserId] = [j1].[UserId] WHERE [j1].[IsActive] = 1");
    }

    #endregion
}
