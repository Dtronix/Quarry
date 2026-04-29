using Quarry;
using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;

/// <summary>
/// Pins runtime behavior of tuple projections at and beyond the C# <c>ValueTuple</c>
/// flattening boundary (7 elements). At 8+ elements the C# compiler nests via
/// <c>ValueTuple&lt;T1..T7, TRest&gt;</c> where <c>TRest</c> is itself another
/// <c>ValueTuple</c>; <c>tuple.Item8</c> is rewritten to <c>tuple.Rest.Item1</c>.
/// Existing test coverage tops out at 6 elements, leaving the boundary unverified.
/// These tests guard against regressions in <c>ProjectionAnalyzer</c> and
/// <c>ReaderCodeGenerator</c> for projections that the upcoming
/// anonymous-type-to-named-tuple code-fix can produce.
/// </summary>
[TestFixture]
internal class CrossDialectWideTupleTests
{
    [Test]
    public async Task Tuple_7Elements_FlatLast()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .OrderBy((u, o) => o.OrderId)
            .Select((u, o) => (u.UserId, u.UserName, u.Email, u.IsActive, u.CreatedAt, o.OrderId, o.Total))
            .Prepare();
        var pg = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id)
            .OrderBy((u, o) => o.OrderId)
            .Select((u, o) => (u.UserId, u.UserName, u.Email, u.IsActive, u.CreatedAt, o.OrderId, o.Total))
            .Prepare();
        var my = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id)
            .OrderBy((u, o) => o.OrderId)
            .Select((u, o) => (u.UserId, u.UserName, u.Email, u.IsActive, u.CreatedAt, o.OrderId, o.Total))
            .Prepare();
        var ss = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id)
            .OrderBy((u, o) => o.OrderId)
            .Select((u, o) => (u.UserId, u.UserName, u.Email, u.IsActive, u.CreatedAt, o.OrderId, o.Total))
            .Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserId\", \"t0\".\"UserName\", \"t0\".\"Email\", \"t0\".\"IsActive\", \"t0\".\"CreatedAt\", \"t1\".\"OrderId\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" ORDER BY \"t1\".\"OrderId\" ASC",
            pg:     "SELECT \"t0\".\"UserId\", \"t0\".\"UserName\", \"t0\".\"Email\", \"t0\".\"IsActive\", \"t0\".\"CreatedAt\", \"t1\".\"OrderId\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" ORDER BY \"t1\".\"OrderId\" ASC",
            mysql:  "SELECT `t0`.`UserId`, `t0`.`UserName`, `t0`.`Email`, `t0`.`IsActive`, `t0`.`CreatedAt`, `t1`.`OrderId`, `t1`.`Total` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` ORDER BY `t1`.`OrderId` ASC",
            ss:     "SELECT [t0].[UserId], [t0].[UserName], [t0].[Email], [t0].[IsActive], [t0].[CreatedAt], [t1].[OrderId], [t1].[Total] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] ORDER BY [t1].[OrderId] ASC");

        // Seed: 3 orders, all owned by users (Alice/Alice/Bob). Ordered by OrderId, the first row is Order 1 / Alice / 250.00.
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0].UserId, Is.EqualTo(1));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
        Assert.That(results[0].OrderId, Is.EqualTo(1));
        Assert.That(results[0].Total, Is.EqualTo(250.00m));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));
        Assert.That(pgResults[0].UserId, Is.EqualTo(1));
        Assert.That(pgResults[0].UserName, Is.EqualTo("Alice"));
        Assert.That(pgResults[0].OrderId, Is.EqualTo(1));
        Assert.That(pgResults[0].Total, Is.EqualTo(250.00m));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));
        Assert.That(myResults[0].UserId, Is.EqualTo(1));
        Assert.That(myResults[0].UserName, Is.EqualTo("Alice"));
        Assert.That(myResults[0].OrderId, Is.EqualTo(1));
        Assert.That(myResults[0].Total, Is.EqualTo(250.00m));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(3));
        Assert.That(ssResults[0].UserId, Is.EqualTo(1));
        Assert.That(ssResults[0].UserName, Is.EqualTo("Alice"));
        Assert.That(ssResults[0].OrderId, Is.EqualTo(1));
        Assert.That(ssResults[0].Total, Is.EqualTo(250.00m));
    }

    [Test]
    public async Task Tuple_8Elements_FirstNested()
    {
        // 8 elements → ValueTuple<T1..T7, ValueTuple<T8>>. The C# compiler rewrites
        // tuple.Status to tuple.Rest.Item1; this test verifies that the named-element
        // access path through Rest still resolves to the right reader ordinal.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .OrderBy((u, o) => o.OrderId)
            .Select((u, o) => (u.UserId, u.UserName, u.Email, u.IsActive, u.CreatedAt, o.OrderId, o.Total, o.Status))
            .Prepare();
        var pg = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id)
            .OrderBy((u, o) => o.OrderId)
            .Select((u, o) => (u.UserId, u.UserName, u.Email, u.IsActive, u.CreatedAt, o.OrderId, o.Total, o.Status))
            .Prepare();
        var my = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id)
            .OrderBy((u, o) => o.OrderId)
            .Select((u, o) => (u.UserId, u.UserName, u.Email, u.IsActive, u.CreatedAt, o.OrderId, o.Total, o.Status))
            .Prepare();
        var ss = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id)
            .OrderBy((u, o) => o.OrderId)
            .Select((u, o) => (u.UserId, u.UserName, u.Email, u.IsActive, u.CreatedAt, o.OrderId, o.Total, o.Status))
            .Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserId\", \"t0\".\"UserName\", \"t0\".\"Email\", \"t0\".\"IsActive\", \"t0\".\"CreatedAt\", \"t1\".\"OrderId\", \"t1\".\"Total\", \"t1\".\"Status\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" ORDER BY \"t1\".\"OrderId\" ASC",
            pg:     "SELECT \"t0\".\"UserId\", \"t0\".\"UserName\", \"t0\".\"Email\", \"t0\".\"IsActive\", \"t0\".\"CreatedAt\", \"t1\".\"OrderId\", \"t1\".\"Total\", \"t1\".\"Status\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" ORDER BY \"t1\".\"OrderId\" ASC",
            mysql:  "SELECT `t0`.`UserId`, `t0`.`UserName`, `t0`.`Email`, `t0`.`IsActive`, `t0`.`CreatedAt`, `t1`.`OrderId`, `t1`.`Total`, `t1`.`Status` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` ORDER BY `t1`.`OrderId` ASC",
            ss:     "SELECT [t0].[UserId], [t0].[UserName], [t0].[Email], [t0].[IsActive], [t0].[CreatedAt], [t1].[OrderId], [t1].[Total], [t1].[Status] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] ORDER BY [t1].[OrderId] ASC");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
        Assert.That(results[0].OrderId, Is.EqualTo(1));
        Assert.That(results[0].Total, Is.EqualTo(250.00m));
        // Item8 — first element after the TRest boundary. Verifies that the named
        // element access (results[0].Status) reaches Rest.Item1 in the nested ValueTuple.
        Assert.That(results[0].Status, Is.EqualTo("Shipped"));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));
        Assert.That(pgResults[0].Status, Is.EqualTo("Shipped"));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));
        Assert.That(myResults[0].Status, Is.EqualTo("Shipped"));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(3));
        Assert.That(ssResults[0].Status, Is.EqualTo("Shipped"));
    }

    [Test]
    public async Task Tuple_10Elements_DeeperNested()
    {
        // 10 elements → ValueTuple<T1..T7, ValueTuple<T8, T9, T10>>. Three positions
        // inside Rest. Verifies the runtime reader still aligns ordinals 7..9 with
        // Rest.Item1..Item3 after the named-element rewrite.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .OrderBy((u, o) => o.OrderId)
            .Select((u, o) => (u.UserId, u.UserName, u.Email, u.IsActive, u.CreatedAt, o.OrderId, o.Total, o.Status, o.Priority, o.OrderDate))
            .Prepare();
        var pg = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id)
            .OrderBy((u, o) => o.OrderId)
            .Select((u, o) => (u.UserId, u.UserName, u.Email, u.IsActive, u.CreatedAt, o.OrderId, o.Total, o.Status, o.Priority, o.OrderDate))
            .Prepare();
        var my = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id)
            .OrderBy((u, o) => o.OrderId)
            .Select((u, o) => (u.UserId, u.UserName, u.Email, u.IsActive, u.CreatedAt, o.OrderId, o.Total, o.Status, o.Priority, o.OrderDate))
            .Prepare();
        var ss = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id)
            .OrderBy((u, o) => o.OrderId)
            .Select((u, o) => (u.UserId, u.UserName, u.Email, u.IsActive, u.CreatedAt, o.OrderId, o.Total, o.Status, o.Priority, o.OrderDate))
            .Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserId\", \"t0\".\"UserName\", \"t0\".\"Email\", \"t0\".\"IsActive\", \"t0\".\"CreatedAt\", \"t1\".\"OrderId\", \"t1\".\"Total\", \"t1\".\"Status\", \"t1\".\"Priority\", \"t1\".\"OrderDate\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" ORDER BY \"t1\".\"OrderId\" ASC",
            pg:     "SELECT \"t0\".\"UserId\", \"t0\".\"UserName\", \"t0\".\"Email\", \"t0\".\"IsActive\", \"t0\".\"CreatedAt\", \"t1\".\"OrderId\", \"t1\".\"Total\", \"t1\".\"Status\", \"t1\".\"Priority\", \"t1\".\"OrderDate\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" ORDER BY \"t1\".\"OrderId\" ASC",
            mysql:  "SELECT `t0`.`UserId`, `t0`.`UserName`, `t0`.`Email`, `t0`.`IsActive`, `t0`.`CreatedAt`, `t1`.`OrderId`, `t1`.`Total`, `t1`.`Status`, `t1`.`Priority`, `t1`.`OrderDate` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` ORDER BY `t1`.`OrderId` ASC",
            ss:     "SELECT [t0].[UserId], [t0].[UserName], [t0].[Email], [t0].[IsActive], [t0].[CreatedAt], [t1].[OrderId], [t1].[Total], [t1].[Status], [t1].[Priority], [t1].[OrderDate] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] ORDER BY [t1].[OrderId] ASC");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
        // Three positions inside the Rest segment.
        Assert.That(results[0].Status, Is.EqualTo("Shipped"));      // Rest.Item1
        Assert.That(results[0].Priority, Is.EqualTo(OrderPriority.High));     // Rest.Item2 (enum)
        Assert.That(results[0].OrderDate, Is.EqualTo(new DateTime(2024, 6, 1))); // Rest.Item3

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));
        Assert.That(pgResults[0].Status, Is.EqualTo("Shipped"));
        Assert.That(pgResults[0].Priority, Is.EqualTo(OrderPriority.High));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));
        Assert.That(myResults[0].Status, Is.EqualTo("Shipped"));
        Assert.That(myResults[0].Priority, Is.EqualTo(OrderPriority.High));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(3));
        Assert.That(ssResults[0].Status, Is.EqualTo("Shipped"));
        Assert.That(ssResults[0].Priority, Is.EqualTo(OrderPriority.High));
    }

    [Test]
    public async Task Tuple_16Elements_DeepDoubleNested()
    {
        // 16 elements crosses TWO TRest boundaries. The runtime shape is
        // ValueTuple<U1..U7, ValueTuple<U8..U14, ValueTuple<U15, U16>>>. Element access
        // goes: items 1-7 → direct, items 8-14 → .Rest.Item1..Item7, items 15-16 →
        // .Rest.Rest.Item1..Item2. Verifies the named-element rewrite still resolves
        // through two levels of Rest in the generated reader output.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Join<OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id)
            .OrderBy((u, o, oi) => oi.OrderItemId)
            .Select((u, o, oi) => (
                u.UserId, u.UserName, u.Email, u.IsActive, u.CreatedAt, u.LastLogin,
                o.OrderId, o.Total, o.Status, o.Priority, o.OrderDate,
                oi.OrderItemId, oi.ProductName, oi.Quantity, oi.UnitPrice, oi.LineTotal))
            .Prepare();
        var pg = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Join<Pg.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id)
            .OrderBy((u, o, oi) => oi.OrderItemId)
            .Select((u, o, oi) => (
                u.UserId, u.UserName, u.Email, u.IsActive, u.CreatedAt, u.LastLogin,
                o.OrderId, o.Total, o.Status, o.Priority, o.OrderDate,
                oi.OrderItemId, oi.ProductName, oi.Quantity, oi.UnitPrice, oi.LineTotal))
            .Prepare();
        var my = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Join<My.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id)
            .OrderBy((u, o, oi) => oi.OrderItemId)
            .Select((u, o, oi) => (
                u.UserId, u.UserName, u.Email, u.IsActive, u.CreatedAt, u.LastLogin,
                o.OrderId, o.Total, o.Status, o.Priority, o.OrderDate,
                oi.OrderItemId, oi.ProductName, oi.Quantity, oi.UnitPrice, oi.LineTotal))
            .Prepare();
        var ss = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Join<Ss.OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id)
            .OrderBy((u, o, oi) => oi.OrderItemId)
            .Select((u, o, oi) => (
                u.UserId, u.UserName, u.Email, u.IsActive, u.CreatedAt, u.LastLogin,
                o.OrderId, o.Total, o.Status, o.Priority, o.OrderDate,
                oi.OrderItemId, oi.ProductName, oi.Quantity, oi.UnitPrice, oi.LineTotal))
            .Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserId\", \"t0\".\"UserName\", \"t0\".\"Email\", \"t0\".\"IsActive\", \"t0\".\"CreatedAt\", \"t0\".\"LastLogin\", \"t1\".\"OrderId\", \"t1\".\"Total\", \"t1\".\"Status\", \"t1\".\"Priority\", \"t1\".\"OrderDate\", \"t2\".\"OrderItemId\", \"t2\".\"ProductName\", \"t2\".\"Quantity\", \"t2\".\"UnitPrice\", \"t2\".\"LineTotal\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" INNER JOIN \"order_items\" AS \"t2\" ON \"t1\".\"OrderId\" = \"t2\".\"OrderId\" ORDER BY \"t2\".\"OrderItemId\" ASC",
            pg:     "SELECT \"t0\".\"UserId\", \"t0\".\"UserName\", \"t0\".\"Email\", \"t0\".\"IsActive\", \"t0\".\"CreatedAt\", \"t0\".\"LastLogin\", \"t1\".\"OrderId\", \"t1\".\"Total\", \"t1\".\"Status\", \"t1\".\"Priority\", \"t1\".\"OrderDate\", \"t2\".\"OrderItemId\", \"t2\".\"ProductName\", \"t2\".\"Quantity\", \"t2\".\"UnitPrice\", \"t2\".\"LineTotal\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" INNER JOIN \"order_items\" AS \"t2\" ON \"t1\".\"OrderId\" = \"t2\".\"OrderId\" ORDER BY \"t2\".\"OrderItemId\" ASC",
            mysql:  "SELECT `t0`.`UserId`, `t0`.`UserName`, `t0`.`Email`, `t0`.`IsActive`, `t0`.`CreatedAt`, `t0`.`LastLogin`, `t1`.`OrderId`, `t1`.`Total`, `t1`.`Status`, `t1`.`Priority`, `t1`.`OrderDate`, `t2`.`OrderItemId`, `t2`.`ProductName`, `t2`.`Quantity`, `t2`.`UnitPrice`, `t2`.`LineTotal` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` INNER JOIN `order_items` AS `t2` ON `t1`.`OrderId` = `t2`.`OrderId` ORDER BY `t2`.`OrderItemId` ASC",
            ss:     "SELECT [t0].[UserId], [t0].[UserName], [t0].[Email], [t0].[IsActive], [t0].[CreatedAt], [t0].[LastLogin], [t1].[OrderId], [t1].[Total], [t1].[Status], [t1].[Priority], [t1].[OrderDate], [t2].[OrderItemId], [t2].[ProductName], [t2].[Quantity], [t2].[UnitPrice], [t2].[LineTotal] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] INNER JOIN [order_items] AS [t2] ON [t1].[OrderId] = [t2].[OrderId] ORDER BY [t2].[OrderItemId] ASC");

        // Seed: OrderItems are 1-Widget(Order 1), 2-Gadget(Order 2), 3-Widget(Order 3).
        // Ordered by OrderItemId, results[0] is OrderItem 1 / Order 1 / Alice / Widget.
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));

        // Depth 0 (direct fields, ordinals 0..6 → Item1..Item7).
        Assert.That(results[0].UserId, Is.EqualTo(1));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
        // Depth 1 (Rest.Item1..Item7, ordinals 7..13).
        Assert.That(results[0].Total, Is.EqualTo(250.00m));         // ordinal 7 → Rest.Item1
        Assert.That(results[0].Status, Is.EqualTo("Shipped"));       // ordinal 8 → Rest.Item2
        Assert.That(results[0].Priority, Is.EqualTo(OrderPriority.High));     // ordinal 9 → Rest.Item3
        Assert.That(results[0].OrderDate, Is.EqualTo(new DateTime(2024, 6, 1))); // ordinal 10 → Rest.Item4 (mid-Rest)
        Assert.That(results[0].OrderItemId, Is.EqualTo(1));          // ordinal 11 → Rest.Item5 (mid-Rest)
        Assert.That(results[0].ProductName, Is.EqualTo("Widget"));   // ordinal 12 → Rest.Item6
        Assert.That(results[0].Quantity, Is.EqualTo(2));             // ordinal 13 → Rest.Item7
        // Depth 2 (Rest.Rest.Item1..Item2, ordinals 14..15).
        Assert.That(results[0].UnitPrice, Is.EqualTo(125.00m));      // ordinal 14 → Rest.Rest.Item1
        Assert.That(results[0].LineTotal, Is.EqualTo(250.00m));      // ordinal 15 → Rest.Rest.Item2

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));
        Assert.That(pgResults[0].UserName, Is.EqualTo("Alice"));
        Assert.That(pgResults[0].Status, Is.EqualTo("Shipped"));
        Assert.That(pgResults[0].UnitPrice, Is.EqualTo(125.00m));
        Assert.That(pgResults[0].LineTotal, Is.EqualTo(250.00m));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));
        Assert.That(myResults[0].UserName, Is.EqualTo("Alice"));
        Assert.That(myResults[0].Status, Is.EqualTo("Shipped"));
        Assert.That(myResults[0].UnitPrice, Is.EqualTo(125.00m));
        Assert.That(myResults[0].LineTotal, Is.EqualTo(250.00m));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(3));
        Assert.That(ssResults[0].UserName, Is.EqualTo("Alice"));
        Assert.That(ssResults[0].Status, Is.EqualTo("Shipped"));
        Assert.That(ssResults[0].UnitPrice, Is.EqualTo(125.00m));
        Assert.That(ssResults[0].LineTotal, Is.EqualTo(250.00m));
    }

    [Test]
    public async Task Tuple_NullableInsideRest()
    {
        // 9 elements with the nullable string `Order.Notes` at ordinal 8 (Rest.Item2).
        // Verifies that the IsDBNull-guarded reader path resolves the right ordinal
        // through Rest. Seed data: Order 1 has Notes='Express'; Order 2 has Notes=NULL —
        // both must materialize without throwing, with results[1].Notes coming back as null.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .OrderBy((u, o) => o.OrderId)
            .Select((u, o) => (u.UserId, u.UserName, u.Email, u.IsActive, u.CreatedAt, o.OrderId, o.Total, o.Status, o.Notes))
            .Prepare();
        var pg = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id)
            .OrderBy((u, o) => o.OrderId)
            .Select((u, o) => (u.UserId, u.UserName, u.Email, u.IsActive, u.CreatedAt, o.OrderId, o.Total, o.Status, o.Notes))
            .Prepare();
        var my = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id)
            .OrderBy((u, o) => o.OrderId)
            .Select((u, o) => (u.UserId, u.UserName, u.Email, u.IsActive, u.CreatedAt, o.OrderId, o.Total, o.Status, o.Notes))
            .Prepare();
        var ss = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id)
            .OrderBy((u, o) => o.OrderId)
            .Select((u, o) => (u.UserId, u.UserName, u.Email, u.IsActive, u.CreatedAt, o.OrderId, o.Total, o.Status, o.Notes))
            .Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserId\", \"t0\".\"UserName\", \"t0\".\"Email\", \"t0\".\"IsActive\", \"t0\".\"CreatedAt\", \"t1\".\"OrderId\", \"t1\".\"Total\", \"t1\".\"Status\", \"t1\".\"Notes\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" ORDER BY \"t1\".\"OrderId\" ASC",
            pg:     "SELECT \"t0\".\"UserId\", \"t0\".\"UserName\", \"t0\".\"Email\", \"t0\".\"IsActive\", \"t0\".\"CreatedAt\", \"t1\".\"OrderId\", \"t1\".\"Total\", \"t1\".\"Status\", \"t1\".\"Notes\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" ORDER BY \"t1\".\"OrderId\" ASC",
            mysql:  "SELECT `t0`.`UserId`, `t0`.`UserName`, `t0`.`Email`, `t0`.`IsActive`, `t0`.`CreatedAt`, `t1`.`OrderId`, `t1`.`Total`, `t1`.`Status`, `t1`.`Notes` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` ORDER BY `t1`.`OrderId` ASC",
            ss:     "SELECT [t0].[UserId], [t0].[UserName], [t0].[Email], [t0].[IsActive], [t0].[CreatedAt], [t1].[OrderId], [t1].[Total], [t1].[Status], [t1].[Notes] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] ORDER BY [t1].[OrderId] ASC");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0].Notes, Is.EqualTo("Express"));   // Order 1 has Notes='Express' — Rest.Item2 (ordinal 8), non-null path
        Assert.That(results[1].Notes, Is.Null);                  // Order 2 has Notes=NULL — Rest.Item2 (ordinal 8), IsDBNull-guarded path

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));
        Assert.That(pgResults[0].Notes, Is.EqualTo("Express"));
        Assert.That(pgResults[1].Notes, Is.Null);

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));
        Assert.That(myResults[0].Notes, Is.EqualTo("Express"));
        Assert.That(myResults[1].Notes, Is.Null);

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(3));
        Assert.That(ssResults[0].Notes, Is.EqualTo("Express"));
        Assert.That(ssResults[1].Notes, Is.Null);
    }

    [Test]
    public async Task Tuple_PostCteWideProjection()
    {
        // 8 elements projected from a CTE-rooted chain. Exercises the late-rebuild
        // tuple type-name path in ChainAnalyzer.cs around lines 2258 / 2292 — the
        // post-CTE Select runs through placeholder analysis without a SemanticModel
        // at discovery time, then has its tuple type-name rebuilt from enriched columns.
        // 8 elements crosses the TRest boundary, so this verifies the late rebuild emits
        // a flat type-name string the C# compiler can fold to ValueTuple<…, ValueTuple<int>>.
        // Element 8 (`Reorder`) is at Rest.Item1 — element 7 (`Notes`) is the last flat slot.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.With<Order>(orders => orders.Where(o => o.Total > 100))
            .FromCte<Order>()
            .Select(o => (o.OrderId, Echo: o.OrderId, o.Total, o.Status, o.Priority, o.OrderDate, o.Notes, Reorder: o.OrderId))
            .Prepare();
        var pg = Pg.With<Pg.Order>(orders => orders.Where(o => o.Total > 100))
            .FromCte<Pg.Order>()
            .Select(o => (o.OrderId, Echo: o.OrderId, o.Total, o.Status, o.Priority, o.OrderDate, o.Notes, Reorder: o.OrderId))
            .Prepare();
        var my = My.With<My.Order>(orders => orders.Where(o => o.Total > 100))
            .FromCte<My.Order>()
            .Select(o => (o.OrderId, Echo: o.OrderId, o.Total, o.Status, o.Priority, o.OrderDate, o.Notes, Reorder: o.OrderId))
            .Prepare();
        var ss = Ss.With<Ss.Order>(orders => orders.Where(o => o.Total > 100))
            .FromCte<Ss.Order>()
            .Select(o => (o.OrderId, Echo: o.OrderId, o.Total, o.Status, o.Priority, o.OrderDate, o.Notes, Reorder: o.OrderId))
            .Prepare();

        // Seed: Total > 100 keeps Order 1 (250.00, Alice, Notes='Express') and Order 3 (150.00, Bob, Notes=NULL).
        // Order 2 (75.50) is filtered out by the CTE inner WHERE.
        // Sort results in-memory by OrderId for stable assertions: post-CTE chain doesn't
        // support OrderBy directly (IEntityAccessor doesn't expose it; Quarry chain-continuation
        // methods require a Where/Select/GroupBy first), so we order client-side.
        var results = (await lt.ExecuteFetchAllAsync()).OrderBy(r => r.OrderId).ToList();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].OrderId, Is.EqualTo(1));
        Assert.That(results[0].Echo, Is.EqualTo(1));
        Assert.That(results[0].Total, Is.EqualTo(250.00m));
        Assert.That(results[0].Notes, Is.EqualTo("Express"));   // ordinal 6 — Item7 (last flat slot)
        Assert.That(results[0].Reorder, Is.EqualTo(1));         // ordinal 7 — Rest.Item1 (first nested slot)
        Assert.That(results[1].Notes, Is.Null);                  // ordinal 6 — IsDBNull through Item7
        Assert.That(results[1].Reorder, Is.EqualTo(3));         // ordinal 7 — Rest.Item1 for second row

        var pgResults = (await pg.ExecuteFetchAllAsync()).OrderBy(r => r.OrderId).ToList();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0].Notes, Is.EqualTo("Express"));
        Assert.That(pgResults[0].Reorder, Is.EqualTo(1));
        Assert.That(pgResults[1].Notes, Is.Null);

        var myResults = (await my.ExecuteFetchAllAsync()).OrderBy(r => r.OrderId).ToList();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0].Notes, Is.EqualTo("Express"));
        Assert.That(myResults[0].Reorder, Is.EqualTo(1));
        Assert.That(myResults[1].Notes, Is.Null);

        var ssResults = (await ss.ExecuteFetchAllAsync()).OrderBy(r => r.OrderId).ToList();
        Assert.That(ssResults, Has.Count.EqualTo(2));
        Assert.That(ssResults[0].Notes, Is.EqualTo("Express"));
        Assert.That(ssResults[0].Reorder, Is.EqualTo(1));
        Assert.That(ssResults[1].Notes, Is.Null);
    }
}
