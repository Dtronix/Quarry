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
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"UserName\" LIKE '%User05%'",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"UserName\" LIKE '%User05%'",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE `UserName` LIKE '%User05%'",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE [UserName] LIKE '%User05%'");
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
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%lic%'",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%lic%'",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users` WHERE `UserName` LIKE '%lic%'",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users] WHERE [UserName] LIKE '%lic%'");

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
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE (\"UserName\" LIKE '%admin%') AND (\"IsActive\" = 1)",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE (\"UserName\" LIKE '%admin%') AND (\"IsActive\" = TRUE)",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE (`UserName` LIKE '%admin%') AND (`IsActive` = 1)",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE ([UserName] LIKE '%admin%') AND ([IsActive] = 1)");
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
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"UserName\" LIKE 'User0%'",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"UserName\" LIKE 'User0%'",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE `UserName` LIKE 'User0%'",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE [UserName] LIKE 'User0%'");
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
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE 'A%'",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE 'A%'",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users` WHERE `UserName` LIKE 'A%'",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users] WHERE [UserName] LIKE 'A%'");

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
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"UserName\" LIKE '%son'",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"UserName\" LIKE '%son'",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE `UserName` LIKE '%son'",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE [UserName] LIKE '%son'");
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
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%z'",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%z'",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users` WHERE `UserName` LIKE '%z'",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users] WHERE [UserName] LIKE '%z'");
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
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"Email\" LIKE '%@example%'",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"Email\" LIKE '%@example%'",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE `Email` LIKE '%@example%'",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE [Email] LIKE '%@example%'");
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
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE (\"UserName\" LIKE '%er%') AND (\"UserName\" LIKE 'Us%')",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE (\"UserName\" LIKE '%er%') AND (\"UserName\" LIKE 'Us%')",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE (`UserName` LIKE '%er%') AND (`UserName` LIKE 'Us%')",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE ([UserName] LIKE '%er%') AND ([UserName] LIKE 'Us%')");
    }

    #endregion

    #region Inlined Constant LIKE Patterns

    private const string ConstSearchTerm = "lic";
    private static readonly string ReadonlySearchTerm = "lic";
    private static string MutableSearchTerm = "lic";

    [Test]
    public async Task Where_Contains_StringLiteral_InlinesPattern()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Where(u => u.UserName.Contains("lic")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Users().Where(u => u.UserName.Contains("lic")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            My.Users().Where(u => u.UserName.Contains("lic")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            Ss.Users().Where(u => u.UserName.Contains("lic")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%lic%'",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%lic%'",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users` WHERE `UserName` LIKE '%lic%'",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users] WHERE [UserName] LIKE '%lic%'");

        Assert.That(lite.ToDiagnostics().Parameters, Has.Count.EqualTo(0));

        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
    }

    [Test]
    public async Task Where_StartsWith_StringLiteral_InlinesPattern()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        QueryTestHarness.AssertDialects(
            Lite.Users().Where(u => u.UserName.StartsWith("A")).ToDiagnostics(),
            Pg.Users().Where(u => u.UserName.StartsWith("A")).ToDiagnostics(),
            My.Users().Where(u => u.UserName.StartsWith("A")).ToDiagnostics(),
            Ss.Users().Where(u => u.UserName.StartsWith("A")).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"UserName\" LIKE 'A%'",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"UserName\" LIKE 'A%'",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE `UserName` LIKE 'A%'",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE [UserName] LIKE 'A%'");
    }

    [Test]
    public async Task Where_EndsWith_StringLiteral_InlinesPattern()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        QueryTestHarness.AssertDialects(
            Lite.Users().Where(u => u.UserName.EndsWith("ce")).ToDiagnostics(),
            Pg.Users().Where(u => u.UserName.EndsWith("ce")).ToDiagnostics(),
            My.Users().Where(u => u.UserName.EndsWith("ce")).ToDiagnostics(),
            Ss.Users().Where(u => u.UserName.EndsWith("ce")).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"UserName\" LIKE '%ce'",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"UserName\" LIKE '%ce'",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE `UserName` LIKE '%ce'",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE [UserName] LIKE '%ce'");
    }

    [Test]
    public async Task Where_Contains_ConstField_InlinesPattern()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Where(u => u.UserName.Contains(ConstSearchTerm)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Users().Where(u => u.UserName.Contains(ConstSearchTerm)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            My.Users().Where(u => u.UserName.Contains(ConstSearchTerm)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            Ss.Users().Where(u => u.UserName.Contains(ConstSearchTerm)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%lic%'",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%lic%'",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users` WHERE `UserName` LIKE '%lic%'",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users] WHERE [UserName] LIKE '%lic%'");

        Assert.That(lite.ToDiagnostics().Parameters, Has.Count.EqualTo(0));

        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
    }

    [Test]
    public async Task Where_Contains_ReadonlyField_InlinesPattern()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Where(u => u.UserName.Contains(ReadonlySearchTerm)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Users().Where(u => u.UserName.Contains(ReadonlySearchTerm)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            My.Users().Where(u => u.UserName.Contains(ReadonlySearchTerm)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            Ss.Users().Where(u => u.UserName.Contains(ReadonlySearchTerm)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%lic%'",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%lic%'",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users` WHERE `UserName` LIKE '%lic%'",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users] WHERE [UserName] LIKE '%lic%'");

        Assert.That(lite.ToDiagnostics().Parameters, Has.Count.EqualTo(0));

        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
    }

    [Test]
    public async Task Where_Contains_LocalConst_InlinesPattern()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        const string search = "lic";

        var lite = Lite.Users().Where(u => u.UserName.Contains(search)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Users().Where(u => u.UserName.Contains(search)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            My.Users().Where(u => u.UserName.Contains(search)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            Ss.Users().Where(u => u.UserName.Contains(search)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%lic%'",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%lic%'",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users` WHERE `UserName` LIKE '%lic%'",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users] WHERE [UserName] LIKE '%lic%'");

        Assert.That(lite.ToDiagnostics().Parameters, Has.Count.EqualTo(0));

        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
    }

    [Test]
    public async Task Where_Contains_MutableField_RemainsParameterized()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Mutable static field — cannot be inlined, must stay parameterized
        var lite = Lite.Users().Where(u => u.UserName.Contains(MutableSearchTerm)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Users().Where(u => u.UserName.Contains(MutableSearchTerm)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            My.Users().Where(u => u.UserName.Contains(MutableSearchTerm)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            Ss.Users().Where(u => u.UserName.Contains(MutableSearchTerm)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%' || @p0 || '%'",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%' || $1 || '%'",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users` WHERE `UserName` LIKE CONCAT('%', ?, '%')",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users] WHERE [UserName] LIKE '%' + @p0 + '%'");

        Assert.That(lite.ToDiagnostics().Parameters, Has.Count.EqualTo(1));

        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
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

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Users().Where(u => u.UserName.Contains(search)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            My.Users().Where(u => u.UserName.Contains(search)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            Ss.Users().Where(u => u.UserName.Contains(search)).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%' || @p0 || '%'",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%' || $1 || '%'",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users` WHERE `UserName` LIKE CONCAT('%', ?, '%')",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users] WHERE [UserName] LIKE '%' + @p0 + '%'");

        Assert.That(lite.ToDiagnostics().Parameters, Has.Count.EqualTo(1));

        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
    }

    [Test]
    public async Task Where_Contains_LiteralWithMetaChars_InlinesWithEscape()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Literal containing LIKE metacharacter _ should be escaped and inlined
        QueryTestHarness.AssertDialects(
            Lite.Users().Where(u => u.UserName.Contains("user_name")).ToDiagnostics(),
            Pg.Users().Where(u => u.UserName.Contains("user_name")).ToDiagnostics(),
            My.Users().Where(u => u.UserName.Contains("user_name")).ToDiagnostics(),
            Ss.Users().Where(u => u.UserName.Contains("user_name")).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"UserName\" LIKE '%user\\_name%' ESCAPE '\\'",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"UserName\" LIKE '%user\\_name%' ESCAPE '\\'",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE `UserName` LIKE '%user\\_name%' ESCAPE '\\'",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE [UserName] LIKE '%user\\_name%' ESCAPE '\\'");
    }

    [Test]
    public async Task Where_Contains_And_StartsWith_BothLiterals_InlinesBoth()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        QueryTestHarness.AssertDialects(
            Lite.Users().Where(u => u.UserName.Contains("er")).Where(u => u.UserName.StartsWith("Us")).ToDiagnostics(),
            Pg.Users().Where(u => u.UserName.Contains("er")).Where(u => u.UserName.StartsWith("Us")).ToDiagnostics(),
            My.Users().Where(u => u.UserName.Contains("er")).Where(u => u.UserName.StartsWith("Us")).ToDiagnostics(),
            Ss.Users().Where(u => u.UserName.Contains("er")).Where(u => u.UserName.StartsWith("Us")).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE (\"UserName\" LIKE '%er%') AND (\"UserName\" LIKE 'Us%')",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE (\"UserName\" LIKE '%er%') AND (\"UserName\" LIKE 'Us%')",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE (`UserName` LIKE '%er%') AND (`UserName` LIKE 'Us%')",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE ([UserName] LIKE '%er%') AND ([UserName] LIKE 'Us%')");
    }

    #endregion
}
