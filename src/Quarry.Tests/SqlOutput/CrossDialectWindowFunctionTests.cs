using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;

[TestFixture]
internal class CrossDialectWindowFunctionTests
{
    #region Ranking Functions

    [Test]
    public async Task WindowFunction_RowNumber_OrderBy()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Orders().Where(o => true).Select(o => (o.OrderId, RowNum: Sql.RowNumber(over => over.OrderBy(o.OrderDate)))).Prepare();
        var pg = Pg.Orders().Where(o => true).Select(o => (o.OrderId, RowNum: Sql.RowNumber(over => over.OrderBy(o.OrderDate)))).Prepare();
        var my = My.Orders().Where(o => true).Select(o => (o.OrderId, RowNum: Sql.RowNumber(over => over.OrderBy(o.OrderDate)))).Prepare();
        var ss = Ss.Orders().Where(o => true).Select(o => (o.OrderId, RowNum: Sql.RowNumber(over => over.OrderBy(o.OrderDate)))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", ROW_NUMBER() OVER (ORDER BY \"OrderDate\") AS \"RowNum\" FROM \"orders\"",
            pg:     "SELECT \"OrderId\", ROW_NUMBER() OVER (ORDER BY \"OrderDate\") AS \"RowNum\" FROM \"orders\"",
            mysql:  "SELECT `OrderId`, ROW_NUMBER() OVER (ORDER BY `OrderDate`) AS `RowNum` FROM `orders`",
            ss:     "SELECT [OrderId], ROW_NUMBER() OVER (ORDER BY [OrderDate]) AS [RowNum] FROM [orders]");

