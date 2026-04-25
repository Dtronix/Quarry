using Quarry;
using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;

/// <summary>
/// Cross-dialect tests focused on the DISTINCT + ORDER BY-on-non-projected-column wrap
/// (#267). PostgreSQL rejects the flat shape (42P10) and SQL Server rejects it under
/// standard rules; SQLite and MySQL accept it but with implementation-defined row
/// selection. The generator wraps such queries in a derived table on all four dialects
/// so semantics are identical everywhere.
/// </summary>
[TestFixture]
internal class CrossDialectDistinctOrderByTests
{
    #region Wrap is applied when ORDER BY references a non-projected column

    [Test]
    public async Task Distinct_OrderBy_NonProjectedColumn_WrapsAcrossAllDialects()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => o.Total > 0)
                .OrderBy((u, o) => o.Total)
                .Distinct()
                .Select((u, o) => u.UserName)
                .Prepare();
        var pg = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => o.Total > 0)
                .OrderBy((u, o) => o.Total)
                .Distinct()
                .Select((u, o) => u.UserName)
                .Prepare();
        var my = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => o.Total > 0)
                .OrderBy((u, o) => o.Total)
                .Distinct()
                .Select((u, o) => u.UserName)
                .Prepare();
        var ss = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => o.Total > 0)
                .OrderBy((u, o) => o.Total)
                .Distinct()
                .Select((u, o) => u.UserName)
                .Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"__d\".\"__c0\" FROM (SELECT DISTINCT \"t0\".\"UserName\" AS \"__c0\", \"t1\".\"Total\" AS \"__o0\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > 0) AS \"__d\" ORDER BY \"__d\".\"__o0\" ASC",
            pg:     "SELECT \"__d\".\"__c0\" FROM (SELECT DISTINCT \"t0\".\"UserName\" AS \"__c0\", \"t1\".\"Total\" AS \"__o0\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > 0) AS \"__d\" ORDER BY \"__d\".\"__o0\" ASC",
            mysql:  "SELECT `__d`.`__c0` FROM (SELECT DISTINCT `t0`.`UserName` AS `__c0`, `t1`.`Total` AS `__o0` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` WHERE `t1`.`Total` > 0) AS `__d` ORDER BY `__d`.`__o0` ASC",
            ss:     "SELECT [__d].[__c0] FROM (SELECT DISTINCT [t0].[UserName] AS [__c0], [t1].[Total] AS [__o0] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] WHERE [t1].[Total] > 0) AS [__d] ORDER BY [__d].[__o0] ASC");

        // Inner DISTINCT yields (Alice, 250.00), (Alice, 75.50), (Bob, 150.00) → 3 rows.
        var liteResults = await lt.ExecuteFetchAllAsync();
        Assert.That(liteResults, Has.Count.EqualTo(3));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));
    }

    #endregion

    #region No wrap when ORDER BY column IS in projection (regression guard)

    [Test]
    public async Task Distinct_OrderBy_ProjectedColumn_NoWrap()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.IsActive)
                .OrderBy(u => u.UserName)
                .Distinct()
                .Select(u => u.UserName)
                .Prepare();
        var pg = Pg.Users().Where(u => u.IsActive)
                .OrderBy(u => u.UserName)
                .Distinct()
                .Select(u => u.UserName)
                .Prepare();
        var my = My.Users().Where(u => u.IsActive)
                .OrderBy(u => u.UserName)
                .Distinct()
                .Select(u => u.UserName)
                .Prepare();
        var ss = Ss.Users().Where(u => u.IsActive)
                .OrderBy(u => u.UserName)
                .Distinct()
                .Select(u => u.UserName)
                .Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT DISTINCT \"UserName\" FROM \"users\" WHERE \"IsActive\" = 1 ORDER BY \"UserName\" ASC",
            pg:     "SELECT DISTINCT \"UserName\" FROM \"users\" WHERE \"IsActive\" = TRUE ORDER BY \"UserName\" ASC",
            mysql:  "SELECT DISTINCT `UserName` FROM `users` WHERE `IsActive` = 1 ORDER BY `UserName` ASC",
            ss:     "SELECT DISTINCT [UserName] FROM [users] WHERE [IsActive] = 1 ORDER BY [UserName] ASC");

        // Alice and Bob are active; Charlie is not. Both run cleanly on PG (flat is portable).
        var liteResults = await lt.ExecuteFetchAllAsync();
        Assert.That(liteResults, Has.Count.EqualTo(2));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
    }

    #endregion

    #region Wrap with multi-column tuple projection

    [Test]
    public async Task Distinct_OrderBy_NonProjected_WithTupleProjection_WrapsAndPreservesColumnOrder()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => o.Total > 0)
                .OrderBy((u, o) => o.Total)
                .Distinct()
                .Select((u, o) => (u.UserName, u.Email))
                .Prepare();
        var pg = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => o.Total > 0)
                .OrderBy((u, o) => o.Total)
                .Distinct()
                .Select((u, o) => (u.UserName, u.Email))
                .Prepare();
        var my = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => o.Total > 0)
                .OrderBy((u, o) => o.Total)
                .Distinct()
                .Select((u, o) => (u.UserName, u.Email))
                .Prepare();
        var ss = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => o.Total > 0)
                .OrderBy((u, o) => o.Total)
                .Distinct()
                .Select((u, o) => (u.UserName, u.Email))
                .Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"__d\".\"__c0\", \"__d\".\"__c1\" FROM (SELECT DISTINCT \"t0\".\"UserName\" AS \"__c0\", \"t0\".\"Email\" AS \"__c1\", \"t1\".\"Total\" AS \"__o0\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > 0) AS \"__d\" ORDER BY \"__d\".\"__o0\" ASC",
            pg:     "SELECT \"__d\".\"__c0\", \"__d\".\"__c1\" FROM (SELECT DISTINCT \"t0\".\"UserName\" AS \"__c0\", \"t0\".\"Email\" AS \"__c1\", \"t1\".\"Total\" AS \"__o0\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > 0) AS \"__d\" ORDER BY \"__d\".\"__o0\" ASC",
            mysql:  "SELECT `__d`.`__c0`, `__d`.`__c1` FROM (SELECT DISTINCT `t0`.`UserName` AS `__c0`, `t0`.`Email` AS `__c1`, `t1`.`Total` AS `__o0` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` WHERE `t1`.`Total` > 0) AS `__d` ORDER BY `__d`.`__o0` ASC",
            ss:     "SELECT [__d].[__c0], [__d].[__c1] FROM (SELECT DISTINCT [t0].[UserName] AS [__c0], [t0].[Email] AS [__c1], [t1].[Total] AS [__o0] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] WHERE [t1].[Total] > 0) AS [__d] ORDER BY [__d].[__o0] ASC");

        // 3 distinct (UserName, Email, Total) rows → projects (UserName, Email).
        var liteResults = await lt.ExecuteFetchAllAsync();
        Assert.That(liteResults, Has.Count.EqualTo(3));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));
    }

    #endregion

    #region Wrap preserves descending direction

    [Test]
    public async Task Distinct_OrderByDescending_NonProjected_WrapsWithDescDirection()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => o.Total > 0)
                .OrderBy((u, o) => o.Total, Direction.Descending)
                .Distinct()
                .Select((u, o) => u.UserName)
                .Prepare();
        var pg = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => o.Total > 0)
                .OrderBy((u, o) => o.Total, Direction.Descending)
                .Distinct()
                .Select((u, o) => u.UserName)
                .Prepare();
        var my = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => o.Total > 0)
                .OrderBy((u, o) => o.Total, Direction.Descending)
                .Distinct()
                .Select((u, o) => u.UserName)
                .Prepare();
        var ss = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => o.Total > 0)
                .OrderBy((u, o) => o.Total, Direction.Descending)
                .Distinct()
                .Select((u, o) => u.UserName)
                .Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"__d\".\"__c0\" FROM (SELECT DISTINCT \"t0\".\"UserName\" AS \"__c0\", \"t1\".\"Total\" AS \"__o0\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > 0) AS \"__d\" ORDER BY \"__d\".\"__o0\" DESC",
            pg:     "SELECT \"__d\".\"__c0\" FROM (SELECT DISTINCT \"t0\".\"UserName\" AS \"__c0\", \"t1\".\"Total\" AS \"__o0\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > 0) AS \"__d\" ORDER BY \"__d\".\"__o0\" DESC",
            mysql:  "SELECT `__d`.`__c0` FROM (SELECT DISTINCT `t0`.`UserName` AS `__c0`, `t1`.`Total` AS `__o0` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` WHERE `t1`.`Total` > 0) AS `__d` ORDER BY `__d`.`__o0` DESC",
            ss:     "SELECT [__d].[__c0] FROM (SELECT DISTINCT [t0].[UserName] AS [__c0], [t1].[Total] AS [__o0] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] WHERE [t1].[Total] > 0) AS [__d] ORDER BY [__d].[__o0] DESC");
    }

    #endregion

    #region Wrap aliases each ORDER BY term independently when mix of in-projection and not

    [Test]
    public async Task Distinct_OrderBy_MixedInAndOutOfProjection_AliasesEachCorrectly()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => o.Total > 0)
                .OrderBy((u, o) => u.UserName)
                .ThenBy((u, o) => o.Total)
                .Distinct()
                .Select((u, o) => u.UserName)
                .Prepare();
        var pg = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => o.Total > 0)
                .OrderBy((u, o) => u.UserName)
                .ThenBy((u, o) => o.Total)
                .Distinct()
                .Select((u, o) => u.UserName)
                .Prepare();
        var my = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => o.Total > 0)
                .OrderBy((u, o) => u.UserName)
                .ThenBy((u, o) => o.Total)
                .Distinct()
                .Select((u, o) => u.UserName)
                .Prepare();
        var ss = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => o.Total > 0)
                .OrderBy((u, o) => u.UserName)
                .ThenBy((u, o) => o.Total)
                .Distinct()
                .Select((u, o) => u.UserName)
                .Prepare();

        // u.UserName is in projection (alias __c0); o.Total is not (alias __o0).
        // Outer ORDER BY references __c0 for the first term and __o0 for the second.
        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"__d\".\"__c0\" FROM (SELECT DISTINCT \"t0\".\"UserName\" AS \"__c0\", \"t1\".\"Total\" AS \"__o0\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > 0) AS \"__d\" ORDER BY \"__d\".\"__c0\" ASC, \"__d\".\"__o0\" ASC",
            pg:     "SELECT \"__d\".\"__c0\" FROM (SELECT DISTINCT \"t0\".\"UserName\" AS \"__c0\", \"t1\".\"Total\" AS \"__o0\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > 0) AS \"__d\" ORDER BY \"__d\".\"__c0\" ASC, \"__d\".\"__o0\" ASC",
            mysql:  "SELECT `__d`.`__c0` FROM (SELECT DISTINCT `t0`.`UserName` AS `__c0`, `t1`.`Total` AS `__o0` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` WHERE `t1`.`Total` > 0) AS `__d` ORDER BY `__d`.`__c0` ASC, `__d`.`__o0` ASC",
            ss:     "SELECT [__d].[__c0] FROM (SELECT DISTINCT [t0].[UserName] AS [__c0], [t1].[Total] AS [__o0] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] WHERE [t1].[Total] > 0) AS [__d] ORDER BY [__d].[__c0] ASC, [__d].[__o0] ASC");

        var liteResults = await lt.ExecuteFetchAllAsync();
        Assert.That(liteResults, Has.Count.EqualTo(3));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));
    }

    #endregion
}
