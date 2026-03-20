using Quarry.Tests.Samples;

namespace Quarry.Tests.Integration;


[TestFixture]
internal class JoinIntegrationTests : SqliteIntegrationTestBase
{
    [Test]
    public async Task Join_InnerJoin_ReturnsMatchedRows()
    {
        var results = await Db.Users()
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total))
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0], Is.EqualTo(("Alice", 250.00m)));
        Assert.That(results[1], Is.EqualTo(("Alice", 75.50m)));
        Assert.That(results[2], Is.EqualTo(("Bob", 150.00m)));
    }

    [Test]
    public async Task Join_Where_LeftTable_FiltersCorrectly()
    {
        var results = await Db.Users()
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Where((u, o) => u.IsActive)
            .Select((u, o) => (u.UserName, o.Total))
            .ExecuteFetchAllAsync();

        // Alice (active) has 2 orders, Bob (active) has 1 order; Charlie (inactive) has none
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0], Is.EqualTo(("Alice", 250.00m)));
        Assert.That(results[1], Is.EqualTo(("Alice", 75.50m)));
        Assert.That(results[2], Is.EqualTo(("Bob", 150.00m)));
    }

    [Test]
    public async Task Join_Where_RightTable_FiltersCorrectly()
    {
        var results = await Db.Users()
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Where((u, o) => o.Total > 100)
            .Select((u, o) => (u.UserName, o.Total))
            .ExecuteFetchAllAsync();

        // Only orders with Total > 100: Alice+250.00, Bob+150.00
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo(("Alice", 250.00m)));
        Assert.That(results[1], Is.EqualTo(("Bob", 150.00m)));
    }
}
