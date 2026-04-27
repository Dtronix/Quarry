using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;

/// <summary>
/// Verifies that ProjectedColumn.IsJoinNullable is set correctly based on join type.
/// Outer joins force columns on the nullable side to be join-nullable, even when
/// the schema declares them NOT NULL.
/// </summary>
[TestFixture]
internal class JoinNullableProjectionTests
{
    #region Inner Join (no join-nullable)

    [Test]
    public async Task InnerJoin_NoColumnsJoinNullable()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var diag = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total)).Prepare().ToDiagnostics();

        // INNER JOIN: neither side is join-nullable
        Assert.That(diag.ProjectionColumns, Has.Count.EqualTo(2));
        Assert.That(diag.ProjectionColumns![0].PropertyName, Is.EqualTo("UserName"));
        Assert.That(diag.ProjectionColumns[0].IsJoinNullable, Is.False);
        Assert.That(diag.ProjectionColumns[1].PropertyName, Is.EqualTo("Total"));
        Assert.That(diag.ProjectionColumns[1].IsJoinNullable, Is.False);
    }

    #endregion

    #region Cross Join (no join-nullable)

    [Test]
    public async Task CrossJoin_NoColumnsJoinNullable()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var diag = Lite.Users().CrossJoin<Order>()
            .Select((u, o) => (u.UserName, o.Total)).Prepare().ToDiagnostics();

        // CROSS JOIN: neither side is join-nullable
        Assert.That(diag.ProjectionColumns, Has.Count.EqualTo(2));
        Assert.That(diag.ProjectionColumns![0].IsJoinNullable, Is.False);
        Assert.That(diag.ProjectionColumns[1].IsJoinNullable, Is.False);
    }

    #endregion

    #region Left Join

    [Test]
    public async Task LeftJoin_RightSideColumnsJoinNullable()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var diag = Lite.Users().LeftJoin<Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total)).Prepare().ToDiagnostics();

        // LEFT JOIN: left side (t0 = Users) not join-nullable, right side (t1 = Orders) is join-nullable
        Assert.That(diag.ProjectionColumns, Has.Count.EqualTo(2));
        Assert.That(diag.ProjectionColumns![0].PropertyName, Is.EqualTo("UserName"));
        Assert.That(diag.ProjectionColumns[0].IsJoinNullable, Is.False, "Left side of LEFT JOIN should not be join-nullable");
        Assert.That(diag.ProjectionColumns[1].PropertyName, Is.EqualTo("Total"));
        Assert.That(diag.ProjectionColumns[1].IsJoinNullable, Is.True, "Right side of LEFT JOIN should be join-nullable");
    }

    [Test]
    public async Task LeftJoin_SchemaAlreadyNullable_StaysNullable()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        // Order.Notes is already nullable in schema
        var diag = Lite.Users().LeftJoin<Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Notes)).Prepare().ToDiagnostics();

        Assert.That(diag.ProjectionColumns, Has.Count.EqualTo(2));
        Assert.That(diag.ProjectionColumns![1].PropertyName, Is.EqualTo("Notes"));
        Assert.That(diag.ProjectionColumns[1].IsNullable, Is.True, "Schema nullable stays nullable");
        Assert.That(diag.ProjectionColumns[1].IsJoinNullable, Is.True, "Right side of LEFT JOIN is also join-nullable");
    }

    #endregion

    #region Right Join

    [Test]
    public async Task RightJoin_LeftSideColumnsJoinNullable()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var diag = Lite.Users().RightJoin<Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total)).Prepare().ToDiagnostics();

        // RIGHT JOIN: left side (t0 = Users) is join-nullable, right side (t1 = Orders) not join-nullable
        Assert.That(diag.ProjectionColumns, Has.Count.EqualTo(2));
        Assert.That(diag.ProjectionColumns![0].PropertyName, Is.EqualTo("UserName"));
        Assert.That(diag.ProjectionColumns[0].IsJoinNullable, Is.True, "Left side of RIGHT JOIN should be join-nullable");
        Assert.That(diag.ProjectionColumns[1].PropertyName, Is.EqualTo("Total"));
        Assert.That(diag.ProjectionColumns[1].IsJoinNullable, Is.False, "Right side of RIGHT JOIN should not be join-nullable");
    }

    #endregion

    #region Full Outer Join

    [Test]
    public async Task FullOuterJoin_BothSidesJoinNullable()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var diag = Lite.Users().FullOuterJoin<Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total)).Prepare().ToDiagnostics();

        // FULL OUTER JOIN: both sides are join-nullable
        Assert.That(diag.ProjectionColumns, Has.Count.EqualTo(2));
        Assert.That(diag.ProjectionColumns![0].PropertyName, Is.EqualTo("UserName"));
        Assert.That(diag.ProjectionColumns[0].IsJoinNullable, Is.True, "Left side of FULL OUTER JOIN should be join-nullable");
        Assert.That(diag.ProjectionColumns[1].PropertyName, Is.EqualTo("Total"));
        Assert.That(diag.ProjectionColumns[1].IsJoinNullable, Is.True, "Right side of FULL OUTER JOIN should be join-nullable");
    }

    #endregion

    #region Cascading Nullability

    [Test]
    public async Task LeftJoin_ThenRightJoin_CascadesNullability()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        // t0 LEFT JOIN t1 RIGHT JOIN t2
        // t0: join-nullable (left side of RIGHT JOIN at index 1)
        // t1: join-nullable (right side of LEFT JOIN at index 0, AND left side of RIGHT JOIN at index 1)
        // t2: not join-nullable (right side of RIGHT JOIN)
        var diag = Lite.Users()
            .LeftJoin<Order>((u, o) => u.UserId == o.UserId.Id)
            .RightJoin<OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id)
            .Select((u, o, oi) => (u.UserName, o.Total, oi.ProductName)).Prepare().ToDiagnostics();

        Assert.That(diag.ProjectionColumns, Has.Count.EqualTo(3));
        Assert.That(diag.ProjectionColumns![0].PropertyName, Is.EqualTo("UserName"));
        Assert.That(diag.ProjectionColumns[0].IsJoinNullable, Is.True, "t0 should be join-nullable due to cascading RIGHT JOIN");
        Assert.That(diag.ProjectionColumns[1].PropertyName, Is.EqualTo("Total"));
        Assert.That(diag.ProjectionColumns[1].IsJoinNullable, Is.True, "t1 should be join-nullable (LEFT + cascading RIGHT)");
        Assert.That(diag.ProjectionColumns[2].PropertyName, Is.EqualTo("ProductName"));
        Assert.That(diag.ProjectionColumns[2].IsJoinNullable, Is.False, "t2 (RIGHT JOIN right side) should not be join-nullable");
    }

    [Test]
    public async Task InnerJoin_ThenLeftJoin_OnlyLastJoinedTableNullable()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        // t0 INNER JOIN t1 LEFT JOIN t2
        // t0: not join-nullable (inner join, no later RIGHT/FULL)
        // t1: not join-nullable (right side of inner join, no later RIGHT/FULL)
        // t2: join-nullable (right side of LEFT JOIN)
        var diag = Lite.Users()
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .LeftJoin<OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id)
            .Select((u, o, oi) => (u.UserName, o.Total, oi.ProductName)).Prepare().ToDiagnostics();

        Assert.That(diag.ProjectionColumns, Has.Count.EqualTo(3));
        Assert.That(diag.ProjectionColumns![0].IsJoinNullable, Is.False, "t0 should not be join-nullable");
        Assert.That(diag.ProjectionColumns[1].IsJoinNullable, Is.False, "t1 should not be join-nullable");
        Assert.That(diag.ProjectionColumns[2].IsJoinNullable, Is.True, "t2 should be join-nullable (RIGHT side of LEFT JOIN)");
    }

    #endregion

    #region Left Join — null materialization (4-dialect execution)

    // The tests above verify the projection-metadata flag (IsJoinNullable). The tests
    // below verify the corresponding runtime behavior on every dialect: when an unmatched
    // outer-join row produces a NULL for a NOT-NULL-in-schema column, the generated reader
    // must materialize the language default (decimal=0, string=null, int=0, enum=default)
    // instead of crashing with InvalidCast. Seed: Alice has 2 orders, Bob has 1, Charlie 0.

    [Test]
    public async Task LeftJoin_TupleProjection_RightSideNullHandled_CrossDialect()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().LeftJoin<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var pg = Pg.Users().LeftJoin<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var my = My.Users().LeftJoin<My.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var ss = Ss.Users().LeftJoin<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" LEFT JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" LEFT JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total` FROM `users` AS `t0` LEFT JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId`",
            ss:     "SELECT [t0].[UserName], [t1].[Total] FROM [users] AS [t0] LEFT JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId]");

        var ltRows = await lt.ExecuteFetchAllAsync();
        Assert.That(ltRows, Has.Count.EqualTo(4));
        Assert.That(ltRows.Where(r => r.UserName == "Alice").Select(r => r.Total).OrderBy(v => v), Is.EquivalentTo(new[] { 75.50m, 250.00m }));
        Assert.That(ltRows.Single(r => r.UserName == "Charlie").Total, Is.EqualTo(0m), "SQLite: join-nullable decimal defaults to 0");

        var pgRows = await pg.ExecuteFetchAllAsync();
        Assert.That(pgRows, Has.Count.EqualTo(4));
        Assert.That(pgRows.Where(r => r.UserName == "Alice").Select(r => r.Total).OrderBy(v => v), Is.EquivalentTo(new[] { 75.50m, 250.00m }));
        Assert.That(pgRows.Single(r => r.UserName == "Charlie").Total, Is.EqualTo(0m), "PG: join-nullable decimal defaults to 0");

        var myRows = await my.ExecuteFetchAllAsync();
        Assert.That(myRows, Has.Count.EqualTo(4));
        Assert.That(myRows.Where(r => r.UserName == "Alice").Select(r => r.Total).OrderBy(v => v), Is.EquivalentTo(new[] { 75.50m, 250.00m }));
        Assert.That(myRows.Single(r => r.UserName == "Charlie").Total, Is.EqualTo(0m), "MySQL: join-nullable decimal defaults to 0");

        var ssRows = await ss.ExecuteFetchAllAsync();
        Assert.That(ssRows, Has.Count.EqualTo(4));
        Assert.That(ssRows.Where(r => r.UserName == "Alice").Select(r => r.Total).OrderBy(v => v), Is.EquivalentTo(new[] { 75.50m, 250.00m }));
        Assert.That(ssRows.Single(r => r.UserName == "Charlie").Total, Is.EqualTo(0m), "SQL Server: join-nullable decimal defaults to 0");
    }

    [Test]
    public async Task LeftJoin_TupleProjection_MultipleRightColumns_CrossDialect()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Right side: Status (string NOT NULL) + Total (decimal NOT NULL). Both NULL for unmatched row.
        var lt = Lite.Users().LeftJoin<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Status, o.Total)).Prepare();
        var pg = Pg.Users().LeftJoin<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Status, o.Total)).Prepare();
        var my = My.Users().LeftJoin<My.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Status, o.Total)).Prepare();
        var ss = Ss.Users().LeftJoin<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Status, o.Total)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Status\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" LEFT JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Status\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" LEFT JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Status`, `t1`.`Total` FROM `users` AS `t0` LEFT JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId`",
            ss:     "SELECT [t0].[UserName], [t1].[Status], [t1].[Total] FROM [users] AS [t0] LEFT JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId]");

        var ltCharlie = (await lt.ExecuteFetchAllAsync()).Single(r => r.UserName == "Charlie");
        Assert.That(ltCharlie.Status, Is.Null, "SQLite: join-nullable string defaults to null");
        Assert.That(ltCharlie.Total, Is.EqualTo(0m), "SQLite: join-nullable decimal defaults to 0");

        var pgCharlie = (await pg.ExecuteFetchAllAsync()).Single(r => r.UserName == "Charlie");
        Assert.That(pgCharlie.Status, Is.Null, "PG: join-nullable string defaults to null");
        Assert.That(pgCharlie.Total, Is.EqualTo(0m), "PG: join-nullable decimal defaults to 0");

        var myCharlie = (await my.ExecuteFetchAllAsync()).Single(r => r.UserName == "Charlie");
        Assert.That(myCharlie.Status, Is.Null, "MySQL: join-nullable string defaults to null");
        Assert.That(myCharlie.Total, Is.EqualTo(0m), "MySQL: join-nullable decimal defaults to 0");

        var ssCharlie = (await ss.ExecuteFetchAllAsync()).Single(r => r.UserName == "Charlie");
        Assert.That(ssCharlie.Status, Is.Null, "SQL Server: join-nullable string defaults to null");
        Assert.That(ssCharlie.Total, Is.EqualTo(0m), "SQL Server: join-nullable decimal defaults to 0");
    }

    [Test]
    public async Task LeftJoin_TupleProjection_WithWhereOnLeftTable_CrossDialect()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Filter to only inactive user (Charlie) — has no orders, so Total must default
        var lt = Lite.Users().LeftJoin<Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => !u.IsActive).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var pg = Pg.Users().LeftJoin<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => !u.IsActive).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var my = My.Users().LeftJoin<My.Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => !u.IsActive).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var ss = Ss.Users().LeftJoin<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => !u.IsActive).Select((u, o) => (u.UserName, o.Total)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" LEFT JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t0\".\"IsActive\" = 0",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" LEFT JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t0\".\"IsActive\" = FALSE",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total` FROM `users` AS `t0` LEFT JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` WHERE `t0`.`IsActive` = 0",
            ss:     "SELECT [t0].[UserName], [t1].[Total] FROM [users] AS [t0] LEFT JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] WHERE [t0].[IsActive] = 0");

        var ltRows = await lt.ExecuteFetchAllAsync();
        Assert.That(ltRows, Has.Count.EqualTo(1));
        Assert.That(ltRows[0].UserName, Is.EqualTo("Charlie"));
        Assert.That(ltRows[0].Total, Is.EqualTo(0m));

        var pgRows = await pg.ExecuteFetchAllAsync();
        Assert.That(pgRows, Has.Count.EqualTo(1));
        Assert.That(pgRows[0].UserName, Is.EqualTo("Charlie"));
        Assert.That(pgRows[0].Total, Is.EqualTo(0m));

        var myRows = await my.ExecuteFetchAllAsync();
        Assert.That(myRows, Has.Count.EqualTo(1));
        Assert.That(myRows[0].UserName, Is.EqualTo("Charlie"));
        Assert.That(myRows[0].Total, Is.EqualTo(0m));

        var ssRows = await ss.ExecuteFetchAllAsync();
        Assert.That(ssRows, Has.Count.EqualTo(1));
        Assert.That(ssRows[0].UserName, Is.EqualTo("Charlie"));
        Assert.That(ssRows[0].Total, Is.EqualTo(0m));
    }

    [Test]
    public async Task LeftJoin_EntityProjection_RightSideNullHandled_CrossDialect()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Project the entire right-side entity — all NOT-NULL columns must default for unmatched row
        var lt = Lite.Users().LeftJoin<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => o).Prepare();
        var pg = Pg.Users().LeftJoin<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => o).Prepare();
        var my = My.Users().LeftJoin<My.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => o).Prepare();
        var ss = Ss.Users().LeftJoin<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => o).Prepare();

        var ltRows = await lt.ExecuteFetchAllAsync();
        Assert.That(ltRows, Has.Count.EqualTo(4));
        Assert.That(ltRows.Count(r => r.OrderId != 0), Is.EqualTo(3));
        var ltDefault = ltRows.Single(r => r.OrderId == 0);
        Assert.That(ltDefault.Total, Is.EqualTo(0m));
        Assert.That(ltDefault.Status, Is.Null);

        var pgRows = await pg.ExecuteFetchAllAsync();
        Assert.That(pgRows, Has.Count.EqualTo(4));
        Assert.That(pgRows.Count(r => r.OrderId != 0), Is.EqualTo(3));
        var pgDefault = pgRows.Single(r => r.OrderId == 0);
        Assert.That(pgDefault.Total, Is.EqualTo(0m));
        Assert.That(pgDefault.Status, Is.Null);

        var myRows = await my.ExecuteFetchAllAsync();
        Assert.That(myRows, Has.Count.EqualTo(4));
        Assert.That(myRows.Count(r => r.OrderId != 0), Is.EqualTo(3));
        var myDefault = myRows.Single(r => r.OrderId == 0);
        Assert.That(myDefault.Total, Is.EqualTo(0m));
        Assert.That(myDefault.Status, Is.Null);

        var ssRows = await ss.ExecuteFetchAllAsync();
        Assert.That(ssRows, Has.Count.EqualTo(4));
        Assert.That(ssRows.Count(r => r.OrderId != 0), Is.EqualTo(3));
        var ssDefault = ssRows.Single(r => r.OrderId == 0);
        Assert.That(ssDefault.Total, Is.EqualTo(0m));
        Assert.That(ssDefault.Status, Is.Null);
    }

    [Test]
    public async Task LeftJoin_IntColumn_NullHandled_CrossDialect()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().LeftJoin<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.OrderId)).Prepare();
        var pg = Pg.Users().LeftJoin<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.OrderId)).Prepare();
        var my = My.Users().LeftJoin<My.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.OrderId)).Prepare();
        var ss = Ss.Users().LeftJoin<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.OrderId)).Prepare();

        var ltCharlie = (await lt.ExecuteFetchAllAsync()).Single(r => r.UserName == "Charlie");
        Assert.That(ltCharlie.OrderId, Is.EqualTo(0), "SQLite: join-nullable int defaults to 0");

        var pgCharlie = (await pg.ExecuteFetchAllAsync()).Single(r => r.UserName == "Charlie");
        Assert.That(pgCharlie.OrderId, Is.EqualTo(0), "PG: join-nullable int defaults to 0");

        var myCharlie = (await my.ExecuteFetchAllAsync()).Single(r => r.UserName == "Charlie");
        Assert.That(myCharlie.OrderId, Is.EqualTo(0), "MySQL: join-nullable int defaults to 0");

        var ssCharlie = (await ss.ExecuteFetchAllAsync()).Single(r => r.UserName == "Charlie");
        Assert.That(ssCharlie.OrderId, Is.EqualTo(0), "SQL Server: join-nullable int defaults to 0");
    }

    [Test]
    public async Task LeftJoin_EnumColumn_NullHandled_CrossDialect()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Order.Priority is OrderPriority enum (NOT NULL in schema)
        var lt = Lite.Users().LeftJoin<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Priority)).Prepare();
        var pg = Pg.Users().LeftJoin<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Priority)).Prepare();
        var my = My.Users().LeftJoin<My.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Priority)).Prepare();
        var ss = Ss.Users().LeftJoin<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Priority)).Prepare();

        var ltCharlie = (await lt.ExecuteFetchAllAsync()).Single(r => r.UserName == "Charlie");
        Assert.That(ltCharlie.Priority, Is.EqualTo(default(OrderPriority)), "SQLite: join-nullable enum defaults");

        var pgCharlie = (await pg.ExecuteFetchAllAsync()).Single(r => r.UserName == "Charlie");
        Assert.That(pgCharlie.Priority, Is.EqualTo(default(OrderPriority)), "PG: join-nullable enum defaults");

        var myCharlie = (await my.ExecuteFetchAllAsync()).Single(r => r.UserName == "Charlie");
        Assert.That(myCharlie.Priority, Is.EqualTo(default(OrderPriority)), "MySQL: join-nullable enum defaults");

        var ssCharlie = (await ss.ExecuteFetchAllAsync()).Single(r => r.UserName == "Charlie");
        Assert.That(ssCharlie.Priority, Is.EqualTo(default(OrderPriority)), "SQL Server: join-nullable enum defaults");
    }

    #endregion
}
