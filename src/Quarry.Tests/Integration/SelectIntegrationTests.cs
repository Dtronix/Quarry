using Quarry.Tests.Samples;

namespace Quarry.Tests.Integration;

#pragma warning disable QRY001

[TestFixture]
internal class SelectIntegrationTests : SqliteIntegrationTestBase
{
    [Test]
    public async Task Select_Tuple_TwoColumns_ReturnsCorrectData()
    {
        var results = await Db.Users()
            .Select(u => (u.UserId, u.UserName))
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
        Assert.That(results[1], Is.EqualTo((2, "Bob")));
        Assert.That(results[2], Is.EqualTo((3, "Charlie")));
    }

    [Test]
    public async Task Select_Tuple_ThreeColumns_ReturnsCorrectData()
    {
        var results = await Db.Users()
            .Select(u => (u.UserId, u.UserName, u.IsActive))
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0], Is.EqualTo((1, "Alice", true)));
        Assert.That(results[1], Is.EqualTo((2, "Bob", true)));
        Assert.That(results[2], Is.EqualTo((3, "Charlie", false)));
    }

    [Test]
    public async Task Select_Dto_UserSummary_ReturnsCorrectData()
    {
        var results = await Db.Users()
            .Select(u => new UserSummaryDto
            {
                UserId = u.UserId,
                UserName = u.UserName,
                IsActive = u.IsActive
            })
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(3));

        Assert.That(results[0].UserId, Is.EqualTo(1));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
        Assert.That(results[0].IsActive, Is.True);

        Assert.That(results[1].UserId, Is.EqualTo(2));
        Assert.That(results[1].UserName, Is.EqualTo("Bob"));
        Assert.That(results[1].IsActive, Is.True);

        Assert.That(results[2].UserId, Is.EqualTo(3));
        Assert.That(results[2].UserName, Is.EqualTo("Charlie"));
        Assert.That(results[2].IsActive, Is.False);
    }

    [Test]
    public async Task Select_Dto_UserWithEmail_ReturnsCorrectData()
    {
        var results = await Db.Users()
            .Select(u => new UserWithEmailDto
            {
                UserId = u.UserId,
                UserName = u.UserName,
                Email = u.Email
            })
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(3));

        Assert.That(results[0].UserId, Is.EqualTo(1));
        Assert.That(results[0].Email, Is.EqualTo("alice@test.com"));

        Assert.That(results[1].UserId, Is.EqualTo(2));
        Assert.That(results[1].Email, Is.Null);

        Assert.That(results[2].UserId, Is.EqualTo(3));
        Assert.That(results[2].Email, Is.EqualTo("charlie@test.com"));
    }

    [Test]
    public async Task Select_OrdersTable_Tuple_ReturnsCorrectData()
    {
        var results = await Db.Orders()
            .Select(o => (o.OrderId, o.Total))
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0], Is.EqualTo((1, 250.00m)));
        Assert.That(results[1], Is.EqualTo((2, 75.50m)));
        Assert.That(results[2], Is.EqualTo((3, 150.00m)));
    }

    [Test]
    public async Task Select_LimitOffset_ReturnsPagedData()
    {
        var results = await Db.Users()
            .Select(u => (u.UserId, u.UserName))
            .Limit(2).Offset(1)
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((2, "Bob")));
        Assert.That(results[1], Is.EqualTo((3, "Charlie")));
    }
}
