using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;


/// <summary>
/// Complex multi-clause cross-dialect tests. Each test exercises multiple
/// builder methods chained together, verifying the full generated SQL.
/// </summary>
[TestFixture]
internal class CrossDialectComplexTests
{
    #region Where + Select

    [Test]
    public async Task Where_Comparison_ThenSelect_Tuple()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt= Lite.Users().Where(u => u.UserId > 10).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.UserId > 10).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => u.UserId > 10).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => u.UserId > 10).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserId\" > 10",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserId\" > 10",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE `UserId` > 10",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE [UserId] > 10");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(0)); // All seeded users have UserId <= 3

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(0));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(0));
    }

    #endregion

    #region Multiple Where + Select

    [Test]
    public async Task Where_NullCheck_And_Boolean_ThenSelect()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt= Lite.Users().Where(u => u.Email != null).Where(u => u.IsActive).Select(u => (u.UserName, u.Email)).Prepare();
        var pg = Pg.Users().Where(u => u.Email != null).Where(u => u.IsActive).Select(u => (u.UserName, u.Email)).Prepare();
        var my = My.Users().Where(u => u.Email != null).Where(u => u.IsActive).Select(u => (u.UserName, u.Email)).Prepare();
        var ss = Ss.Users().Where(u => u.Email != null).Where(u => u.IsActive).Select(u => (u.UserName, u.Email)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserName\", \"Email\" FROM \"users\" WHERE (\"Email\" IS NOT NULL) AND (\"IsActive\" = 1)",
            pg:     "SELECT \"UserName\", \"Email\" FROM \"users\" WHERE (\"Email\" IS NOT NULL) AND (\"IsActive\" = TRUE)",
            mysql:  "SELECT `UserName`, `Email` FROM `users` WHERE (`Email` IS NOT NULL) AND (`IsActive` = 1)",
            ss:     "SELECT [UserName], [Email] FROM [users] WHERE ([Email] IS NOT NULL) AND ([IsActive] = 1)");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1)); // Alice: has email + active
        Assert.That(results[0], Is.EqualTo(("Alice", (string?)"alice@test.com")));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));
        Assert.That(pgResults[0], Is.EqualTo(("Alice", (string?)"alice@test.com")));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(1));
        Assert.That(myResults[0], Is.EqualTo(("Alice", (string?)"alice@test.com")));
    }

    [Test]
    public async Task Where_Boolean_And_Comparison_ThenSelect()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt= Lite.Users().Where(u => u.IsActive).Where(u => u.UserId > 5).Select(u => (u.UserId, u.UserName, u.Email)).Prepare();
        var pg = Pg.Users().Where(u => u.IsActive).Where(u => u.UserId > 5).Select(u => (u.UserId, u.UserName, u.Email)).Prepare();
        var my = My.Users().Where(u => u.IsActive).Where(u => u.UserId > 5).Select(u => (u.UserId, u.UserName, u.Email)).Prepare();
        var ss = Ss.Users().Where(u => u.IsActive).Where(u => u.UserId > 5).Select(u => (u.UserId, u.UserName, u.Email)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\" FROM \"users\" WHERE (\"IsActive\" = 1) AND (\"UserId\" > 5)",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\" FROM \"users\" WHERE (\"IsActive\" = TRUE) AND (\"UserId\" > 5)",
            mysql:  "SELECT `UserId`, `UserName`, `Email` FROM `users` WHERE (`IsActive` = 1) AND (`UserId` > 5)",
            ss:     "SELECT [UserId], [UserName], [Email] FROM [users] WHERE ([IsActive] = 1) AND ([UserId] > 5)");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(0));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(0));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(0));
    }

    #endregion

    #region Distinct + Where + Select

    [Test]
    public async Task Distinct_Where_Select()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt= Lite.Users().Distinct().Where(u => u.IsActive).Select(u => (u.UserName, u.Email)).Prepare();
        var pg = Pg.Users().Distinct().Where(u => u.IsActive).Select(u => (u.UserName, u.Email)).Prepare();
        var my = My.Users().Distinct().Where(u => u.IsActive).Select(u => (u.UserName, u.Email)).Prepare();
        var ss = Ss.Users().Distinct().Where(u => u.IsActive).Select(u => (u.UserName, u.Email)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT DISTINCT \"UserName\", \"Email\" FROM \"users\" WHERE \"IsActive\" = 1",
            pg:     "SELECT DISTINCT \"UserName\", \"Email\" FROM \"users\" WHERE \"IsActive\" = TRUE",
            mysql:  "SELECT DISTINCT `UserName`, `Email` FROM `users` WHERE `IsActive` = 1",
            ss:     "SELECT DISTINCT [UserName], [Email] FROM [users] WHERE [IsActive] = 1");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2)); // Alice and Bob are active

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
    }

    #endregion

    #region Where + Select + Pagination

    [Test]
    public async Task Where_Select_LimitOffset()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt= Lite.Users().Where(u => u.IsActive).Select(u => (u.UserName, u.Email)).Limit(10).Offset(20).Prepare();
        var pg = Pg.Users().Where(u => u.IsActive).Select(u => (u.UserName, u.Email)).Limit(10).Offset(20).Prepare();
        var my = My.Users().Where(u => u.IsActive).Select(u => (u.UserName, u.Email)).Limit(10).Offset(20).Prepare();
        var ss = Ss.Users().Where(u => u.IsActive).Select(u => (u.UserName, u.Email)).Limit(10).Offset(20).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserName\", \"Email\" FROM \"users\" WHERE \"IsActive\" = 1 LIMIT 10 OFFSET 20",
            pg:     "SELECT \"UserName\", \"Email\" FROM \"users\" WHERE \"IsActive\" = TRUE LIMIT 10 OFFSET 20",
            mysql:  "SELECT `UserName`, `Email` FROM `users` WHERE `IsActive` = 1 LIMIT 10 OFFSET 20",
            ss:     "SELECT [UserName], [Email] FROM [users] WHERE [IsActive] = 1 ORDER BY (SELECT NULL) OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(0)); // Offset 20 skips all 2 active users

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(0));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(0));
    }

    [Test]
    public async Task Where_Select_Limit()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt= Lite.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Limit(5).Prepare();
        var pg = Pg.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Limit(5).Prepare();
        var my = My.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Limit(5).Prepare();
        var ss = Ss.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Limit(5).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"IsActive\" = 1 LIMIT 5",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"IsActive\" = TRUE LIMIT 5",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE `IsActive` = 1 LIMIT 5",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE [IsActive] = 1 ORDER BY (SELECT NULL) OFFSET 0 ROWS FETCH NEXT 5 ROWS ONLY");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2)); // Alice and Bob

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
    }

    #endregion

    #region Orders Table — Complex

    [Test]
    public async Task Orders_Where_Comparison_Select()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt= Lite.Orders().Where(o => o.Total > 100).Select(o => (o.OrderId, o.Total, o.Status)).Prepare();
        var pg = Pg.Orders().Where(o => o.Total > 100).Select(o => (o.OrderId, o.Total, o.Status)).Prepare();
        var my = My.Orders().Where(o => o.Total > 100).Select(o => (o.OrderId, o.Total, o.Status)).Prepare();
        var ss = Ss.Orders().Where(o => o.Total > 100).Select(o => (o.OrderId, o.Total, o.Status)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", \"Total\", \"Status\" FROM \"orders\" WHERE \"Total\" > 100",
            pg:     "SELECT \"OrderId\", \"Total\", \"Status\" FROM \"orders\" WHERE \"Total\" > 100",
            mysql:  "SELECT `OrderId`, `Total`, `Status` FROM `orders` WHERE `Total` > 100",
            ss:     "SELECT [OrderId], [Total], [Status] FROM [orders] WHERE [Total] > 100");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2)); // Order 1 (250) and Order 3 (150)

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
    }

    #endregion

    #region Join + Where + Select

    [Test]
    public async Task Join_Where_Boolean_Select()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt= Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => u.IsActive).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var pg = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => u.IsActive).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var my = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => u.IsActive).Select((u, o) => (u.UserName, o.Total)).Prepare();
        var ss = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => u.IsActive).Select((u, o) => (u.UserName, o.Total)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t0\".\"IsActive\" = 1",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t0\".\"IsActive\" = TRUE",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` WHERE `t0`.`IsActive` = 1",
            ss:     "SELECT [t0].[UserName], [t1].[Total] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] WHERE [t0].[IsActive] = 1");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3)); // Alice's 2 orders + Bob's 1 order (both active)

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task Join_Where_RightTable_Select()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt= Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => o.Total > 50).Select((u, o) => (u.UserName, o.Total, o.Status)).Prepare();
        var pg = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => o.Total > 50).Select((u, o) => (u.UserName, o.Total, o.Status)).Prepare();
        var my = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => o.Total > 50).Select((u, o) => (u.UserName, o.Total, o.Status)).Prepare();
        var ss = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Where((u, o) => o.Total > 50).Select((u, o) => (u.UserName, o.Total, o.Status)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\", \"t1\".\"Status\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > 50",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\", \"t1\".\"Status\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > 50",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total`, `t1`.`Status` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` WHERE `t1`.`Total` > 50",
            ss:     "SELECT [t0].[UserName], [t1].[Total], [t1].[Status] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] WHERE [t1].[Total] > 50");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3)); // All 3 orders have Total > 50

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));
    }

    #endregion
}
