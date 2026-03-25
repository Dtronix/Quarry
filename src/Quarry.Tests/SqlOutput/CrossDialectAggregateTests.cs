using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;


[TestFixture]
internal class CrossDialectAggregateTests
{
    #region GroupBy

    [Test]
    public async Task GroupBy_SingleColumn()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Count())).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Count())).ToDiagnostics(),
            My.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Count())).ToDiagnostics(),
            Ss.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Count())).ToDiagnostics(),
            sqlite: "SELECT \"Status\", COUNT(*) AS \"Item2\" FROM \"orders\" GROUP BY \"Status\"",
            pg:     "SELECT \"Status\", COUNT(*) AS \"Item2\" FROM \"orders\" GROUP BY \"Status\"",
            mysql:  "SELECT `Status`, COUNT(*) AS `Item2` FROM `orders` GROUP BY `Status`",
            ss:     "SELECT [Status], COUNT(*) AS [Item2] FROM [orders] GROUP BY [Status]");

        // Seed has 2 "Shipped" orders and 1 "Pending" — verify "Shipped" count = 2
        var results = await lite.ExecuteFetchAllAsync();
        var shipped = results.First(r => r.Item1 == "Shipped");
        Assert.That(shipped.Item2, Is.EqualTo(2));
    }

    #endregion

    #region Having

    [Test]
    public async Task Having_CountGreaterThan()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Orders().Where(o => true).GroupBy(o => o.Status).Having(o => Sql.Count() > 5).Select(o => (o.Status, Sql.Count())).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Orders().Where(o => true).GroupBy(o => o.Status).Having(o => Sql.Count() > 5).Select(o => (o.Status, Sql.Count())).ToDiagnostics(),
            My.Orders().Where(o => true).GroupBy(o => o.Status).Having(o => Sql.Count() > 5).Select(o => (o.Status, Sql.Count())).ToDiagnostics(),
            Ss.Orders().Where(o => true).GroupBy(o => o.Status).Having(o => Sql.Count() > 5).Select(o => (o.Status, Sql.Count())).ToDiagnostics(),
            sqlite: "SELECT \"Status\", COUNT(*) AS \"Item2\" FROM \"orders\" GROUP BY \"Status\" HAVING COUNT(*) > 5",
            pg:     "SELECT \"Status\", COUNT(*) AS \"Item2\" FROM \"orders\" GROUP BY \"Status\" HAVING COUNT(*) > 5",
            mysql:  "SELECT `Status`, COUNT(*) AS `Item2` FROM `orders` GROUP BY `Status` HAVING COUNT(*) > 5",
            ss:     "SELECT [Status], COUNT(*) AS [Item2] FROM [orders] GROUP BY [Status] HAVING COUNT(*) > 5");

        // No status has count > 5 with only 3 orders — 0 rows returned
        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(0));
    }

    #endregion

    #region Aggregate Functions

    [Test]
    public async Task Select_Count_Sum()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Count(), Sql.Sum(o.Total))).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Count(), Sql.Sum(o.Total))).ToDiagnostics(),
            My.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Count(), Sql.Sum(o.Total))).ToDiagnostics(),
            Ss.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Count(), Sql.Sum(o.Total))).ToDiagnostics(),
            sqlite: "SELECT \"Status\", COUNT(*) AS \"Item2\", SUM(\"Total\") AS \"Item3\" FROM \"orders\" GROUP BY \"Status\"",
            pg:     "SELECT \"Status\", COUNT(*) AS \"Item2\", SUM(\"Total\") AS \"Item3\" FROM \"orders\" GROUP BY \"Status\"",
            mysql:  "SELECT `Status`, COUNT(*) AS `Item2`, SUM(\"Total\") AS `Item3` FROM `orders` GROUP BY `Status`",
            ss:     "SELECT [Status], COUNT(*) AS [Item2], SUM(\"Total\") AS [Item3] FROM [orders] GROUP BY [Status]");

        // "Shipped" has orders 250.00 + 150.00 = 400.00
        var results = await lite.ExecuteFetchAllAsync();
        var shipped = results.First(r => r.Item1 == "Shipped");
        Assert.That(shipped.Item2, Is.EqualTo(2));
        Assert.That(shipped.Item3, Is.EqualTo(400.00m).Within(0.01m));
    }

    #endregion

    #region Avg / Min / Max

    [Test]
    public async Task GroupBy_Select_WithAvg()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Avg(o.Total))).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Avg(o.Total))).ToDiagnostics(),
            My.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Avg(o.Total))).ToDiagnostics(),
            Ss.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Avg(o.Total))).ToDiagnostics(),
            sqlite: "SELECT \"Status\", AVG(\"Total\") AS \"Item2\" FROM \"orders\" GROUP BY \"Status\"",
            pg:     "SELECT \"Status\", AVG(\"Total\") AS \"Item2\" FROM \"orders\" GROUP BY \"Status\"",
            mysql:  "SELECT `Status`, AVG(\"Total\") AS `Item2` FROM `orders` GROUP BY `Status`",
            ss:     "SELECT [Status], AVG(\"Total\") AS [Item2] FROM [orders] GROUP BY [Status]");

        // "Shipped" avg = (250 + 150) / 2 = 200.00
        var results = await lite.ExecuteFetchAllAsync();
        var shipped = results.First(r => r.Item1 == "Shipped");
        Assert.That(shipped.Item2, Is.EqualTo(200.00m).Within(0.01m));
    }

    [Test]
    public async Task GroupBy_Select_WithMin()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Min(o.Total))).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Min(o.Total))).ToDiagnostics(),
            My.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Min(o.Total))).ToDiagnostics(),
            Ss.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Min(o.Total))).ToDiagnostics(),
            sqlite: "SELECT \"Status\", MIN(\"Total\") AS \"Item2\" FROM \"orders\" GROUP BY \"Status\"",
            pg:     "SELECT \"Status\", MIN(\"Total\") AS \"Item2\" FROM \"orders\" GROUP BY \"Status\"",
            mysql:  "SELECT `Status`, MIN(\"Total\") AS `Item2` FROM `orders` GROUP BY `Status`",
            ss:     "SELECT [Status], MIN(\"Total\") AS [Item2] FROM [orders] GROUP BY [Status]");

        // "Shipped" min = 150.00
        var results = await lite.ExecuteFetchAllAsync();
        var shipped = results.First(r => r.Item1 == "Shipped");
        Assert.That(shipped.Item2, Is.EqualTo(150.00m).Within(0.01m));
    }

    [Test]
    public async Task GroupBy_Select_WithMax()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Max(o.Total))).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Max(o.Total))).ToDiagnostics(),
            My.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Max(o.Total))).ToDiagnostics(),
            Ss.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Max(o.Total))).ToDiagnostics(),
            sqlite: "SELECT \"Status\", MAX(\"Total\") AS \"Item2\" FROM \"orders\" GROUP BY \"Status\"",
            pg:     "SELECT \"Status\", MAX(\"Total\") AS \"Item2\" FROM \"orders\" GROUP BY \"Status\"",
            mysql:  "SELECT `Status`, MAX(\"Total\") AS `Item2` FROM `orders` GROUP BY `Status`",
            ss:     "SELECT [Status], MAX(\"Total\") AS [Item2] FROM [orders] GROUP BY [Status]");

        // "Shipped" max = 250.00
        var results = await lite.ExecuteFetchAllAsync();
        var shipped = results.First(r => r.Item1 == "Shipped");
        Assert.That(shipped.Item2, Is.EqualTo(250.00m).Within(0.01m));
    }

    [Test]
    public async Task Having_CountGreaterThan1_OnlyShippedQualifies()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Orders().Where(o => true).GroupBy(o => o.Status).Having(o => Sql.Count() > 1).Select(o => (o.Status, Sql.Count())).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Orders().Where(o => true).GroupBy(o => o.Status).Having(o => Sql.Count() > 1).Select(o => (o.Status, Sql.Count())).ToDiagnostics(),
            My.Orders().Where(o => true).GroupBy(o => o.Status).Having(o => Sql.Count() > 1).Select(o => (o.Status, Sql.Count())).ToDiagnostics(),
            Ss.Orders().Where(o => true).GroupBy(o => o.Status).Having(o => Sql.Count() > 1).Select(o => (o.Status, Sql.Count())).ToDiagnostics(),
            sqlite: "SELECT \"Status\", COUNT(*) AS \"Item2\" FROM \"orders\" GROUP BY \"Status\" HAVING COUNT(*) > 1",
            pg:     "SELECT \"Status\", COUNT(*) AS \"Item2\" FROM \"orders\" GROUP BY \"Status\" HAVING COUNT(*) > 1",
            mysql:  "SELECT `Status`, COUNT(*) AS `Item2` FROM `orders` GROUP BY `Status` HAVING COUNT(*) > 1",
            ss:     "SELECT [Status], COUNT(*) AS [Item2] FROM [orders] GROUP BY [Status] HAVING COUNT(*) > 1");

        // Only "Shipped" has count > 1 (2 orders), "Pending" has only 1
        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Item1, Is.EqualTo("Shipped"));
        Assert.That(results[0].Item2, Is.EqualTo(2));
    }

    #endregion
}
