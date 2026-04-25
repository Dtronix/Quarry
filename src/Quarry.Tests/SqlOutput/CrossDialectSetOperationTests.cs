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

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(2));
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

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(3));
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

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));
        Assert.That(pgResults[0].UserName, Is.EqualTo("Alice"));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(1));
        Assert.That(myResults[0].UserName, Is.EqualTo("Alice"));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(1));
        Assert.That(ssResults[0].UserName, Is.EqualTo("Alice"));
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

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));
        Assert.That(pgResults[0].UserName, Is.EqualTo("Bob"));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(1));
        Assert.That(myResults[0].UserName, Is.EqualTo("Bob"));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(1));
        Assert.That(ssResults[0].UserName, Is.EqualTo("Bob"));
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

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(3));
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

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));
        Assert.That(pgResults[0].UserName, Is.EqualTo("Alice"));
        Assert.That(pgResults[1].UserName, Is.EqualTo("Bob"));
        Assert.That(pgResults[2].UserName, Is.EqualTo("Charlie"));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));
        Assert.That(myResults[0].UserName, Is.EqualTo("Alice"));
        Assert.That(myResults[1].UserName, Is.EqualTo("Bob"));
        Assert.That(myResults[2].UserName, Is.EqualTo("Charlie"));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(3));
        Assert.That(ssResults[0].UserName, Is.EqualTo("Alice"));
        Assert.That(ssResults[1].UserName, Is.EqualTo("Bob"));
        Assert.That(ssResults[2].UserName, Is.EqualTo("Charlie"));
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

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0].UserName, Is.EqualTo("Alice"));
        Assert.That(pgResults[1].UserName, Is.EqualTo("Bob"));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0].UserName, Is.EqualTo("Alice"));
        Assert.That(myResults[1].UserName, Is.EqualTo("Bob"));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(2));
        Assert.That(ssResults[0].UserName, Is.EqualTo("Alice"));
        Assert.That(ssResults[1].UserName, Is.EqualTo("Bob"));
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

        var pg = Pg.Users().Select(u => (u.UserId, u.UserName))
            .Union(Pg.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)))
            .Except(Pg.Users().Where(u => u.UserId == 1).Select(u => (u.UserId, u.UserName)))
            .Prepare();
        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));

        var my = My.Users().Select(u => (u.UserId, u.UserName))
            .Union(My.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)))
            .Except(My.Users().Where(u => u.UserId == 1).Select(u => (u.UserId, u.UserName)))
            .Prepare();
        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));

        var ss = Ss.Users().Select(u => (u.UserId, u.UserName))
            .Union(Ss.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)))
            .Except(Ss.Users().Where(u => u.UserId == 1).Select(u => (u.UserId, u.UserName)))
            .Prepare();
        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(2));
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

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Union_WithPostUnionGroupBy()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Select(u => (u.IsActive, u.UserName))
            .UnionAll(Lite.Users().Select(u => (u.IsActive, u.UserName)))
            .GroupBy(u => u.IsActive).Prepare();

        // Verify subquery wrapping for post-union GroupBy
        var diag = lt.ToDiagnostics();
        Assert.That(diag.Sql, Does.Contain("SELECT * FROM ("));
        Assert.That(diag.Sql, Does.Contain(") AS \"__set\""));
        Assert.That(diag.Sql, Does.Contain("GROUP BY \"IsActive\""));
    }

    [Test]
    public async Task Union_WithPostUnionGroupByAndHaving()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Select(u => (u.IsActive, u.UserName))
            .UnionAll(Lite.Users().Select(u => (u.IsActive, u.UserName)))
            .GroupBy(u => u.IsActive).Having(u => u.IsActive).Prepare();

        // Verify subquery wrapping for post-union GroupBy + Having
        var diag = lt.ToDiagnostics();
        Assert.That(diag.Sql, Does.Contain("SELECT * FROM ("));
        Assert.That(diag.Sql, Does.Contain(") AS \"__set\""));
        Assert.That(diag.Sql, Does.Contain("GROUP BY \"IsActive\""));
        Assert.That(diag.Sql, Does.Contain("HAVING \"IsActive\" = 1"));
    }

    #endregion

    #region Parameterized Set Operations

    [Test]
    public async Task Union_WithCapturedVariable_CrossDialectParameters()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var minId = 2;
        var maxId = 3;
        var lt = Lite.Users().Where(u => u.UserId >= minId).Select(u => (u.UserId, u.UserName))
            .Union(Lite.Users().Where(u => u.UserId <= maxId).Select(u => (u.UserId, u.UserName)))
            .Prepare();
        var pg = Pg.Users().Where(u => u.UserId >= minId).Select(u => (u.UserId, u.UserName))
            .Union(Pg.Users().Where(u => u.UserId <= maxId).Select(u => (u.UserId, u.UserName)))
            .Prepare();
        var my = My.Users().Where(u => u.UserId >= minId).Select(u => (u.UserId, u.UserName))
            .Union(My.Users().Where(u => u.UserId <= maxId).Select(u => (u.UserId, u.UserName)))
            .Prepare();
        var ss = Ss.Users().Where(u => u.UserId >= minId).Select(u => (u.UserId, u.UserName))
            .Union(Ss.Users().Where(u => u.UserId <= maxId).Select(u => (u.UserId, u.UserName)))
            .Prepare();

        // Verify parameter indices are distinct across all dialects
        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserId\" >= @p0 UNION SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserId\" <= @p1",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserId\" >= $1 UNION SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserId\" <= $2",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE `UserId` >= ? UNION SELECT `UserId`, `UserName` FROM `users` WHERE `UserId` <= ?",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE [UserId] >= @p0 UNION SELECT [UserId], [UserName] FROM [users] WHERE [UserId] <= @p1");

        var results = await lt.ExecuteFetchAllAsync();
        // UserId >= 2: (2,Bob), (3,Charlie). UserId <= 3: (1,Alice), (2,Bob), (3,Charlie).
        // UNION: (1,Alice), (2,Bob), (3,Charlie) → 3 results
        Assert.That(results, Has.Count.EqualTo(3));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task Union_WithCapturedVariable_PostUnionWhere_ThreeLayerParams()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var minId = 1;
        var maxId = 3;
        var postFilter = 2;
        var lt = Lite.Users().Where(u => u.UserId >= minId).Select(u => (u.UserId, u.UserName))
            .Union(Lite.Users().Where(u => u.UserId <= maxId).Select(u => (u.UserId, u.UserName)))
            .Where(u => u.UserId == postFilter).Prepare();
        var pg = Pg.Users().Where(u => u.UserId >= minId).Select(u => (u.UserId, u.UserName))
            .Union(Pg.Users().Where(u => u.UserId <= maxId).Select(u => (u.UserId, u.UserName)))
            .Where(u => u.UserId == postFilter).Prepare();
        var my = My.Users().Where(u => u.UserId >= minId).Select(u => (u.UserId, u.UserName))
            .Union(My.Users().Where(u => u.UserId <= maxId).Select(u => (u.UserId, u.UserName)))
            .Where(u => u.UserId == postFilter).Prepare();
        var ss = Ss.Users().Where(u => u.UserId >= minId).Select(u => (u.UserId, u.UserName))
            .Union(Ss.Users().Where(u => u.UserId <= maxId).Select(u => (u.UserId, u.UserName)))
            .Where(u => u.UserId == postFilter).Prepare();

        // Verify 3-layer parameter indices: @p0 (left), @p1 (operand), @p2 (post-union)
        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT * FROM (SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserId\" >= @p0 UNION SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserId\" <= @p1) AS \"__set\" WHERE \"UserId\" = @p2",
            pg:     "SELECT * FROM (SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserId\" >= $1 UNION SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserId\" <= $2) AS \"__set\" WHERE \"UserId\" = $3",
            mysql:  "SELECT * FROM (SELECT `UserId`, `UserName` FROM `users` WHERE `UserId` >= ? UNION SELECT `UserId`, `UserName` FROM `users` WHERE `UserId` <= ?) AS `__set` WHERE `UserId` = ?",
            ss:     "SELECT * FROM (SELECT [UserId], [UserName] FROM [users] WHERE [UserId] >= @p0 UNION SELECT [UserId], [UserName] FROM [users] WHERE [UserId] <= @p1) AS [__set] WHERE [UserId] = @p2");

        var results = await lt.ExecuteFetchAllAsync();
        // Left: UserId >= 1 = all 3 users. Operand: UserId <= 3 = all 3 users.
        // UNION: all 3 users. Post-union WHERE UserId == 2: (2,Bob) → 1 result
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].UserName, Is.EqualTo("Bob"));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));
        Assert.That(pgResults[0].UserName, Is.EqualTo("Bob"));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(1));
        Assert.That(myResults[0].UserName, Is.EqualTo("Bob"));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(1));
        Assert.That(ssResults[0].UserName, Is.EqualTo("Bob"));
    }

    [Test]
    public async Task Union_WithCapturedVariable_PostUnionOrderBy()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var minId = 1;
        var maxId = 3;
        var lt = Lite.Users().Where(u => u.UserId >= minId).Select(u => (u.UserId, u.UserName))
            .Union(Lite.Users().Where(u => u.UserId <= maxId).Select(u => (u.UserId, u.UserName)))
            .OrderBy(u => u.UserName, Direction.Descending).Limit(2).Prepare();
        var pg = Pg.Users().Where(u => u.UserId >= minId).Select(u => (u.UserId, u.UserName))
            .Union(Pg.Users().Where(u => u.UserId <= maxId).Select(u => (u.UserId, u.UserName)))
            .OrderBy(u => u.UserName, Direction.Descending).Limit(2).Prepare();
        var my = My.Users().Where(u => u.UserId >= minId).Select(u => (u.UserId, u.UserName))
            .Union(My.Users().Where(u => u.UserId <= maxId).Select(u => (u.UserId, u.UserName)))
            .OrderBy(u => u.UserName, Direction.Descending).Limit(2).Prepare();
        var ss = Ss.Users().Where(u => u.UserId >= minId).Select(u => (u.UserId, u.UserName))
            .Union(Ss.Users().Where(u => u.UserId <= maxId).Select(u => (u.UserId, u.UserName)))
            .OrderBy(u => u.UserName, Direction.Descending).Limit(2).Prepare();

        // Verify parameter indices: @p0 (left WHERE), @p1 (operand WHERE), ORDER BY + LIMIT applied directly
        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserId\" >= @p0 UNION SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserId\" <= @p1 ORDER BY \"UserName\" DESC LIMIT 2",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserId\" >= $1 UNION SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserId\" <= $2 ORDER BY \"UserName\" DESC LIMIT 2",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE `UserId` >= ? UNION SELECT `UserId`, `UserName` FROM `users` WHERE `UserId` <= ? ORDER BY `UserName` DESC LIMIT 2",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE [UserId] >= @p0 UNION SELECT [UserId], [UserName] FROM [users] WHERE [UserId] <= @p1 ORDER BY [UserName] DESC OFFSET 0 ROWS FETCH NEXT 2 ROWS ONLY");

        var results = await lt.ExecuteFetchAllAsync();
        // All 3 users in both sides → UNION = 3 unique. ORDER BY UserName DESC: Charlie, Bob, Alice.
        // LIMIT 2: Charlie, Bob
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].UserName, Is.EqualTo("Charlie"));
        Assert.That(results[1].UserName, Is.EqualTo("Bob"));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0].UserName, Is.EqualTo("Charlie"));
        Assert.That(pgResults[1].UserName, Is.EqualTo("Bob"));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults[0].UserName, Is.EqualTo("Charlie"));
        Assert.That(myResults[1].UserName, Is.EqualTo("Bob"));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(2));
        Assert.That(ssResults[0].UserName, Is.EqualTo("Charlie"));
        Assert.That(ssResults[1].UserName, Is.EqualTo("Bob"));
    }

    [Test]
    public async Task Union_WithCapturedVariable_PostUnionWhere_DiagnosticClauseParams()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var minId = 1;
        var maxId = 3;
        var postFilter = 2;
        var lt = Lite.Users().Where(u => u.UserId >= minId).Select(u => (u.UserId, u.UserName))
            .Union(Lite.Users().Where(u => u.UserId <= maxId).Select(u => (u.UserId, u.UserName)))
            .Where(u => u.UserId == postFilter).Prepare();

        var diag = lt.ToDiagnostics();

        // AllParameters includes all 3: left + operand + post-union
        Assert.That(diag.AllParameters, Has.Count.EqualTo(3));
        Assert.That(diag.AllParameters![0].Name, Is.EqualTo("@p0"));
        Assert.That(diag.AllParameters[1].Name, Is.EqualTo("@p1"));
        Assert.That(diag.AllParameters[2].Name, Is.EqualTo("@p2"));

        // Parameters is derived from clause params (excludes operand param @p1 which has no clause)
        Assert.That(diag.Parameters, Has.Count.EqualTo(2));
        Assert.That(diag.Parameters[0].Name, Is.EqualTo("@p0"));
        Assert.That(diag.Parameters[1].Name, Is.EqualTo("@p2"));

        // Verify the post-union WHERE clause has the correct parameter name (@p2, not @p1)
        var postUnionWhere = diag.Clauses.LastOrDefault(c => c.ClauseType == "Where");
        Assert.That(postUnionWhere, Is.Not.Null, "Should have a post-union WHERE clause");
        Assert.That(postUnionWhere!.Parameters, Has.Count.EqualTo(1));
        Assert.That(postUnionWhere.Parameters[0].Name, Is.EqualTo("@p2"));
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task Union_WithDistinctOnOperand()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Select(u => (u.UserId, u.UserName))
            .Union(Lite.Users().Distinct().Select(u => (u.UserId, u.UserName)))
            .Prepare();

        var diag = lt.ToDiagnostics();
        Assert.That(diag.Sql, Does.Contain("UNION"));
        Assert.That(diag.Sql, Does.Contain("SELECT DISTINCT"));

        var results = await lt.ExecuteFetchAllAsync();
        // All 3 users from both sides, UNION removes duplicates → 3 results
        Assert.That(results, Has.Count.EqualTo(3));

        var pg = Pg.Users().Select(u => (u.UserId, u.UserName))
            .Union(Pg.Users().Distinct().Select(u => (u.UserId, u.UserName)))
            .Prepare();
        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));

        var my = My.Users().Select(u => (u.UserId, u.UserName))
            .Union(My.Users().Distinct().Select(u => (u.UserId, u.UserName)))
            .Prepare();
        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));

        var ss = Ss.Users().Select(u => (u.UserId, u.UserName))
            .Union(Ss.Users().Distinct().Select(u => (u.UserId, u.UserName)))
            .Prepare();
        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task UnionAll_WithPostUnionGroupByAndParameterizedHaving()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var activeFilter = true;
        var lt = Lite.Users().Select(u => (u.IsActive, u.UserName))
            .UnionAll(Lite.Users().Select(u => (u.IsActive, u.UserName)))
            .GroupBy(u => u.IsActive).Having(u => u.IsActive == activeFilter).Prepare();

        // Verify subquery wrapping and parameterized HAVING
        var diag = lt.ToDiagnostics();
        Assert.That(diag.Sql, Does.Contain("SELECT * FROM ("));
        Assert.That(diag.Sql, Does.Contain(") AS \"__set\""));
        Assert.That(diag.Sql, Does.Contain("GROUP BY \"IsActive\""));
        Assert.That(diag.Sql, Does.Contain("HAVING \"IsActive\" = @p0"));
        Assert.That(diag.Parameters, Has.Count.EqualTo(1));
    }

    #endregion

    #region Cross-Entity Set Operations

    [Test]
    public async Task CrossEntity_Union_TupleProjection()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Select(u => (u.UserId, u.UserName))
            .Union(Lite.Products().Select(p => (p.ProductId, p.ProductName)))
            .Prepare();
        var pg = Pg.Users().Select(u => (u.UserId, u.UserName))
            .Union(Pg.Products().Select(p => (p.ProductId, p.ProductName)))
            .Prepare();
        var my = My.Users().Select(u => (u.UserId, u.UserName))
            .Union(My.Products().Select(p => (p.ProductId, p.ProductName)))
            .Prepare();
        var ss = Ss.Users().Select(u => (u.UserId, u.UserName))
            .Union(Ss.Products().Select(p => (p.ProductId, p.ProductName)))
            .Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" UNION SELECT \"ProductId\", \"ProductName\" FROM \"products\"",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" UNION SELECT \"ProductId\", \"ProductName\" FROM \"products\"",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` UNION SELECT `ProductId`, `ProductName` FROM `products`",
            ss:     "SELECT [UserId], [UserName] FROM [users] UNION SELECT [ProductId], [ProductName] FROM [products]");

        var results = await lt.ExecuteFetchAllAsync();
        // Users: (1,Alice),(2,Bob),(3,Charlie). Products: (1,Widget),(2,Gadget),(3,Doohickey).
        // UNION removes dupes by value — all 6 are distinct → 6 results.
        // The reader uses positional column access (GetInt32(0), GetString(1)), so the C# tuple
        // element labels (UserId, UserName) come from the receiver projection but the actual rows
        // are interleaved from both tables. Verify by value to confirm the reader sees product
        // rows, not just user rows.
        var values = results.OrderBy(r => r.UserName).ToList();
        Assert.That(values, Has.Count.EqualTo(6));
        Assert.That(values, Is.EqualTo(new[]
        {
            (1, "Alice"),
            (2, "Bob"),
            (3, "Charlie"),
            (3, "Doohickey"),
            (2, "Gadget"),
            (1, "Widget"),
        }));

        var pgResults = await pg.ExecuteFetchAllAsync();
        var pgValues = pgResults.OrderBy(r => r.UserName).ToList();
        Assert.That(pgValues, Has.Count.EqualTo(6));
        Assert.That(pgValues, Is.EqualTo(new[]
        {
            (1, "Alice"),
            (2, "Bob"),
            (3, "Charlie"),
            (3, "Doohickey"),
            (2, "Gadget"),
            (1, "Widget"),
        }));

        var myResults = await my.ExecuteFetchAllAsync();
        var myValues = myResults.OrderBy(r => r.UserName).ToList();
        Assert.That(myValues, Has.Count.EqualTo(6));
        Assert.That(myValues, Is.EqualTo(new[]
        {
            (1, "Alice"),
            (2, "Bob"),
            (3, "Charlie"),
            (3, "Doohickey"),
            (2, "Gadget"),
            (1, "Widget"),
        }));

        var ssResults = await ss.ExecuteFetchAllAsync();
        var ssValues = ssResults.OrderBy(r => r.UserName).ToList();
        Assert.That(ssValues, Has.Count.EqualTo(6));
        Assert.That(ssValues, Is.EqualTo(new[]
        {
            (1, "Alice"),
            (2, "Bob"),
            (3, "Charlie"),
            (3, "Doohickey"),
            (2, "Gadget"),
            (1, "Widget"),
        }));
    }

    [Test]
    public async Task CrossEntity_UnionAll_TupleProjection()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Select(u => (u.UserId, u.UserName))
            .UnionAll(Lite.Products().Select(p => (p.ProductId, p.ProductName)))
            .Prepare();
        var pg = Pg.Users().Select(u => (u.UserId, u.UserName))
            .UnionAll(Pg.Products().Select(p => (p.ProductId, p.ProductName)))
            .Prepare();
        var my = My.Users().Select(u => (u.UserId, u.UserName))
            .UnionAll(My.Products().Select(p => (p.ProductId, p.ProductName)))
            .Prepare();
        var ss = Ss.Users().Select(u => (u.UserId, u.UserName))
            .UnionAll(Ss.Products().Select(p => (p.ProductId, p.ProductName)))
            .Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" UNION ALL SELECT \"ProductId\", \"ProductName\" FROM \"products\"",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" UNION ALL SELECT \"ProductId\", \"ProductName\" FROM \"products\"",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` UNION ALL SELECT `ProductId`, `ProductName` FROM `products`",
            ss:     "SELECT [UserId], [UserName] FROM [users] UNION ALL SELECT [ProductId], [ProductName] FROM [products]");

        var results = await lt.ExecuteFetchAllAsync();
        // UNION ALL keeps all rows: 3 users + 3 products → 6 results.
        // Verify by value to confirm both tables flow through the positional reader.
        var values = results.OrderBy(r => r.UserName).ToList();
        Assert.That(values, Has.Count.EqualTo(6));
        Assert.That(values, Is.EqualTo(new[]
        {
            (1, "Alice"),
            (2, "Bob"),
            (3, "Charlie"),
            (3, "Doohickey"),
            (2, "Gadget"),
            (1, "Widget"),
        }));

        var pgResults = await pg.ExecuteFetchAllAsync();
        var pgValues = pgResults.OrderBy(r => r.UserName).ToList();
        Assert.That(pgValues, Has.Count.EqualTo(6));
        Assert.That(pgValues, Is.EqualTo(new[]
        {
            (1, "Alice"),
            (2, "Bob"),
            (3, "Charlie"),
            (3, "Doohickey"),
            (2, "Gadget"),
            (1, "Widget"),
        }));

        var myResults = await my.ExecuteFetchAllAsync();
        var myValues = myResults.OrderBy(r => r.UserName).ToList();
        Assert.That(myValues, Has.Count.EqualTo(6));
        Assert.That(myValues, Is.EqualTo(new[]
        {
            (1, "Alice"),
            (2, "Bob"),
            (3, "Charlie"),
            (3, "Doohickey"),
            (2, "Gadget"),
            (1, "Widget"),
        }));

        var ssResults = await ss.ExecuteFetchAllAsync();
        var ssValues = ssResults.OrderBy(r => r.UserName).ToList();
        Assert.That(ssValues, Has.Count.EqualTo(6));
        Assert.That(ssValues, Is.EqualTo(new[]
        {
            (1, "Alice"),
            (2, "Bob"),
            (3, "Charlie"),
            (3, "Doohickey"),
            (2, "Gadget"),
            (1, "Widget"),
        }));
    }

    [Test]
    public async Task CrossEntity_Intersect_TupleProjection()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Select(u => (u.UserId, u.UserName))
            .Intersect(Lite.Products().Select(p => (p.ProductId, p.ProductName)))
            .Prepare();
        var pg = Pg.Users().Select(u => (u.UserId, u.UserName))
            .Intersect(Pg.Products().Select(p => (p.ProductId, p.ProductName)))
            .Prepare();
        var my = My.Users().Select(u => (u.UserId, u.UserName))
            .Intersect(My.Products().Select(p => (p.ProductId, p.ProductName)))
            .Prepare();
        var ss = Ss.Users().Select(u => (u.UserId, u.UserName))
            .Intersect(Ss.Products().Select(p => (p.ProductId, p.ProductName)))
            .Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" INTERSECT SELECT \"ProductId\", \"ProductName\" FROM \"products\"",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" INTERSECT SELECT \"ProductId\", \"ProductName\" FROM \"products\"",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` INTERSECT SELECT `ProductId`, `ProductName` FROM `products`",
            ss:     "SELECT [UserId], [UserName] FROM [users] INTERSECT SELECT [ProductId], [ProductName] FROM [products]");

        var results = await lt.ExecuteFetchAllAsync();
        // Same IDs (1,2,3) but different names → no exact row matches → 0 results
        Assert.That(results, Has.Count.EqualTo(0));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(0));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(0));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(0));
    }

    [Test]
    public async Task CrossEntity_Except_TupleProjection()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Select(u => (u.UserId, u.UserName))
            .Except(Lite.Products().Select(p => (p.ProductId, p.ProductName)))
            .Prepare();
        var pg = Pg.Users().Select(u => (u.UserId, u.UserName))
            .Except(Pg.Products().Select(p => (p.ProductId, p.ProductName)))
            .Prepare();
        var my = My.Users().Select(u => (u.UserId, u.UserName))
            .Except(My.Products().Select(p => (p.ProductId, p.ProductName)))
            .Prepare();
        var ss = Ss.Users().Select(u => (u.UserId, u.UserName))
            .Except(Ss.Products().Select(p => (p.ProductId, p.ProductName)))
            .Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" EXCEPT SELECT \"ProductId\", \"ProductName\" FROM \"products\"",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" EXCEPT SELECT \"ProductId\", \"ProductName\" FROM \"products\"",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` EXCEPT SELECT `ProductId`, `ProductName` FROM `products`",
            ss:     "SELECT [UserId], [UserName] FROM [users] EXCEPT SELECT [ProductId], [ProductName] FROM [products]");

        var results = await lt.ExecuteFetchAllAsync();
        // All user rows are unique vs product rows → all 3 user rows survive EXCEPT.
        // Verify the surviving rows are the user rows (positional reader, not product rows).
        var values = results.OrderBy(r => r.UserName).ToList();
        Assert.That(values, Is.EqualTo(new[]
        {
            (1, "Alice"),
            (2, "Bob"),
            (3, "Charlie"),
        }));

        var pgResults = await pg.ExecuteFetchAllAsync();
        var pgValues = pgResults.OrderBy(r => r.UserName).ToList();
        Assert.That(pgValues, Is.EqualTo(new[]
        {
            (1, "Alice"),
            (2, "Bob"),
            (3, "Charlie"),
        }));

        var myResults = await my.ExecuteFetchAllAsync();
        var myValues = myResults.OrderBy(r => r.UserName).ToList();
        Assert.That(myValues, Is.EqualTo(new[]
        {
            (1, "Alice"),
            (2, "Bob"),
            (3, "Charlie"),
        }));

        var ssResults = await ss.ExecuteFetchAllAsync();
        var ssValues = ssResults.OrderBy(r => r.UserName).ToList();
        Assert.That(ssValues, Is.EqualTo(new[]
        {
            (1, "Alice"),
            (2, "Bob"),
            (3, "Charlie"),
        }));
    }

    [Test]
    public async Task CrossEntity_IntersectAll_TupleProjection()
    {
        // INTERSECT ALL is PostgreSQL-only (QRY070 blocks other dialects).
        await using var t = await QueryTestHarness.CreateAsync();
        var Pg = t.Pg;

        var pg = Pg.Users().Select(u => (u.UserId, u.UserName))
            .IntersectAll(Pg.Products().Select(p => (p.ProductId, p.ProductName)))
            .Prepare();

        Assert.That(pg.ToDiagnostics().Sql, Is.EqualTo(
            "SELECT \"UserId\", \"UserName\" FROM \"users\" INTERSECT ALL SELECT \"ProductId\", \"ProductName\" FROM \"products\""));
    }

    [Test]
    public async Task CrossEntity_ExceptAll_TupleProjection()
    {
        // EXCEPT ALL is PostgreSQL-only (QRY071 blocks other dialects).
        await using var t = await QueryTestHarness.CreateAsync();
        var Pg = t.Pg;

        var pg = Pg.Users().Select(u => (u.UserId, u.UserName))
            .ExceptAll(Pg.Products().Select(p => (p.ProductId, p.ProductName)))
            .Prepare();

        Assert.That(pg.ToDiagnostics().Sql, Is.EqualTo(
            "SELECT \"UserId\", \"UserName\" FROM \"users\" EXCEPT ALL SELECT \"ProductId\", \"ProductName\" FROM \"products\""));
    }

    [Test]
    public async Task CrossEntity_Union_WithParameters()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var minUserId = 2;
        var maxPrice = 40.0m;
        var lt = Lite.Users().Where(u => u.UserId >= minUserId).Select(u => (u.UserId, u.UserName))
            .Union(Lite.Products().Where(p => p.Price <= maxPrice).Select(p => (p.ProductId, p.ProductName)))
            .Prepare();
        var pg = Pg.Users().Where(u => u.UserId >= minUserId).Select(u => (u.UserId, u.UserName))
            .Union(Pg.Products().Where(p => p.Price <= maxPrice).Select(p => (p.ProductId, p.ProductName)))
            .Prepare();
        var my = My.Users().Where(u => u.UserId >= minUserId).Select(u => (u.UserId, u.UserName))
            .Union(My.Products().Where(p => p.Price <= maxPrice).Select(p => (p.ProductId, p.ProductName)))
            .Prepare();
        var ss = Ss.Users().Where(u => u.UserId >= minUserId).Select(u => (u.UserId, u.UserName))
            .Union(Ss.Products().Where(p => p.Price <= maxPrice).Select(p => (p.ProductId, p.ProductName)))
            .Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserId\" >= @p0 UNION SELECT \"ProductId\", \"ProductName\" FROM \"products\" WHERE \"Price\" <= @p1",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserId\" >= $1 UNION SELECT \"ProductId\", \"ProductName\" FROM \"products\" WHERE \"Price\" <= $2",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE `UserId` >= ? UNION SELECT `ProductId`, `ProductName` FROM `products` WHERE `Price` <= ?",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE [UserId] >= @p0 UNION SELECT [ProductId], [ProductName] FROM [products] WHERE [Price] <= @p1");

        var results = await lt.ExecuteFetchAllAsync();
        // Users where UserId >= 2: Bob(2), Charlie(3). Products where Price <= 40: Widget(1, 29.99), Doohickey(3, 9.95).
        // UNION: 4 distinct rows. Verify by value (not just count) to confirm product rows
        // actually flow through the positional reader, matching the row-value strengthening
        // already applied to CrossEntity_Union_TupleProjection.
        var values = results.OrderBy(r => r.UserName).ToList();
        Assert.That(values, Is.EqualTo(new[]
        {
            (2, "Bob"),
            (3, "Charlie"),
            (3, "Doohickey"),
            (1, "Widget"),
        }));

        var pgResults = await pg.ExecuteFetchAllAsync();
        var pgValues = pgResults.OrderBy(r => r.UserName).ToList();
        Assert.That(pgValues, Is.EqualTo(new[]
        {
            (2, "Bob"),
            (3, "Charlie"),
            (3, "Doohickey"),
            (1, "Widget"),
        }));

        var myResults = await my.ExecuteFetchAllAsync();
        var myValues = myResults.OrderBy(r => r.UserName).ToList();
        Assert.That(myValues, Is.EqualTo(new[]
        {
            (2, "Bob"),
            (3, "Charlie"),
            (3, "Doohickey"),
            (1, "Widget"),
        }));

        var ssResults = await ss.ExecuteFetchAllAsync();
        var ssValues = ssResults.OrderBy(r => r.UserName).ToList();
        Assert.That(ssValues, Is.EqualTo(new[]
        {
            (2, "Bob"),
            (3, "Charlie"),
            (3, "Doohickey"),
            (1, "Widget"),
        }));
    }

    [Test]
    public async Task CrossEntity_Union_WithPostUnionOrderByLimit()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Select(u => (u.UserId, u.UserName))
            .Union(Lite.Products().Select(p => (p.ProductId, p.ProductName)))
            .OrderBy(u => u.UserName).Limit(3).Prepare();
        var pg = Pg.Users().Select(u => (u.UserId, u.UserName))
            .Union(Pg.Products().Select(p => (p.ProductId, p.ProductName)))
            .OrderBy(u => u.UserName).Limit(3).Prepare();
        var my = My.Users().Select(u => (u.UserId, u.UserName))
            .Union(My.Products().Select(p => (p.ProductId, p.ProductName)))
            .OrderBy(u => u.UserName).Limit(3).Prepare();
        var ss = Ss.Users().Select(u => (u.UserId, u.UserName))
            .Union(Ss.Products().Select(p => (p.ProductId, p.ProductName)))
            .OrderBy(u => u.UserName).Limit(3).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" UNION SELECT \"ProductId\", \"ProductName\" FROM \"products\" ORDER BY \"UserName\" ASC LIMIT 3",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" UNION SELECT \"ProductId\", \"ProductName\" FROM \"products\" ORDER BY \"UserName\" ASC LIMIT 3",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` UNION SELECT `ProductId`, `ProductName` FROM `products` ORDER BY `UserName` ASC LIMIT 3",
            ss:     "SELECT [UserId], [UserName] FROM [users] UNION SELECT [ProductId], [ProductName] FROM [products] ORDER BY [UserName] ASC OFFSET 0 ROWS FETCH NEXT 3 ROWS ONLY");

        var results = await lt.ExecuteFetchAllAsync();
        // All 6 rows sorted by name ASC: Alice, Bob, Charlie, Doohickey, Gadget, Widget → first 3
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
        Assert.That(results[1].UserName, Is.EqualTo("Bob"));
        Assert.That(results[2].UserName, Is.EqualTo("Charlie"));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));
        Assert.That(pgResults[0].UserName, Is.EqualTo("Alice"));
        Assert.That(pgResults[1].UserName, Is.EqualTo("Bob"));
        Assert.That(pgResults[2].UserName, Is.EqualTo("Charlie"));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));
        Assert.That(myResults[0].UserName, Is.EqualTo("Alice"));
        Assert.That(myResults[1].UserName, Is.EqualTo("Bob"));
        Assert.That(myResults[2].UserName, Is.EqualTo("Charlie"));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(3));
        Assert.That(ssResults[0].UserName, Is.EqualTo("Alice"));
        Assert.That(ssResults[1].UserName, Is.EqualTo("Bob"));
        Assert.That(ssResults[2].UserName, Is.EqualTo("Charlie"));
    }

    #endregion

    #region Lambda-form set operations (multi-context entity resolution)

    [Test]
    public async Task LambdaUnion_SameEntity_Identity()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.IsActive).Union(x => x.Where(u => u.UserId == 1)).Prepare();
        var pg = Pg.Users().Where(u => u.IsActive).Union(x => x.Where(u => u.UserId == 1)).Prepare();
        var my = My.Users().Where(u => u.IsActive).Union(x => x.Where(u => u.UserId == 1)).Prepare();
        var ss = Ss.Users().Where(u => u.IsActive).Union(x => x.Where(u => u.UserId == 1)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"IsActive\" = 1 UNION SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"UserId\" = 1",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"IsActive\" = TRUE UNION SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"UserId\" = 1",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE `IsActive` = 1 UNION SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE `UserId` = 1",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE [IsActive] = 1 UNION SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE [UserId] = 1");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task LambdaUnionAll_SameEntity_Identity()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.IsActive).UnionAll(x => x.Where(u => u.UserId == 1)).Prepare();
        var pg = Pg.Users().Where(u => u.IsActive).UnionAll(x => x.Where(u => u.UserId == 1)).Prepare();
        var my = My.Users().Where(u => u.IsActive).UnionAll(x => x.Where(u => u.UserId == 1)).Prepare();
        var ss = Ss.Users().Where(u => u.IsActive).UnionAll(x => x.Where(u => u.UserId == 1)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"IsActive\" = 1 UNION ALL SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"UserId\" = 1",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"IsActive\" = TRUE UNION ALL SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"UserId\" = 1",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE `IsActive` = 1 UNION ALL SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE `UserId` = 1",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE [IsActive] = 1 UNION ALL SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE [UserId] = 1");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task LambdaIntersect_SameEntity_Identity()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.IsActive).Intersect(x => x.Where(u => u.UserId == 1)).Prepare();
        var pg = Pg.Users().Where(u => u.IsActive).Intersect(x => x.Where(u => u.UserId == 1)).Prepare();
        var my = My.Users().Where(u => u.IsActive).Intersect(x => x.Where(u => u.UserId == 1)).Prepare();
        var ss = Ss.Users().Where(u => u.IsActive).Intersect(x => x.Where(u => u.UserId == 1)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"IsActive\" = 1 INTERSECT SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"UserId\" = 1",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"IsActive\" = TRUE INTERSECT SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"UserId\" = 1",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE `IsActive` = 1 INTERSECT SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE `UserId` = 1",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE [IsActive] = 1 INTERSECT SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE [UserId] = 1");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));
        Assert.That(pgResults[0].UserName, Is.EqualTo("Alice"));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(1));
        Assert.That(myResults[0].UserName, Is.EqualTo("Alice"));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(1));
        Assert.That(ssResults[0].UserName, Is.EqualTo("Alice"));
    }

    [Test]
    public async Task LambdaExcept_SameEntity_Identity()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.IsActive).Except(x => x.Where(u => u.UserId == 1)).Prepare();
        var pg = Pg.Users().Where(u => u.IsActive).Except(x => x.Where(u => u.UserId == 1)).Prepare();
        var my = My.Users().Where(u => u.IsActive).Except(x => x.Where(u => u.UserId == 1)).Prepare();
        var ss = Ss.Users().Where(u => u.IsActive).Except(x => x.Where(u => u.UserId == 1)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"IsActive\" = 1 EXCEPT SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"UserId\" = 1",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"IsActive\" = TRUE EXCEPT SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"UserId\" = 1",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE `IsActive` = 1 EXCEPT SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE `UserId` = 1",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE [IsActive] = 1 EXCEPT SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE [UserId] = 1");

        var results = await lt.ExecuteFetchAllAsync();
        // EXCEPT: active users minus UserId=1(Alice) → Bob only
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].UserName, Is.EqualTo("Bob"));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));
        Assert.That(pgResults[0].UserName, Is.EqualTo("Bob"));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(1));
        Assert.That(myResults[0].UserName, Is.EqualTo("Bob"));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(1));
        Assert.That(ssResults[0].UserName, Is.EqualTo("Bob"));
    }

    #endregion

    #region Set Operations with Parameterized Window Functions

    [Test]
    public async Task UnionAll_WithVariableWindowFunctionArg()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var buckets = 2;
        // Main query has identity select; operand has a Select with variable Ntile arg.
        // The operand's projection parameter must be remapped to a global index.
        var lt = Lite.Orders().Where(o => true).Select(o => (o.OrderId, Grp: Sql.Ntile(2, over => over.OrderBy(o.OrderDate))))
            .UnionAll(Lite.Orders().Where(o => true).Select(o => (o.OrderId, Grp: Sql.Ntile(buckets, over => over.OrderBy(o.OrderDate)))))
            .Prepare();
        var pg = Pg.Orders().Where(o => true).Select(o => (o.OrderId, Grp: Sql.Ntile(2, over => over.OrderBy(o.OrderDate))))
            .UnionAll(Pg.Orders().Where(o => true).Select(o => (o.OrderId, Grp: Sql.Ntile(buckets, over => over.OrderBy(o.OrderDate)))))
            .Prepare();
        var my = My.Orders().Where(o => true).Select(o => (o.OrderId, Grp: Sql.Ntile(2, over => over.OrderBy(o.OrderDate))))
            .UnionAll(My.Orders().Where(o => true).Select(o => (o.OrderId, Grp: Sql.Ntile(buckets, over => over.OrderBy(o.OrderDate)))))
            .Prepare();
        var ss = Ss.Orders().Where(o => true).Select(o => (o.OrderId, Grp: Sql.Ntile(2, over => over.OrderBy(o.OrderDate))))
            .UnionAll(Ss.Orders().Where(o => true).Select(o => (o.OrderId, Grp: Sql.Ntile(buckets, over => over.OrderBy(o.OrderDate)))))
            .Prepare();

        // The operand's variable Ntile arg should be @p0/$1/?/@p0 — not raw @__proj0
        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", NTILE(2) OVER (ORDER BY \"OrderDate\") AS \"Grp\" FROM \"orders\" UNION ALL SELECT \"OrderId\", NTILE(@p0) OVER (ORDER BY \"OrderDate\") AS \"Grp\" FROM \"orders\"",
            pg:     "SELECT \"OrderId\", NTILE(2) OVER (ORDER BY \"OrderDate\") AS \"Grp\" FROM \"orders\" UNION ALL SELECT \"OrderId\", NTILE($1) OVER (ORDER BY \"OrderDate\") AS \"Grp\" FROM \"orders\"",
            mysql:  "SELECT `OrderId`, NTILE(2) OVER (ORDER BY `OrderDate`) AS `Grp` FROM `orders` UNION ALL SELECT `OrderId`, NTILE(?) OVER (ORDER BY `OrderDate`) AS `Grp` FROM `orders`",
            ss:     "SELECT [OrderId], NTILE(2) OVER (ORDER BY [OrderDate]) AS [Grp] FROM [orders] UNION ALL SELECT [OrderId], NTILE(@p0) OVER (ORDER BY [OrderDate]) AS [Grp] FROM [orders]");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(6)); // 3 orders x 2 sides (UNION ALL keeps dupes)

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(6));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(6));

        // ss execution skipped — see #274 (BIGINT-vs-Int32 narrow on
        // NTILE(...) projection). SQL-string assertion above already
        // covers the emit shape on Ss.
    }

    [Test]
    public async Task UnionAll_WithVariableWindowFunctionArg_ParamOffset()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var minId = 0;
        var buckets = 2;
        // Main query has a parameterized Where (@p0), so the operand's Ntile variable
        // must land at @p1/$2, verifying global index offset across the set boundary.
        var lt = Lite.Orders().Where(o => o.OrderId > minId).Select(o => (o.OrderId, Grp: Sql.Ntile(2, over => over.OrderBy(o.OrderDate))))
            .UnionAll(Lite.Orders().Where(o => true).Select(o => (o.OrderId, Grp: Sql.Ntile(buckets, over => over.OrderBy(o.OrderDate)))))
            .Prepare();
        var pg = Pg.Orders().Where(o => o.OrderId > minId).Select(o => (o.OrderId, Grp: Sql.Ntile(2, over => over.OrderBy(o.OrderDate))))
            .UnionAll(Pg.Orders().Where(o => true).Select(o => (o.OrderId, Grp: Sql.Ntile(buckets, over => over.OrderBy(o.OrderDate)))))
            .Prepare();
        var my = My.Orders().Where(o => o.OrderId > minId).Select(o => (o.OrderId, Grp: Sql.Ntile(2, over => over.OrderBy(o.OrderDate))))
            .UnionAll(My.Orders().Where(o => true).Select(o => (o.OrderId, Grp: Sql.Ntile(buckets, over => over.OrderBy(o.OrderDate)))))
            .Prepare();
        var ss = Ss.Orders().Where(o => o.OrderId > minId).Select(o => (o.OrderId, Grp: Sql.Ntile(2, over => over.OrderBy(o.OrderDate))))
            .UnionAll(Ss.Orders().Where(o => true).Select(o => (o.OrderId, Grp: Sql.Ntile(buckets, over => over.OrderBy(o.OrderDate)))))
            .Prepare();

        // @p0/$1 = Where param (minId), @p1/$2 = operand Ntile param (buckets)
        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", NTILE(2) OVER (ORDER BY \"OrderDate\") AS \"Grp\" FROM \"orders\" WHERE \"OrderId\" > @p0 UNION ALL SELECT \"OrderId\", NTILE(@p1) OVER (ORDER BY \"OrderDate\") AS \"Grp\" FROM \"orders\"",
            pg:     "SELECT \"OrderId\", NTILE(2) OVER (ORDER BY \"OrderDate\") AS \"Grp\" FROM \"orders\" WHERE \"OrderId\" > $1 UNION ALL SELECT \"OrderId\", NTILE($2) OVER (ORDER BY \"OrderDate\") AS \"Grp\" FROM \"orders\"",
            mysql:  "SELECT `OrderId`, NTILE(2) OVER (ORDER BY `OrderDate`) AS `Grp` FROM `orders` WHERE `OrderId` > ? UNION ALL SELECT `OrderId`, NTILE(?) OVER (ORDER BY `OrderDate`) AS `Grp` FROM `orders`",
            ss:     "SELECT [OrderId], NTILE(2) OVER (ORDER BY [OrderDate]) AS [Grp] FROM [orders] WHERE [OrderId] > @p0 UNION ALL SELECT [OrderId], NTILE(@p1) OVER (ORDER BY [OrderDate]) AS [Grp] FROM [orders]");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(6)); // 3 orders (all > 0) + 3 orders

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(6));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(6));

        // ss execution skipped — see #274 (BIGINT-vs-Int32 narrow on
        // NTILE(...) projection). SQL-string assertion above already
        // covers the emit shape on Ss.
    }

    #endregion
}
