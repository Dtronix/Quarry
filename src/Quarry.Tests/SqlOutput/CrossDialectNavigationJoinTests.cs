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
            sqlite: "SELECT \"t0\".\"Total\" FROM \"orders\" AS \"t0\" INNER JOIN \"users\" AS \"j0\" ON \"t0\".\"UserId\" = \"j0\".\"UserId\" WHERE \"j0\".\"IsActive\" = 1",
            pg:     "SELECT \"t0\".\"Total\" FROM \"orders\" AS \"t0\" INNER JOIN \"users\" AS \"j0\" ON \"t0\".\"UserId\" = \"j0\".\"UserId\" WHERE \"j0\".\"IsActive\" = TRUE",
            mysql:  "SELECT `t0`.`Total` FROM `orders` AS `t0` INNER JOIN `users` AS `j0` ON `t0`.`UserId` = `j0`.`UserId` WHERE `j0`.`IsActive` = 1",
            ss:     "SELECT [t0].[Total] FROM [orders] AS [t0] INNER JOIN [users] AS [j0] ON [t0].[UserId] = [j0].[UserId] WHERE [j0].[IsActive] = 1");
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
            sqlite: "SELECT \"t0\".\"Total\" FROM \"orders\" AS \"t0\" INNER JOIN \"users\" AS \"j0\" ON \"t0\".\"UserId\" = \"j0\".\"UserId\" WHERE \"j0\".\"UserName\" = @p0",
            pg:     "SELECT \"t0\".\"Total\" FROM \"orders\" AS \"t0\" INNER JOIN \"users\" AS \"j0\" ON \"t0\".\"UserId\" = \"j0\".\"UserId\" WHERE \"j0\".\"UserName\" = $1",
            mysql:  "SELECT `t0`.`Total` FROM `orders` AS `t0` INNER JOIN `users` AS `j0` ON `t0`.`UserId` = `j0`.`UserId` WHERE `j0`.`UserName` = ?",
            ss:     "SELECT [t0].[Total] FROM [orders] AS [t0] INNER JOIN [users] AS [j0] ON [t0].[UserId] = [j0].[UserId] WHERE [j0].[UserName] = @p0");
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
            sqlite: "SELECT \"t0\".\"Total\" FROM \"orders\" AS \"t0\" INNER JOIN \"users\" AS \"j0\" ON \"t0\".\"UserId\" = \"j0\".\"UserId\" ORDER BY \"j0\".\"UserName\" ASC",
            pg:     "SELECT \"t0\".\"Total\" FROM \"orders\" AS \"t0\" INNER JOIN \"users\" AS \"j0\" ON \"t0\".\"UserId\" = \"j0\".\"UserId\" ORDER BY \"j0\".\"UserName\" ASC",
            mysql:  "SELECT `t0`.`Total` FROM `orders` AS `t0` INNER JOIN `users` AS `j0` ON `t0`.`UserId` = `j0`.`UserId` ORDER BY `j0`.`UserName` ASC",
            ss:     "SELECT [t0].[Total] FROM [orders] AS [t0] INNER JOIN [users] AS [j0] ON [t0].[UserId] = [j0].[UserId] ORDER BY [j0].[UserName] ASC");
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
            sqlite: "SELECT \"t0\".\"ProductName\" FROM \"order_items\" AS \"t0\" INNER JOIN \"orders\" AS \"j0\" ON \"t0\".\"OrderId\" = \"j0\".\"OrderId\" INNER JOIN \"users\" AS \"j1\" ON \"j0\".\"UserId\" = \"j1\".\"UserId\" WHERE \"j1\".\"IsActive\" = 1",
            pg:     "SELECT \"t0\".\"ProductName\" FROM \"order_items\" AS \"t0\" INNER JOIN \"orders\" AS \"j0\" ON \"t0\".\"OrderId\" = \"j0\".\"OrderId\" INNER JOIN \"users\" AS \"j1\" ON \"j0\".\"UserId\" = \"j1\".\"UserId\" WHERE \"j1\".\"IsActive\" = TRUE",
            mysql:  "SELECT `t0`.`ProductName` FROM `order_items` AS `t0` INNER JOIN `orders` AS `j0` ON `t0`.`OrderId` = `j0`.`OrderId` INNER JOIN `users` AS `j1` ON `j0`.`UserId` = `j1`.`UserId` WHERE `j1`.`IsActive` = 1",
            ss:     "SELECT [t0].[ProductName] FROM [order_items] AS [t0] INNER JOIN [orders] AS [j0] ON [t0].[OrderId] = [j0].[OrderId] INNER JOIN [users] AS [j1] ON [j0].[UserId] = [j1].[UserId] WHERE [j1].[IsActive] = 1");
    }

    #endregion

    #region One<T> navigation in Select

    [Test]
    public async Task NavigationJoin_Select_TupleWithNavigation()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Orders().Where(o => o.User!.IsActive)
            .Select(o => (o.OrderId, o.User!.UserName)).ToDiagnostics();
        var pg = Pg.Orders().Where(o => o.User!.IsActive)
            .Select(o => (o.OrderId, o.User!.UserName)).ToDiagnostics();
        var my = My.Orders().Where(o => o.User!.IsActive)
            .Select(o => (o.OrderId, o.User!.UserName)).ToDiagnostics();
        var ss = Ss.Orders().Where(o => o.User!.IsActive)
            .Select(o => (o.OrderId, o.User!.UserName)).ToDiagnostics();

        QueryTestHarness.AssertDialects(
            lt, pg, my, ss,
            sqlite: "SELECT \"t0\".\"OrderId\", \"j0\".\"UserName\" FROM \"orders\" AS \"t0\" INNER JOIN \"users\" AS \"j0\" ON \"t0\".\"UserId\" = \"j0\".\"UserId\" WHERE \"j0\".\"IsActive\" = 1",
            pg:     "SELECT \"t0\".\"OrderId\", \"j0\".\"UserName\" FROM \"orders\" AS \"t0\" INNER JOIN \"users\" AS \"j0\" ON \"t0\".\"UserId\" = \"j0\".\"UserId\" WHERE \"j0\".\"IsActive\" = TRUE",
            mysql:  "SELECT `t0`.`OrderId`, `j0`.`UserName` FROM `orders` AS `t0` INNER JOIN `users` AS `j0` ON `t0`.`UserId` = `j0`.`UserId` WHERE `j0`.`IsActive` = 1",
            ss:     "SELECT [t0].[OrderId], [j0].[UserName] FROM [orders] AS [t0] INNER JOIN [users] AS [j0] ON [t0].[UserId] = [j0].[UserId] WHERE [j0].[IsActive] = 1");
    }

    [Test]
    public async Task NavigationJoin_Select_NavigationOnlyInSelect()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Navigation appears only in Select — the implicit join must still be created
        var lt = Lite.Orders()
            .Select(o => (o.OrderId, o.User!.UserName)).ToDiagnostics();
        var pg = Pg.Orders()
            .Select(o => (o.OrderId, o.User!.UserName)).ToDiagnostics();
        var my = My.Orders()
            .Select(o => (o.OrderId, o.User!.UserName)).ToDiagnostics();
        var ss = Ss.Orders()
            .Select(o => (o.OrderId, o.User!.UserName)).ToDiagnostics();

        QueryTestHarness.AssertDialects(
            lt, pg, my, ss,
            sqlite: "SELECT \"t0\".\"OrderId\", \"j0\".\"UserName\" FROM \"orders\" AS \"t0\" INNER JOIN \"users\" AS \"j0\" ON \"t0\".\"UserId\" = \"j0\".\"UserId\"",
            pg:     "SELECT \"t0\".\"OrderId\", \"j0\".\"UserName\" FROM \"orders\" AS \"t0\" INNER JOIN \"users\" AS \"j0\" ON \"t0\".\"UserId\" = \"j0\".\"UserId\"",
            mysql:  "SELECT `t0`.`OrderId`, `j0`.`UserName` FROM `orders` AS `t0` INNER JOIN `users` AS `j0` ON `t0`.`UserId` = `j0`.`UserId`",
            ss:     "SELECT [t0].[OrderId], [j0].[UserName] FROM [orders] AS [t0] INNER JOIN [users] AS [j0] ON [t0].[UserId] = [j0].[UserId]");
    }

    [Test]
    public async Task NavigationJoin_Select_SingleColumn()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Orders().Select(o => o.User!.UserName).ToDiagnostics();
        var pg = Pg.Orders().Select(o => o.User!.UserName).ToDiagnostics();
        var my = My.Orders().Select(o => o.User!.UserName).ToDiagnostics();
        var ss = Ss.Orders().Select(o => o.User!.UserName).ToDiagnostics();

        QueryTestHarness.AssertDialects(
            lt, pg, my, ss,
            sqlite: "SELECT \"j0\".\"UserName\" FROM \"orders\" AS \"t0\" INNER JOIN \"users\" AS \"j0\" ON \"t0\".\"UserId\" = \"j0\".\"UserId\"",
            pg:     "SELECT \"j0\".\"UserName\" FROM \"orders\" AS \"t0\" INNER JOIN \"users\" AS \"j0\" ON \"t0\".\"UserId\" = \"j0\".\"UserId\"",
            mysql:  "SELECT `j0`.`UserName` FROM `orders` AS `t0` INNER JOIN `users` AS `j0` ON `t0`.`UserId` = `j0`.`UserId`",
            ss:     "SELECT [j0].[UserName] FROM [orders] AS [t0] INNER JOIN [users] AS [j0] ON [t0].[UserId] = [j0].[UserId]");
    }

    [Test]
    public async Task NavigationJoin_Select_DeepChain()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.OrderItems()
            .Select(i => (i.ProductName, i.Order!.User!.UserName)).ToDiagnostics();
        var pg = Pg.OrderItems()
            .Select(i => (i.ProductName, i.Order!.User!.UserName)).ToDiagnostics();
        var my = My.OrderItems()
            .Select(i => (i.ProductName, i.Order!.User!.UserName)).ToDiagnostics();
        var ss = Ss.OrderItems()
            .Select(i => (i.ProductName, i.Order!.User!.UserName)).ToDiagnostics();

        QueryTestHarness.AssertDialects(
            lt, pg, my, ss,
            sqlite: "SELECT \"t0\".\"ProductName\", \"j1\".\"UserName\" FROM \"order_items\" AS \"t0\" INNER JOIN \"orders\" AS \"j0\" ON \"t0\".\"OrderId\" = \"j0\".\"OrderId\" INNER JOIN \"users\" AS \"j1\" ON \"j0\".\"UserId\" = \"j1\".\"UserId\"",
            pg:     "SELECT \"t0\".\"ProductName\", \"j1\".\"UserName\" FROM \"order_items\" AS \"t0\" INNER JOIN \"orders\" AS \"j0\" ON \"t0\".\"OrderId\" = \"j0\".\"OrderId\" INNER JOIN \"users\" AS \"j1\" ON \"j0\".\"UserId\" = \"j1\".\"UserId\"",
            mysql:  "SELECT `t0`.`ProductName`, `j1`.`UserName` FROM `order_items` AS `t0` INNER JOIN `orders` AS `j0` ON `t0`.`OrderId` = `j0`.`OrderId` INNER JOIN `users` AS `j1` ON `j0`.`UserId` = `j1`.`UserId`",
            ss:     "SELECT [t0].[ProductName], [j1].[UserName] FROM [order_items] AS [t0] INNER JOIN [orders] AS [j0] ON [t0].[OrderId] = [j0].[OrderId] INNER JOIN [users] AS [j1] ON [j0].[UserId] = [j1].[UserId]");
    }

    #endregion

    #region Execution verification

    [Test]
    public async Task NavigationJoin_Where_ExecutesCorrectly()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var results = await Lite.Orders().Where(o => o.User!.IsActive)
            .Select(o => (o.OrderId, o.User!.UserName)).Prepare().ExecuteFetchAllAsync();

        // Alice (active) has orders 1, 2; Bob (active) has order 3; Charlie (inactive) excluded
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
        Assert.That(results[1], Is.EqualTo((2, "Alice")));
        Assert.That(results[2], Is.EqualTo((3, "Bob")));

        var pgResults = await Pg.Orders().Where(o => o.User!.IsActive)
            .Select(o => (o.OrderId, o.User!.UserName)).Prepare().ExecuteFetchAllAsync();

        Assert.That(pgResults, Has.Count.EqualTo(3));
        Assert.That(pgResults[0], Is.EqualTo((1, "Alice")));
        Assert.That(pgResults[1], Is.EqualTo((2, "Alice")));
        Assert.That(pgResults[2], Is.EqualTo((3, "Bob")));
    }

    #endregion

    #region LEFT JOIN for nullable FK

    [Test]
    public async Task NavigationJoin_NullableFk_RendersLeftJoin()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // ReturnWarehouseId is Ref<WarehouseSchema, int?> — nullable FK should produce LEFT JOIN
        var lt = Lite.Shipments().Select(s => (s.ShipmentId, s.ReturnWarehouse!.WarehouseName)).ToDiagnostics();
        var pg = Pg.Shipments().Select(s => (s.ShipmentId, s.ReturnWarehouse!.WarehouseName)).ToDiagnostics();
        var my = My.Shipments().Select(s => (s.ShipmentId, s.ReturnWarehouse!.WarehouseName)).ToDiagnostics();
        var ss = Ss.Shipments().Select(s => (s.ShipmentId, s.ReturnWarehouse!.WarehouseName)).ToDiagnostics();

        QueryTestHarness.AssertDialects(
            lt, pg, my, ss,
            sqlite: "SELECT \"t0\".\"ShipmentId\", \"j0\".\"WarehouseName\" FROM \"shipments\" AS \"t0\" LEFT JOIN \"warehouses\" AS \"j0\" ON \"t0\".\"ReturnWarehouseId\" = \"j0\".\"WarehouseId\"",
            pg:     "SELECT \"t0\".\"ShipmentId\", \"j0\".\"WarehouseName\" FROM \"shipments\" AS \"t0\" LEFT JOIN \"warehouses\" AS \"j0\" ON \"t0\".\"ReturnWarehouseId\" = \"j0\".\"WarehouseId\"",
            mysql:  "SELECT `t0`.`ShipmentId`, `j0`.`WarehouseName` FROM `shipments` AS `t0` LEFT JOIN `warehouses` AS `j0` ON `t0`.`ReturnWarehouseId` = `j0`.`WarehouseId`",
            ss:     "SELECT [t0].[ShipmentId], [j0].[WarehouseName] FROM [shipments] AS [t0] LEFT JOIN [warehouses] AS [j0] ON [t0].[ReturnWarehouseId] = [j0].[WarehouseId]");
    }

    #endregion

    #region Navigation dedup

    [Test]
    public async Task NavigationJoin_SameNavInWhereAndOrderBy_SingleJoin()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Same o.User navigation in both Where and OrderBy — should produce only one JOIN
        var lt = Lite.Orders().Where(o => o.User!.IsActive).OrderBy(o => o.User!.UserName).Select(o => o.Total).ToDiagnostics();
        var pg = Pg.Orders().Where(o => o.User!.IsActive).OrderBy(o => o.User!.UserName).Select(o => o.Total).ToDiagnostics();
        var my = My.Orders().Where(o => o.User!.IsActive).OrderBy(o => o.User!.UserName).Select(o => o.Total).ToDiagnostics();
        var ss = Ss.Orders().Where(o => o.User!.IsActive).OrderBy(o => o.User!.UserName).Select(o => o.Total).ToDiagnostics();

        // Only one INNER JOIN — dedup should merge the two navigation accesses
        QueryTestHarness.AssertDialects(
            lt, pg, my, ss,
            sqlite: "SELECT \"t0\".\"Total\" FROM \"orders\" AS \"t0\" INNER JOIN \"users\" AS \"j0\" ON \"t0\".\"UserId\" = \"j0\".\"UserId\" WHERE \"j0\".\"IsActive\" = 1 ORDER BY \"j0\".\"UserName\" ASC",
            pg:     "SELECT \"t0\".\"Total\" FROM \"orders\" AS \"t0\" INNER JOIN \"users\" AS \"j0\" ON \"t0\".\"UserId\" = \"j0\".\"UserId\" WHERE \"j0\".\"IsActive\" = TRUE ORDER BY \"j0\".\"UserName\" ASC",
            mysql:  "SELECT `t0`.`Total` FROM `orders` AS `t0` INNER JOIN `users` AS `j0` ON `t0`.`UserId` = `j0`.`UserId` WHERE `j0`.`IsActive` = 1 ORDER BY `j0`.`UserName` ASC",
            ss:     "SELECT [t0].[Total] FROM [orders] AS [t0] INNER JOIN [users] AS [j0] ON [t0].[UserId] = [j0].[UserId] WHERE [j0].[IsActive] = 1 ORDER BY [j0].[UserName] ASC");
    }

    #endregion

    #region One<T> navigation in GroupBy

    [Test]
    public async Task NavigationJoin_GroupBy_Navigation()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Group orders by user name via navigation
        var lite = Lite.Orders().GroupBy(o => o.User!.UserName).Select(o => (o.User!.UserName, Sql.Count())).Prepare();
        var pg   = Pg.Orders().GroupBy(o => o.User!.UserName).Select(o => (o.User!.UserName, Sql.Count())).Prepare();
        var my   = My.Orders().GroupBy(o => o.User!.UserName).Select(o => (o.User!.UserName, Sql.Count())).Prepare();
        var ss   = Ss.Orders().GroupBy(o => o.User!.UserName).Select(o => (o.User!.UserName, Sql.Count())).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"j0\".\"UserName\", COUNT(*) AS \"Item2\" FROM \"orders\" AS \"t0\" INNER JOIN \"users\" AS \"j0\" ON \"t0\".\"UserId\" = \"j0\".\"UserId\" GROUP BY \"j0\".\"UserName\"",
            pg:     "SELECT \"j0\".\"UserName\", COUNT(*) AS \"Item2\" FROM \"orders\" AS \"t0\" INNER JOIN \"users\" AS \"j0\" ON \"t0\".\"UserId\" = \"j0\".\"UserId\" GROUP BY \"j0\".\"UserName\"",
            mysql:  "SELECT `j0`.`UserName`, COUNT(*) AS `Item2` FROM `orders` AS `t0` INNER JOIN `users` AS `j0` ON `t0`.`UserId` = `j0`.`UserId` GROUP BY `j0`.`UserName`",
            ss:     "SELECT [j0].[UserName], COUNT(*) AS [Item2] FROM [orders] AS [t0] INNER JOIN [users] AS [j0] ON [t0].[UserId] = [j0].[UserId] GROUP BY [j0].[UserName]");

        // Alice: 2 orders, Bob: 1 order
        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo(("Alice", 2)));
        Assert.That(results[1], Is.EqualTo(("Bob", 1)));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0], Is.EqualTo(("Alice", 2)));
        Assert.That(pgResults[1], Is.EqualTo(("Bob", 1)));
    }

    #endregion

    #region One<T> navigation in GroupBy + Having

    [Test]
    public async Task NavigationJoin_GroupByNavigation_WithHaving()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Group by navigated user name, having count > 1
        var lite = Lite.Orders().GroupBy(o => o.User!.UserName).Having(o => Sql.Count() > 1).Select(o => (o.User!.UserName, Sql.Count())).Prepare();
        var pg   = Pg.Orders().GroupBy(o => o.User!.UserName).Having(o => Sql.Count() > 1).Select(o => (o.User!.UserName, Sql.Count())).Prepare();
        var my   = My.Orders().GroupBy(o => o.User!.UserName).Having(o => Sql.Count() > 1).Select(o => (o.User!.UserName, Sql.Count())).Prepare();
        var ss   = Ss.Orders().GroupBy(o => o.User!.UserName).Having(o => Sql.Count() > 1).Select(o => (o.User!.UserName, Sql.Count())).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"j0\".\"UserName\", COUNT(*) AS \"Item2\" FROM \"orders\" AS \"t0\" INNER JOIN \"users\" AS \"j0\" ON \"t0\".\"UserId\" = \"j0\".\"UserId\" GROUP BY \"j0\".\"UserName\" HAVING COUNT(*) > 1",
            pg:     "SELECT \"j0\".\"UserName\", COUNT(*) AS \"Item2\" FROM \"orders\" AS \"t0\" INNER JOIN \"users\" AS \"j0\" ON \"t0\".\"UserId\" = \"j0\".\"UserId\" GROUP BY \"j0\".\"UserName\" HAVING COUNT(*) > 1",
            mysql:  "SELECT `j0`.`UserName`, COUNT(*) AS `Item2` FROM `orders` AS `t0` INNER JOIN `users` AS `j0` ON `t0`.`UserId` = `j0`.`UserId` GROUP BY `j0`.`UserName` HAVING COUNT(*) > 1",
            ss:     "SELECT [j0].[UserName], COUNT(*) AS [Item2] FROM [orders] AS [t0] INNER JOIN [users] AS [j0] ON [t0].[UserId] = [j0].[UserId] GROUP BY [j0].[UserName] HAVING COUNT(*) > 1");

        // Only Alice has > 1 order
        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo(("Alice", 2)));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));
        Assert.That(pgResults[0], Is.EqualTo(("Alice", 2)));
    }

    #endregion

    #region Deep chain execution verification

    [Test]
    public async Task NavigationJoin_DeepChain_ExecutesCorrectly()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, _, _) = t;

        // OrderItem → Order → User (two hops) in Select, executed against SQLite
        var results = await Lite.OrderItems()
            .Select(i => (i.ProductName, i.Order!.User!.UserName)).Prepare().ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0], Is.EqualTo(("Widget", "Alice")));
        Assert.That(results[1], Is.EqualTo(("Gadget", "Alice")));
        Assert.That(results[2], Is.EqualTo(("Widget", "Bob")));

        var pgResults = await Pg.OrderItems()
            .Select(i => (i.ProductName, i.Order!.User!.UserName)).Prepare().ExecuteFetchAllAsync();

        Assert.That(pgResults, Has.Count.EqualTo(3));
        Assert.That(pgResults[0], Is.EqualTo(("Widget", "Alice")));
        Assert.That(pgResults[1], Is.EqualTo(("Gadget", "Alice")));
        Assert.That(pgResults[2], Is.EqualTo(("Widget", "Bob")));
    }

    #endregion
}
