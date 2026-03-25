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

        QueryTestHarness.AssertDialects(
            Lite.Users().Where(u => u.UserName.Contains("User05")).ToDiagnostics(),
            Pg.Users().Where(u => u.UserName.Contains("User05")).ToDiagnostics(),
            My.Users().Where(u => u.UserName.Contains("User05")).ToDiagnostics(),
            Ss.Users().Where(u => u.UserName.Contains("User05")).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE \"UserName\" LIKE '%' || @p0 || '%'",
            pg:     "SELECT * FROM \"users\" WHERE \"UserName\" LIKE '%' || $1 || '%'",
            mysql:  "SELECT * FROM `users` WHERE `UserName` LIKE CONCAT('%', ?, '%')",
            ss:     "SELECT * FROM [users] WHERE [UserName] LIKE '%' + @p0 + '%'");
    }

    [Test]
    public async Task Where_Contains_WithSelect()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Where(u => u.UserName.Contains("lic")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Users().Where(u => u.UserName.Contains("lic")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            My.Users().Where(u => u.UserName.Contains("lic")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            Ss.Users().Where(u => u.UserName.Contains("lic")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%' || @p0 || '%'",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%' || $1 || '%'",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users` WHERE `UserName` LIKE CONCAT('%', ?, '%')",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users] WHERE [UserName] LIKE '%' + @p0 + '%'");

        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
    }

    [Test]
    public async Task Where_Contains_ChainedWithBoolean()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        QueryTestHarness.AssertDialects(
            Lite.Users().Where(u => u.UserName.Contains("admin")).Where(u => u.IsActive).ToDiagnostics(),
            Pg.Users().Where(u => u.UserName.Contains("admin")).Where(u => u.IsActive).ToDiagnostics(),
            My.Users().Where(u => u.UserName.Contains("admin")).Where(u => u.IsActive).ToDiagnostics(),
            Ss.Users().Where(u => u.UserName.Contains("admin")).Where(u => u.IsActive).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE (\"UserName\" LIKE '%' || @p0 || '%') AND (\"IsActive\" = 1)",
            pg:     "SELECT * FROM \"users\" WHERE (\"UserName\" LIKE '%' || $1 || '%') AND (\"IsActive\" = TRUE)",
            mysql:  "SELECT * FROM `users` WHERE (`UserName` LIKE CONCAT('%', ?, '%')) AND (`IsActive` = 1)",
            ss:     "SELECT * FROM [users] WHERE ([UserName] LIKE '%' + @p0 + '%') AND ([IsActive] = 1)");
    }

    #endregion

    #region StartsWith

    [Test]
    public async Task Where_StartsWith_LiteralString()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        QueryTestHarness.AssertDialects(
            Lite.Users().Where(u => u.UserName.StartsWith("User0")).ToDiagnostics(),
            Pg.Users().Where(u => u.UserName.StartsWith("User0")).ToDiagnostics(),
            My.Users().Where(u => u.UserName.StartsWith("User0")).ToDiagnostics(),
            Ss.Users().Where(u => u.UserName.StartsWith("User0")).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE \"UserName\" LIKE @p0 || '%'",
            pg:     "SELECT * FROM \"users\" WHERE \"UserName\" LIKE $1 || '%'",
            mysql:  "SELECT * FROM `users` WHERE `UserName` LIKE CONCAT(?, '%')",
            ss:     "SELECT * FROM [users] WHERE [UserName] LIKE @p0 + '%'");
    }

    [Test]
    public async Task Where_StartsWith_WithSelect()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Where(u => u.UserName.StartsWith("A")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Users().Where(u => u.UserName.StartsWith("A")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            My.Users().Where(u => u.UserName.StartsWith("A")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            Ss.Users().Where(u => u.UserName.StartsWith("A")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE @p0 || '%'",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE $1 || '%'",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users` WHERE `UserName` LIKE CONCAT(?, '%')",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users] WHERE [UserName] LIKE @p0 + '%'");

        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
    }

    #endregion

    #region EndsWith

    [Test]
    public async Task Where_EndsWith_LiteralString()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        QueryTestHarness.AssertDialects(
            Lite.Users().Where(u => u.UserName.EndsWith("son")).ToDiagnostics(),
            Pg.Users().Where(u => u.UserName.EndsWith("son")).ToDiagnostics(),
            My.Users().Where(u => u.UserName.EndsWith("son")).ToDiagnostics(),
            Ss.Users().Where(u => u.UserName.EndsWith("son")).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE \"UserName\" LIKE '%' || @p0",
            pg:     "SELECT * FROM \"users\" WHERE \"UserName\" LIKE '%' || $1",
            mysql:  "SELECT * FROM `users` WHERE `UserName` LIKE CONCAT('%', ?)",
            ss:     "SELECT * FROM [users] WHERE [UserName] LIKE '%' + @p0");
    }

    [Test]
    public async Task Where_EndsWith_WithSelect()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        QueryTestHarness.AssertDialects(
            Lite.Users().Where(u => u.UserName.EndsWith("z")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            Pg.Users().Where(u => u.UserName.EndsWith("z")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            My.Users().Where(u => u.UserName.EndsWith("z")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            Ss.Users().Where(u => u.UserName.EndsWith("z")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%' || @p0",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%' || $1",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users` WHERE `UserName` LIKE CONCAT('%', ?)",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users] WHERE [UserName] LIKE '%' + @p0");
    }

    #endregion

    #region Nullable String Column

    [Test]
    public async Task Where_Contains_NullableColumn()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        QueryTestHarness.AssertDialects(
            Lite.Users().Where(u => u.Email!.Contains("@example")).ToDiagnostics(),
            Pg.Users().Where(u => u.Email!.Contains("@example")).ToDiagnostics(),
            My.Users().Where(u => u.Email!.Contains("@example")).ToDiagnostics(),
            Ss.Users().Where(u => u.Email!.Contains("@example")).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE \"Email\" LIKE '%' || @p0 || '%'",
            pg:     "SELECT * FROM \"users\" WHERE \"Email\" LIKE '%' || $1 || '%'",
            mysql:  "SELECT * FROM `users` WHERE `Email` LIKE CONCAT('%', ?, '%')",
            ss:     "SELECT * FROM [users] WHERE [Email] LIKE '%' + @p0 + '%'");
    }

    #endregion

    #region Combined String Ops

    [Test]
    public async Task Where_Contains_And_StartsWith_Chained()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        QueryTestHarness.AssertDialects(
            Lite.Users().Where(u => u.UserName.Contains("er")).Where(u => u.UserName.StartsWith("Us")).ToDiagnostics(),
            Pg.Users().Where(u => u.UserName.Contains("er")).Where(u => u.UserName.StartsWith("Us")).ToDiagnostics(),
            My.Users().Where(u => u.UserName.Contains("er")).Where(u => u.UserName.StartsWith("Us")).ToDiagnostics(),
            Ss.Users().Where(u => u.UserName.Contains("er")).Where(u => u.UserName.StartsWith("Us")).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE (\"UserName\" LIKE '%' || @p0 || '%') AND (\"UserName\" LIKE @p1 || '%')",
            pg:     "SELECT * FROM \"users\" WHERE (\"UserName\" LIKE '%' || $1 || '%') AND (\"UserName\" LIKE $2 || '%')",
            mysql:  "SELECT * FROM `users` WHERE (`UserName` LIKE CONCAT('%', ?, '%')) AND (`UserName` LIKE CONCAT(?, '%'))",
            ss:     "SELECT * FROM [users] WHERE ([UserName] LIKE '%' + @p0 + '%') AND ([UserName] LIKE @p1 + '%')");
    }

    #endregion
}
