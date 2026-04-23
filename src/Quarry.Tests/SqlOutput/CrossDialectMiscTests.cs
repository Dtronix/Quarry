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

    [Test]
    public async Task Select_SqlRaw_Joined_WithColumnReference()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, Upper: Sql.Raw<string>("UPPER({0})", o.Status))).Prepare();
        var pg = Pg.Users().Join<Pg.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, Upper: Sql.Raw<string>("UPPER({0})", o.Status))).Prepare();
        var my = My.Users().Join<My.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, Upper: Sql.Raw<string>("UPPER({0})", o.Status))).Prepare();
        var ss = Ss.Users().Join<Ss.Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, Upper: Sql.Raw<string>("UPPER({0})", o.Status))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", UPPER(\"t1\".\"Status\") AS \"Upper\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            pg:     "SELECT \"t0\".\"UserName\", UPPER(\"t1\".\"Status\") AS \"Upper\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            mysql:  "SELECT `t0`.`UserName`, UPPER(`t1`.`Status`) AS `Upper` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId`",
            ss:     "SELECT [t0].[UserName], UPPER([t1].[Status]) AS [Upper] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId]");
    }

    [Test]
    public async Task Select_SqlRaw_SingleColumn()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Select(u => Sql.Raw<string>("UPPER({0})", u.UserName)).Prepare();
        var pg = Pg.Users().Select(u => Sql.Raw<string>("UPPER({0})", u.UserName)).Prepare();
        var my = My.Users().Select(u => Sql.Raw<string>("UPPER({0})", u.UserName)).Prepare();
        var ss = Ss.Users().Select(u => Sql.Raw<string>("UPPER({0})", u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT UPPER(\"UserName\") FROM \"users\"",
            pg:     "SELECT UPPER(\"UserName\") FROM \"users\"",
            mysql:  "SELECT UPPER(`UserName`) FROM `users`",
            ss:     "SELECT UPPER([UserName]) FROM [users]");
    }

    [Test]
    public async Task Select_SqlRaw_MultipleColumnReferences()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Select(u => (u.UserId, Tag: Sql.Raw<string>("coalesce({0}, {1})", u.UserName, u.Email))).Prepare();
        var pg = Pg.Users().Select(u => (u.UserId, Tag: Sql.Raw<string>("coalesce({0}, {1})", u.UserName, u.Email))).Prepare();
        var my = My.Users().Select(u => (u.UserId, Tag: Sql.Raw<string>("coalesce({0}, {1})", u.UserName, u.Email))).Prepare();
        var ss = Ss.Users().Select(u => (u.UserId, Tag: Sql.Raw<string>("coalesce({0}, {1})", u.UserName, u.Email))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", coalesce(\"UserName\", \"Email\") AS \"Tag\" FROM \"users\"",
            pg:     "SELECT \"UserId\", coalesce(\"UserName\", \"Email\") AS \"Tag\" FROM \"users\"",
            mysql:  "SELECT `UserId`, coalesce(`UserName`, `Email`) AS `Tag` FROM `users`",
            ss:     "SELECT [UserId], coalesce([UserName], [Email]) AS [Tag] FROM [users]");
    }

    [Test]
    public async Task Select_SqlRaw_WithCapturedVariable()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var threshold = 10;
        var lt = Lite.Users().Select(u => (u.UserId, Bucket: Sql.Raw<string>("CASE WHEN {0} > {1} THEN 'high' ELSE 'low' END", u.UserId, threshold))).Prepare();
        var pg = Pg.Users().Select(u => (u.UserId, Bucket: Sql.Raw<string>("CASE WHEN {0} > {1} THEN 'high' ELSE 'low' END", u.UserId, threshold))).Prepare();
        var my = My.Users().Select(u => (u.UserId, Bucket: Sql.Raw<string>("CASE WHEN {0} > {1} THEN 'high' ELSE 'low' END", u.UserId, threshold))).Prepare();
        var ss = Ss.Users().Select(u => (u.UserId, Bucket: Sql.Raw<string>("CASE WHEN {0} > {1} THEN 'high' ELSE 'low' END", u.UserId, threshold))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", CASE WHEN \"UserId\" > @p0 THEN 'high' ELSE 'low' END AS \"Bucket\" FROM \"users\"",
            pg:     "SELECT \"UserId\", CASE WHEN \"UserId\" > $1 THEN 'high' ELSE 'low' END AS \"Bucket\" FROM \"users\"",
            mysql:  "SELECT `UserId`, CASE WHEN `UserId` > ? THEN 'high' ELSE 'low' END AS `Bucket` FROM `users`",
            ss:     "SELECT [UserId], CASE WHEN [UserId] > @p0 THEN 'high' ELSE 'low' END AS [Bucket] FROM [users]");
    }

    [Test]
    public async Task Select_SqlRaw_WithLiteralParameter()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Select(u => (u.UserId, Flag: Sql.Raw<int>("coalesce({0}, {1})", u.UserId, 42))).Prepare();
        var pg = Pg.Users().Select(u => (u.UserId, Flag: Sql.Raw<int>("coalesce({0}, {1})", u.UserId, 42))).Prepare();
        var my = My.Users().Select(u => (u.UserId, Flag: Sql.Raw<int>("coalesce({0}, {1})", u.UserId, 42))).Prepare();
        var ss = Ss.Users().Select(u => (u.UserId, Flag: Sql.Raw<int>("coalesce({0}, {1})", u.UserId, 42))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", coalesce(\"UserId\", 42) AS \"Flag\" FROM \"users\"",
            pg:     "SELECT \"UserId\", coalesce(\"UserId\", 42) AS \"Flag\" FROM \"users\"",
            mysql:  "SELECT `UserId`, coalesce(`UserId`, 42) AS `Flag` FROM `users`",
            ss:     "SELECT [UserId], coalesce([UserId], 42) AS [Flag] FROM [users]");
    }

    [Test]
    public async Task Select_SqlRaw_NoPlaceholders()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Select(u => (u.UserId, Literal: Sql.Raw<string>("'fixed'"))).Prepare();
        var pg = Pg.Users().Select(u => (u.UserId, Literal: Sql.Raw<string>("'fixed'"))).Prepare();
        var my = My.Users().Select(u => (u.UserId, Literal: Sql.Raw<string>("'fixed'"))).Prepare();
        var ss = Ss.Users().Select(u => (u.UserId, Literal: Sql.Raw<string>("'fixed'"))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", 'fixed' AS \"Literal\" FROM \"users\"",
            pg:     "SELECT \"UserId\", 'fixed' AS \"Literal\" FROM \"users\"",
            mysql:  "SELECT `UserId`, 'fixed' AS `Literal` FROM `users`",
            ss:     "SELECT [UserId], 'fixed' AS [Literal] FROM [users]");
    }

    [Test]
    public async Task Select_SqlRaw_InDtoInitializer()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Select(u => new UserSummaryDto { UserId = u.UserId, UserName = Sql.Raw<string>("UPPER({0})", u.UserName), IsActive = u.IsActive }).Prepare();
        var pg = Pg.Users().Select(u => new UserSummaryDto { UserId = u.UserId, UserName = Sql.Raw<string>("UPPER({0})", u.UserName), IsActive = u.IsActive }).Prepare();
        var my = My.Users().Select(u => new UserSummaryDto { UserId = u.UserId, UserName = Sql.Raw<string>("UPPER({0})", u.UserName), IsActive = u.IsActive }).Prepare();
        var ss = Ss.Users().Select(u => new UserSummaryDto { UserId = u.UserId, UserName = Sql.Raw<string>("UPPER({0})", u.UserName), IsActive = u.IsActive }).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", UPPER(\"UserName\") AS \"UserName\", \"IsActive\" FROM \"users\"",
            pg:     "SELECT \"UserId\", UPPER(\"UserName\") AS \"UserName\", \"IsActive\" FROM \"users\"",
            mysql:  "SELECT `UserId`, UPPER(`UserName`) AS `UserName`, `IsActive` FROM `users`",
            ss:     "SELECT [UserId], UPPER([UserName]) AS [UserName], [IsActive] FROM [users]");
    }

    [Test]
    public async Task Select_SqlRaw_BinaryOpArg()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Arg is a binary op on a column: u.UserId * 10
        // Exercises the IR-based argument rendering — SqlExprParser builds a BinaryOpExpr tree
        // and the projection walker emits "("UserId" * 10)" in canonical form.
        var lt = Lite.Users().Select(u => (u.UserId, Scaled: Sql.Raw<int>("bucket({0})", u.UserId * 10))).Prepare();
        var pg = Pg.Users().Select(u => (u.UserId, Scaled: Sql.Raw<int>("bucket({0})", u.UserId * 10))).Prepare();
        var my = My.Users().Select(u => (u.UserId, Scaled: Sql.Raw<int>("bucket({0})", u.UserId * 10))).Prepare();
        var ss = Ss.Users().Select(u => (u.UserId, Scaled: Sql.Raw<int>("bucket({0})", u.UserId * 10))).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", bucket((\"UserId\" * 10)) AS \"Scaled\" FROM \"users\"",
            pg:     "SELECT \"UserId\", bucket((\"UserId\" * 10)) AS \"Scaled\" FROM \"users\"",
            mysql:  "SELECT `UserId`, bucket((`UserId` * 10)) AS `Scaled` FROM `users`",
            ss:     "SELECT [UserId], bucket(([UserId] * 10)) AS [Scaled] FROM [users]");
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
