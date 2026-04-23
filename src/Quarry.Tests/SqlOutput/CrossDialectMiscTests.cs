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

        var lt = Lite.Users().Where(u => u.UserName.ToLower() == "john").Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.UserName.ToLower() == "john").Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => u.UserName.ToLower() == "john").Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => u.UserName.ToLower() == "john").Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE LOWER(\"UserName\") = @p0",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE LOWER(\"UserName\") = $1",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE LOWER(`UserName`) = ?",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE LOWER([UserName]) = @p0");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(0)); // "john" matches no seeded users
    }

    #endregion

    #region String: ToUpper

    [Test]
    public async Task Where_ToUpper()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.UserName.ToUpper() == "JOHN").Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.UserName.ToUpper() == "JOHN").Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => u.UserName.ToUpper() == "JOHN").Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => u.UserName.ToUpper() == "JOHN").Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE UPPER(\"UserName\") = @p0",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE UPPER(\"UserName\") = $1",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE UPPER(`UserName`) = ?",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE UPPER([UserName]) = @p0");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(0)); // "JOHN" matches no seeded users
    }

    #endregion

    #region String: Trim

    [Test]
    public async Task Where_Trim()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.UserName.Trim() == "john").Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => u.UserName.Trim() == "john").Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => u.UserName.Trim() == "john").Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => u.UserName.Trim() == "john").Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE TRIM(\"UserName\") = @p0",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE TRIM(\"UserName\") = $1",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE TRIM(`UserName`) = ?",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE TRIM([UserName]) = @p0");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(0)); // "john" matches no seeded users
    }

    #endregion

    #region Sql.Raw with column reference

    [Test]
    public async Task Where_SqlRaw_WithColumnReference()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => Sql.Raw<bool>("custom_func({0})", u.UserId)).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => Sql.Raw<bool>("custom_func({0})", u.UserId)).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => Sql.Raw<bool>("custom_func({0})", u.UserId)).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => Sql.Raw<bool>("custom_func({0})", u.UserId)).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE custom_func(\"UserId\")",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE custom_func(\"UserId\")",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE custom_func(`UserId`)",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE custom_func([UserId])");
    }

    [Test]
    public async Task Where_SqlRaw_WithMultipleColumnReferences()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => Sql.Raw<bool>("check_cols({0}, {1})", u.UserId, u.IsActive)).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => Sql.Raw<bool>("check_cols({0}, {1})", u.UserId, u.IsActive)).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => Sql.Raw<bool>("check_cols({0}, {1})", u.UserId, u.IsActive)).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => Sql.Raw<bool>("check_cols({0}, {1})", u.UserId, u.IsActive)).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE check_cols(\"UserId\", \"IsActive\")",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE check_cols(\"UserId\", \"IsActive\")",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE check_cols(`UserId`, `IsActive`)",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE check_cols([UserId], [IsActive])");
    }

    [Test]
    public async Task Where_SqlRaw_WithCapturedVariable()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var searchTerm = "john";
        var lt = Lite.Users().Where(u => Sql.Raw<bool>("CONTAINS({0}, {1})", u.UserName, searchTerm)).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => Sql.Raw<bool>("CONTAINS({0}, {1})", u.UserName, searchTerm)).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => Sql.Raw<bool>("CONTAINS({0}, {1})", u.UserName, searchTerm)).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => Sql.Raw<bool>("CONTAINS({0}, {1})", u.UserName, searchTerm)).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE CONTAINS(\"UserName\", @p0)",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE CONTAINS(\"UserName\", $1)",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE CONTAINS(`UserName`, ?)",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE CONTAINS([UserName], @p0)");
    }

    [Test]
    public async Task Where_SqlRaw_WithLiteralParameter()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => Sql.Raw<bool>("status_check({0}, {1})", u.UserName, 42)).Select(u => (u.UserId, u.UserName)).Prepare();
        var pg = Pg.Users().Where(u => Sql.Raw<bool>("status_check({0}, {1})", u.UserName, 42)).Select(u => (u.UserId, u.UserName)).Prepare();
        var my = My.Users().Where(u => Sql.Raw<bool>("status_check({0}, {1})", u.UserName, 42)).Select(u => (u.UserId, u.UserName)).Prepare();
        var ss = Ss.Users().Where(u => Sql.Raw<bool>("status_check({0}, {1})", u.UserName, 42)).Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE status_check(\"UserName\", 42)",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE status_check(\"UserName\", 42)",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE status_check(`UserName`, 42)",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE status_check([UserName], 42)");
    }

    #endregion

    #region Sql.Raw in Select projection

    [Test]
    public async Task Select_SqlRaw_WithColumnReference()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Select(u => (u.UserId, Upper: Sql.Raw<string>("UPPER({0})", u.UserName))).Prepare();
        var pg = Pg.Users().Select(u => (u.UserId, Upper: Sql.Raw<string>("UPPER({0})", u.UserName))).Prepare();
        var my = My.Users().Select(u => (u.UserId, Upper: Sql.Raw<string>("UPPER({0})", u.UserName))).Prepare();
        var ss = Ss.Users().Select(u => (u.UserId, Upper: Sql.Raw<string>("UPPER({0})", u.UserName))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", UPPER(\"UserName\") AS \"Upper\" FROM \"users\"",
            pg:     "SELECT \"UserId\", UPPER(\"UserName\") AS \"Upper\" FROM \"users\"",
            mysql:  "SELECT `UserId`, UPPER(`UserName`) AS `Upper` FROM `users`",
            ss:     "SELECT [UserId], UPPER([UserName]) AS [Upper] FROM [users]");
    }

    #endregion

    #region Instance Field Capture

    private int _instanceUserId = 1;

    [Test]
    public async Task Where_InstanceFieldCapture_UsesFieldAccessor()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Instance field on the test class — must use UnsafeAccessorKind.Field + func.Target!
        // (not StaticField + null!, which would throw MissingFieldException at runtime)
        var lt = Lite.Users().Where(u => u.UserId == _instanceUserId).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var pg = Pg.Users().Where(u => u.UserId == _instanceUserId).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var my = My.Users().Where(u => u.UserId == _instanceUserId).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();
        var ss = Ss.Users().Where(u => u.UserId == _instanceUserId).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserId\" = @p0",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserId\" = $1",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users` WHERE `UserId` = ?",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users] WHERE [UserId] = @p0");

        // Runtime execution — would throw MissingFieldException if StaticField was used
        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].UserId, Is.EqualTo(1));
    }

    #endregion
}
