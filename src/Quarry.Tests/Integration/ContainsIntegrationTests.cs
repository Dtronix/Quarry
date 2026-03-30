using Quarry.Tests.Samples;

namespace Quarry.Tests.Integration;


/// <summary>
/// SQLite integration tests for collection .Contains() in Where clauses.
/// Exercises both IReadOnlyList (List, array) and IEnumerable (LINQ .Select()) paths.
/// </summary>
[TestFixture]
internal class ContainsIntegrationTests
{
    #region IReadOnlyList path (List<T>)

    [Test]
    public async Task Select_Where_ListContains_FiltersCorrectly()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var ids = new List<int> { 1, 3 };
        var results = await Lite.Users()
            .Where(u => ids.Contains(u.UserId))
            .Select(u => u.UserName)
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results, Does.Contain("Alice"));
        Assert.That(results, Does.Contain("Charlie"));
    }

    [Test]
    public async Task Delete_Where_ListContains_DeletesMatchingRows()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var ids = new List<int> { 2, 3 };
        var affected = await Lite.Users()
            .Delete()
            .Where(u => ids.Contains(u.UserId))
            .ExecuteNonQueryAsync();

        Assert.That(affected, Is.EqualTo(2));

        // Verify only Alice remains
        var remaining = await Lite.Users()
            .Select(u => u.UserName)
            .ExecuteFetchAllAsync();
        Assert.That(remaining, Has.Count.EqualTo(1));
        Assert.That(remaining[0], Is.EqualTo("Alice"));
    }

    #endregion

    #region IEnumerable path (LINQ .Select())

    [Test]
    public async Task Select_Where_EnumerableContains_FiltersCorrectly()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var sources = new[] { new { Id = 1 }, new { Id = 3 } };
        IEnumerable<int> ids = sources.Select(s => s.Id);
        var results = await Lite.Users()
            .Where(u => ids.Contains(u.UserId))
            .Select(u => u.UserName)
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results, Does.Contain("Alice"));
        Assert.That(results, Does.Contain("Charlie"));
    }

    [Test]
    public async Task Delete_Where_EnumerableContains_DeletesMatchingRows()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var sources = new[] { new { Id = 1 }, new { Id = 2 } };
        IEnumerable<int> ids = sources.Select(s => s.Id);
        var affected = await Lite.Users()
            .Delete()
            .Where(u => ids.Contains(u.UserId))
            .ExecuteNonQueryAsync();

        Assert.That(affected, Is.EqualTo(2));

        // Verify only Charlie remains
        var remaining = await Lite.Users()
            .Select(u => u.UserName)
            .ExecuteFetchAllAsync();
        Assert.That(remaining, Has.Count.EqualTo(1));
        Assert.That(remaining[0], Is.EqualTo("Charlie"));
    }

    #endregion
}
