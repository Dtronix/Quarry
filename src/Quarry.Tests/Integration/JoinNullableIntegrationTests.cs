using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.Integration;

/// <summary>
/// Integration tests verifying that the generated reader correctly handles NULL values
/// from outer join columns at runtime. Before the join-nullable fix, reading a NOT NULL
/// column from the unmatched side of an outer join would crash with an InvalidCast.
/// </summary>
[TestFixture]
internal class JoinNullableIntegrationTests
{
    [Test]
    public async Task LeftJoin_TupleProjection_RightSideNullHandled()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        // Users LEFT JOIN Orders: Charlie has 0 orders → right side columns are NULL
        var query = Lite.Users()
            .LeftJoin<Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total))
            .Prepare();

        var results = await query.ExecuteFetchAllAsync();

        // 4 rows: Alice (2 orders) + Bob (1 order) + Charlie (0 orders = NULL right side)
        Assert.That(results, Has.Count.EqualTo(4));

        // Matched rows have real values
        var aliceRows = results.Where(r => r.UserName == "Alice").ToList();
        Assert.That(aliceRows, Has.Count.EqualTo(2));
        Assert.That(aliceRows.Select(r => r.Total).OrderBy(v => v), Is.EquivalentTo(new[] { 75.50m, 250.00m }));

        // Unmatched row (Charlie): Total is default(decimal) = 0 because the column is
        // NOT NULL in schema but NULL at runtime due to LEFT JOIN
        var charlieRow = results.Single(r => r.UserName == "Charlie");
        Assert.That(charlieRow.Total, Is.EqualTo(0m), "Join-nullable decimal should default to 0 when NULL");
    }

    [Test]
    public async Task LeftJoin_TupleProjection_MultipleRightColumns()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        // Select multiple right-side columns: Status (string, NOT NULL) and Total (decimal, NOT NULL)
        var query = Lite.Users()
            .LeftJoin<Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Status, o.Total))
            .Prepare();

        var results = await query.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(4));

        // Unmatched row: both right-side columns are NULL → defaults
        var charlieRow = results.Single(r => r.UserName == "Charlie");
        Assert.That(charlieRow.Status, Is.Null, "Join-nullable string should be null when NULL");
        Assert.That(charlieRow.Total, Is.EqualTo(0m), "Join-nullable decimal should default to 0 when NULL");
    }

    [Test]
    public async Task LeftJoin_TupleProjection_WithWhereOnLeftTable()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        // Filter to only inactive user (Charlie) who has no orders
        var query = Lite.Users()
            .LeftJoin<Order>((u, o) => u.UserId == o.UserId.Id)
            .Where((u, o) => !u.IsActive)
            .Select((u, o) => (u.UserName, o.Total))
            .Prepare();

        var results = await query.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].UserName, Is.EqualTo("Charlie"));
        Assert.That(results[0].Total, Is.EqualTo(0m), "Charlie has no orders, Total should be default(decimal)");
    }

    [Test]
    public async Task LeftJoin_EntityProjection_RightSideNullHandled()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        // Project entire right-side entity
        var query = Lite.Users()
            .LeftJoin<Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => o)
            .Prepare();

        var results = await query.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(4));

        // Matched rows have real values
        var matched = results.Where(r => r.OrderId != 0).ToList();
        Assert.That(matched, Has.Count.EqualTo(3));

        // Unmatched row: entity has all default values
        var defaultOrder = results.Single(r => r.OrderId == 0);
        Assert.That(defaultOrder.Total, Is.EqualTo(0m));
        Assert.That(defaultOrder.Status, Is.Null);
    }

    [Test]
    public async Task LeftJoin_IntColumn_NullHandled()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        // Order.OrderId is an int (NOT NULL in schema)
        var query = Lite.Users()
            .LeftJoin<Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.OrderId))
            .Prepare();

        var results = await query.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(4));

        // Matched rows have real values
        var aliceRow = results.First(r => r.UserName == "Alice");
        Assert.That(aliceRow.OrderId, Is.GreaterThan(0));

        // Unmatched row: int defaults to 0
        var charlieRow = results.Single(r => r.UserName == "Charlie");
        Assert.That(charlieRow.OrderId, Is.EqualTo(0), "Join-nullable int should default to 0 when NULL");
    }

    [Test]
    public async Task LeftJoin_EnumColumn_NullHandled()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        // Order.Priority is an enum (OrderPriority) — NOT NULL in schema
        var query = Lite.Users()
            .LeftJoin<Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Priority))
            .Prepare();

        var results = await query.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(4));

        // Unmatched row: enum defaults to default(OrderPriority) = 0
        var charlieRow = results.Single(r => r.UserName == "Charlie");
        Assert.That(charlieRow.Priority, Is.EqualTo(default(OrderPriority)), "Join-nullable enum should default when NULL");
    }

    [Test]
    public async Task RightJoin_SqlVerification_LeftSideMarkedJoinNullable()
    {
        // SQLite doesn't support RIGHT JOIN for execution, but we can verify
        // the SQL generation and projection analysis across all dialects
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().RightJoin<Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total)).Prepare();
        var pg = Pg.Users().RightJoin<Pg.Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total)).Prepare();
        var my = My.Users().RightJoin<My.Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total)).Prepare();
        var ss = Ss.Users().RightJoin<Ss.Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" RIGHT JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" RIGHT JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total` FROM `users` AS `t0` RIGHT JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId`",
            ss:     "SELECT [t0].[UserName], [t1].[Total] FROM [users] AS [t0] RIGHT JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId]");

        // Verify projection: left side (UserName) should be join-nullable, right side (Total) should not
        var diag = lt.ToDiagnostics();
        Assert.That(diag.ProjectionColumns![0].IsJoinNullable, Is.True, "LEFT side of RIGHT JOIN should be join-nullable");
        Assert.That(diag.ProjectionColumns[1].IsJoinNullable, Is.False, "RIGHT side of RIGHT JOIN should not be join-nullable");
    }

    [Test]
    public async Task FullOuterJoin_SqlVerification_BothSidesMarkedJoinNullable()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().FullOuterJoin<Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total)).Prepare();
        var pg = Pg.Users().FullOuterJoin<Pg.Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total)).Prepare();
        var my = My.Users().FullOuterJoin<My.Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total)).Prepare();
        var ss = Ss.Users().FullOuterJoin<Ss.Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" FULL OUTER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" FULL OUTER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total` FROM `users` AS `t0` FULL OUTER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId`",
            ss:     "SELECT [t0].[UserName], [t1].[Total] FROM [users] AS [t0] FULL OUTER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId]");

        // Verify projection: both sides should be join-nullable
        var diag = lt.ToDiagnostics();
        Assert.That(diag.ProjectionColumns![0].IsJoinNullable, Is.True, "LEFT side of FULL OUTER JOIN should be join-nullable");
        Assert.That(diag.ProjectionColumns[1].IsJoinNullable, Is.True, "RIGHT side of FULL OUTER JOIN should be join-nullable");
    }
}