        // Verify execution — ordered by OrderDate: order 1 (2024-06-01), 2 (2024-06-15), 3 (2024-07-01)
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        var byRowNum = results.OrderBy(r => r.RowNum).ToList();
        Assert.That(byRowNum[0].OrderId, Is.EqualTo(1));
        Assert.That(byRowNum[1].OrderId, Is.EqualTo(2));
        Assert.That(byRowNum[2].OrderId, Is.EqualTo(3));
    }

    [Test]
    public async Task WindowFunction_Rank_PartitionBy_OrderBy()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Orders().Where(o => true).Select(o => (o.OrderId, Rnk: Sql.Rank(over => over.PartitionBy(o.Status).OrderBy(o.Total)))).Prepare();
        var pg = Pg.Orders().Where(o => true).Select(o => (o.OrderId, Rnk: Sql.Rank(over => over.PartitionBy(o.Status).OrderBy(o.Total)))).Prepare();
        var my = My.Orders().Where(o => true).Select(o => (o.OrderId, Rnk: Sql.Rank(over => over.PartitionBy(o.Status).OrderBy(o.Total)))).Prepare();
        var ss = Ss.Orders().Where(o => true).Select(o => (o.OrderId, Rnk: Sql.Rank(over => over.PartitionBy(o.Status).OrderBy(o.Total)))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", RANK() OVER (PARTITION BY \"Status\" ORDER BY \"Total\") AS \"Rnk\" FROM \"orders\"",
            pg:     "SELECT \"OrderId\", RANK() OVER (PARTITION BY \"Status\" ORDER BY \"Total\") AS \"Rnk\" FROM \"orders\"",
            mysql:  "SELECT `OrderId`, RANK() OVER (PARTITION BY `Status` ORDER BY `Total`) AS `Rnk` FROM `orders`",
            ss:     "SELECT [OrderId], RANK() OVER (PARTITION BY [Status] ORDER BY [Total]) AS [Rnk] FROM [orders]");
    }

    [Test]
    public async Task WindowFunction_DenseRank_OrderByDescending()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Orders().Where(o => true).Select(o => (o.OrderId, DRnk: Sql.DenseRank(over => over.OrderByDescending(o.Total)))).Prepare();
        var pg = Pg.Orders().Where(o => true).Select(o => (o.OrderId, DRnk: Sql.DenseRank(over => over.OrderByDescending(o.Total)))).Prepare();
        var my = My.Orders().Where(o => true).Select(o => (o.OrderId, DRnk: Sql.DenseRank(over => over.OrderByDescending(o.Total)))).Prepare();
        var ss = Ss.Orders().Where(o => true).Select(o => (o.OrderId, DRnk: Sql.DenseRank(over => over.OrderByDescending(o.Total)))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", DENSE_RANK() OVER (ORDER BY \"Total\" DESC) AS \"DRnk\" FROM \"orders\"",
            pg:     "SELECT \"OrderId\", DENSE_RANK() OVER (ORDER BY \"Total\" DESC) AS \"DRnk\" FROM \"orders\"",
            mysql:  "SELECT `OrderId`, DENSE_RANK() OVER (ORDER BY `Total` DESC) AS `DRnk` FROM `orders`",
            ss:     "SELECT [OrderId], DENSE_RANK() OVER (ORDER BY [Total] DESC) AS [DRnk] FROM [orders]");

        // Descending order: 250 > 150 > 75.50 → ranks 1, 2, 3
        var results = await lt.ExecuteFetchAllAsync();
        var order1 = results.First(r => r.OrderId == 1); // Total=250
        Assert.That(order1.DRnk, Is.EqualTo(1));
    }

    [Test]
    public async Task WindowFunction_Ntile()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Orders().Where(o => true).Select(o => (o.OrderId, Grp: Sql.Ntile(2, over => over.OrderBy(o.OrderDate)))).Prepare();
        var pg = Pg.Orders().Where(o => true).Select(o => (o.OrderId, Grp: Sql.Ntile(2, over => over.OrderBy(o.OrderDate)))).Prepare();
        var my = My.Orders().Where(o => true).Select(o => (o.OrderId, Grp: Sql.Ntile(2, over => over.OrderBy(o.OrderDate)))).Prepare();
        var ss = Ss.Orders().Where(o => true).Select(o => (o.OrderId, Grp: Sql.Ntile(2, over => over.OrderBy(o.OrderDate)))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", NTILE(2) OVER (ORDER BY \"OrderDate\") AS \"Grp\" FROM \"orders\"",
            pg:     "SELECT \"OrderId\", NTILE(2) OVER (ORDER BY \"OrderDate\") AS \"Grp\" FROM \"orders\"",
            mysql:  "SELECT `OrderId`, NTILE(2) OVER (ORDER BY `OrderDate`) AS `Grp` FROM `orders`",
            ss:     "SELECT [OrderId], NTILE(2) OVER (ORDER BY [OrderDate]) AS [Grp] FROM [orders]");
    }

    #endregion

    #region Value Functions

    [Test]
    public async Task WindowFunction_Lag_Simple()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Orders().Where(o => true).Select(o => (o.OrderId, PrevTotal: Sql.Lag(o.Total, over => over.OrderBy(o.OrderDate)))).Prepare();
        var pg = Pg.Orders().Where(o => true).Select(o => (o.OrderId, PrevTotal: Sql.Lag(o.Total, over => over.OrderBy(o.OrderDate)))).Prepare();
        var my = My.Orders().Where(o => true).Select(o => (o.OrderId, PrevTotal: Sql.Lag(o.Total, over => over.OrderBy(o.OrderDate)))).Prepare();
        var ss = Ss.Orders().Where(o => true).Select(o => (o.OrderId, PrevTotal: Sql.Lag(o.Total, over => over.OrderBy(o.OrderDate)))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", LAG(\"Total\") OVER (ORDER BY \"OrderDate\") AS \"PrevTotal\" FROM \"orders\"",
            pg:     "SELECT \"OrderId\", LAG(\"Total\") OVER (ORDER BY \"OrderDate\") AS \"PrevTotal\" FROM \"orders\"",
            mysql:  "SELECT `OrderId`, LAG(`Total`) OVER (ORDER BY `OrderDate`) AS `PrevTotal` FROM `orders`",
            ss:     "SELECT [OrderId], LAG([Total]) OVER (ORDER BY [OrderDate]) AS [PrevTotal] FROM [orders]");
    }

    [Test]
    public async Task WindowFunction_Lag_WithOffset()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Orders().Where(o => true).Select(o => (o.OrderId, PrevTotal: Sql.Lag(o.Total, 2, over => over.OrderBy(o.OrderDate)))).Prepare();
        var pg = Pg.Orders().Where(o => true).Select(o => (o.OrderId, PrevTotal: Sql.Lag(o.Total, 2, over => over.OrderBy(o.OrderDate)))).Prepare();
        var my = My.Orders().Where(o => true).Select(o => (o.OrderId, PrevTotal: Sql.Lag(o.Total, 2, over => over.OrderBy(o.OrderDate)))).Prepare();
        var ss = Ss.Orders().Where(o => true).Select(o => (o.OrderId, PrevTotal: Sql.Lag(o.Total, 2, over => over.OrderBy(o.OrderDate)))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", LAG(\"Total\", 2) OVER (ORDER BY \"OrderDate\") AS \"PrevTotal\" FROM \"orders\"",
            pg:     "SELECT \"OrderId\", LAG(\"Total\", 2) OVER (ORDER BY \"OrderDate\") AS \"PrevTotal\" FROM \"orders\"",
            mysql:  "SELECT `OrderId`, LAG(`Total`, 2) OVER (ORDER BY `OrderDate`) AS `PrevTotal` FROM `orders`",
            ss:     "SELECT [OrderId], LAG([Total], 2) OVER (ORDER BY [OrderDate]) AS [PrevTotal] FROM [orders]");
    }

    [Test]
    public async Task WindowFunction_Lead_Simple()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Orders().Where(o => true).Select(o => (o.OrderId, NextTotal: Sql.Lead(o.Total, over => over.OrderBy(o.OrderDate)))).Prepare();
        var pg = Pg.Orders().Where(o => true).Select(o => (o.OrderId, NextTotal: Sql.Lead(o.Total, over => over.OrderBy(o.OrderDate)))).Prepare();
        var my = My.Orders().Where(o => true).Select(o => (o.OrderId, NextTotal: Sql.Lead(o.Total, over => over.OrderBy(o.OrderDate)))).Prepare();
        var ss = Ss.Orders().Where(o => true).Select(o => (o.OrderId, NextTotal: Sql.Lead(o.Total, over => over.OrderBy(o.OrderDate)))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", LEAD(\"Total\") OVER (ORDER BY \"OrderDate\") AS \"NextTotal\" FROM \"orders\"",
            pg:     "SELECT \"OrderId\", LEAD(\"Total\") OVER (ORDER BY \"OrderDate\") AS \"NextTotal\" FROM \"orders\"",
            mysql:  "SELECT `OrderId`, LEAD(`Total`) OVER (ORDER BY `OrderDate`) AS `NextTotal` FROM `orders`",
            ss:     "SELECT [OrderId], LEAD([Total]) OVER (ORDER BY [OrderDate]) AS [NextTotal] FROM [orders]");
    }

    [Test]
    public async Task WindowFunction_FirstValue()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Orders().Where(o => true).Select(o => (o.OrderId, First: Sql.FirstValue(o.Total, over => over.PartitionBy(o.Status).OrderBy(o.OrderDate)))).Prepare();
        var pg = Pg.Orders().Where(o => true).Select(o => (o.OrderId, First: Sql.FirstValue(o.Total, over => over.PartitionBy(o.Status).OrderBy(o.OrderDate)))).Prepare();
        var my = My.Orders().Where(o => true).Select(o => (o.OrderId, First: Sql.FirstValue(o.Total, over => over.PartitionBy(o.Status).OrderBy(o.OrderDate)))).Prepare();
        var ss = Ss.Orders().Where(o => true).Select(o => (o.OrderId, First: Sql.FirstValue(o.Total, over => over.PartitionBy(o.Status).OrderBy(o.OrderDate)))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", FIRST_VALUE(\"Total\") OVER (PARTITION BY \"Status\" ORDER BY \"OrderDate\") AS \"First\" FROM \"orders\"",
            pg:     "SELECT \"OrderId\", FIRST_VALUE(\"Total\") OVER (PARTITION BY \"Status\" ORDER BY \"OrderDate\") AS \"First\" FROM \"orders\"",
            mysql:  "SELECT `OrderId`, FIRST_VALUE(`Total`) OVER (PARTITION BY `Status` ORDER BY `OrderDate`) AS `First` FROM `orders`",
            ss:     "SELECT [OrderId], FIRST_VALUE([Total]) OVER (PARTITION BY [Status] ORDER BY [OrderDate]) AS [First] FROM [orders]");
    }

    [Test]
    public async Task WindowFunction_LastValue()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Orders().Where(o => true).Select(o => (o.OrderId, Last: Sql.LastValue(o.Total, over => over.PartitionBy(o.Status).OrderBy(o.OrderDate)))).Prepare();
        var pg = Pg.Orders().Where(o => true).Select(o => (o.OrderId, Last: Sql.LastValue(o.Total, over => over.PartitionBy(o.Status).OrderBy(o.OrderDate)))).Prepare();
        var my = My.Orders().Where(o => true).Select(o => (o.OrderId, Last: Sql.LastValue(o.Total, over => over.PartitionBy(o.Status).OrderBy(o.OrderDate)))).Prepare();
        var ss = Ss.Orders().Where(o => true).Select(o => (o.OrderId, Last: Sql.LastValue(o.Total, over => over.PartitionBy(o.Status).OrderBy(o.OrderDate)))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", LAST_VALUE(\"Total\") OVER (PARTITION BY \"Status\" ORDER BY \"OrderDate\") AS \"Last\" FROM \"orders\"",
            pg:     "SELECT \"OrderId\", LAST_VALUE(\"Total\") OVER (PARTITION BY \"Status\" ORDER BY \"OrderDate\") AS \"Last\" FROM \"orders\"",
            mysql:  "SELECT `OrderId`, LAST_VALUE(`Total`) OVER (PARTITION BY `Status` ORDER BY `OrderDate`) AS `Last` FROM `orders`",
            ss:     "SELECT [OrderId], LAST_VALUE([Total]) OVER (PARTITION BY [Status] ORDER BY [OrderDate]) AS [Last] FROM [orders]");
    }

    #endregion

    #region Aggregate OVER

    [Test]
    public async Task WindowFunction_SumOver()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Orders().Where(o => true).Select(o => (o.OrderId, RunSum: Sql.Sum(o.Total, over => over.PartitionBy(o.Status)))).Prepare();
        var pg = Pg.Orders().Where(o => true).Select(o => (o.OrderId, RunSum: Sql.Sum(o.Total, over => over.PartitionBy(o.Status)))).Prepare();
        var my = My.Orders().Where(o => true).Select(o => (o.OrderId, RunSum: Sql.Sum(o.Total, over => over.PartitionBy(o.Status)))).Prepare();
        var ss = Ss.Orders().Where(o => true).Select(o => (o.OrderId, RunSum: Sql.Sum(o.Total, over => over.PartitionBy(o.Status)))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", SUM(\"Total\") OVER (PARTITION BY \"Status\") AS \"RunSum\" FROM \"orders\"",
            pg:     "SELECT \"OrderId\", SUM(\"Total\") OVER (PARTITION BY \"Status\") AS \"RunSum\" FROM \"orders\"",
            mysql:  "SELECT `OrderId`, SUM(`Total`) OVER (PARTITION BY `Status`) AS `RunSum` FROM `orders`",
            ss:     "SELECT [OrderId], SUM([Total]) OVER (PARTITION BY [Status]) AS [RunSum] FROM [orders]");

        // "Shipped" orders: 250 + 150 = 400. Both shipped orders should have RunSum=400.
        var results = await lt.ExecuteFetchAllAsync();
        var order1 = results.First(r => r.OrderId == 1); // Shipped
        Assert.That(order1.RunSum, Is.EqualTo(400.00m).Within(0.01m));
        var order3 = results.First(r => r.OrderId == 3); // Shipped
        Assert.That(order3.RunSum, Is.EqualTo(400.00m).Within(0.01m));
    }

    [Test]
    public async Task WindowFunction_CountOver()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Orders().Where(o => true).Select(o => (o.OrderId, Cnt: Sql.Count(over => over.PartitionBy(o.Status)))).Prepare();
        var pg = Pg.Orders().Where(o => true).Select(o => (o.OrderId, Cnt: Sql.Count(over => over.PartitionBy(o.Status)))).Prepare();
        var my = My.Orders().Where(o => true).Select(o => (o.OrderId, Cnt: Sql.Count(over => over.PartitionBy(o.Status)))).Prepare();
        var ss = Ss.Orders().Where(o => true).Select(o => (o.OrderId, Cnt: Sql.Count(over => over.PartitionBy(o.Status)))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", COUNT(*) OVER (PARTITION BY \"Status\") AS \"Cnt\" FROM \"orders\"",
            pg:     "SELECT \"OrderId\", COUNT(*) OVER (PARTITION BY \"Status\") AS \"Cnt\" FROM \"orders\"",
            mysql:  "SELECT `OrderId`, COUNT(*) OVER (PARTITION BY `Status`) AS `Cnt` FROM `orders`",
            ss:     "SELECT [OrderId], COUNT(*) OVER (PARTITION BY [Status]) AS [Cnt] FROM [orders]");

        // "Shipped" count = 2, "Pending" count = 1
        var results = await lt.ExecuteFetchAllAsync();
        var order1 = results.First(r => r.OrderId == 1); // Shipped
        Assert.That(order1.Cnt, Is.EqualTo(2));
        var order2 = results.First(r => r.OrderId == 2); // Pending
        Assert.That(order2.Cnt, Is.EqualTo(1));
    }

    #endregion

    #region Multi-column and Mixed

    [Test]
    public async Task WindowFunction_PartitionByMultipleColumns()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Orders().Where(o => true).Select(o => (o.OrderId, Rnk: Sql.RowNumber(over => over.PartitionBy(o.Status, o.UserId).OrderBy(o.OrderDate)))).Prepare();
        var pg = Pg.Orders().Where(o => true).Select(o => (o.OrderId, Rnk: Sql.RowNumber(over => over.PartitionBy(o.Status, o.UserId).OrderBy(o.OrderDate)))).Prepare();
        var my = My.Orders().Where(o => true).Select(o => (o.OrderId, Rnk: Sql.RowNumber(over => over.PartitionBy(o.Status, o.UserId).OrderBy(o.OrderDate)))).Prepare();
        var ss = Ss.Orders().Where(o => true).Select(o => (o.OrderId, Rnk: Sql.RowNumber(over => over.PartitionBy(o.Status, o.UserId).OrderBy(o.OrderDate)))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", ROW_NUMBER() OVER (PARTITION BY \"Status\", \"UserId\" ORDER BY \"OrderDate\") AS \"Rnk\" FROM \"orders\"",
            pg:     "SELECT \"OrderId\", ROW_NUMBER() OVER (PARTITION BY \"Status\", \"UserId\" ORDER BY \"OrderDate\") AS \"Rnk\" FROM \"orders\"",
            mysql:  "SELECT `OrderId`, ROW_NUMBER() OVER (PARTITION BY `Status`, `UserId` ORDER BY `OrderDate`) AS `Rnk` FROM `orders`",
            ss:     "SELECT [OrderId], ROW_NUMBER() OVER (PARTITION BY [Status], [UserId] ORDER BY [OrderDate]) AS [Rnk] FROM [orders]");
    }

    [Test]
    public async Task WindowFunction_MixedWithRegularColumns()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Orders().Where(o => true).Select(o => (o.OrderId, o.Total, RowNum: Sql.RowNumber(over => over.OrderBy(o.OrderDate)))).Prepare();
        var pg = Pg.Orders().Where(o => true).Select(o => (o.OrderId, o.Total, RowNum: Sql.RowNumber(over => over.OrderBy(o.OrderDate)))).Prepare();
        var my = My.Orders().Where(o => true).Select(o => (o.OrderId, o.Total, RowNum: Sql.RowNumber(over => over.OrderBy(o.OrderDate)))).Prepare();
        var ss = Ss.Orders().Where(o => true).Select(o => (o.OrderId, o.Total, RowNum: Sql.RowNumber(over => over.OrderBy(o.OrderDate)))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", \"Total\", ROW_NUMBER() OVER (ORDER BY \"OrderDate\") AS \"RowNum\" FROM \"orders\"",
            pg:     "SELECT \"OrderId\", \"Total\", ROW_NUMBER() OVER (ORDER BY \"OrderDate\") AS \"RowNum\" FROM \"orders\"",
            mysql:  "SELECT `OrderId`, `Total`, ROW_NUMBER() OVER (ORDER BY `OrderDate`) AS `RowNum` FROM `orders`",
            ss:     "SELECT [OrderId], [Total], ROW_NUMBER() OVER (ORDER BY [OrderDate]) AS [RowNum] FROM [orders]");
    }

    [Test]
    public async Task WindowFunction_MultipleWindowsInSelect()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Orders().Where(o => true).Select(o => (o.OrderId, RowNum: Sql.RowNumber(over => over.OrderBy(o.OrderDate)), Rnk: Sql.Rank(over => over.OrderBy(o.Total)))).Prepare();
        var pg = Pg.Orders().Where(o => true).Select(o => (o.OrderId, RowNum: Sql.RowNumber(over => over.OrderBy(o.OrderDate)), Rnk: Sql.Rank(over => over.OrderBy(o.Total)))).Prepare();
        var my = My.Orders().Where(o => true).Select(o => (o.OrderId, RowNum: Sql.RowNumber(over => over.OrderBy(o.OrderDate)), Rnk: Sql.Rank(over => over.OrderBy(o.Total)))).Prepare();
        var ss = Ss.Orders().Where(o => true).Select(o => (o.OrderId, RowNum: Sql.RowNumber(over => over.OrderBy(o.OrderDate)), Rnk: Sql.Rank(over => over.OrderBy(o.Total)))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", ROW_NUMBER() OVER (ORDER BY \"OrderDate\") AS \"RowNum\", RANK() OVER (ORDER BY \"Total\") AS \"Rnk\" FROM \"orders\"",
            pg:     "SELECT \"OrderId\", ROW_NUMBER() OVER (ORDER BY \"OrderDate\") AS \"RowNum\", RANK() OVER (ORDER BY \"Total\") AS \"Rnk\" FROM \"orders\"",
            mysql:  "SELECT `OrderId`, ROW_NUMBER() OVER (ORDER BY `OrderDate`) AS `RowNum`, RANK() OVER (ORDER BY `Total`) AS `Rnk` FROM `orders`",
            ss:     "SELECT [OrderId], ROW_NUMBER() OVER (ORDER BY [OrderDate]) AS [RowNum], RANK() OVER (ORDER BY [Total]) AS [Rnk] FROM [orders]");
    }

    #endregion
}
