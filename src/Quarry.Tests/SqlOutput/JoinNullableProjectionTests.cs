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
}
