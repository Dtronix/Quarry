using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;


[TestFixture]
internal class CrossDialectMiscTests
{
    #region String: ToLower

    [Test]
    public async Task Where_ToLower()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        QueryTestHarness.AssertDialects(
            Lite.Users().Where(u => u.UserName.ToLower() == "john").ToDiagnostics(),
            Pg.Users().Where(u => u.UserName.ToLower() == "john").ToDiagnostics(),
            My.Users().Where(u => u.UserName.ToLower() == "john").ToDiagnostics(),
            Ss.Users().Where(u => u.UserName.ToLower() == "john").ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE LOWER(\"UserName\") = @p0",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE LOWER(\"UserName\") = $1",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE LOWER(`UserName`) = ?",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE LOWER([UserName]) = @p0");
    }

    #endregion

    #region String: ToUpper

    [Test]
    public async Task Where_ToUpper()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        QueryTestHarness.AssertDialects(
            Lite.Users().Where(u => u.UserName.ToUpper() == "JOHN").ToDiagnostics(),
            Pg.Users().Where(u => u.UserName.ToUpper() == "JOHN").ToDiagnostics(),
            My.Users().Where(u => u.UserName.ToUpper() == "JOHN").ToDiagnostics(),
            Ss.Users().Where(u => u.UserName.ToUpper() == "JOHN").ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE UPPER(\"UserName\") = @p0",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE UPPER(\"UserName\") = $1",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE UPPER(`UserName`) = ?",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE UPPER([UserName]) = @p0");
    }

    #endregion

    #region String: Trim

    [Test]
    public async Task Where_Trim()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        QueryTestHarness.AssertDialects(
            Lite.Users().Where(u => u.UserName.Trim() == "john").ToDiagnostics(),
            Pg.Users().Where(u => u.UserName.Trim() == "john").ToDiagnostics(),
            My.Users().Where(u => u.UserName.Trim() == "john").ToDiagnostics(),
            Ss.Users().Where(u => u.UserName.Trim() == "john").ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE TRIM(\"UserName\") = @p0",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE TRIM(\"UserName\") = $1",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE TRIM(`UserName`) = ?",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE TRIM([UserName]) = @p0");
    }

    #endregion

    #region Sql.Raw with column reference

    [Test]
    public async Task Where_SqlRaw_WithColumnReference()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        QueryTestHarness.AssertDialects(
            Lite.Users().Where(u => Sql.Raw<bool>("custom_func({0})", u.UserId)).ToDiagnostics(),
            Pg.Users().Where(u => Sql.Raw<bool>("custom_func({0})", u.UserId)).ToDiagnostics(),
            My.Users().Where(u => Sql.Raw<bool>("custom_func({0})", u.UserId)).ToDiagnostics(),
            Ss.Users().Where(u => Sql.Raw<bool>("custom_func({0})", u.UserId)).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE custom_func(\"UserId\")",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE custom_func(\"UserId\")",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE custom_func(`UserId`)",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE custom_func([UserId])");
    }

    [Test]
    public async Task Where_SqlRaw_WithMultipleColumnReferences()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        QueryTestHarness.AssertDialects(
            Lite.Users().Where(u => Sql.Raw<bool>("check_cols({0}, {1})", u.UserId, u.IsActive)).ToDiagnostics(),
            Pg.Users().Where(u => Sql.Raw<bool>("check_cols({0}, {1})", u.UserId, u.IsActive)).ToDiagnostics(),
            My.Users().Where(u => Sql.Raw<bool>("check_cols({0}, {1})", u.UserId, u.IsActive)).ToDiagnostics(),
            Ss.Users().Where(u => Sql.Raw<bool>("check_cols({0}, {1})", u.UserId, u.IsActive)).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE check_cols(\"UserId\", \"IsActive\")",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE check_cols(\"UserId\", \"IsActive\")",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE check_cols(`UserId`, `IsActive`)",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE check_cols([UserId], [IsActive])");
    }

    [Test]
    public async Task Where_SqlRaw_WithCapturedVariable()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var searchTerm = "john";
        QueryTestHarness.AssertDialects(
            Lite.Users().Where(u => Sql.Raw<bool>("CONTAINS({0}, {1})", u.UserName, searchTerm)).ToDiagnostics(),
            Pg.Users().Where(u => Sql.Raw<bool>("CONTAINS({0}, {1})", u.UserName, searchTerm)).ToDiagnostics(),
            My.Users().Where(u => Sql.Raw<bool>("CONTAINS({0}, {1})", u.UserName, searchTerm)).ToDiagnostics(),
            Ss.Users().Where(u => Sql.Raw<bool>("CONTAINS({0}, {1})", u.UserName, searchTerm)).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE CONTAINS(\"UserName\", @p0)",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE CONTAINS(\"UserName\", $1)",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE CONTAINS(`UserName`, ?)",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE CONTAINS([UserName], @p0)");
    }

    [Test]
    public async Task Where_SqlRaw_WithLiteralParameter()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        QueryTestHarness.AssertDialects(
            Lite.Users().Where(u => Sql.Raw<bool>("status_check({0}, {1})", u.UserName, 42)).ToDiagnostics(),
            Pg.Users().Where(u => Sql.Raw<bool>("status_check({0}, {1})", u.UserName, 42)).ToDiagnostics(),
            My.Users().Where(u => Sql.Raw<bool>("status_check({0}, {1})", u.UserName, 42)).ToDiagnostics(),
            Ss.Users().Where(u => Sql.Raw<bool>("status_check({0}, {1})", u.UserName, 42)).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE status_check(\"UserName\", 42)",
            pg:     "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE status_check(\"UserName\", 42)",
            mysql:  "SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `users` WHERE status_check(`UserName`, 42)",
            ss:     "SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [users] WHERE status_check([UserName], 42)");
    }

    #endregion
}
