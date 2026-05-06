using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;

/// <summary>
/// Cross-dialect SQL coverage for 3+ level nested navigation subqueries.
/// Built atop the Many&lt;TagSchema&gt; navigation added to OrderItemSchema, which
/// enables chains like User.Orders.Items.Tags.Any(...). Verifies the
/// subquery alias allocator (sq0/sq1/sq2), per-level correlation, and
/// dialect-specific identifier quoting at depth.
///
/// Seed (see QueryTestHarness.SeedData / *TestContainer.SeedDataAsync):
///   tag 1: OrderItemId=1, TagName='urgent',  TagValue='P1'
///   tag 2: OrderItemId=1, TagName='fragile', TagValue='yes'
///   tag 3: OrderItemId=2, TagName='urgent',  TagValue='P1'
///   tag 4: OrderItemId=3, TagName='urgent',  TagValue='P1'
///   tag 5: OrderItemId=3, TagName='bulky',   TagValue='yes'
/// OrderItem 2's only tag is 'urgent'; OrderItems 1 and 3 each have at least
/// one non-urgent tag, so .Tags.All(t => t.TagName == 'urgent') is true only
/// for OrderItem 2 (Alice / order 2).
/// </summary>
[TestFixture]
internal class CrossDialectNestedSubqueryTests
{
    #region 3-Level Any/Any/Any

