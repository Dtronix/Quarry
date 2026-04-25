using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;

[TestFixture]
internal class CrossDialectHasManyThroughTests
{
    #region HasManyThrough with Any predicate

    [Test]
    public async Task HasManyThrough_Any_WithPredicate()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.Addresses.Any(a => a.City == "Portland"))
            .Select(u => u.UserName).ToDiagnostics();
        var pg = Pg.Users().Where(u => u.Addresses.Any(a => a.City == "Portland"))
            .Select(u => u.UserName).ToDiagnostics();
        var my = My.Users().Where(u => u.Addresses.Any(a => a.City == "Portland"))
            .Select(u => u.UserName).ToDiagnostics();
        var ss = Ss.Users().Where(u => u.Addresses.Any(a => a.City == "Portland"))
            .Select(u => u.UserName).ToDiagnostics();

        // String literals in subquery predicates are inlined across all dialects
        QueryTestHarness.AssertDialects(
            lt, pg, my, ss,
            sqlite: "SELECT \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"user_addresses\" AS \"sq0\" INNER JOIN \"addresses\" AS \"j0\" ON \"sq0\".\"AddressId\" = \"j0\".\"AddressId\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"j0\".\"City\" = 'Portland'))",
            pg:     "SELECT \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"user_addresses\" AS \"sq0\" INNER JOIN \"addresses\" AS \"j0\" ON \"sq0\".\"AddressId\" = \"j0\".\"AddressId\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"j0\".\"City\" = 'Portland'))",
            mysql:  "SELECT `UserName` FROM `users` WHERE EXISTS (SELECT 1 FROM `user_addresses` AS `sq0` INNER JOIN `addresses` AS `j0` ON `sq0`.`AddressId` = `j0`.`AddressId` WHERE `sq0`.`UserId` = `users`.`UserId` AND (`j0`.`City` = 'Portland'))",
            ss:     "SELECT [UserName] FROM [users] WHERE EXISTS (SELECT 1 FROM [user_addresses] AS [sq0] INNER JOIN [addresses] AS [j0] ON [sq0].[AddressId] = [j0].[AddressId] WHERE [sq0].[UserId] = [users].[UserId] AND ([j0].[City] = 'Portland'))");
    }

    #endregion

    #region HasManyThrough execution verification

    [Test]
    public async Task HasManyThrough_Any_ExecutesCorrectly()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, _, _) = t;

        // Alice → Portland, Seattle; Bob → Portland; Charlie → none
        var results = await Lite.Users().Where(u => u.Addresses.Any(a => a.City == "Portland"))
            .Select(u => u.UserName).Prepare().ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo("Alice"));
        Assert.That(results[1], Is.EqualTo("Bob"));

        var pgResults = await Pg.Users().Where(u => u.Addresses.Any(a => a.City == "Portland"))
            .Select(u => u.UserName).Prepare().ExecuteFetchAllAsync();

        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0], Is.EqualTo("Alice"));
        Assert.That(pgResults[1], Is.EqualTo("Bob"));
    }

    [Test]
    public async Task HasManyThrough_Any_NoPredicate_ExecutesCorrectly()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, _, _) = t;

        // Alice → Portland, Seattle; Bob → Portland; Charlie → none
        var results = await Lite.Users().Where(u => u.Addresses.Any())
            .Select(u => u.UserName).Prepare().ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo("Alice"));
        Assert.That(results[1], Is.EqualTo("Bob"));

        var pgResults = await Pg.Users().Where(u => u.Addresses.Any())
            .Select(u => u.UserName).Prepare().ExecuteFetchAllAsync();

        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0], Is.EqualTo("Alice"));
        Assert.That(pgResults[1], Is.EqualTo("Bob"));
    }

    #endregion

    #region HasManyThrough Count

    [Test]
    public async Task HasManyThrough_Count_CrossDialect()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var threshold = 1;
        var lite = Lite.Users().Where(u => u.Addresses.Count() > threshold).Select(u => u.UserName).Prepare();
        var pg   = Pg.Users().Where(u => u.Addresses.Count() > threshold).Select(u => u.UserName).Prepare();
        var my   = My.Users().Where(u => u.Addresses.Count() > threshold).Select(u => u.UserName).Prepare();
        var ss   = Ss.Users().Where(u => u.Addresses.Count() > threshold).Select(u => u.UserName).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserName\" FROM \"users\" WHERE (SELECT COUNT(*) FROM \"user_addresses\" AS \"sq0\" INNER JOIN \"addresses\" AS \"j0\" ON \"sq0\".\"AddressId\" = \"j0\".\"AddressId\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") > @p0",
            pg:     "SELECT \"UserName\" FROM \"users\" WHERE (SELECT COUNT(*) FROM \"user_addresses\" AS \"sq0\" INNER JOIN \"addresses\" AS \"j0\" ON \"sq0\".\"AddressId\" = \"j0\".\"AddressId\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") > $1",
            mysql:  "SELECT `UserName` FROM `users` WHERE (SELECT COUNT(*) FROM `user_addresses` AS `sq0` INNER JOIN `addresses` AS `j0` ON `sq0`.`AddressId` = `j0`.`AddressId` WHERE `sq0`.`UserId` = `users`.`UserId`) > ?",
            ss:     "SELECT [UserName] FROM [users] WHERE (SELECT COUNT(*) FROM [user_addresses] AS [sq0] INNER JOIN [addresses] AS [j0] ON [sq0].[AddressId] = [j0].[AddressId] WHERE [sq0].[UserId] = [users].[UserId]) > @p0");

        // Only Alice has > 1 address (Portland + Seattle)
        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo("Alice"));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));
        Assert.That(pgResults[0], Is.EqualTo("Alice"));
    }

    [Test]
    public async Task HasManyThrough_Count_WithPredicate_CrossDialect()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var threshold = 0;
        var lt = Lite.Users().Where(u => u.Addresses.Count(a => a.City == "Portland") > threshold)
            .Select(u => u.UserName).ToDiagnostics();
        var pg = Pg.Users().Where(u => u.Addresses.Count(a => a.City == "Portland") > threshold)
            .Select(u => u.UserName).ToDiagnostics();
        var my = My.Users().Where(u => u.Addresses.Count(a => a.City == "Portland") > threshold)
            .Select(u => u.UserName).ToDiagnostics();
        var ss = Ss.Users().Where(u => u.Addresses.Count(a => a.City == "Portland") > threshold)
            .Select(u => u.UserName).ToDiagnostics();

        QueryTestHarness.AssertDialects(
            lt, pg, my, ss,
            sqlite: "SELECT \"UserName\" FROM \"users\" WHERE (SELECT COUNT(*) FROM \"user_addresses\" AS \"sq0\" INNER JOIN \"addresses\" AS \"j0\" ON \"sq0\".\"AddressId\" = \"j0\".\"AddressId\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"j0\".\"City\" = 'Portland')) > @p0",
            pg:     "SELECT \"UserName\" FROM \"users\" WHERE (SELECT COUNT(*) FROM \"user_addresses\" AS \"sq0\" INNER JOIN \"addresses\" AS \"j0\" ON \"sq0\".\"AddressId\" = \"j0\".\"AddressId\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"j0\".\"City\" = 'Portland')) > $1",
            mysql:  "SELECT `UserName` FROM `users` WHERE (SELECT COUNT(*) FROM `user_addresses` AS `sq0` INNER JOIN `addresses` AS `j0` ON `sq0`.`AddressId` = `j0`.`AddressId` WHERE `sq0`.`UserId` = `users`.`UserId` AND (`j0`.`City` = 'Portland')) > ?",
            ss:     "SELECT [UserName] FROM [users] WHERE (SELECT COUNT(*) FROM [user_addresses] AS [sq0] INNER JOIN [addresses] AS [j0] ON [sq0].[AddressId] = [j0].[AddressId] WHERE [sq0].[UserId] = [users].[UserId] AND ([j0].[City] = 'Portland')) > @p0");
    }

    #endregion

    #region HasManyThrough Any without predicate

    [Test]
    public async Task HasManyThrough_Any_NoPredicate_CrossDialect()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.Addresses.Any())
            .Select(u => u.UserName).ToDiagnostics();
        var pg = Pg.Users().Where(u => u.Addresses.Any())
            .Select(u => u.UserName).ToDiagnostics();
        var my = My.Users().Where(u => u.Addresses.Any())
            .Select(u => u.UserName).ToDiagnostics();
        var ss = Ss.Users().Where(u => u.Addresses.Any())
            .Select(u => u.UserName).ToDiagnostics();

        QueryTestHarness.AssertDialects(
            lt, pg, my, ss,
            sqlite: "SELECT \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"user_addresses\" AS \"sq0\" INNER JOIN \"addresses\" AS \"j0\" ON \"sq0\".\"AddressId\" = \"j0\".\"AddressId\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\")",
            pg:     "SELECT \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"user_addresses\" AS \"sq0\" INNER JOIN \"addresses\" AS \"j0\" ON \"sq0\".\"AddressId\" = \"j0\".\"AddressId\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\")",
            mysql:  "SELECT `UserName` FROM `users` WHERE EXISTS (SELECT 1 FROM `user_addresses` AS `sq0` INNER JOIN `addresses` AS `j0` ON `sq0`.`AddressId` = `j0`.`AddressId` WHERE `sq0`.`UserId` = `users`.`UserId`)",
            ss:     "SELECT [UserName] FROM [users] WHERE EXISTS (SELECT 1 FROM [user_addresses] AS [sq0] INNER JOIN [addresses] AS [j0] ON [sq0].[AddressId] = [j0].[AddressId] WHERE [sq0].[UserId] = [users].[UserId])");
    }

    #endregion

    #region Issue #257 — HasManyThrough aggregates in Select projection

    /// <summary>
    /// Exercises ChainAnalyzer.ResolveSubqueryTargetEntity's ThroughNavigation branch
    /// (Sum on a HasManyThrough nav resolves the selector against the target entity, not
    /// the junction). Uses Max on AddressId because AddressSchema has no decimal columns.
    /// </summary>
    [Test]
    public async Task Select_HasManyThrough_Max_InTuple()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.Addresses.Any())
            .Select(u => (u.UserName, MaxAddrId: u.Addresses.Max(a => a.AddressId))).Prepare();
        var pg = Pg.Users().Where(u => u.Addresses.Any())
            .Select(u => (u.UserName, MaxAddrId: u.Addresses.Max(a => a.AddressId))).Prepare();
        var my = My.Users().Where(u => u.Addresses.Any())
            .Select(u => (u.UserName, MaxAddrId: u.Addresses.Max(a => a.AddressId))).Prepare();
        var ss = Ss.Users().Where(u => u.Addresses.Any())
            .Select(u => (u.UserName, MaxAddrId: u.Addresses.Max(a => a.AddressId))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserName\", (SELECT MAX(\"j0\".\"AddressId\") FROM \"user_addresses\" AS \"sq0\" INNER JOIN \"addresses\" AS \"j0\" ON \"sq0\".\"AddressId\" = \"j0\".\"AddressId\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"MaxAddrId\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"user_addresses\" AS \"sq0\" INNER JOIN \"addresses\" AS \"j0\" ON \"sq0\".\"AddressId\" = \"j0\".\"AddressId\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\")",
            pg:     "SELECT \"UserName\", (SELECT MAX(\"j0\".\"AddressId\") FROM \"user_addresses\" AS \"sq0\" INNER JOIN \"addresses\" AS \"j0\" ON \"sq0\".\"AddressId\" = \"j0\".\"AddressId\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\") AS \"MaxAddrId\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"user_addresses\" AS \"sq0\" INNER JOIN \"addresses\" AS \"j0\" ON \"sq0\".\"AddressId\" = \"j0\".\"AddressId\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\")",
            mysql:  "SELECT `UserName`, (SELECT MAX(`j0`.`AddressId`) FROM `user_addresses` AS `sq0` INNER JOIN `addresses` AS `j0` ON `sq0`.`AddressId` = `j0`.`AddressId` WHERE `sq0`.`UserId` = `users`.`UserId`) AS `MaxAddrId` FROM `users` WHERE EXISTS (SELECT 1 FROM `user_addresses` AS `sq0` INNER JOIN `addresses` AS `j0` ON `sq0`.`AddressId` = `j0`.`AddressId` WHERE `sq0`.`UserId` = `users`.`UserId`)",
            ss:     "SELECT [UserName], (SELECT MAX([j0].[AddressId]) FROM [user_addresses] AS [sq0] INNER JOIN [addresses] AS [j0] ON [sq0].[AddressId] = [j0].[AddressId] WHERE [sq0].[UserId] = [users].[UserId]) AS [MaxAddrId] FROM [users] WHERE EXISTS (SELECT 1 FROM [user_addresses] AS [sq0] INNER JOIN [addresses] AS [j0] ON [sq0].[AddressId] = [j0].[AddressId] WHERE [sq0].[UserId] = [users].[UserId])");

        // Alice has Addresses {1, 2} → max 2; Bob has {1} → max 1; Charlie filtered out.
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo(("Alice", 2)));
        Assert.That(results[1], Is.EqualTo(("Bob", 1)));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults[0], Is.EqualTo(("Alice", 2)));
        Assert.That(pgResults[1], Is.EqualTo(("Bob", 1)));
    }

    #endregion
}
