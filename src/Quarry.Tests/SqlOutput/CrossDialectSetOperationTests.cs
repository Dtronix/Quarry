using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;

[TestFixture]
internal class CrossDialectSetOperationTests
{
    #region Basic UNION

    [Test]
    public async Task Union_SameEntity_Identity()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.IsActive).Union(Lite.Users().Where(u => u.UserId == 1)).Prepare();
        var pg = Pg.Users().Where(u => u.IsActive).Union(Pg.Users().Where(u => u.UserId == 1)).Prepare();
        var my = My.Users().Where(u => u.IsActive).Union(My.Users().Where(u => u.UserId == 1)).Prepare();
        var ss = Ss.Users().Where(u => u.IsActive).Union(Ss.Users().Where(u => u.UserId == 1)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"IsActive\" = 1 UNION SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"UserId\" = 1",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"IsActive\" = TRUE UNION SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"UserId\" = 1",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE `IsActive` = 1 UNION SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE `UserId` = 1",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE [IsActive] = 1 UNION SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE [UserId] = 1");

        var results = await lt.ExecuteFetchAllAsync();
        // Alice(active), Bob(active), Charlie(inactive) → active = Alice, Bob. UserId=1 = Alice.
        // UNION removes duplicates so Alice appears once → 2 results (Alice, Bob)
        Assert.That(results, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task UnionAll_SameEntity_Identity()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.IsActive).UnionAll(Lite.Users().Where(u => u.UserId == 1)).Prepare();
        var pg = Pg.Users().Where(u => u.IsActive).UnionAll(Pg.Users().Where(u => u.UserId == 1)).Prepare();
        var my = My.Users().Where(u => u.IsActive).UnionAll(My.Users().Where(u => u.UserId == 1)).Prepare();
        var ss = Ss.Users().Where(u => u.IsActive).UnionAll(Ss.Users().Where(u => u.UserId == 1)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"IsActive\" = 1 UNION ALL SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"UserId\" = 1",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"IsActive\" = TRUE UNION ALL SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"UserId\" = 1",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE `IsActive` = 1 UNION ALL SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE `UserId` = 1",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE [IsActive] = 1 UNION ALL SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE [UserId] = 1");

        var results = await lt.ExecuteFetchAllAsync();
        // UNION ALL keeps duplicates: active=Alice,Bob + UserId=1=Alice → 3 results
        Assert.That(results, Has.Count.EqualTo(3));
    }

    #endregion

    #region INTERSECT and EXCEPT

    [Test]
    public async Task Intersect_SameEntity_Identity()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.IsActive).Intersect(Lite.Users().Where(u => u.UserId == 1)).Prepare();
        var pg = Pg.Users().Where(u => u.IsActive).Intersect(Pg.Users().Where(u => u.UserId == 1)).Prepare();
        var my = My.Users().Where(u => u.IsActive).Intersect(My.Users().Where(u => u.UserId == 1)).Prepare();
        var ss = Ss.Users().Where(u => u.IsActive).Intersect(Ss.Users().Where(u => u.UserId == 1)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"IsActive\" = 1 INTERSECT SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"UserId\" = 1",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"IsActive\" = TRUE INTERSECT SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"UserId\" = 1",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE `IsActive` = 1 INTERSECT SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE `UserId` = 1",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE [IsActive] = 1 INTERSECT SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE [UserId] = 1");

        var results = await lt.ExecuteFetchAllAsync();
        // INTERSECT: rows in both sets. Active=Alice,Bob. UserId=1=Alice. Intersection=Alice → 1 result
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
    }

    [Test]
    public async Task Except_SameEntity_Identity()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.IsActive).Except(Lite.Users().Where(u => u.UserId == 1)).Prepare();
        var pg = Pg.Users().Where(u => u.IsActive).Except(Pg.Users().Where(u => u.UserId == 1)).Prepare();
        var my = My.Users().Where(u => u.IsActive).Except(My.Users().Where(u => u.UserId == 1)).Prepare();
        var ss = Ss.Users().Where(u => u.IsActive).Except(Ss.Users().Where(u => u.UserId == 1)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"IsActive\" = 1 EXCEPT SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"UserId\" = 1",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"IsActive\" = TRUE EXCEPT SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"UserId\" = 1",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE `IsActive` = 1 EXCEPT SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE `UserId` = 1",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE [IsActive] = 1 EXCEPT SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE [UserId] = 1");

        var results = await lt.ExecuteFetchAllAsync();
        // EXCEPT: active minus UserId=1. Active=Alice,Bob. Minus Alice → Bob only → 1 result
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].UserName, Is.EqualTo("Bob"));
    }

    #endregion

    #region With Tuple Projection

    [Test]
    public async Task Union_WithSelect_TupleProjection()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName))
            .Union(Lite.Users().Where(u => u.UserId == 3).Select(u => (u.UserId, u.UserName))).Prepare();
        var pg = Pg.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName))
            .Union(Pg.Users().Where(u => u.UserId == 3).Select(u => (u.UserId, u.UserName))).Prepare();
        var my = My.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName))
            .Union(My.Users().Where(u => u.UserId == 3).Select(u => (u.UserId, u.UserName))).Prepare();
        var ss = Ss.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName))
            .Union(Ss.Users().Where(u => u.UserId == 3).Select(u => (u.UserId, u.UserName))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"IsActive\" = 1 UNION SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserId\" = 3",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"IsActive\" = TRUE UNION SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserId\" = 3",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE `IsActive` = 1 UNION SELECT `UserId`, `UserName` FROM `users` WHERE `UserId` = 3",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE [IsActive] = 1 UNION SELECT [UserId], [UserName] FROM [users] WHERE [UserId] = 3");

        var results = await lt.ExecuteFetchAllAsync();
        // Active=Alice(1),Bob(2). UserId=3=Charlie. UNION → 3 unique results
        Assert.That(results, Has.Count.EqualTo(3));
    }

    #endregion

    #region With ORDER BY and LIMIT

    [Test]
    public async Task Union_WithOrderBy()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName))
            .Union(Lite.Users().Where(u => u.UserId == 3).Select(u => (u.UserId, u.UserName)))
            .OrderBy(u => u.UserName).Prepare();
        var pg = Pg.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName))
            .Union(Pg.Users().Where(u => u.UserId == 3).Select(u => (u.UserId, u.UserName)))
            .OrderBy(u => u.UserName).Prepare();
        var my = My.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName))
            .Union(My.Users().Where(u => u.UserId == 3).Select(u => (u.UserId, u.UserName)))
            .OrderBy(u => u.UserName).Prepare();
        var ss = Ss.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName))
            .Union(Ss.Users().Where(u => u.UserId == 3).Select(u => (u.UserId, u.UserName)))
            .OrderBy(u => u.UserName).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"IsActive\" = 1 UNION SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserId\" = 3 ORDER BY \"UserName\" ASC",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"IsActive\" = TRUE UNION SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserId\" = 3 ORDER BY \"UserName\" ASC",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE `IsActive` = 1 UNION SELECT `UserId`, `UserName` FROM `users` WHERE `UserId` = 3 ORDER BY `UserName` ASC",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE [IsActive] = 1 UNION SELECT [UserId], [UserName] FROM [users] WHERE [UserId] = 3 ORDER BY [UserName] ASC");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
        Assert.That(results[1].UserName, Is.EqualTo("Bob"));
        Assert.That(results[2].UserName, Is.EqualTo("Charlie"));
    }

    [Test]
    public async Task Union_WithOrderByAndLimit()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName))
            .Union(Lite.Users().Where(u => u.UserId == 3).Select(u => (u.UserId, u.UserName)))
            .OrderBy(u => u.UserName).Limit(2).Prepare();
        var pg = Pg.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName))
            .Union(Pg.Users().Where(u => u.UserId == 3).Select(u => (u.UserId, u.UserName)))
            .OrderBy(u => u.UserName).Limit(2).Prepare();
        var my = My.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName))
            .Union(My.Users().Where(u => u.UserId == 3).Select(u => (u.UserId, u.UserName)))
            .OrderBy(u => u.UserName).Limit(2).Prepare();
        var ss = Ss.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName))
            .Union(Ss.Users().Where(u => u.UserId == 3).Select(u => (u.UserId, u.UserName)))
            .OrderBy(u => u.UserName).Limit(2).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"IsActive\" = 1 UNION SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserId\" = 3 ORDER BY \"UserName\" ASC LIMIT 2",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"IsActive\" = TRUE UNION SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserId\" = 3 ORDER BY \"UserName\" ASC LIMIT 2",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE `IsActive` = 1 UNION SELECT `UserId`, `UserName` FROM `users` WHERE `UserId` = 3 ORDER BY `UserName` ASC LIMIT 2",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE [IsActive] = 1 UNION SELECT [UserId], [UserName] FROM [users] WHERE [UserId] = 3 ORDER BY [UserName] ASC OFFSET 0 ROWS FETCH NEXT 2 ROWS ONLY");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
        Assert.That(results[1].UserName, Is.EqualTo("Bob"));
    }

    #endregion

    #region Chained Set Operations

    [Test]
    public async Task Union_Then_Except_Chained()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // All users UNION active users EXCEPT UserId=1
        var lt = Lite.Users().Select(u => (u.UserId, u.UserName))
            .Union(Lite.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)))
            .Except(Lite.Users().Where(u => u.UserId == 1).Select(u => (u.UserId, u.UserName)))
            .Prepare();

        var diag = lt.ToDiagnostics();
        // All users: (1,Alice), (2,Bob), (3,Charlie)
        // Active: (1,Alice), (2,Bob)
        // UNION: (1,Alice), (2,Bob), (3,Charlie)
        // EXCEPT UserId=1: (2,Bob), (3,Charlie)
        Assert.That(diag.Sql, Does.Contain("UNION"));
        Assert.That(diag.Sql, Does.Contain("EXCEPT"));

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
    }

    #endregion

    #region Post-Union WHERE (subquery wrapping)

    [Test]
    public async Task Union_WithPostUnionWhere()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Select(u => (u.UserId, u.UserName))
            .Union(Lite.Users().Where(u => u.UserId == 3).Select(u => (u.UserId, u.UserName)))
            .Where(u => u.UserId <= 2).Prepare();
        var pg = Pg.Users().Select(u => (u.UserId, u.UserName))
            .Union(Pg.Users().Where(u => u.UserId == 3).Select(u => (u.UserId, u.UserName)))
            .Where(u => u.UserId <= 2).Prepare();
        var my = My.Users().Select(u => (u.UserId, u.UserName))
            .Union(My.Users().Where(u => u.UserId == 3).Select(u => (u.UserId, u.UserName)))
            .Where(u => u.UserId <= 2).Prepare();
        var ss = Ss.Users().Select(u => (u.UserId, u.UserName))
            .Union(Ss.Users().Where(u => u.UserId == 3).Select(u => (u.UserId, u.UserName)))
            .Where(u => u.UserId <= 2).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT * FROM (SELECT \"UserId\", \"UserName\" FROM \"users\" UNION SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserId\" = 3) AS \"__set\" WHERE \"UserId\" <= 2",
            pg:     "SELECT * FROM (SELECT \"UserId\", \"UserName\" FROM \"users\" UNION SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserId\" = 3) AS \"__set\" WHERE \"UserId\" <= 2",
            mysql:  "SELECT * FROM (SELECT `UserId`, `UserName` FROM `users` UNION SELECT `UserId`, `UserName` FROM `users` WHERE `UserId` = 3) AS `__set` WHERE `UserId` <= 2",
            ss:     "SELECT * FROM (SELECT [UserId], [UserName] FROM [users] UNION SELECT [UserId], [UserName] FROM [users] WHERE [UserId] = 3) AS [__set] WHERE [UserId] <= 2");

        var results = await lt.ExecuteFetchAllAsync();
        // All users: (1,Alice), (2,Bob), (3,Charlie). UNION with UserId=3: same set.
        // Post-union WHERE UserId <= 2: (1,Alice), (2,Bob) → 2 results
        Assert.That(results, Has.Count.EqualTo(2));
    }

    #endregion

    #region Parameterized Set Operations

    [Test]
    public async Task Union_WithCapturedVariable_Parameters()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var minId = 2;
        var maxId = 3;
        var lt = Lite.Users().Where(u => u.UserId >= minId).Select(u => (u.UserId, u.UserName))
            .Union(Lite.Users().Where(u => u.UserId <= maxId).Select(u => (u.UserId, u.UserName)))
            .Prepare();

        var results = await lt.ExecuteFetchAllAsync();
        // UserId >= 2: (2,Bob), (3,Charlie). UserId <= 3: (1,Alice), (2,Bob), (3,Charlie).
        // UNION: (1,Alice), (2,Bob), (3,Charlie) → 3 results
        Assert.That(results, Has.Count.EqualTo(3));
    }

    #endregion
}
