using Quarry.Tests.Samples;

namespace Quarry.Tests.Integration;


[TestFixture]
internal class WhereIntegrationTests : SqliteIntegrationTestBase
{
    #region Boolean

    [Test]
    public async Task Where_Boolean_FiltersCorrectly()
    {
        var results = await Db.Users()
            .Where(u => u.IsActive)
            .Select(u => (u.UserId, u.UserName))
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
        Assert.That(results[1], Is.EqualTo((2, "Bob")));
    }

    [Test]
    public async Task Where_NegatedBoolean_FiltersCorrectly()
    {
        var results = await Db.Users()
            .Where(u => !u.IsActive)
            .Select(u => (u.UserId, u.UserName))
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo((3, "Charlie")));
    }

    #endregion

    #region Comparison Operators

    [Test]
    public async Task Where_GreaterThan_FiltersCorrectly()
    {
        var results = await Db.Users()
            .Where(u => u.UserId > 1)
            .Select(u => (u.UserId, u.UserName))
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((2, "Bob")));
        Assert.That(results[1], Is.EqualTo((3, "Charlie")));
    }

    [Test]
    public async Task Where_LessThanOrEqual_FiltersCorrectly()
    {
        var results = await Db.Users()
            .Where(u => u.UserId <= 2)
            .Select(u => (u.UserId, u.UserName))
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
        Assert.That(results[1], Is.EqualTo((2, "Bob")));
    }

    #endregion

    #region Null Checks

    [Test]
    public async Task Where_IsNull_FiltersCorrectly()
    {
        var results = await Db.Users()
            .Where(u => u.Email == null)
            .Select(u => (u.UserId, u.UserName))
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo((2, "Bob")));
    }

    [Test]
    public async Task Where_IsNotNull_FiltersCorrectly()
    {
        var results = await Db.Users()
            .Where(u => u.Email != null)
            .Select(u => (u.UserId, u.UserName))
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
        Assert.That(results[1], Is.EqualTo((3, "Charlie")));
    }

    #endregion

    #region Chained Where

    [Test]
    public async Task Where_MultipleChained_FiltersCorrectly()
    {
        var results = await Db.Users()
            .Where(u => u.IsActive)
            .Where(u => u.UserId > 0)
            .Select(u => (u.UserId, u.UserName))
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
        Assert.That(results[1], Is.EqualTo((2, "Bob")));
    }

    [Test]
    public async Task Where_NullCheck_And_Boolean_FiltersCorrectly()
    {
        var results = await Db.Users()
            .Where(u => u.Email != null)
            .Where(u => u.IsActive)
            .Select(u => (u.UserId, u.UserName))
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
    }

    #endregion

    #region Where + Select Combined

    [Test]
    public async Task Where_ThenSelect_Tuple_ReturnsCorrectData()
    {
        var results = await Db.Users()
            .Where(u => u.IsActive)
            .Select(u => (u.UserId, u.UserName))
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
        Assert.That(results[1], Is.EqualTo((2, "Bob")));
    }

    [Test]
    public async Task Where_ThenSelect_Dto_ReturnsCorrectData()
    {
        var results = await Db.Users()
            .Where(u => u.IsActive)
            .Select(u => new UserSummaryDto
            {
                UserId = u.UserId,
                UserName = u.UserName,
                IsActive = u.IsActive
            })
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(2));

        Assert.That(results[0].UserId, Is.EqualTo(1));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
        Assert.That(results[0].IsActive, Is.True);

        Assert.That(results[1].UserId, Is.EqualTo(2));
        Assert.That(results[1].UserName, Is.EqualTo("Bob"));
        Assert.That(results[1].IsActive, Is.True);
    }

    #endregion
}
