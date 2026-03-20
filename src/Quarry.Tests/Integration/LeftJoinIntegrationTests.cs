using Quarry.Tests.Samples;

namespace Quarry.Tests.Integration;


[TestFixture]
internal class LeftJoinIntegrationTests : SqliteIntegrationTestBase
{
    [Test]
    public async Task LeftJoin_ReturnsAllLeftRowsIncludingUnmatched()
    {
        // Project only left-table columns to avoid NULL read errors on right-table columns
        var results = await Db.Users()
            .LeftJoin<Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => u.UserName)
            .ExecuteFetchAllAsync();

        // Alice has 2 orders, Bob has 1 order, Charlie has 0 orders (NULL row)
        Assert.That(results, Has.Count.EqualTo(4));
        Assert.That(results.Count(r => r == "Alice"), Is.EqualTo(2));
        Assert.That(results.Count(r => r == "Charlie"), Is.EqualTo(1));
    }

    [Test]
    public async Task LeftJoin_Where_LeftTable_FiltersCorrectly()
    {
        var results = await Db.Users()
            .LeftJoin<Order>((u, o) => u.UserId == o.UserId.Id)
            .Where((u, o) => u.IsActive)
            .Select((u, o) => (u.UserName, o.Total))
            .ExecuteFetchAllAsync();

        // Alice (active, 2 orders) + Bob (active, 1 order) = 3 rows
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results.Any(r => r.Item1 == "Charlie"), Is.False);
    }

    [Test]
    public async Task LeftJoin_Where_RightTable_FiltersCorrectly()
    {
        var results = await Db.Users()
            .LeftJoin<Order>((u, o) => u.UserId == o.UserId.Id)
            .Where((u, o) => o.Total > 100)
            .Select((u, o) => (u.UserName, o.Total))
            .ExecuteFetchAllAsync();

        // Only Alice+250.00 and Bob+150.00 match; Charlie's NULL row excluded by WHERE
        Assert.That(results, Has.Count.EqualTo(2));
    }
}
