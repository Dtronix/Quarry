using Quarry.Tests.Samples;

namespace Quarry.Tests.Integration;

#pragma warning disable QRY001

[TestFixture]
internal class ComplexIntegrationTests : SqliteIntegrationTestBase
{
    #region Where + Select

    [Test]
    public async Task Where_Comparison_Select_ReturnsFilteredData()
    {
        var results = await Db.Users
            .Where(u => u.UserId > 1)
            .Select(u => (u.UserId, u.UserName))
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((2, "Bob")));
        Assert.That(results[1], Is.EqualTo((3, "Charlie")));
    }

    #endregion

    #region Multiple Where + Select

    [Test]
    public async Task Where_NullAndBoolean_Select_Correct()
    {
        var results = await Db.Users
            .Where(u => u.Email != null)
            .Where(u => u.IsActive)
            .Select(u => (u.UserName, u.Email))
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Item1, Is.EqualTo("Alice"));
        Assert.That(results[0].Item2, Is.EqualTo("alice@test.com"));
    }

    [Test]
    public async Task Where_BoolAndComparison_Select_Correct()
    {
        var results = await Db.Users
            .Where(u => u.IsActive)
            .Where(u => u.UserId > 1)
            .Select(u => (u.UserId, u.UserName, u.Email))
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Item1, Is.EqualTo(2));
        Assert.That(results[0].Item2, Is.EqualTo("Bob"));
        Assert.That(results[0].Item3, Is.Null);
    }

    #endregion

    #region Distinct + Where + Select

    [Test]
    public async Task Distinct_Where_Select_ReturnsDistinctRows()
    {
        var results = await Db.Users
            .Distinct()
            .Where(u => u.IsActive)
            .Select(u => (u.UserName, u.Email))
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].Item1, Is.EqualTo("Alice"));
        Assert.That(results[0].Item2, Is.EqualTo("alice@test.com"));
        Assert.That(results[1].Item1, Is.EqualTo("Bob"));
        Assert.That(results[1].Item2, Is.Null);
    }

    #endregion

    #region Where + Select + Pagination

    [Test]
    public async Task Where_Select_LimitOffset_Correct()
    {
        var results = await Db.Users
            .Where(u => u.IsActive)
            .Select(u => (u.UserName, u.Email))
            .Limit(1).Offset(1)
            .ExecuteFetchAllAsync();

        // 2 active users (Alice, Bob); skip 1 → Bob
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Item1, Is.EqualTo("Bob"));
    }

    [Test]
    public async Task Where_Select_Limit_Correct()
    {
        var results = await Db.Users
            .Where(u => u.IsActive)
            .Select(u => (u.UserId, u.UserName))
            .Limit(1)
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
    }

    #endregion

    #region Orders Table — Complex

    [Test]
    public async Task Orders_Where_Select_Correct()
    {
        var results = await Db.Orders
            .Where(o => o.Total > 100)
            .Select(o => (o.OrderId, o.Total, o.Status))
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, 250.00m, "Shipped")));
        Assert.That(results[1], Is.EqualTo((3, 150.00m, "Shipped")));
    }

    #endregion

    #region Join + Where + Select

    [Test]
    public async Task Join_Where_Boolean_Select_Correct()
    {
        var results = await Db.Users
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Where((u, o) => u.IsActive)
            .Select((u, o) => (u.UserName, o.Total))
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0], Is.EqualTo(("Alice", 250.00m)));
        Assert.That(results[1], Is.EqualTo(("Alice", 75.50m)));
        Assert.That(results[2], Is.EqualTo(("Bob", 150.00m)));
    }

    [Test]
    public async Task Join_Where_RightTable_Select_Correct()
    {
        var results = await Db.Users
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Where((u, o) => o.Total > 50)
            .Select((u, o) => (u.UserName, o.Total, o.Status))
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0], Is.EqualTo(("Alice", 250.00m, "Shipped")));
        Assert.That(results[1], Is.EqualTo(("Alice", 75.50m, "Pending")));
        Assert.That(results[2], Is.EqualTo(("Bob", 150.00m, "Shipped")));
    }

    #endregion
}
