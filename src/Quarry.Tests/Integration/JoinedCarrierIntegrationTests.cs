using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.Integration;

/// <summary>
/// Integration tests verifying that joined carrier classes (which now implement interfaces
/// directly via default interface methods instead of inheriting from CarrierBase classes)
/// produce correct SQL across all dialects and execute correctly against a real database.
/// Covers 2-table, 3-table, and 4-table join depths.
/// </summary>
[TestFixture]
internal class JoinedCarrierIntegrationTests
{
    [Test]
    public async Task JoinedCarrier_TwoTable_CrossDialect()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var pg   = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var my   = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var ss   = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId`",
            ss:     "SELECT [t0].[UserName], [t1].[Total] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId]");

        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0], Is.EqualTo(("Alice", 250.00m)));
        Assert.That(results[1], Is.EqualTo(("Alice", 75.50m)));
        Assert.That(results[2], Is.EqualTo(("Bob", 150.00m)));
    }

    [Test]
    public async Task JoinedCarrier_TwoTable_PreJoinWhere_CrossDialect()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Where(u => u.IsActive).Join<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var pg   = Pg.Users().Where(u => u.IsActive).Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var my   = My.Users().Where(u => u.IsActive).Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var ss   = Ss.Users().Where(u => u.IsActive).Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t0\".\"IsActive\" = 1",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t0\".\"IsActive\" = TRUE",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` WHERE `t0`.`IsActive` = 1",
            ss:     "SELECT [t0].[UserName], [t1].[Total] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] WHERE [t0].[IsActive] = 1");

        // Alice (active): orders 250.00 and 75.50; Bob (active): order 150.00; Charlie (inactive): no orders
        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0], Is.EqualTo(("Alice", 250.00m)));
        Assert.That(results[1], Is.EqualTo(("Alice", 75.50m)));
        Assert.That(results[2], Is.EqualTo(("Bob", 150.00m)));
    }

    [Test]
    public async Task JoinedCarrier_TwoTable_WithCapturedWhere_CrossDialect()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var minTotal = 100m;
        var lite = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => o.Total > minTotal).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var pg   = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => o.Total > minTotal).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var my   = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => o.Total > minTotal).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var ss   = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => o.Total > minTotal).Select((u, o) => (u.UserName, o.Total)).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > @p0",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > $1",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` WHERE `t1`.`Total` > ?",
            ss:     "SELECT [t0].[UserName], [t1].[Total] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] WHERE [t1].[Total] > @p0");

        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo(("Alice", 250.00m)));
        Assert.That(results[1], Is.EqualTo(("Bob", 150.00m)));
    }

    [Test]
    public async Task JoinedCarrier_ThreeTable_CrossDialect()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Join<OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Select((u, o, oi) => (u.UserName, o.Total, oi.ProductName)).Prepare();
        var pg   = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Join<Pg.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Select((u, o, oi) => (u.UserName, o.Total, oi.ProductName)).Prepare();
        var my   = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Join<My.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Select((u, o, oi) => (u.UserName, o.Total, oi.ProductName)).Prepare();
        var ss   = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Join<Ss.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Select((u, o, oi) => (u.UserName, o.Total, oi.ProductName)).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\", \"t2\".\"ProductName\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" INNER JOIN \"order_items\" AS \"t2\" ON \"t1\".\"OrderId\" = \"t2\".\"OrderId\"",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\", \"t2\".\"ProductName\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" INNER JOIN \"order_items\" AS \"t2\" ON \"t1\".\"OrderId\" = \"t2\".\"OrderId\"",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total`, `t2`.`ProductName` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` INNER JOIN `order_items` AS `t2` ON `t1`.`OrderId` = `t2`.`OrderId`",
            ss:     "SELECT [t0].[UserName], [t1].[Total], [t2].[ProductName] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] INNER JOIN [order_items] AS [t2] ON [t1].[OrderId] = [t2].[OrderId]");

        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task JoinedCarrier_FourTable_CrossDialect()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Join<OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Join<Account>((u, o, oi, a) => u.UserId == a.UserId.Id).Select((u, o, oi, a) => (u.UserName, o.Total, oi.ProductName, a.AccountName)).Prepare();
        var pg   = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Join<Pg.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Join<Pg.Account>((u, o, oi, a) => u.UserId == a.UserId.Id).Select((u, o, oi, a) => (u.UserName, o.Total, oi.ProductName, a.AccountName)).Prepare();
        var my   = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Join<My.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Join<My.Account>((u, o, oi, a) => u.UserId == a.UserId.Id).Select((u, o, oi, a) => (u.UserName, o.Total, oi.ProductName, a.AccountName)).Prepare();
        var ss   = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Join<Ss.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Join<Ss.Account>((u, o, oi, a) => u.UserId == a.UserId.Id).Select((u, o, oi, a) => (u.UserName, o.Total, oi.ProductName, a.AccountName)).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\", \"t2\".\"ProductName\", \"t3\".\"AccountName\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" INNER JOIN \"order_items\" AS \"t2\" ON \"t1\".\"OrderId\" = \"t2\".\"OrderId\" INNER JOIN \"accounts\" AS \"t3\" ON \"t0\".\"UserId\" = \"t3\".\"UserId\"",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\", \"t2\".\"ProductName\", \"t3\".\"AccountName\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" INNER JOIN \"order_items\" AS \"t2\" ON \"t1\".\"OrderId\" = \"t2\".\"OrderId\" INNER JOIN \"accounts\" AS \"t3\" ON \"t0\".\"UserId\" = \"t3\".\"UserId\"",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total`, `t2`.`ProductName`, `t3`.`AccountName` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` INNER JOIN `order_items` AS `t2` ON `t1`.`OrderId` = `t2`.`OrderId` INNER JOIN `accounts` AS `t3` ON `t0`.`UserId` = `t3`.`UserId`",
            ss:     "SELECT [t0].[UserName], [t1].[Total], [t2].[ProductName], [t3].[AccountName] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] INNER JOIN [order_items] AS [t2] ON [t1].[OrderId] = [t2].[OrderId] INNER JOIN [accounts] AS [t3] ON [t0].[UserId] = [t3].[UserId]");

        // Alice: 2 orders × 2 accounts = 4, Bob: 1 order × 1 account = 1. Total = 5
        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(5));
    }

    [Test]
    public async Task JoinedCarrier_FiveTable_CrossDialect()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Users → Orders → OrderItems → Shipments → Warehouses
        var lite = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Join<OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Join<Shipment>((u, o, oi, s) => o.OrderId == s.OrderId.Id).Join<Warehouse>((u, o, oi, s, w) => s.WarehouseId.Id == w.WarehouseId).Select((u, o, oi, s, w) => (u.UserName, o.Total, oi.ProductName, w.WarehouseName)).Prepare();
        var pg   = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Join<Pg.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Join<Pg.Shipment>((u, o, oi, s) => o.OrderId == s.OrderId.Id).Join<Pg.Warehouse>((u, o, oi, s, w) => s.WarehouseId.Id == w.WarehouseId).Select((u, o, oi, s, w) => (u.UserName, o.Total, oi.ProductName, w.WarehouseName)).Prepare();
        var my   = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Join<My.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Join<My.Shipment>((u, o, oi, s) => o.OrderId == s.OrderId.Id).Join<My.Warehouse>((u, o, oi, s, w) => s.WarehouseId.Id == w.WarehouseId).Select((u, o, oi, s, w) => (u.UserName, o.Total, oi.ProductName, w.WarehouseName)).Prepare();
        var ss   = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Join<Ss.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Join<Ss.Shipment>((u, o, oi, s) => o.OrderId == s.OrderId.Id).Join<Ss.Warehouse>((u, o, oi, s, w) => s.WarehouseId.Id == w.WarehouseId).Select((u, o, oi, s, w) => (u.UserName, o.Total, oi.ProductName, w.WarehouseName)).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\", \"t2\".\"ProductName\", \"t4\".\"WarehouseName\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" INNER JOIN \"order_items\" AS \"t2\" ON \"t1\".\"OrderId\" = \"t2\".\"OrderId\" INNER JOIN \"shipments\" AS \"t3\" ON \"t1\".\"OrderId\" = \"t3\".\"OrderId\" INNER JOIN \"warehouses\" AS \"t4\" ON \"t3\".\"WarehouseId\" = \"t4\".\"WarehouseId\"",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\", \"t2\".\"ProductName\", \"t4\".\"WarehouseName\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" INNER JOIN \"order_items\" AS \"t2\" ON \"t1\".\"OrderId\" = \"t2\".\"OrderId\" INNER JOIN \"shipments\" AS \"t3\" ON \"t1\".\"OrderId\" = \"t3\".\"OrderId\" INNER JOIN \"warehouses\" AS \"t4\" ON \"t3\".\"WarehouseId\" = \"t4\".\"WarehouseId\"",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total`, `t2`.`ProductName`, `t4`.`WarehouseName` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` INNER JOIN `order_items` AS `t2` ON `t1`.`OrderId` = `t2`.`OrderId` INNER JOIN `shipments` AS `t3` ON `t1`.`OrderId` = `t3`.`OrderId` INNER JOIN `warehouses` AS `t4` ON `t3`.`WarehouseId` = `t4`.`WarehouseId`",
            ss:     "SELECT [t0].[UserName], [t1].[Total], [t2].[ProductName], [t4].[WarehouseName] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] INNER JOIN [order_items] AS [t2] ON [t1].[OrderId] = [t2].[OrderId] INNER JOIN [shipments] AS [t3] ON [t1].[OrderId] = [t3].[OrderId] INNER JOIN [warehouses] AS [t4] ON [t3].[WarehouseId] = [t4].[WarehouseId]");

        // Order 1 (Alice, 250.00, Widget, West Coast Hub) + Order 3 (Bob, 150.00, Widget, EU Central)
        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo(("Alice", 250.00m, "Widget", "West Coast Hub")));
        Assert.That(results[1], Is.EqualTo(("Bob", 150.00m, "Widget", "EU Central")));
    }

    [Test]
    public async Task JoinedCarrier_SixTable_CrossDialect()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Users → Orders → OrderItems → Shipments → Warehouses → Accounts (on Users)
        var lite = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Join<OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Join<Shipment>((u, o, oi, s) => o.OrderId == s.OrderId.Id).Join<Warehouse>((u, o, oi, s, w) => s.WarehouseId.Id == w.WarehouseId).Join<Account>((u, o, oi, s, w, a) => u.UserId == a.UserId.Id).Select((u, o, oi, s, w, a) => (u.UserName, o.Total, oi.ProductName, w.WarehouseName, a.AccountName)).Prepare();
        var pg   = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Join<Pg.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Join<Pg.Shipment>((u, o, oi, s) => o.OrderId == s.OrderId.Id).Join<Pg.Warehouse>((u, o, oi, s, w) => s.WarehouseId.Id == w.WarehouseId).Join<Pg.Account>((u, o, oi, s, w, a) => u.UserId == a.UserId.Id).Select((u, o, oi, s, w, a) => (u.UserName, o.Total, oi.ProductName, w.WarehouseName, a.AccountName)).Prepare();
        var my   = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Join<My.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Join<My.Shipment>((u, o, oi, s) => o.OrderId == s.OrderId.Id).Join<My.Warehouse>((u, o, oi, s, w) => s.WarehouseId.Id == w.WarehouseId).Join<My.Account>((u, o, oi, s, w, a) => u.UserId == a.UserId.Id).Select((u, o, oi, s, w, a) => (u.UserName, o.Total, oi.ProductName, w.WarehouseName, a.AccountName)).Prepare();
        var ss   = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Join<Ss.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Join<Ss.Shipment>((u, o, oi, s) => o.OrderId == s.OrderId.Id).Join<Ss.Warehouse>((u, o, oi, s, w) => s.WarehouseId.Id == w.WarehouseId).Join<Ss.Account>((u, o, oi, s, w, a) => u.UserId == a.UserId.Id).Select((u, o, oi, s, w, a) => (u.UserName, o.Total, oi.ProductName, w.WarehouseName, a.AccountName)).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\", \"t2\".\"ProductName\", \"t4\".\"WarehouseName\", \"t5\".\"AccountName\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" INNER JOIN \"order_items\" AS \"t2\" ON \"t1\".\"OrderId\" = \"t2\".\"OrderId\" INNER JOIN \"shipments\" AS \"t3\" ON \"t1\".\"OrderId\" = \"t3\".\"OrderId\" INNER JOIN \"warehouses\" AS \"t4\" ON \"t3\".\"WarehouseId\" = \"t4\".\"WarehouseId\" INNER JOIN \"accounts\" AS \"t5\" ON \"t0\".\"UserId\" = \"t5\".\"UserId\"",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\", \"t2\".\"ProductName\", \"t4\".\"WarehouseName\", \"t5\".\"AccountName\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" INNER JOIN \"order_items\" AS \"t2\" ON \"t1\".\"OrderId\" = \"t2\".\"OrderId\" INNER JOIN \"shipments\" AS \"t3\" ON \"t1\".\"OrderId\" = \"t3\".\"OrderId\" INNER JOIN \"warehouses\" AS \"t4\" ON \"t3\".\"WarehouseId\" = \"t4\".\"WarehouseId\" INNER JOIN \"accounts\" AS \"t5\" ON \"t0\".\"UserId\" = \"t5\".\"UserId\"",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total`, `t2`.`ProductName`, `t4`.`WarehouseName`, `t5`.`AccountName` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` INNER JOIN `order_items` AS `t2` ON `t1`.`OrderId` = `t2`.`OrderId` INNER JOIN `shipments` AS `t3` ON `t1`.`OrderId` = `t3`.`OrderId` INNER JOIN `warehouses` AS `t4` ON `t3`.`WarehouseId` = `t4`.`WarehouseId` INNER JOIN `accounts` AS `t5` ON `t0`.`UserId` = `t5`.`UserId`",
            ss:     "SELECT [t0].[UserName], [t1].[Total], [t2].[ProductName], [t4].[WarehouseName], [t5].[AccountName] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] INNER JOIN [order_items] AS [t2] ON [t1].[OrderId] = [t2].[OrderId] INNER JOIN [shipments] AS [t3] ON [t1].[OrderId] = [t3].[OrderId] INNER JOIN [warehouses] AS [t4] ON [t3].[WarehouseId] = [t4].[WarehouseId] INNER JOIN [accounts] AS [t5] ON [t0].[UserId] = [t5].[UserId]");

        // Alice: 1 order × 1 item × 1 shipment × 1 warehouse × 2 accounts = 2
        // Bob:   1 order × 1 item × 1 shipment × 1 warehouse × 1 account  = 1
        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task JoinedCarrier_ThreeTable_ScalarAggregate_CrossDialect()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Where(u => u.IsActive).Join<Order>((u, o) => u.UserId == o.UserId.Id).Join<OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Select((u, o, oi) => Sql.Count()).Prepare();
        var pg   = Pg.Users().Where(u => u.IsActive).Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Join<Pg.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Select((u, o, oi) => Sql.Count()).Prepare();
        var my   = My.Users().Where(u => u.IsActive).Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Join<My.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Select((u, o, oi) => Sql.Count()).Prepare();
        var ss   = Ss.Users().Where(u => u.IsActive).Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Join<Ss.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id).Select((u, o, oi) => Sql.Count()).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT COUNT(*) FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" INNER JOIN \"order_items\" AS \"t2\" ON \"t1\".\"OrderId\" = \"t2\".\"OrderId\" WHERE \"t0\".\"IsActive\" = 1",
            pg:     "SELECT COUNT(*) FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" INNER JOIN \"order_items\" AS \"t2\" ON \"t1\".\"OrderId\" = \"t2\".\"OrderId\" WHERE \"t0\".\"IsActive\" = TRUE",
            mysql:  "SELECT COUNT(*) FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` INNER JOIN `order_items` AS `t2` ON `t1`.`OrderId` = `t2`.`OrderId` WHERE `t0`.`IsActive` = 1",
            ss:     "SELECT COUNT(*) FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] INNER JOIN [order_items] AS [t2] ON [t1].[OrderId] = [t2].[OrderId] WHERE [t0].[IsActive] = 1");

        // Alice has 2 orders (items 1, 2), Bob has 1 order (item 3) — all active users
        var count = await lite.ExecuteScalarAsync<int>();
        Assert.That(count, Is.EqualTo(3));
    }
}
