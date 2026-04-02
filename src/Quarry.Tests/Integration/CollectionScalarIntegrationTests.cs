using Quarry.Tests.Samples;

namespace Quarry.Tests.Integration;

/// <summary>
/// Integration tests for collection + scalar parameter combinations.
/// Verifies that runtime parameter index shifting produces correct SQL
/// when array.Contains(column) is combined with scalar predicates.
/// Regression tests for #140: collection parameter collision.
/// </summary>
[TestFixture]
internal class CollectionScalarIntegrationTests
{
    [Test]
    public async Task Where_CollectionPlusScalar_ReturnsCorrectRows()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var ids = new List<int> { 1, 2, 3 };
        var minId = 1;
        var results = await Lite.Users()
            .Where(u => ids.Contains(u.UserId) && u.UserId >= minId)
            .Select(u => u.UserName)
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results, Does.Contain("Alice"));
        Assert.That(results, Does.Contain("Bob"));
        Assert.That(results, Does.Contain("Charlie"));
    }

    [Test]
    public async Task Where_CollectionPlusScalar_FiltersCorrectly()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var ids = new List<int> { 1, 2, 3 };
        var maxId = 2;
        var results = await Lite.Users()
            .Where(u => ids.Contains(u.UserId) && u.UserId <= maxId)
            .Select(u => u.UserName)
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results, Does.Contain("Alice"));
        Assert.That(results, Does.Contain("Bob"));
    }

    [Test]
    public async Task Where_ScalarPlusCollection_ReturnsCorrectRows()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        // Scalar before collection in the expression
        var ids = new List<int> { 1, 2, 3 };
        var maxId = 2;
        var results = await Lite.Users()
            .Where(u => u.UserId <= maxId && ids.Contains(u.UserId))
            .Select(u => u.UserName)
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results, Does.Contain("Alice"));
        Assert.That(results, Does.Contain("Bob"));
    }

    [Test]
    public async Task Where_EmptyCollectionPlusScalar_ReturnsNoRows()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var ids = new List<int>();
        var minId = 0;
        var results = await Lite.Users()
            .Where(u => ids.Contains(u.UserId) && u.UserId > minId)
            .Select(u => u.UserName)
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(0));
    }

    [Test]
    public async Task Where_SingleElementCollectionPlusScalar_ReturnsCorrectRows()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var ids = new List<int> { 1 };
        var minId = 0;
        var results = await Lite.Users()
            .Where(u => ids.Contains(u.UserId) && u.UserId > minId)
            .Select(u => u.UserName)
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo("Alice"));
    }

    [Test]
    public async Task Where_CollectionPlusScalar_WithPagination_ReturnsCorrectRows()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var ids = new List<int> { 1, 2, 3 };
        var minId = 0;
        var limit = 1;
        var results = await Lite.Users()
            .Where(u => ids.Contains(u.UserId) && u.UserId > minId)
            .Select(u => u.UserName)
            .Limit(limit)
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Where_LargeCollectionPlusScalar_ReturnsCorrectRows()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        // Large collection — ensures shift arithmetic works for many elements
        var ids = Enumerable.Range(1, 100).ToList();
        var minId = 0;
        var results = await Lite.Users()
            .Where(u => ids.Contains(u.UserId) && u.UserId > minId)
            .Select(u => u.UserName)
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(3));
    }
}
