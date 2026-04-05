using Quarry.Tests.Samples;

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
}