    [Test]
    public async Task Where_ThreeLevel_Any_AllowsDeepCorrelation()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.Orders.Any(o => o.Items.Any(i => i.Tags.Any(tag => tag.TagName == "urgent")))).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Any(o => o.Items.Any(i => i.Tags.Any(tag => tag.TagName == "urgent")))).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => u.Orders.Any(o => o.Items.Any(i => i.Tags.Any(tag => tag.TagName == "urgent")))).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Any(o => o.Items.Any(i => i.Tags.Any(tag => tag.TagName == "urgent")))).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (EXISTS (SELECT 1 FROM \"order_items\" AS \"sq1\" WHERE \"sq1\".\"OrderId\" = \"sq0\".\"OrderId\" AND (EXISTS (SELECT 1 FROM \"tags\" AS \"sq2\" WHERE \"sq2\".\"OrderItemId\" = \"sq1\".\"OrderItemId\" AND (\"sq2\".\"TagName\" = 'urgent'))))))",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (EXISTS (SELECT 1 FROM \"order_items\" AS \"sq1\" WHERE \"sq1\".\"OrderId\" = \"sq0\".\"OrderId\" AND (EXISTS (SELECT 1 FROM \"tags\" AS \"sq2\" WHERE \"sq2\".\"OrderItemId\" = \"sq1\".\"OrderItemId\" AND (\"sq2\".\"TagName\" = 'urgent'))))))",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (EXISTS (SELECT 1 FROM `order_items` AS `sq1` WHERE `sq1`.`OrderId` = `sq0`.`OrderId` AND (EXISTS (SELECT 1 FROM `tags` AS `sq2` WHERE `sq2`.`OrderItemId` = `sq1`.`OrderItemId` AND (`sq2`.`TagName` = 'urgent'))))))",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND (EXISTS (SELECT 1 FROM [order_items] AS [sq1] WHERE [sq1].[OrderId] = [sq0].[OrderId] AND (EXISTS (SELECT 1 FROM [tags] AS [sq2] WHERE [sq2].[OrderItemId] = [sq1].[OrderItemId] AND ([sq2].[TagName] = 'urgent'))))))");

        // Alice (orders 1, 2 — items 1, 2 — items 1, 2 each tagged 'urgent') and Bob
        // (order 3 — item 3 — tagged 'urgent') match. Charlie has no orders.
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
        Assert.That(results[1], Is.EqualTo((2, "Bob")));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Where_ThreeLevel_Any_All_RendersDoubleNotExists()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Outer .Any chains EXISTS down two levels; deepest .All becomes NOT EXISTS (... AND NOT pred).
        // Per llm.md §"Subquery & Aggregate Support": All(x => pred) -> NOT EXISTS (SELECT 1 FROM t WHERE correlation AND NOT pred).
        var lt = Lite.Users().Where(u => u.Orders.Any(o => o.Items.Any(i => i.Tags.All(tag => tag.TagName == "urgent")))).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Any(o => o.Items.Any(i => i.Tags.All(tag => tag.TagName == "urgent")))).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => u.Orders.Any(o => o.Items.Any(i => i.Tags.All(tag => tag.TagName == "urgent")))).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Any(o => o.Items.Any(i => i.Tags.All(tag => tag.TagName == "urgent")))).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (EXISTS (SELECT 1 FROM \"order_items\" AS \"sq1\" WHERE \"sq1\".\"OrderId\" = \"sq0\".\"OrderId\" AND (NOT EXISTS (SELECT 1 FROM \"tags\" AS \"sq2\" WHERE \"sq2\".\"OrderItemId\" = \"sq1\".\"OrderItemId\" AND NOT (\"sq2\".\"TagName\" = 'urgent'))))))",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (EXISTS (SELECT 1 FROM \"order_items\" AS \"sq1\" WHERE \"sq1\".\"OrderId\" = \"sq0\".\"OrderId\" AND (NOT EXISTS (SELECT 1 FROM \"tags\" AS \"sq2\" WHERE \"sq2\".\"OrderItemId\" = \"sq1\".\"OrderItemId\" AND NOT (\"sq2\".\"TagName\" = 'urgent'))))))",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (EXISTS (SELECT 1 FROM `order_items` AS `sq1` WHERE `sq1`.`OrderId` = `sq0`.`OrderId` AND (NOT EXISTS (SELECT 1 FROM `tags` AS `sq2` WHERE `sq2`.`OrderItemId` = `sq1`.`OrderItemId` AND NOT (`sq2`.`TagName` = 'urgent'))))))",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND (EXISTS (SELECT 1 FROM [order_items] AS [sq1] WHERE [sq1].[OrderId] = [sq0].[OrderId] AND (NOT EXISTS (SELECT 1 FROM [tags] AS [sq2] WHERE [sq2].[OrderItemId] = [sq1].[OrderItemId] AND NOT ([sq2].[TagName] = 'urgent'))))))");

        // Only OrderItem 2 has all tags 'urgent' (just one tag, 'urgent'). OrderItem 2 belongs
        // to order 2 (Alice). No other user qualifies — Bob's OrderItem 3 has tag 'bulky'.
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));
        Assert.That(pgResults[0], Is.EqualTo((1, "Alice")));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(1));
        Assert.That(myResults[0], Is.EqualTo((1, "Alice")));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(1));
        Assert.That(ssResults[0], Is.EqualTo((1, "Alice")));
    }

    [Test]
    public async Task Where_TwoLevel_Any_Sum_MixedAggregate()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Outer .Any over Orders, inner subquery uses .Sum on Items.UnitPrice. Two scalar/exists
        // subqueries get separate aliases (sq0 outer EXISTS, sq1 inner SUM).
        var lt = Lite.Users().Where(u => u.Orders.Any(o => o.Items.Sum(i => i.UnitPrice) > 100m)).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Any(o => o.Items.Sum(i => i.UnitPrice) > 100m)).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => u.Orders.Any(o => o.Items.Sum(i => i.UnitPrice) > 100m)).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Any(o => o.Items.Sum(i => i.UnitPrice) > 100m)).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND ((SELECT SUM(\"sq1\".\"UnitPrice\") FROM \"order_items\" AS \"sq1\" WHERE \"sq1\".\"OrderId\" = \"sq0\".\"OrderId\") > 100))",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND ((SELECT SUM(\"sq1\".\"UnitPrice\") FROM \"order_items\" AS \"sq1\" WHERE \"sq1\".\"OrderId\" = \"sq0\".\"OrderId\") > 100))",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND ((SELECT SUM(`sq1`.`UnitPrice`) FROM `order_items` AS `sq1` WHERE `sq1`.`OrderId` = `sq0`.`OrderId`) > 100))",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND ((SELECT SUM([sq1].[UnitPrice]) FROM [order_items] AS [sq1] WHERE [sq1].[OrderId] = [sq0].[OrderId]) > 100))");

        // Order 1: Item 1 unit price 125 > 100 ✓ (Alice)
        // Order 2: Item 2 unit price 75.50 ✗
        // Order 3: Item 3 unit price 50 ✗ (Bob — but Bob has only this order, fails)
        // Charlie no orders.
        // Result: Alice only.
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(1));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Where_ThreeLevel_CapturedParam_PropagatesToInnerSubquery()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var capturedTag = "P1";

        var lt = Lite.Users().Where(u => u.Orders.Any(o => o.Items.Any(i => i.Tags.Any(tag => tag.TagValue == capturedTag)))).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Orders.Any(o => o.Items.Any(i => i.Tags.Any(tag => tag.TagValue == capturedTag)))).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => u.Orders.Any(o => o.Items.Any(i => i.Tags.Any(tag => tag.TagValue == capturedTag)))).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => u.Orders.Any(o => o.Items.Any(i => i.Tags.Any(tag => tag.TagValue == capturedTag)))).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (EXISTS (SELECT 1 FROM \"order_items\" AS \"sq1\" WHERE \"sq1\".\"OrderId\" = \"sq0\".\"OrderId\" AND (EXISTS (SELECT 1 FROM \"tags\" AS \"sq2\" WHERE \"sq2\".\"OrderItemId\" = \"sq1\".\"OrderItemId\" AND (\"sq2\".\"TagValue\" = @p0))))))",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (EXISTS (SELECT 1 FROM \"order_items\" AS \"sq1\" WHERE \"sq1\".\"OrderId\" = \"sq0\".\"OrderId\" AND (EXISTS (SELECT 1 FROM \"tags\" AS \"sq2\" WHERE \"sq2\".\"OrderItemId\" = \"sq1\".\"OrderItemId\" AND (\"sq2\".\"TagValue\" = $1))))))",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE EXISTS (SELECT 1 FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (EXISTS (SELECT 1 FROM `order_items` AS `sq1` WHERE `sq1`.`OrderId` = `sq0`.`OrderId` AND (EXISTS (SELECT 1 FROM `tags` AS `sq2` WHERE `sq2`.`OrderItemId` = `sq1`.`OrderItemId` AND (`sq2`.`TagValue` = ?))))))",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE EXISTS (SELECT 1 FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND (EXISTS (SELECT 1 FROM [order_items] AS [sq1] WHERE [sq1].[OrderId] = [sq0].[OrderId] AND (EXISTS (SELECT 1 FROM [tags] AS [sq2] WHERE [sq2].[OrderItemId] = [sq1].[OrderItemId] AND ([sq2].[TagValue] = @p0))))))");

        // P1 tags exist on items 1, 2, 3 — covers Alice (orders 1, 2) and Bob (order 3).
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Select_TwoSiblingProjectionSubqueries_AliasReusesPerColumn()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Two projection-side subqueries: Sum of order totals, Count of orders.
        // Each top-level projection scalar subquery has its own alias namespace —
        // both reuse "sq0" because they are sibling top-level subqueries (not nested).
        var lt = Lite.Users()
            .Where(u => u.IsActive)
            .Select(u => (u.UserName, OrderTotal: u.Orders.Sum(o => o.Total), OrderCount: u.Orders.Count()))
            .OrderBy(u => u.UserId)
            .Prepare();
        var pg = Pg.Users()
            .Where(u => u.IsActive)
            .Select(u => (u.UserName, OrderTotal: u.Orders.Sum(o => o.Total), OrderCount: u.Orders.Count()))
            .OrderBy(u => u.UserId)
            .Prepare();
        var my = My.Users()
            .Where(u => u.IsActive)
            .Select(u => (u.UserName, OrderTotal: u.Orders.Sum(o => o.Total), OrderCount: u.Orders.Count()))
            .OrderBy(u => u.UserId)
            .Prepare();
        var ss = Ss.Users()
            .Where(u => u.IsActive)
            .Select(u => (u.UserName, OrderTotal: u.Orders.Sum(o => o.Total), OrderCount: u.Orders.Count()))
            .OrderBy(u => u.UserId)
            .Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserName\", (SELECT SUM(\"sq0\".\"Total\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"OrderTotal\", (SELECT COUNT(*) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"OrderCount\" FROM \"users\" WHERE \"IsActive\" = 1 ORDER BY \"UserId\" ASC",
            pg:     "SELECT \"UserName\", (SELECT SUM(\"sq0\".\"Total\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"OrderTotal\", (SELECT COUNT(*) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"OrderCount\" FROM \"users\" WHERE \"IsActive\" = TRUE ORDER BY \"UserId\" ASC",
            mysql:  "SELECT `UserName`, (SELECT SUM(`sq0`.`Total`) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`) AS `OrderTotal`, (SELECT COUNT(*) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`) AS `OrderCount` FROM `users` WHERE `IsActive` = 1 ORDER BY `UserId` ASC",
            ss:     "SELECT [UserName], (SELECT SUM([sq0].[Total]) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId]) AS [OrderTotal], (SELECT COUNT(*) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId]) AS [OrderCount] FROM [users] WHERE [IsActive] = 1 ORDER BY [UserId] ASC");

        // Active users: Alice (1, total 325.50, 2 orders), Bob (2, total 150.00, 1 order). Charlie inactive.
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
        Assert.That(results[0].OrderTotal, Is.EqualTo(325.50m));
        Assert.That(results[0].OrderCount, Is.EqualTo(2));
        Assert.That(results[1].UserName, Is.EqualTo("Bob"));
        Assert.That(results[1].OrderTotal, Is.EqualTo(150.00m));
        Assert.That(results[1].OrderCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Select_ProjectionMixedNestingDepths_OrderTotalAndItemTotal()
    {
        // Two simultaneous projection-side subqueries with mixed nesting depths:
        //   - OrderTotal: 1-level Sum over u.Orders
        //   - ItemTotal:  2-level Sum/Sum traversal u.Orders → o.Items.LineTotal
        // Each top-level projection scalar subquery owns its own alias namespace,
        // so both start at sq0; the nested one extends to sq1 inside its own
        // tree. Closes the deep-projection-side gap that the sibling 1-level
        // test (Select_TwoSiblingProjectionSubqueries_AliasReusesPerColumn) does
        // not exercise. The plan originally called for a 3-level Sum/Sum/Count
        // (Orders → Items → Tags), but the generator projection-type resolver
        // does not propagate `int` through nested aggregates (it resolves nested
        // Sum<int>/Count() as decimal, so interceptor signatures mismatch with
        // CS9144). Holding ItemTotal at decimal sidesteps that resolver gap;
        // tracked separately in #294.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users()
            .Where(u => u.IsActive)
            .Select(u => (
                u.UserName,
                OrderTotal: u.Orders.Sum(o => o.Total),
                ItemTotal: u.Orders.Sum(o => o.Items.Sum(i => i.LineTotal))))
            .OrderBy(u => u.UserId)
            .Prepare();
        var pg = Pg.Users()
            .Where(u => u.IsActive)
            .Select(u => (
                u.UserName,
                OrderTotal: u.Orders.Sum(o => o.Total),
                ItemTotal: u.Orders.Sum(o => o.Items.Sum(i => i.LineTotal))))
            .OrderBy(u => u.UserId)
            .Prepare();
        var my = My.Users()
            .Where(u => u.IsActive)
            .Select(u => (
                u.UserName,
                OrderTotal: u.Orders.Sum(o => o.Total),
                ItemTotal: u.Orders.Sum(o => o.Items.Sum(i => i.LineTotal))))
            .OrderBy(u => u.UserId)
            .Prepare();
        var ss = Ss.Users()
            .Where(u => u.IsActive)
            .Select(u => (
                u.UserName,
                OrderTotal: u.Orders.Sum(o => o.Total),
                ItemTotal: u.Orders.Sum(o => o.Items.Sum(i => i.LineTotal))))
            .OrderBy(u => u.UserId)
            .Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserName\", (SELECT SUM(\"sq0\".\"Total\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"OrderTotal\", (SELECT SUM((SELECT SUM(\"sq1\".\"LineTotal\") FROM \"order_items\" AS \"sq1\" WHERE \"sq1\".\"OrderId\" = \"sq0\".\"OrderId\")) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"ItemTotal\" FROM \"users\" WHERE \"IsActive\" = 1 ORDER BY \"UserId\" ASC",
            pg:     "SELECT \"UserName\", (SELECT SUM(\"sq0\".\"Total\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"OrderTotal\", (SELECT SUM((SELECT SUM(\"sq1\".\"LineTotal\") FROM \"order_items\" AS \"sq1\" WHERE \"sq1\".\"OrderId\" = \"sq0\".\"OrderId\")) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"ItemTotal\" FROM \"users\" WHERE \"IsActive\" = TRUE ORDER BY \"UserId\" ASC",
            mysql:  "SELECT `UserName`, (SELECT SUM(`sq0`.`Total`) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`) AS `OrderTotal`, (SELECT SUM((SELECT SUM(`sq1`.`LineTotal`) FROM `order_items` AS `sq1` WHERE `sq1`.`OrderId` = `sq0`.`OrderId`)) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`) AS `ItemTotal` FROM `users` WHERE `IsActive` = 1 ORDER BY `UserId` ASC",
            ss:     "SELECT [UserName], (SELECT SUM([sq0].[Total]) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId]) AS [OrderTotal], (SELECT SUM((SELECT SUM([sq1].[LineTotal]) FROM [order_items] AS [sq1] WHERE [sq1].[OrderId] = [sq0].[OrderId])) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId]) AS [ItemTotal] FROM [users] WHERE [IsActive] = 1 ORDER BY [UserId] ASC");

        // Active users:
        //   Alice  → orders 1+2 (Total=250+75.50=325.50; Items LineTotal: 250+75.50=325.50)
        //   Bob    → order 3    (Total=150;             Items LineTotal: 150)
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
        Assert.That(results[0].OrderTotal, Is.EqualTo(325.50m));
        Assert.That(results[0].ItemTotal, Is.EqualTo(325.50m));
        Assert.That(results[1].UserName, Is.EqualTo("Bob"));
        Assert.That(results[1].OrderTotal, Is.EqualTo(150.00m));
        Assert.That(results[1].ItemTotal, Is.EqualTo(150.00m));
    }

    #endregion
}
