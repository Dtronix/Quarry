using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;


[TestFixture]
internal class CrossDialectStringOpTests
{
    #region Contains

    [Test]
    public async Task Where_Contains_LiteralString()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.UserName.Contains("User05")).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.UserName.Contains("User05")).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => u.UserName.Contains("User05")).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => u.UserName.Contains("User05")).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserName\" LIKE '%User05%'",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserName\" LIKE '%User05%'",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE `UserName` LIKE '%User05%'",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE [UserName] LIKE '%User05%'");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(0));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(0));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(0));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(0));
    }

    [Test]
    public async Task Where_Contains_WithSelect()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.UserName.Contains("lic")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var pg = Pg.Users().Where(u => u.UserName.Contains("lic")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var my = My.Users().Where(u => u.UserName.Contains("lic")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var ss = Ss.Users().Where(u => u.UserName.Contains("lic")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%lic%'",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%lic%'",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users` WHERE `UserName` LIKE '%lic%'",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users] WHERE [UserName] LIKE '%lic%'");

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
    public async Task Where_Contains_ChainedWithBoolean()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.UserName.Contains("admin")).Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.UserName.Contains("admin")).Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => u.UserName.Contains("admin")).Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => u.UserName.Contains("admin")).Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE (\"UserName\" LIKE '%admin%') AND (\"IsActive\" = 1)",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE (\"UserName\" LIKE '%admin%') AND (\"IsActive\" = TRUE)",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE (`UserName` LIKE '%admin%') AND (`IsActive` = 1)",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE ([UserName] LIKE '%admin%') AND ([IsActive] = 1)");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(0));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(0));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(0));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(0));
    }

    #endregion

    #region StartsWith

    [Test]
    public async Task Where_StartsWith_LiteralString()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.UserName.StartsWith("User0")).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.UserName.StartsWith("User0")).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => u.UserName.StartsWith("User0")).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => u.UserName.StartsWith("User0")).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserName\" LIKE 'User0%'",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserName\" LIKE 'User0%'",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE `UserName` LIKE 'User0%'",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE [UserName] LIKE 'User0%'");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(0));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(0));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(0));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(0));
    }

    [Test]
    public async Task Where_StartsWith_WithSelect()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.UserName.StartsWith("A")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var pg = Pg.Users().Where(u => u.UserName.StartsWith("A")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var my = My.Users().Where(u => u.UserName.StartsWith("A")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var ss = Ss.Users().Where(u => u.UserName.StartsWith("A")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE 'A%'",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE 'A%'",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users` WHERE `UserName` LIKE 'A%'",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users] WHERE [UserName] LIKE 'A%'");

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

    #endregion

    #region EndsWith

    [Test]
    public async Task Where_EndsWith_LiteralString()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.UserName.EndsWith("son")).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.UserName.EndsWith("son")).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => u.UserName.EndsWith("son")).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => u.UserName.EndsWith("son")).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserName\" LIKE '%son'",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserName\" LIKE '%son'",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE `UserName` LIKE '%son'",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE [UserName] LIKE '%son'");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(0));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(0));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(0));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(0));
    }

    [Test]
    public async Task Where_EndsWith_WithSelect()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.UserName.EndsWith("z")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var pg = Pg.Users().Where(u => u.UserName.EndsWith("z")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var my = My.Users().Where(u => u.UserName.EndsWith("z")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var ss = Ss.Users().Where(u => u.UserName.EndsWith("z")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%z'",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%z'",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users` WHERE `UserName` LIKE '%z'",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users] WHERE [UserName] LIKE '%z'");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(0));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(0));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(0));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(0));
    }

    #endregion

    #region Nullable String Column

    [Test]
    public async Task Where_Contains_NullableColumn()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.Email!.Contains("@example")).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.Email!.Contains("@example")).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => u.Email!.Contains("@example")).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => u.Email!.Contains("@example")).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"Email\" LIKE '%@example%'",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"Email\" LIKE '%@example%'",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE `Email` LIKE '%@example%'",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE [Email] LIKE '%@example%'");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(0));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(0));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(0));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(0));
    }

    #endregion

    #region Combined String Ops

    [Test]
    public async Task Where_Contains_And_StartsWith_Chained()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.UserName.Contains("er")).Where(u => u.UserName.StartsWith("Us")).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.UserName.Contains("er")).Where(u => u.UserName.StartsWith("Us")).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => u.UserName.Contains("er")).Where(u => u.UserName.StartsWith("Us")).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => u.UserName.Contains("er")).Where(u => u.UserName.StartsWith("Us")).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE (\"UserName\" LIKE '%er%') AND (\"UserName\" LIKE 'Us%')",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE (\"UserName\" LIKE '%er%') AND (\"UserName\" LIKE 'Us%')",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE (`UserName` LIKE '%er%') AND (`UserName` LIKE 'Us%')",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE ([UserName] LIKE '%er%') AND ([UserName] LIKE 'Us%')");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(0));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(0));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(0));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(0));
    }

    #endregion

    #region Inlined Constant LIKE Patterns

    private const string ConstSearchTerm = "lic";
    private static readonly string ReadonlySearchTerm = "lic";
    private static string MutableSearchTerm = "lic";

    private static class StringConstants
    {
        public const string SearchTerm = "lic";
        public const string MetaSearchTerm = "50%";
    }

    [Test]
    public async Task Where_Contains_StringLiteral_InlinesPattern()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.UserName.Contains("lic")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var pg = Pg.Users().Where(u => u.UserName.Contains("lic")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var my = My.Users().Where(u => u.UserName.Contains("lic")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var ss = Ss.Users().Where(u => u.UserName.Contains("lic")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%lic%'",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%lic%'",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users` WHERE `UserName` LIKE '%lic%'",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users] WHERE [UserName] LIKE '%lic%'");

        Assert.That(lt.ToDiagnostics().Parameters, Has.Count.EqualTo(0));

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
    public async Task Where_StartsWith_StringLiteral_InlinesPattern()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.UserName.StartsWith("A")).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.UserName.StartsWith("A")).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => u.UserName.StartsWith("A")).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => u.UserName.StartsWith("A")).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserName\" LIKE 'A%'",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserName\" LIKE 'A%'",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE `UserName` LIKE 'A%'",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE [UserName] LIKE 'A%'");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(1));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Where_EndsWith_StringLiteral_InlinesPattern()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.UserName.EndsWith("ce")).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.UserName.EndsWith("ce")).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => u.UserName.EndsWith("ce")).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => u.UserName.EndsWith("ce")).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserName\" LIKE '%ce'",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserName\" LIKE '%ce'",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE `UserName` LIKE '%ce'",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE [UserName] LIKE '%ce'");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(1));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Where_Contains_ConstField_InlinesPattern()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.UserName.Contains(ConstSearchTerm)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var pg = Pg.Users().Where(u => u.UserName.Contains(ConstSearchTerm)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var my = My.Users().Where(u => u.UserName.Contains(ConstSearchTerm)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var ss = Ss.Users().Where(u => u.UserName.Contains(ConstSearchTerm)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%lic%'",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%lic%'",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users` WHERE `UserName` LIKE '%lic%'",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users] WHERE [UserName] LIKE '%lic%'");

        Assert.That(lt.ToDiagnostics().Parameters, Has.Count.EqualTo(0));

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
    public async Task Where_Contains_ReadonlyField_InlinesPattern()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.UserName.Contains(ReadonlySearchTerm)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var pg = Pg.Users().Where(u => u.UserName.Contains(ReadonlySearchTerm)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var my = My.Users().Where(u => u.UserName.Contains(ReadonlySearchTerm)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var ss = Ss.Users().Where(u => u.UserName.Contains(ReadonlySearchTerm)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%lic%'",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%lic%'",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users` WHERE `UserName` LIKE '%lic%'",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users] WHERE [UserName] LIKE '%lic%'");

        Assert.That(lt.ToDiagnostics().Parameters, Has.Count.EqualTo(0));

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
    public async Task Where_Contains_LocalConst_InlinesPattern()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        const string search = "lic";

        var lt = Lite.Users().Where(u => u.UserName.Contains(search)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var pg = Pg.Users().Where(u => u.UserName.Contains(search)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var my = My.Users().Where(u => u.UserName.Contains(search)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var ss = Ss.Users().Where(u => u.UserName.Contains(search)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%lic%'",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%lic%'",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users` WHERE `UserName` LIKE '%lic%'",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users] WHERE [UserName] LIKE '%lic%'");

        Assert.That(lt.ToDiagnostics().Parameters, Has.Count.EqualTo(0));

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
    public async Task Where_Contains_MutableField_RemainsParameterized()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Mutable static field — cannot be inlined, must stay parameterized
        var lt = Lite.Users().Where(u => u.UserName.Contains(MutableSearchTerm)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var pg = Pg.Users().Where(u => u.UserName.Contains(MutableSearchTerm)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var my = My.Users().Where(u => u.UserName.Contains(MutableSearchTerm)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var ss = Ss.Users().Where(u => u.UserName.Contains(MutableSearchTerm)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%' || @p0 || '%'",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%' || $1 || '%'",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users` WHERE `UserName` LIKE CONCAT('%', ?, '%')",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users] WHERE [UserName] LIKE '%' + @p0 + '%'");

        Assert.That(lt.ToDiagnostics().Parameters, Has.Count.EqualTo(1));

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
    public async Task Where_Contains_MethodParameter_RemainsParameterized()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Helper that takes a runtime string — verifies parameterization is preserved
        await AssertContainsParameterized(t, GetSearchValue());
    }

    private static string GetSearchValue() => "lic";

    private async Task AssertContainsParameterized(QueryTestHarness t, string search)
    {
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Where(u => u.UserName.Contains(search)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var pg = Pg.Users().Where(u => u.UserName.Contains(search)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var my = My.Users().Where(u => u.UserName.Contains(search)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var ss = Ss.Users().Where(u => u.UserName.Contains(search)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            pg.ToDiagnostics(),
            my.ToDiagnostics(),
            Ss.Users().Where(u => u.UserName.Contains(search)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%' || @p0 || '%'",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%' || $1 || '%'",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users` WHERE `UserName` LIKE CONCAT('%', ?, '%')",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users] WHERE [UserName] LIKE '%' + @p0 + '%'");

        Assert.That(lite.ToDiagnostics().Parameters, Has.Count.EqualTo(1));

        var results = await lite.ExecuteFetchAllAsync();
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
    public async Task Where_Contains_LiteralWithMetaChars_InlinesWithEscape()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Literal containing LIKE metacharacter _ should be escaped and inlined
        var lt = Lite.Users().Where(u => u.UserName.Contains("user_name")).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.UserName.Contains("user_name")).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => u.UserName.Contains("user_name")).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => u.UserName.Contains("user_name")).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserName\" LIKE '%user\\_name%' ESCAPE '\\'",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"UserName\" LIKE '%user\\_name%' ESCAPE '\\'",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE `UserName` LIKE '%user\\_name%' ESCAPE '\\'",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE [UserName] LIKE '%user\\_name%' ESCAPE '\\'");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(0));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(0));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(0));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(0));
    }

    [Test]
    public async Task Where_Contains_And_StartsWith_BothLiterals_InlinesBoth()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.UserName.Contains("er")).Where(u => u.UserName.StartsWith("Us")).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.UserName.Contains("er")).Where(u => u.UserName.StartsWith("Us")).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => u.UserName.Contains("er")).Where(u => u.UserName.StartsWith("Us")).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => u.UserName.Contains("er")).Where(u => u.UserName.StartsWith("Us")).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE (\"UserName\" LIKE '%er%') AND (\"UserName\" LIKE 'Us%')",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE (\"UserName\" LIKE '%er%') AND (\"UserName\" LIKE 'Us%')",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE (`UserName` LIKE '%er%') AND (`UserName` LIKE 'Us%')",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE ([UserName] LIKE '%er%') AND ([UserName] LIKE 'Us%')");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(0));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(0));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(0));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(0));
    }

    [Test]
    public async Task Where_Contains_QualifiedConstField_InlinesPattern()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Qualified member access to const string (e.g., StringConstants.SearchTerm) should be folded
        var lt = Lite.Users().Where(u => u.UserName.Contains(StringConstants.SearchTerm)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var pg = Pg.Users().Where(u => u.UserName.Contains(StringConstants.SearchTerm)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var my = My.Users().Where(u => u.UserName.Contains(StringConstants.SearchTerm)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var ss = Ss.Users().Where(u => u.UserName.Contains(StringConstants.SearchTerm)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%lic%'",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%lic%'",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users` WHERE `UserName` LIKE '%lic%'",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users] WHERE [UserName] LIKE '%lic%'");

        Assert.That(lt.ToDiagnostics().Parameters, Has.Count.EqualTo(0));

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
    public async Task Where_Contains_QualifiedConstField_WithMetaChars_EscapesPattern()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Qualified const containing LIKE metacharacter (%) must be escaped
        var lt = Lite.Users().Where(u => u.UserName.Contains(StringConstants.MetaSearchTerm)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var pg = Pg.Users().Where(u => u.UserName.Contains(StringConstants.MetaSearchTerm)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var my = My.Users().Where(u => u.UserName.Contains(StringConstants.MetaSearchTerm)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var ss = Ss.Users().Where(u => u.UserName.Contains(StringConstants.MetaSearchTerm)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%50\\%%' ESCAPE '\\'",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%50\\%%' ESCAPE '\\'",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users` WHERE `UserName` LIKE '%50\\%%' ESCAPE '\\'",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users] WHERE [UserName] LIKE '%50\\%%' ESCAPE '\\'");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(0));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(0));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(0));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(0));
    }

    #endregion
}
