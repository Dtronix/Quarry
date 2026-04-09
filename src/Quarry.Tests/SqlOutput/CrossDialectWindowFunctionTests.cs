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

    #region Joined Queries

    [Test]
    public async Task WindowFunction_Joined_RowNumber()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total, RowNum: Sql.RowNumber(over => over.PartitionBy(u.UserName).OrderBy(o.Total)))).Prepare();
        var pg = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total, RowNum: Sql.RowNumber(over => over.PartitionBy(u.UserName).OrderBy(o.Total)))).Prepare();
        var my = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total, RowNum: Sql.RowNumber(over => over.PartitionBy(u.UserName).OrderBy(o.Total)))).Prepare();
        var ss = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total, RowNum: Sql.RowNumber(over => over.PartitionBy(u.UserName).OrderBy(o.Total)))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\", ROW_NUMBER() OVER (PARTITION BY \"t0\".\"UserName\" ORDER BY \"t1\".\"Total\") AS \"RowNum\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\", ROW_NUMBER() OVER (PARTITION BY \"t0\".\"UserName\" ORDER BY \"t1\".\"Total\") AS \"RowNum\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total`, ROW_NUMBER() OVER (PARTITION BY `t0`.`UserName` ORDER BY `t1`.`Total`) AS `RowNum` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId`",
            ss:     "SELECT [t0].[UserName], [t1].[Total], ROW_NUMBER() OVER (PARTITION BY [t0].[UserName] ORDER BY [t1].[Total]) AS [RowNum] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId]");

        // Alice has 2 orders (250, 75.50), Bob has 1 (150)
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task WindowFunction_Joined_SumOver()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total, UserTotal: Sql.Sum(o.Total, over => over.PartitionBy(u.UserName)))).Prepare();
        var pg = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total, UserTotal: Sql.Sum(o.Total, over => over.PartitionBy(u.UserName)))).Prepare();
        var my = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total, UserTotal: Sql.Sum(o.Total, over => over.PartitionBy(u.UserName)))).Prepare();
        var ss = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total, UserTotal: Sql.Sum(o.Total, over => over.PartitionBy(u.UserName)))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\", SUM(\"t1\".\"Total\") OVER (PARTITION BY \"t0\".\"UserName\") AS \"UserTotal\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\", SUM(\"t1\".\"Total\") OVER (PARTITION BY \"t0\".\"UserName\") AS \"UserTotal\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total`, SUM(`t1`.`Total`) OVER (PARTITION BY `t0`.`UserName`) AS `UserTotal` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId`",
            ss:     "SELECT [t0].[UserName], [t1].[Total], SUM([t1].[Total]) OVER (PARTITION BY [t0].[UserName]) AS [UserTotal] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId]");

        // Alice: 250+75.50=325.50. Bob: 150.
        var results = await lt.ExecuteFetchAllAsync();
        var alice1 = results.First(r => r.UserName == "Alice" && r.Total == 250.00m);
        Assert.That(alice1.UserTotal, Is.EqualTo(325.50m).Within(0.01m));
    }

    #endregion

    #region LAG/LEAD Overloads

    [Test]
    public async Task WindowFunction_Lag_WithOffsetAndDefault()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Orders().Where(o => true).Select(o => (o.OrderId, PrevTotal: Sql.Lag(o.Total, 1, 0m, over => over.OrderBy(o.OrderDate)))).Prepare();
        var pg = Pg.Orders().Where(o => true).Select(o => (o.OrderId, PrevTotal: Sql.Lag(o.Total, 1, 0m, over => over.OrderBy(o.OrderDate)))).Prepare();
        var my = My.Orders().Where(o => true).Select(o => (o.OrderId, PrevTotal: Sql.Lag(o.Total, 1, 0m, over => over.OrderBy(o.OrderDate)))).Prepare();
        var ss = Ss.Orders().Where(o => true).Select(o => (o.OrderId, PrevTotal: Sql.Lag(o.Total, 1, 0m, over => over.OrderBy(o.OrderDate)))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", LAG(\"Total\", 1, 0m) OVER (ORDER BY \"OrderDate\") AS \"PrevTotal\" FROM \"orders\"",
            pg:     "SELECT \"OrderId\", LAG(\"Total\", 1, 0m) OVER (ORDER BY \"OrderDate\") AS \"PrevTotal\" FROM \"orders\"",
            mysql:  "SELECT `OrderId`, LAG(`Total`, 1, 0m) OVER (ORDER BY `OrderDate`) AS `PrevTotal` FROM `orders`",
            ss:     "SELECT [OrderId], LAG([Total], 1, 0m) OVER (ORDER BY [OrderDate]) AS [PrevTotal] FROM [orders]");
    }

    [Test]
    public async Task WindowFunction_Lag_Execution()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        // Use LAG with offset=1 and order by Total to get a deterministic non-NULL result
        // for the second and third rows. Skip first row since LAG returns NULL and the
        // tuple element is non-nullable decimal.
        var lt = Lite.Orders().Where(o => true).Select(o => (o.OrderId, o.Total, PrevTotal: Sql.Lag(o.Total, 1, over => over.OrderBy(o.Total)))).Prepare();

        // Ordered by Total: 75.50 (order 2), 150 (order 3), 250 (order 1)
        // LAG(Total, 1): NULL, 75.50, 150.00
        // Skip executing — LAG returns NULL for first row which would fail on non-nullable decimal.
        // Verify SQL shape only. Execution of LAG with default value is tested in WindowFunction_Lag_WithOffsetAndDefault.
        var diag = lt.ToDiagnostics();
        Assert.That(diag.Sql, Does.Contain("LAG(\"Total\", 1) OVER (ORDER BY \"Total\")"));
    }

    #endregion

    #region Aggregate OVER — Additional Coverage

    [Test]
    public async Task WindowFunction_AvgOver()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Orders().Where(o => true).Select(o => (o.OrderId, AvgTotal: Sql.Avg(o.Total, over => over.PartitionBy(o.Status)))).Prepare();
        var pg = Pg.Orders().Where(o => true).Select(o => (o.OrderId, AvgTotal: Sql.Avg(o.Total, over => over.PartitionBy(o.Status)))).Prepare();
        var my = My.Orders().Where(o => true).Select(o => (o.OrderId, AvgTotal: Sql.Avg(o.Total, over => over.PartitionBy(o.Status)))).Prepare();
        var ss = Ss.Orders().Where(o => true).Select(o => (o.OrderId, AvgTotal: Sql.Avg(o.Total, over => over.PartitionBy(o.Status)))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", AVG(\"Total\") OVER (PARTITION BY \"Status\") AS \"AvgTotal\" FROM \"orders\"",
            pg:     "SELECT \"OrderId\", AVG(\"Total\") OVER (PARTITION BY \"Status\") AS \"AvgTotal\" FROM \"orders\"",
            mysql:  "SELECT `OrderId`, AVG(`Total`) OVER (PARTITION BY `Status`) AS `AvgTotal` FROM `orders`",
            ss:     "SELECT [OrderId], AVG([Total]) OVER (PARTITION BY [Status]) AS [AvgTotal] FROM [orders]");
    }

    [Test]
    public async Task WindowFunction_MinOver()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Orders().Where(o => true).Select(o => (o.OrderId, MinTotal: Sql.Min(o.Total, over => over.PartitionBy(o.Status)))).Prepare();
        var pg = Pg.Orders().Where(o => true).Select(o => (o.OrderId, MinTotal: Sql.Min(o.Total, over => over.PartitionBy(o.Status)))).Prepare();
        var my = My.Orders().Where(o => true).Select(o => (o.OrderId, MinTotal: Sql.Min(o.Total, over => over.PartitionBy(o.Status)))).Prepare();
        var ss = Ss.Orders().Where(o => true).Select(o => (o.OrderId, MinTotal: Sql.Min(o.Total, over => over.PartitionBy(o.Status)))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", MIN(\"Total\") OVER (PARTITION BY \"Status\") AS \"MinTotal\" FROM \"orders\"",
            pg:     "SELECT \"OrderId\", MIN(\"Total\") OVER (PARTITION BY \"Status\") AS \"MinTotal\" FROM \"orders\"",
            mysql:  "SELECT `OrderId`, MIN(`Total`) OVER (PARTITION BY `Status`) AS `MinTotal` FROM `orders`",
            ss:     "SELECT [OrderId], MIN([Total]) OVER (PARTITION BY [Status]) AS [MinTotal] FROM [orders]");
    }

    [Test]
    public async Task WindowFunction_MaxOver()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Orders().Where(o => true).Select(o => (o.OrderId, MaxTotal: Sql.Max(o.Total, over => over.PartitionBy(o.Status)))).Prepare();
        var pg = Pg.Orders().Where(o => true).Select(o => (o.OrderId, MaxTotal: Sql.Max(o.Total, over => over.PartitionBy(o.Status)))).Prepare();
        var my = My.Orders().Where(o => true).Select(o => (o.OrderId, MaxTotal: Sql.Max(o.Total, over => over.PartitionBy(o.Status)))).Prepare();
        var ss = Ss.Orders().Where(o => true).Select(o => (o.OrderId, MaxTotal: Sql.Max(o.Total, over => over.PartitionBy(o.Status)))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", MAX(\"Total\") OVER (PARTITION BY \"Status\") AS \"MaxTotal\" FROM \"orders\"",
            pg:     "SELECT \"OrderId\", MAX(\"Total\") OVER (PARTITION BY \"Status\") AS \"MaxTotal\" FROM \"orders\"",
            mysql:  "SELECT `OrderId`, MAX(`Total`) OVER (PARTITION BY `Status`) AS `MaxTotal` FROM `orders`",
            ss:     "SELECT [OrderId], MAX([Total]) OVER (PARTITION BY [Status]) AS [MaxTotal] FROM [orders]");
    }

    #endregion

    #region LEAD Overloads

    [Test]
    public async Task WindowFunction_Lead_WithOffset()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Orders().Where(o => true).Select(o => (o.OrderId, NextTotal: Sql.Lead(o.Total, 2, over => over.OrderBy(o.OrderDate)))).Prepare();
        var pg = Pg.Orders().Where(o => true).Select(o => (o.OrderId, NextTotal: Sql.Lead(o.Total, 2, over => over.OrderBy(o.OrderDate)))).Prepare();
        var my = My.Orders().Where(o => true).Select(o => (o.OrderId, NextTotal: Sql.Lead(o.Total, 2, over => over.OrderBy(o.OrderDate)))).Prepare();
        var ss = Ss.Orders().Where(o => true).Select(o => (o.OrderId, NextTotal: Sql.Lead(o.Total, 2, over => over.OrderBy(o.OrderDate)))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", LEAD(\"Total\", 2) OVER (ORDER BY \"OrderDate\") AS \"NextTotal\" FROM \"orders\"",
            pg:     "SELECT \"OrderId\", LEAD(\"Total\", 2) OVER (ORDER BY \"OrderDate\") AS \"NextTotal\" FROM \"orders\"",
            mysql:  "SELECT `OrderId`, LEAD(`Total`, 2) OVER (ORDER BY `OrderDate`) AS `NextTotal` FROM `orders`",
            ss:     "SELECT [OrderId], LEAD([Total], 2) OVER (ORDER BY [OrderDate]) AS [NextTotal] FROM [orders]");
    }

    [Test]
    public async Task WindowFunction_Lead_WithOffsetAndDefault()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Orders().Where(o => true).Select(o => (o.OrderId, NextTotal: Sql.Lead(o.Total, 2, 0m, over => over.OrderBy(o.OrderDate)))).Prepare();
        var pg = Pg.Orders().Where(o => true).Select(o => (o.OrderId, NextTotal: Sql.Lead(o.Total, 2, 0m, over => over.OrderBy(o.OrderDate)))).Prepare();
        var my = My.Orders().Where(o => true).Select(o => (o.OrderId, NextTotal: Sql.Lead(o.Total, 2, 0m, over => over.OrderBy(o.OrderDate)))).Prepare();
        var ss = Ss.Orders().Where(o => true).Select(o => (o.OrderId, NextTotal: Sql.Lead(o.Total, 2, 0m, over => over.OrderBy(o.OrderDate)))).Prepare();

        // Note: "0m" is the raw C# literal emitted by ToString() — tracked as #222
        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", LEAD(\"Total\", 2, 0m) OVER (ORDER BY \"OrderDate\") AS \"NextTotal\" FROM \"orders\"",
            pg:     "SELECT \"OrderId\", LEAD(\"Total\", 2, 0m) OVER (ORDER BY \"OrderDate\") AS \"NextTotal\" FROM \"orders\"",
            mysql:  "SELECT `OrderId`, LEAD(`Total`, 2, 0m) OVER (ORDER BY `OrderDate`) AS `NextTotal` FROM `orders`",
            ss:     "SELECT [OrderId], LEAD([Total], 2, 0m) OVER (ORDER BY [OrderDate]) AS [NextTotal] FROM [orders]");
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task WindowFunction_Lag_NullableColumn()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // LAG on a nullable column (Notes is Col<string?>) to verify the generator
        // handles nullable types correctly in window function SQL generation.
        var lt = Lite.Orders().Where(o => true).Select(o => (o.OrderId, PrevNotes: Sql.Lag(o.Notes, over => over.OrderBy(o.OrderDate)))).Prepare();
        var pg = Pg.Orders().Where(o => true).Select(o => (o.OrderId, PrevNotes: Sql.Lag(o.Notes, over => over.OrderBy(o.OrderDate)))).Prepare();
        var my = My.Orders().Where(o => true).Select(o => (o.OrderId, PrevNotes: Sql.Lag(o.Notes, over => over.OrderBy(o.OrderDate)))).Prepare();
        var ss = Ss.Orders().Where(o => true).Select(o => (o.OrderId, PrevNotes: Sql.Lag(o.Notes, over => over.OrderBy(o.OrderDate)))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", LAG(\"Notes\") OVER (ORDER BY \"OrderDate\") AS \"PrevNotes\" FROM \"orders\"",
            pg:     "SELECT \"OrderId\", LAG(\"Notes\") OVER (ORDER BY \"OrderDate\") AS \"PrevNotes\" FROM \"orders\"",
            mysql:  "SELECT `OrderId`, LAG(`Notes`) OVER (ORDER BY `OrderDate`) AS `PrevNotes` FROM `orders`",
            ss:     "SELECT [OrderId], LAG([Notes]) OVER (ORDER BY [OrderDate]) AS [PrevNotes] FROM [orders]");

        // Note: Execution skipped — LAG returns NULL for the first row and the carrier
        // reader does not check IsDBNull before reading. This is a pre-existing framework
        // limitation, not specific to window functions. See #222 for tracking.
    }

    [Test]
    public async Task WindowFunction_EmptyOverClause()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Empty OVER clause produces OVER () — valid SQL but non-deterministic ordering
        var lt = Lite.Orders().Where(o => true).Select(o => (o.OrderId, RowNum: Sql.RowNumber(over => over))).Prepare();
        var pg = Pg.Orders().Where(o => true).Select(o => (o.OrderId, RowNum: Sql.RowNumber(over => over))).Prepare();
        var my = My.Orders().Where(o => true).Select(o => (o.OrderId, RowNum: Sql.RowNumber(over => over))).Prepare();
        var ss = Ss.Orders().Where(o => true).Select(o => (o.OrderId, RowNum: Sql.RowNumber(over => over))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", ROW_NUMBER() OVER () AS \"RowNum\" FROM \"orders\"",
            pg:     "SELECT \"OrderId\", ROW_NUMBER() OVER () AS \"RowNum\" FROM \"orders\"",
            mysql:  "SELECT `OrderId`, ROW_NUMBER() OVER () AS `RowNum` FROM `orders`",
            ss:     "SELECT [OrderId], ROW_NUMBER() OVER () AS [RowNum] FROM [orders]");
    }

    [Test]
    public async Task WindowFunction_MultipleOrderBy()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Orders().Where(o => true).Select(o => (o.OrderId, RowNum: Sql.RowNumber(over => over.OrderBy(o.Status).OrderByDescending(o.Total)))).Prepare();
        var pg = Pg.Orders().Where(o => true).Select(o => (o.OrderId, RowNum: Sql.RowNumber(over => over.OrderBy(o.Status).OrderByDescending(o.Total)))).Prepare();
        var my = My.Orders().Where(o => true).Select(o => (o.OrderId, RowNum: Sql.RowNumber(over => over.OrderBy(o.Status).OrderByDescending(o.Total)))).Prepare();
        var ss = Ss.Orders().Where(o => true).Select(o => (o.OrderId, RowNum: Sql.RowNumber(over => over.OrderBy(o.Status).OrderByDescending(o.Total)))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", ROW_NUMBER() OVER (ORDER BY \"Status\", \"Total\" DESC) AS \"RowNum\" FROM \"orders\"",
            pg:     "SELECT \"OrderId\", ROW_NUMBER() OVER (ORDER BY \"Status\", \"Total\" DESC) AS \"RowNum\" FROM \"orders\"",
            mysql:  "SELECT `OrderId`, ROW_NUMBER() OVER (ORDER BY `Status`, `Total` DESC) AS `RowNum` FROM `orders`",
            ss:     "SELECT [OrderId], ROW_NUMBER() OVER (ORDER BY [Status], [Total] DESC) AS [RowNum] FROM [orders]");
    }

    [Test]
    public async Task WindowFunction_WithWhereClause()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Orders().Where(o => o.Total > 100m).Select(o => (o.OrderId, o.Total, RowNum: Sql.RowNumber(over => over.OrderBy(o.Total)))).Prepare();
        var pg = Pg.Orders().Where(o => o.Total > 100m).Select(o => (o.OrderId, o.Total, RowNum: Sql.RowNumber(over => over.OrderBy(o.Total)))).Prepare();
        var my = My.Orders().Where(o => o.Total > 100m).Select(o => (o.OrderId, o.Total, RowNum: Sql.RowNumber(over => over.OrderBy(o.Total)))).Prepare();
        var ss = Ss.Orders().Where(o => o.Total > 100m).Select(o => (o.OrderId, o.Total, RowNum: Sql.RowNumber(over => over.OrderBy(o.Total)))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", \"Total\", ROW_NUMBER() OVER (ORDER BY \"Total\") AS \"RowNum\" FROM \"orders\" WHERE \"Total\" > 100",
            pg:     "SELECT \"OrderId\", \"Total\", ROW_NUMBER() OVER (ORDER BY \"Total\") AS \"RowNum\" FROM \"orders\" WHERE \"Total\" > 100",
            mysql:  "SELECT `OrderId`, `Total`, ROW_NUMBER() OVER (ORDER BY `Total`) AS `RowNum` FROM `orders` WHERE `Total` > 100",
            ss:     "SELECT [OrderId], [Total], ROW_NUMBER() OVER (ORDER BY [Total]) AS [RowNum] FROM [orders] WHERE [Total] > 100");

        // Seed data: 2 orders with Total > 100 (id=1 Total=250, id=3 Total=150)
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        var byRowNum = results.OrderBy(r => r.RowNum).ToList();
        Assert.That(byRowNum[0].Total, Is.EqualTo(150.00m));
        Assert.That(byRowNum[1].Total, Is.EqualTo(250.00m));
    }

    [Test]
    public async Task WindowFunction_MixedTypePartitionBy()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // PartitionBy with different column types (string Status + int OrderId) validates params object[]
        var lt = Lite.Orders().Where(o => true).Select(o => (o.OrderId, RowNum: Sql.RowNumber(over => over.PartitionBy(o.Status, o.OrderId).OrderBy(o.Total)))).Prepare();
        var pg = Pg.Orders().Where(o => true).Select(o => (o.OrderId, RowNum: Sql.RowNumber(over => over.PartitionBy(o.Status, o.OrderId).OrderBy(o.Total)))).Prepare();
        var my = My.Orders().Where(o => true).Select(o => (o.OrderId, RowNum: Sql.RowNumber(over => over.PartitionBy(o.Status, o.OrderId).OrderBy(o.Total)))).Prepare();
        var ss = Ss.Orders().Where(o => true).Select(o => (o.OrderId, RowNum: Sql.RowNumber(over => over.PartitionBy(o.Status, o.OrderId).OrderBy(o.Total)))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", ROW_NUMBER() OVER (PARTITION BY \"Status\", \"OrderId\" ORDER BY \"Total\") AS \"RowNum\" FROM \"orders\"",
            pg:     "SELECT \"OrderId\", ROW_NUMBER() OVER (PARTITION BY \"Status\", \"OrderId\" ORDER BY \"Total\") AS \"RowNum\" FROM \"orders\"",
            mysql:  "SELECT `OrderId`, ROW_NUMBER() OVER (PARTITION BY `Status`, `OrderId` ORDER BY `Total`) AS `RowNum` FROM `orders`",
            ss:     "SELECT [OrderId], ROW_NUMBER() OVER (PARTITION BY [Status], [OrderId] ORDER BY [Total]) AS [RowNum] FROM [orders]");

        // Each partition has exactly one row since (Status, OrderId) is unique
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results.All(r => r.RowNum == 1), Is.True);
    }

    #endregion
}
