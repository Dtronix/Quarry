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
            sqlite: "SELECT \"d\".\"UserName\" FROM (SELECT DISTINCT \"t0\".\"UserName\" AS \"UserName\", \"t1\".\"Total\" AS \"_o0\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > 0) AS \"d\" ORDER BY \"d\".\"_o0\" ASC",
            pg:     "SELECT \"d\".\"UserName\" FROM (SELECT DISTINCT \"t0\".\"UserName\" AS \"UserName\", \"t1\".\"Total\" AS \"_o0\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > 0) AS \"d\" ORDER BY \"d\".\"_o0\" ASC",
            mysql:  "SELECT `d`.`UserName` FROM (SELECT DISTINCT `t0`.`UserName` AS `UserName`, `t1`.`Total` AS `_o0` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` WHERE `t1`.`Total` > 0) AS `d` ORDER BY `d`.`_o0` ASC",
            ss:     "SELECT [d].[UserName] FROM (SELECT DISTINCT [t0].[UserName] AS [UserName], [t1].[Total] AS [_o0] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] WHERE [t1].[Total] > 0) AS [d] ORDER BY [d].[_o0] ASC");

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
            sqlite: "SELECT \"d\".\"UserName\", \"d\".\"Email\" FROM (SELECT DISTINCT \"t0\".\"UserName\" AS \"UserName\", \"t0\".\"Email\" AS \"Email\", \"t1\".\"Total\" AS \"_o0\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > 0) AS \"d\" ORDER BY \"d\".\"_o0\" ASC",
            pg:     "SELECT \"d\".\"UserName\", \"d\".\"Email\" FROM (SELECT DISTINCT \"t0\".\"UserName\" AS \"UserName\", \"t0\".\"Email\" AS \"Email\", \"t1\".\"Total\" AS \"_o0\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > 0) AS \"d\" ORDER BY \"d\".\"_o0\" ASC",
            mysql:  "SELECT `d`.`UserName`, `d`.`Email` FROM (SELECT DISTINCT `t0`.`UserName` AS `UserName`, `t0`.`Email` AS `Email`, `t1`.`Total` AS `_o0` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` WHERE `t1`.`Total` > 0) AS `d` ORDER BY `d`.`_o0` ASC",
            ss:     "SELECT [d].[UserName], [d].[Email] FROM (SELECT DISTINCT [t0].[UserName] AS [UserName], [t0].[Email] AS [Email], [t1].[Total] AS [_o0] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] WHERE [t1].[Total] > 0) AS [d] ORDER BY [d].[_o0] ASC");

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
            sqlite: "SELECT \"d\".\"UserName\" FROM (SELECT DISTINCT \"t0\".\"UserName\" AS \"UserName\", \"t1\".\"Total\" AS \"_o0\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > 0) AS \"d\" ORDER BY \"d\".\"_o0\" DESC",
            pg:     "SELECT \"d\".\"UserName\" FROM (SELECT DISTINCT \"t0\".\"UserName\" AS \"UserName\", \"t1\".\"Total\" AS \"_o0\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > 0) AS \"d\" ORDER BY \"d\".\"_o0\" DESC",
            mysql:  "SELECT `d`.`UserName` FROM (SELECT DISTINCT `t0`.`UserName` AS `UserName`, `t1`.`Total` AS `_o0` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` WHERE `t1`.`Total` > 0) AS `d` ORDER BY `d`.`_o0` DESC",
            ss:     "SELECT [d].[UserName] FROM (SELECT DISTINCT [t0].[UserName] AS [UserName], [t1].[Total] AS [_o0] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] WHERE [t1].[Total] > 0) AS [d] ORDER BY [d].[_o0] DESC");

        // Descending order: Alice (250.00) first, Bob (150.00), then Alice (75.50).
        var liteResults = await lt.ExecuteFetchAllAsync();
        Assert.That(liteResults, Has.Count.EqualTo(3));
        Assert.That(liteResults[0], Is.EqualTo("Alice"));
        Assert.That(liteResults[1], Is.EqualTo("Bob"));
        Assert.That(liteResults[2], Is.EqualTo("Alice"));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));
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

        // u.UserName is in projection (alias UserName); o.Total is not (alias _o0).
        // Outer ORDER BY references UserName for the first term and _o0 for the second.
        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"d\".\"UserName\" FROM (SELECT DISTINCT \"t0\".\"UserName\" AS \"UserName\", \"t1\".\"Total\" AS \"_o0\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > 0) AS \"d\" ORDER BY \"d\".\"UserName\" ASC, \"d\".\"_o0\" ASC",
            pg:     "SELECT \"d\".\"UserName\" FROM (SELECT DISTINCT \"t0\".\"UserName\" AS \"UserName\", \"t1\".\"Total\" AS \"_o0\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > 0) AS \"d\" ORDER BY \"d\".\"UserName\" ASC, \"d\".\"_o0\" ASC",
            mysql:  "SELECT `d`.`UserName` FROM (SELECT DISTINCT `t0`.`UserName` AS `UserName`, `t1`.`Total` AS `_o0` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` WHERE `t1`.`Total` > 0) AS `d` ORDER BY `d`.`UserName` ASC, `d`.`_o0` ASC",
            ss:     "SELECT [d].[UserName] FROM (SELECT DISTINCT [t0].[UserName] AS [UserName], [t1].[Total] AS [_o0] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] WHERE [t1].[Total] > 0) AS [d] ORDER BY [d].[UserName] ASC, [d].[_o0] ASC");

        var liteResults = await lt.ExecuteFetchAllAsync();
        Assert.That(liteResults, Has.Count.EqualTo(3));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));
    }

    #endregion

    #region Multi-term all-in-projection: regression guard for no-wrap shape

    [Test]
    public async Task Distinct_MultipleOrderBy_AllInProjection_NoWrap()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.IsActive)
                .OrderBy(u => u.UserName)
                .ThenBy(u => u.Email)
                .Distinct()
                .Select(u => (u.UserName, u.Email))
                .Prepare();
        var pg = Pg.Users().Where(u => u.IsActive)
                .OrderBy(u => u.UserName)
                .ThenBy(u => u.Email)
                .Distinct()
                .Select(u => (u.UserName, u.Email))
                .Prepare();
        var my = My.Users().Where(u => u.IsActive)
                .OrderBy(u => u.UserName)
                .ThenBy(u => u.Email)
                .Distinct()
                .Select(u => (u.UserName, u.Email))
                .Prepare();
        var ss = Ss.Users().Where(u => u.IsActive)
                .OrderBy(u => u.UserName)
                .ThenBy(u => u.Email)
                .Distinct()
                .Select(u => (u.UserName, u.Email))
                .Prepare();

        // Both ORDER BY columns are in the projection — no wrap, flat DISTINCT.
        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT DISTINCT \"UserName\", \"Email\" FROM \"users\" WHERE \"IsActive\" = 1 ORDER BY \"UserName\" ASC, \"Email\" ASC",
            pg:     "SELECT DISTINCT \"UserName\", \"Email\" FROM \"users\" WHERE \"IsActive\" = TRUE ORDER BY \"UserName\" ASC, \"Email\" ASC",
            mysql:  "SELECT DISTINCT `UserName`, `Email` FROM `users` WHERE `IsActive` = 1 ORDER BY `UserName` ASC, `Email` ASC",
            ss:     "SELECT DISTINCT [UserName], [Email] FROM [users] WHERE [IsActive] = 1 ORDER BY [UserName] ASC, [Email] ASC");

        var liteResults = await lt.ExecuteFetchAllAsync();
        Assert.That(liteResults, Has.Count.EqualTo(2));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
    }

    #endregion

    #region Param threading: ORDER BY expression carrying a captured parameter

    [Test]
    public async Task Distinct_OrderBy_NonProjectedExprWithCapturedParam_ThreadsParamIndex()
    {
        // Regression test: prior to #267 follow-up the wrap rendered ORDER BY exprs
        // with paramIndex BEFORE body clauses, colliding @p slots between ORDER BY
        // and WHERE/HAVING. Chain order is Where→OrderBy→Distinct→Select, so the
        // carrier assigns @p0 to the WHERE param and @p1 to the OrderBy param. The
        // wrap must render the inner ORDER BY expression at the post-body offset
        // even though it appears textually before the WHERE clause in the SQL.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        decimal threshold = 50.00m;
        // Non-zero bias is load-bearing for the regression assertion below: a prior
        // generator defect (CS0649 on Chain_N.P1) silently bound default(decimal) (= 0)
        // to the OrderBy parameter, masked by bias=0 here. With bias=100, an unassigned
        // P-field would bind 0 — the AllParameters.Value check catches that immediately.
        decimal bias = 100.00m;

        var lt = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => o.Total > threshold)
                .OrderBy((u, o) => o.Total + bias)
                .Distinct()
                .Select((u, o) => u.UserName)
                .Prepare();
        var pg = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => o.Total > threshold)
                .OrderBy((u, o) => o.Total + bias)
                .Distinct()
                .Select((u, o) => u.UserName)
                .Prepare();
        var my = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => o.Total > threshold)
                .OrderBy((u, o) => o.Total + bias)
                .Distinct()
                .Select((u, o) => u.UserName)
                .Prepare();
        var ss = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id)
                .Where((u, o) => o.Total > threshold)
                .OrderBy((u, o) => o.Total + bias)
                .Distinct()
                .Select((u, o) => u.UserName)
                .Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"d\".\"UserName\" FROM (SELECT DISTINCT \"t0\".\"UserName\" AS \"UserName\", (\"t1\".\"Total\" + @p1) AS \"_o0\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > @p0) AS \"d\" ORDER BY \"d\".\"_o0\" ASC",
            pg:     "SELECT \"d\".\"UserName\" FROM (SELECT DISTINCT \"t0\".\"UserName\" AS \"UserName\", (\"t1\".\"Total\" + $2) AS \"_o0\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\" WHERE \"t1\".\"Total\" > $1) AS \"d\" ORDER BY \"d\".\"_o0\" ASC",
            mysql:  "SELECT `d`.`UserName` FROM (SELECT DISTINCT `t0`.`UserName` AS `UserName`, (`t1`.`Total` + ?) AS `_o0` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId` WHERE `t1`.`Total` > ?) AS `d` ORDER BY `d`.`_o0` ASC",
            ss:     "SELECT [d].[UserName] FROM (SELECT DISTINCT [t0].[UserName] AS [UserName], ([t1].[Total] + @p1) AS [_o0] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId] WHERE [t1].[Total] > @p0) AS [d] ORDER BY [d].[_o0] ASC");

        // Regression guard: the OrderBy capture (@p1) must bind the captured `bias` value,
        // not default(decimal). Pre-fix, the four affected emitters skipped the per-clause
        // extraction plan and Chain_N.P1 stayed at its default — undetectable by SQL-shape
        // assertion alone. AllParameters surfaces the bound values from the carrier fields.
        AssertOrderByCapturedParamBound(lt.ToDiagnostics(), threshold, bias, "sqlite");
        AssertOrderByCapturedParamBound(pg.ToDiagnostics(), threshold, bias, "postgres");
        AssertOrderByCapturedParamBound(my.ToDiagnostics(), threshold, bias, "mysql");
        AssertOrderByCapturedParamBound(ss.ToDiagnostics(), threshold, bias, "sqlserver");

        // threshold=50.00 keeps all 3 orders; bias is a constant offset that doesn't
        // change row order (Total + bias preserves Total's relative ordering).
        var liteResults = await lt.ExecuteFetchAllAsync();
        Assert.That(liteResults, Has.Count.EqualTo(3));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));
    }

    private static void AssertOrderByCapturedParamBound(QueryDiagnostics diag, decimal expectedThreshold, decimal expectedBias, string dialect)
    {
        Assert.That(diag.AllParameters, Has.Count.EqualTo(2),
            $"[{dialect}] expected exactly two bound parameters (threshold, bias)");
        Assert.That(diag.AllParameters[0].Value, Is.EqualTo(expectedThreshold),
            $"[{dialect}] @p0 must bind the captured threshold value");
        Assert.That(diag.AllParameters[1].Value, Is.EqualTo(expectedBias),
            $"[{dialect}] @p1 must bind the captured bias value, not default(decimal). Pre-fix bug bound 0.");
    }

    #endregion

    #region No explicit Select (implicit identity projection) interacts correctly with the wrap

    [Test]
    public async Task Distinct_OrderBy_NoExplicitSelect_OrderByEntityColumn_StaysFlat()
    {
        // Without an explicit Select, the projection is the entity's columns. ORDER BY
        // referencing any of those columns is by definition in-projection — no wrap.
        // This test guards against the wrap accidentally firing for the implicit-identity
        // path (e.g., if the empty-projection early-return ever stopped catching this).
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.IsActive).OrderBy(u => u.UserName).Distinct().Prepare();
        var pg = Pg.Users().Where(u => u.IsActive).OrderBy(u => u.UserName).Distinct().Prepare();
        var my = My.Users().Where(u => u.IsActive).OrderBy(u => u.UserName).Distinct().Prepare();
        var ss = Ss.Users().Where(u => u.IsActive).OrderBy(u => u.UserName).Distinct().Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT DISTINCT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"IsActive\" = 1 ORDER BY \"UserName\" ASC",
            pg:     "SELECT DISTINCT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"IsActive\" = TRUE ORDER BY \"UserName\" ASC",
            mysql:  "SELECT DISTINCT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE `IsActive` = 1 ORDER BY `UserName` ASC",
            ss:     "SELECT DISTINCT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE [IsActive] = 1 ORDER BY [UserName] ASC");

        var liteResults = await lt.ExecuteFetchAllAsync();
        Assert.That(liteResults, Has.Count.EqualTo(2));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
    }

    #endregion

    #region Aggregate-projection alias-keep branch under wrap

    [Test]
    public async Task Distinct_OrderBy_NonProjected_NavAggregateProjection_KeepsAliasInWrap()
    {
        // u.Orders.Count() is a navigation aggregate column with IsAggregateFunction=true
        // and an explicit Alias. Inside the wrap, the inner SELECT must emit the aggregate
        // SQL and project it under the user-given alias (here "OrderCount") rather than
        // the default __c{i} alias. The outer SELECT must reference the same alias.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.IsActive)
                .OrderBy(u => u.Email)
                .Distinct()
                .Select(u => (u.UserName, OrderCount: u.Orders.Count()))
                .Prepare();
        var pg = Pg.Users().Where(u => u.IsActive)
                .OrderBy(u => u.Email)
                .Distinct()
                .Select(u => (u.UserName, OrderCount: u.Orders.Count()))
                .Prepare();
        var my = My.Users().Where(u => u.IsActive)
                .OrderBy(u => u.Email)
                .Distinct()
                .Select(u => (u.UserName, OrderCount: u.Orders.Count()))
                .Prepare();
        var ss = Ss.Users().Where(u => u.IsActive)
                .OrderBy(u => u.Email)
                .Distinct()
                .Select(u => (u.UserName, OrderCount: u.Orders.Count()))
                .Prepare();

        // Outer projects d.UserName (UserName) and d.OrderCount (kept alias).
        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"d\".\"UserName\", \"d\".\"OrderCount\" FROM (SELECT DISTINCT \"UserName\" AS \"UserName\", (SELECT COUNT(*) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"OrderCount\", \"Email\" AS \"_o0\" FROM \"users\" WHERE \"IsActive\" = 1) AS \"d\" ORDER BY \"d\".\"_o0\" ASC",
            pg:     "SELECT \"d\".\"UserName\", \"d\".\"OrderCount\" FROM (SELECT DISTINCT \"UserName\" AS \"UserName\", (SELECT COUNT(*) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"OrderCount\", \"Email\" AS \"_o0\" FROM \"users\" WHERE \"IsActive\" = TRUE) AS \"d\" ORDER BY \"d\".\"_o0\" ASC",
            mysql:  "SELECT `d`.`UserName`, `d`.`OrderCount` FROM (SELECT DISTINCT `UserName` AS `UserName`, (SELECT COUNT(*) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId`) AS `OrderCount`, `Email` AS `_o0` FROM `users` WHERE `IsActive` = 1) AS `d` ORDER BY `d`.`_o0` ASC",
            ss:     "SELECT [d].[UserName], [d].[OrderCount] FROM (SELECT DISTINCT [UserName] AS [UserName], (SELECT COUNT(*) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId]) AS [OrderCount], [Email] AS [_o0] FROM [users] WHERE [IsActive] = 1) AS [d] ORDER BY [d].[_o0] ASC");

        // Alice (Email "alice@test.com") and Bob (NULL email). DISTINCT on (UserName, OrderCount, Email).
        // Each user appears once because (UserName, Count) is unique per Email value.
        var liteResults = await lt.ExecuteFetchAllAsync();
        Assert.That(liteResults, Has.Count.EqualTo(2));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
    }

    #endregion

    #region Conditional ORDER BY: per-mask wrap-vs-flat dispatch

    [Test]
    public async Task Distinct_ConditionalOrderByOnNonProjected_PerMaskDispatch_FlatWhenInactive_WrapWhenActive()
    {
        // Conditional reassignment of the chain produces a 2-mask plan. The wrap should
        // fire only on the mask where the OrderBy is active and references a non-projected
        // column. The other mask emits the flat DISTINCT form. This guards the
        // MayNeedDistinctOrderByWrap → canBatch=false fallback in Assemble: when at least
        // one mask can need the wrap, the dispatch must use per-mask single rendering
        // rather than the batch fast path.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        // Single-entity chain — projection is u.UserName, ORDER BY references u.Email which
        // is not in projection. The conditional reassignment makes ORDER BY active only when
        // the runtime predicate is true, exercising both mask variants in the carrier.
        IQueryBuilder<User, string> lt = Lite.Users().Where(u => u.IsActive).Distinct().Select(u => u.UserName);
        if (DateTime.UtcNow.Year > 2000)
            lt = lt.OrderBy(u => u.Email);

        var diag = lt.Prepare().ToDiagnostics();
        Assert.That(diag.SqlVariants, Has.Count.EqualTo(2),
            "conditional OrderBy should produce 2 mask variants");

        // mask=0 → OrderBy inactive: flat DISTINCT, no wrap
        var maskFlat = diag.SqlVariants[0];
        Assert.That(maskFlat.Sql, Does.StartWith("SELECT DISTINCT \"UserName\""),
            "mask=0 should emit flat DISTINCT (no derived-table wrap)");
        Assert.That(maskFlat.Sql, Does.Not.Contain("FROM (SELECT DISTINCT"),
            "mask=0 must not be wrapped");
        Assert.That(maskFlat.Sql, Does.Not.Contain("ORDER BY"),
            "mask=0 must not have ORDER BY");

        // mask=1 → OrderBy active: wrap fires, ORDER BY uses inner-alias _o0
        var maskWrap = diag.SqlVariants[1];
        Assert.That(maskWrap.Sql, Does.StartWith("SELECT \"d\".\"UserName\" FROM (SELECT DISTINCT"),
            "mask=1 should emit the derived-table wrap");
        Assert.That(maskWrap.Sql, Does.Contain("\"Email\" AS \"_o0\""),
            "mask=1 inner SELECT must include the non-projected ORDER BY column with _o0 alias");
        Assert.That(maskWrap.Sql, Does.EndWith("ORDER BY \"d\".\"_o0\" ASC"),
            "mask=1 outer ORDER BY must reference the inner alias");
    }

    #endregion

    // GROUP BY + HAVING + Distinct + OrderBy(non-projected) is unreachable in Quarry
    // chains: after GroupBy(g).Select(g_or_aggregates), only the GROUP BY key columns and
    // aggregates are in the projection. OrderBy on any non-aggregate, non-grouped column
    // can't bind (the identifier is no longer available in the post-GROUP BY scope).
    // Therefore the wrap path's GROUP BY/HAVING blocks are not exercisable through the
    // chain API for the wrap-firing case. They are exercised through the flat path by
    // many existing CrossDialect* tests, and the wrap shares the same AppendGroupByAndHaving
    // helper — so divergence between flat and wrap is structurally impossible.
}
