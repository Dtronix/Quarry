using Quarry;
using Quarry.Tests.Samples;

namespace Quarry.Tests.Integration;

#pragma warning disable QRY001

/// <summary>
/// Integration tests for aggregate functions in tuple projections (Issue #49).
/// Verifies that Sql.Avg(), Sql.Min(), Sql.Max() resolve correct return types
/// and execute successfully against a real SQLite database.
/// </summary>
[TestFixture]
internal class AggregateIntegrationTests : SqliteIntegrationTestBase
{
    [Test]
    public async Task GroupBy_SelectWithCount_ReturnsCorrectData()
    {
        var results = await Db.Orders()
            .Where(o => true)
            .GroupBy(o => o.Status)
            .Select(o => (o.Status, Sql.Count()))
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.GreaterThan(0));
        // "Shipped" has 2 orders, "Pending" has 1
        var shipped = results.FirstOrDefault(r => r.Item1 == "Shipped");
        Assert.That(shipped.Item2, Is.EqualTo(2));
    }

    [Test]
    public async Task GroupBy_SelectWithSum_ReturnsCorrectData()
    {
        var results = await Db.Orders()
            .Where(o => true)
            .GroupBy(o => o.Status)
            .Select(o => (o.Status, Sql.Sum(o.Total)))
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.GreaterThan(0));
        var shipped = results.FirstOrDefault(r => r.Item1 == "Shipped");
        Assert.That(shipped.Item2, Is.EqualTo(400.00m));
    }

    [Test]
    public async Task GroupBy_SelectWithAvg_ReturnsCorrectData()
    {
        var results = await Db.Orders()
            .Where(o => true)
            .GroupBy(o => o.Status)
            .Select(o => (o.Status, Sql.Avg(o.Total)))
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.GreaterThan(0));
        // Shipped: AVG(250, 150) = 200
        var shipped = results.FirstOrDefault(r => r.Item1 == "Shipped");
        Assert.That(shipped.Item2, Is.EqualTo(200.00m));
    }

    [Test]
    public async Task GroupBy_SelectWithMin_ReturnsCorrectData()
    {
        var results = await Db.Orders()
            .Where(o => true)
            .GroupBy(o => o.Status)
            .Select(o => (o.Status, Sql.Min(o.Total)))
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.GreaterThan(0));
        // Shipped: MIN(250, 150) = 150
        var shipped = results.FirstOrDefault(r => r.Item1 == "Shipped");
        Assert.That(shipped.Item2, Is.EqualTo(150.00m));
    }

    [Test]
    public async Task GroupBy_SelectWithMax_ReturnsCorrectData()
    {
        var results = await Db.Orders()
            .Where(o => true)
            .GroupBy(o => o.Status)
            .Select(o => (o.Status, Sql.Max(o.Total)))
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.GreaterThan(0));
        // Shipped: MAX(250, 150) = 250
        var shipped = results.FirstOrDefault(r => r.Item1 == "Shipped");
        Assert.That(shipped.Item2, Is.EqualTo(250.00m));
    }

    [Test]
    public async Task GroupBy_SelectWithMultipleAggregates_ReturnsCorrectData()
    {
        var results = await Db.Orders()
            .Where(o => true)
            .GroupBy(o => o.Status)
            .Select(o => (o.Status, Sql.Count(), Sql.Sum(o.Total), Sql.Avg(o.Total)))
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.GreaterThan(0));
        var shipped = results.FirstOrDefault(r => r.Item1 == "Shipped");
        Assert.That(shipped.Item2, Is.EqualTo(2));       // COUNT
        Assert.That(shipped.Item3, Is.EqualTo(400.00m));  // SUM
        Assert.That(shipped.Item4, Is.EqualTo(200.00m));  // AVG
    }

    [Test]
    public async Task GroupBy_Having_SelectWithAggregates_FiltersGroups()
    {
        var results = await Db.Orders()
            .Where(o => true)
            .GroupBy(o => o.Status)
            .Having(o => Sql.Count() > 1)
            .Select(o => (o.Status, Sql.Count(), Sql.Avg(o.Total)))
            .ExecuteFetchAllAsync();

        // Only "Shipped" has count > 1
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Item1, Is.EqualTo("Shipped"));
        Assert.That(results[0].Item2, Is.EqualTo(2));
    }
}
