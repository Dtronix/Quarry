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

        // No top-level ORDER BY, so the real providers are asserted as an unordered
        // collection rather than positionally.
        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults, Is.EquivalentTo(new[] { (1, "Alice"), (2, "Bob") }));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults, Is.EquivalentTo(new[] { (1, "Alice"), (2, "Bob") }));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(2));
        Assert.That(ssResults, Is.EquivalentTo(new[] { (1, "Alice"), (2, "Bob") }));
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
        Assert.That(pgResults[0], Is.EqualTo((1, "Alice")));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(1));
        Assert.That(myResults[0], Is.EqualTo((1, "Alice")));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(1));
        Assert.That(ssResults[0], Is.EqualTo((1, "Alice")));
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
        // No top-level ORDER BY, so row values are asserted as an unordered collection.
        // This also pins the captured @p0 binding: a wrong/unbound TagValue would match
        // a different user set, which a bare count check cannot see.
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results, Is.EquivalentTo(new[] { (1, "Alice"), (2, "Bob") }));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults, Is.EquivalentTo(new[] { (1, "Alice"), (2, "Bob") }));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults, Is.EquivalentTo(new[] { (1, "Alice"), (2, "Bob") }));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(2));
        Assert.That(ssResults, Is.EquivalentTo(new[] { (1, "Alice"), (2, "Bob") }));
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
    public async Task Select_ProjectionNestedSumCount_TwoLevel_ItemCountPerUser()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // 2-level nested int aggregate: Sum over Orders of Items.Count(). Inner Count
        // returns int; the outer Sum binds to Many<T>.Sum(Func<T, int>) -> int. Before
        // the #294 fix, the generator inferred the projection element as decimal,
        // mismatching the user-tuple's int element and producing CS9144 at compile time.
        var lt = Lite.Users()
            .Where(u => u.IsActive)
            .Select(u => (u.UserName, ItemCount: u.Orders.Sum(o => o.Items.Count())))
            .OrderBy(u => u.UserId)
            .Prepare();
        var pg = Pg.Users()
            .Where(u => u.IsActive)
            .Select(u => (u.UserName, ItemCount: u.Orders.Sum(o => o.Items.Count())))
            .OrderBy(u => u.UserId)
            .Prepare();
        var my = My.Users()
            .Where(u => u.IsActive)
            .Select(u => (u.UserName, ItemCount: u.Orders.Sum(o => o.Items.Count())))
            .OrderBy(u => u.UserId)
            .Prepare();
        var ss = Ss.Users()
            .Where(u => u.IsActive)
            .Select(u => (u.UserName, ItemCount: u.Orders.Sum(o => o.Items.Count())))
            .OrderBy(u => u.UserId)
            .Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserName\", (SELECT SUM((SELECT COUNT(*) FROM \"order_items\" AS \"sq1\" WHERE \"sq1\".\"OrderId\" = \"sq0\".\"OrderId\")) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"ItemCount\" FROM \"users\" WHERE \"IsActive\" = 1 ORDER BY \"UserId\" ASC",
            pg:     "SELECT \"UserName\", (SELECT SUM((SELECT COUNT(*) FROM \"order_items\" AS \"sq1\" WHERE \"sq1\".\"OrderId\" = \"sq0\".\"OrderId\")) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"ItemCount\" FROM \"users\" WHERE \"IsActive\" = TRUE ORDER BY \"UserId\" ASC",
            mysql:  "SELECT `UserName`, (SELECT SUM((SELECT COUNT(*) FROM `order_items` AS `sq1` WHERE `sq1`.`OrderId` = `sq0`.`OrderId`)) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`) AS `ItemCount` FROM `users` WHERE `IsActive` = 1 ORDER BY `UserId` ASC",
            ss:     "SELECT [UserName], (SELECT SUM((SELECT COUNT(*) FROM [order_items] AS [sq1] WHERE [sq1].[OrderId] = [sq0].[OrderId])) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId]) AS [ItemCount] FROM [users] WHERE [IsActive] = 1 ORDER BY [UserId] ASC");

        // Active users:
        //   Alice → orders 1, 2 → items 1 (in order 1), 2 (in order 2). Count per order: 1, 1. Outer sum = 2.
        //   Bob   → order 3 → item 3. Count per order: 1. Outer sum = 1.
        // Execution: SQLite, Postgres, MySQL. SQL Server rejects the resulting
        // SUM((SELECT COUNT(*) ...)) shape with "Cannot perform an aggregate
        // function on an expression containing an aggregate or a subquery" — a
        // platform-level constraint, not a generator/runtime issue. SQL string
        // is still asserted for all four dialects via AssertDialects above.
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
        Assert.That(results[0].ItemCount, Is.EqualTo(2));
        Assert.That(results[1].UserName, Is.EqualTo("Bob"));
        Assert.That(results[1].ItemCount, Is.EqualTo(1));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0].ItemCount, Is.EqualTo(2));
        Assert.That(pgResults[1].ItemCount, Is.EqualTo(1));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0].ItemCount, Is.EqualTo(2));
        Assert.That(myResults[1].ItemCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Select_ProjectionNestedSumSum_TwoLevel_QuantityPerUser()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // 2-level nested int Sum-of-Sum: outer Sum over Orders of inner Sum of Items.Quantity.
        // Quantity is Col<int>; inner Sum binds to Many<T>.Sum(Func<T, int>) -> int, and the
        // outer Sum binds to the same int overload. Confirms the #294 fix propagates the
        // inner ColumnRef-int type through the outer aggregate (not just through Count).
        var lt = Lite.Users()
            .Where(u => u.IsActive)
            .Select(u => (u.UserName, QuantityTotal: u.Orders.Sum(o => o.Items.Sum(i => i.Quantity))))
            .OrderBy(u => u.UserId)
            .Prepare();
        var pg = Pg.Users()
            .Where(u => u.IsActive)
            .Select(u => (u.UserName, QuantityTotal: u.Orders.Sum(o => o.Items.Sum(i => i.Quantity))))
            .OrderBy(u => u.UserId)
            .Prepare();
        var my = My.Users()
            .Where(u => u.IsActive)
            .Select(u => (u.UserName, QuantityTotal: u.Orders.Sum(o => o.Items.Sum(i => i.Quantity))))
            .OrderBy(u => u.UserId)
            .Prepare();
        var ss = Ss.Users()
            .Where(u => u.IsActive)
            .Select(u => (u.UserName, QuantityTotal: u.Orders.Sum(o => o.Items.Sum(i => i.Quantity))))
            .OrderBy(u => u.UserId)
            .Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserName\", (SELECT SUM((SELECT SUM(\"sq1\".\"Quantity\") FROM \"order_items\" AS \"sq1\" WHERE \"sq1\".\"OrderId\" = \"sq0\".\"OrderId\")) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"QuantityTotal\" FROM \"users\" WHERE \"IsActive\" = 1 ORDER BY \"UserId\" ASC",
            pg:     "SELECT \"UserName\", (SELECT SUM((SELECT SUM(\"sq1\".\"Quantity\") FROM \"order_items\" AS \"sq1\" WHERE \"sq1\".\"OrderId\" = \"sq0\".\"OrderId\")) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"QuantityTotal\" FROM \"users\" WHERE \"IsActive\" = TRUE ORDER BY \"UserId\" ASC",
            mysql:  "SELECT `UserName`, (SELECT SUM((SELECT SUM(`sq1`.`Quantity`) FROM `order_items` AS `sq1` WHERE `sq1`.`OrderId` = `sq0`.`OrderId`)) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`) AS `QuantityTotal` FROM `users` WHERE `IsActive` = 1 ORDER BY `UserId` ASC",
            ss:     "SELECT [UserName], (SELECT SUM((SELECT SUM([sq1].[Quantity]) FROM [order_items] AS [sq1] WHERE [sq1].[OrderId] = [sq0].[OrderId])) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId]) AS [QuantityTotal] FROM [users] WHERE [IsActive] = 1 ORDER BY [UserId] ASC");

        // Active users:
        //   Alice → order 1 → item 1 qty 2; order 2 → item 2 qty 1. Outer = 2 + 1 = 3.
        //   Bob   → order 3 → item 3 qty 3. Outer = 3.
        // Execution: SQLite, Postgres, MySQL. SQL Server rejects nested
        // aggregate-in-aggregate (see SumCount sibling test for details).
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
        Assert.That(results[0].QuantityTotal, Is.EqualTo(3));
        Assert.That(results[1].UserName, Is.EqualTo("Bob"));
        Assert.That(results[1].QuantityTotal, Is.EqualTo(3));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0].QuantityTotal, Is.EqualTo(3));
        Assert.That(pgResults[1].QuantityTotal, Is.EqualTo(3));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0].QuantityTotal, Is.EqualTo(3));
        Assert.That(myResults[1].QuantityTotal, Is.EqualTo(3));
    }

    [Test]
    public async Task Select_ProjectionMixedSumIntCountDecimal_SiblingColumns()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Sibling projection columns with different CLR types: an int nested Sum-of-Count
        // alongside a decimal Sum over Orders.Total. Both subqueries are top-level scalar
        // subqueries, so each owns its own alias namespace (both start at sq0). Confirms
        // the #294 fix doesn't disturb sibling decimal paths and that mixed-type tuples
        // bind correctly.
        var lt = Lite.Users()
            .Where(u => u.IsActive)
            .Select(u => (
                u.UserName,
                ItemCount: u.Orders.Sum(o => o.Items.Count()),
                OrderTotal: u.Orders.Sum(o => o.Total)))
            .OrderBy(u => u.UserId)
            .Prepare();
        var pg = Pg.Users()
            .Where(u => u.IsActive)
            .Select(u => (
                u.UserName,
                ItemCount: u.Orders.Sum(o => o.Items.Count()),
                OrderTotal: u.Orders.Sum(o => o.Total)))
            .OrderBy(u => u.UserId)
            .Prepare();
        var my = My.Users()
            .Where(u => u.IsActive)
            .Select(u => (
                u.UserName,
                ItemCount: u.Orders.Sum(o => o.Items.Count()),
                OrderTotal: u.Orders.Sum(o => o.Total)))
            .OrderBy(u => u.UserId)
            .Prepare();
        var ss = Ss.Users()
            .Where(u => u.IsActive)
            .Select(u => (
                u.UserName,
                ItemCount: u.Orders.Sum(o => o.Items.Count()),
                OrderTotal: u.Orders.Sum(o => o.Total)))
            .OrderBy(u => u.UserId)
            .Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserName\", (SELECT SUM((SELECT COUNT(*) FROM \"order_items\" AS \"sq1\" WHERE \"sq1\".\"OrderId\" = \"sq0\".\"OrderId\")) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"ItemCount\", (SELECT SUM(\"sq0\".\"Total\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"OrderTotal\" FROM \"users\" WHERE \"IsActive\" = 1 ORDER BY \"UserId\" ASC",
            pg:     "SELECT \"UserName\", (SELECT SUM((SELECT COUNT(*) FROM \"order_items\" AS \"sq1\" WHERE \"sq1\".\"OrderId\" = \"sq0\".\"OrderId\")) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"ItemCount\", (SELECT SUM(\"sq0\".\"Total\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"OrderTotal\" FROM \"users\" WHERE \"IsActive\" = TRUE ORDER BY \"UserId\" ASC",
            mysql:  "SELECT `UserName`, (SELECT SUM((SELECT COUNT(*) FROM `order_items` AS `sq1` WHERE `sq1`.`OrderId` = `sq0`.`OrderId`)) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`) AS `ItemCount`, (SELECT SUM(`sq0`.`Total`) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`) AS `OrderTotal` FROM `users` WHERE `IsActive` = 1 ORDER BY `UserId` ASC",
            ss:     "SELECT [UserName], (SELECT SUM((SELECT COUNT(*) FROM [order_items] AS [sq1] WHERE [sq1].[OrderId] = [sq0].[OrderId])) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId]) AS [ItemCount], (SELECT SUM([sq0].[Total]) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId]) AS [OrderTotal] FROM [users] WHERE [IsActive] = 1 ORDER BY [UserId] ASC");

        // Active users (same seed as the surrounding tests):
        //   Alice → ItemCount=2 (1 item per order, 2 orders),  OrderTotal=325.50 (250.00 + 75.50)
        //   Bob   → ItemCount=1 (1 item, 1 order),             OrderTotal=150.00
        // Execution: SQLite, Postgres, MySQL. SQL Server rejects the int
        // sibling's nested aggregate (see SumCount sibling test).
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
        Assert.That(results[0].ItemCount, Is.EqualTo(2));
        Assert.That(results[0].OrderTotal, Is.EqualTo(325.50m));
        Assert.That(results[1].UserName, Is.EqualTo("Bob"));
        Assert.That(results[1].ItemCount, Is.EqualTo(1));
        Assert.That(results[1].OrderTotal, Is.EqualTo(150.00m));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0].ItemCount, Is.EqualTo(2));
        Assert.That(pgResults[0].OrderTotal, Is.EqualTo(325.50m));
        Assert.That(pgResults[1].ItemCount, Is.EqualTo(1));
        Assert.That(pgResults[1].OrderTotal, Is.EqualTo(150.00m));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0].ItemCount, Is.EqualTo(2));
        Assert.That(myResults[0].OrderTotal, Is.EqualTo(325.50m));
        Assert.That(myResults[1].ItemCount, Is.EqualTo(1));
        Assert.That(myResults[1].OrderTotal, Is.EqualTo(150.00m));
    }

    [Test]
    public async Task Select_ProjectionMixedNestingDepths_OrderTotalAndUrgentTagCount()
    {
        // Two simultaneous projection-side subqueries with mixed nesting depths:
        //   - OrderTotal:    1-level Sum over u.Orders
        //   - UrgentTagCount: 3-level Sum/Sum/Count traversal u.Orders → o.Items → i.Tags
        //                     filtered on TagName == "urgent"
        // Each top-level projection scalar subquery owns its own alias namespace,
        // so both start at sq0; the nested chain extends to sq2 inside its own
        // tree. Closes the deep-projection-side gap that the sibling 1-level
        // test (Select_TwoSiblingProjectionSubqueries_AliasReusesPerColumn) does
        // not exercise, and exercises the #294 resolver fix that propagates int
        // through nested aggregate selectors (the inner Count returns int and
        // the surrounding Sums preserve that type all the way up).
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users()
            .Where(u => u.IsActive)
            .Select(u => (
                u.UserName,
                OrderTotal: u.Orders.Sum(o => o.Total),
                UrgentTagCount: u.Orders.Sum(o => o.Items.Sum(i => i.Tags.Count(t => t.TagName == "urgent")))))
            .OrderBy(u => u.UserId)
            .Prepare();
        var pg = Pg.Users()
            .Where(u => u.IsActive)
            .Select(u => (
                u.UserName,
                OrderTotal: u.Orders.Sum(o => o.Total),
                UrgentTagCount: u.Orders.Sum(o => o.Items.Sum(i => i.Tags.Count(t => t.TagName == "urgent")))))
            .OrderBy(u => u.UserId)
            .Prepare();
        var my = My.Users()
            .Where(u => u.IsActive)
            .Select(u => (
                u.UserName,
                OrderTotal: u.Orders.Sum(o => o.Total),
                UrgentTagCount: u.Orders.Sum(o => o.Items.Sum(i => i.Tags.Count(t => t.TagName == "urgent")))))
            .OrderBy(u => u.UserId)
            .Prepare();
        var ss = Ss.Users()
            .Where(u => u.IsActive)
            .Select(u => (
                u.UserName,
                OrderTotal: u.Orders.Sum(o => o.Total),
                UrgentTagCount: u.Orders.Sum(o => o.Items.Sum(i => i.Tags.Count(t => t.TagName == "urgent")))))
            .OrderBy(u => u.UserId)
            .Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserName\", (SELECT SUM(\"sq0\".\"Total\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"OrderTotal\", (SELECT SUM((SELECT SUM((SELECT COUNT(*) FROM \"tags\" AS \"sq2\" WHERE \"sq2\".\"OrderItemId\" = \"sq1\".\"OrderItemId\" AND (\"sq2\".\"TagName\" = 'urgent'))) FROM \"order_items\" AS \"sq1\" WHERE \"sq1\".\"OrderId\" = \"sq0\".\"OrderId\")) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"UrgentTagCount\" FROM \"users\" WHERE \"IsActive\" = 1 ORDER BY \"UserId\" ASC",
            pg:     "SELECT \"UserName\", (SELECT SUM(\"sq0\".\"Total\") FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"OrderTotal\", (SELECT SUM((SELECT SUM((SELECT COUNT(*) FROM \"tags\" AS \"sq2\" WHERE \"sq2\".\"OrderItemId\" = \"sq1\".\"OrderItemId\" AND (\"sq2\".\"TagName\" = 'urgent'))) FROM \"order_items\" AS \"sq1\" WHERE \"sq1\".\"OrderId\" = \"sq0\".\"OrderId\")) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"UrgentTagCount\" FROM \"users\" WHERE \"IsActive\" = TRUE ORDER BY \"UserId\" ASC",
            mysql:  "SELECT `UserName`, (SELECT SUM(`sq0`.`Total`) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`) AS `OrderTotal`, (SELECT SUM((SELECT SUM((SELECT COUNT(*) FROM `tags` AS `sq2` WHERE `sq2`.`OrderItemId` = `sq1`.`OrderItemId` AND (`sq2`.`TagName` = 'urgent'))) FROM `order_items` AS `sq1` WHERE `sq1`.`OrderId` = `sq0`.`OrderId`)) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`) AS `UrgentTagCount` FROM `users` WHERE `IsActive` = 1 ORDER BY `UserId` ASC",
            ss:     "SELECT [UserName], (SELECT SUM([sq0].[Total]) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId]) AS [OrderTotal], (SELECT SUM((SELECT SUM((SELECT COUNT(*) FROM [tags] AS [sq2] WHERE [sq2].[OrderItemId] = [sq1].[OrderItemId] AND ([sq2].[TagName] = 'urgent'))) FROM [order_items] AS [sq1] WHERE [sq1].[OrderId] = [sq0].[OrderId])) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId]) AS [UrgentTagCount] FROM [users] WHERE [IsActive] = 1 ORDER BY [UserId] ASC");

        // Active users:
        //   Alice  → orders 1+2 (Total=250+75.50=325.50).
        //          Tag breakdown:
        //            order 1 → item 1 → tags 1,2 → 1 'urgent' (tag 1)   → order 1 sum = 1
        //            order 2 → item 2 → tag 3    → 1 'urgent' (tag 3)   → order 2 sum = 1
        //          Outer UrgentTagCount = 2.
        //   Bob    → order 3 (Total=150).
        //          order 3 → item 3 → tags 4,5 → 1 'urgent' (tag 4) → order 3 sum = 1.
        //          Outer UrgentTagCount = 1.
        // Execution: SQLite, Postgres, MySQL. SQL Server rejects nested
        // aggregate-in-aggregate (see SumCount sibling test for details).
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
        Assert.That(results[0].OrderTotal, Is.EqualTo(325.50m));
        Assert.That(results[0].UrgentTagCount, Is.EqualTo(2));
        Assert.That(results[1].UserName, Is.EqualTo("Bob"));
        Assert.That(results[1].OrderTotal, Is.EqualTo(150.00m));
        Assert.That(results[1].UrgentTagCount, Is.EqualTo(1));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0].OrderTotal, Is.EqualTo(325.50m));
        Assert.That(pgResults[0].UrgentTagCount, Is.EqualTo(2));
        Assert.That(pgResults[1].OrderTotal, Is.EqualTo(150.00m));
        Assert.That(pgResults[1].UrgentTagCount, Is.EqualTo(1));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0].OrderTotal, Is.EqualTo(325.50m));
        Assert.That(myResults[0].UrgentTagCount, Is.EqualTo(2));
        Assert.That(myResults[1].OrderTotal, Is.EqualTo(150.00m));
        Assert.That(myResults[1].UrgentTagCount, Is.EqualTo(1));
    }

    #endregion
}
