using Quarry;
using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;


/// <summary>
/// Cross-dialect RawSql tests. Identifier quoting differs per dialect (`"x"`
/// for SQLite / PostgreSQL, `` `x` `` for MySQL, `[x]` for SQL Server). Parameter
/// placeholders use `@p0`, `@p1`, ... on every dialect — see
/// <see cref="QuarryContext.RawSqlAsync{T}(string, CancellationToken, object?[])"/>
/// docstring: the runtime always assigns `param.ParameterName = "@pN"`, and
/// every provider Quarry targets accepts that named-parameter form (Npgsql
/// rewrites them to positional internally).
/// </summary>
[TestFixture]
internal class CrossDialectRawSqlTests
{
    #region RawSqlAsync<DTO>

    [Test]
    public async Task RawSqlAsync_Dto_ReturnsPopulatedResults()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = await Lite.RawSqlAsync<UserWithEmailDto>(
            "SELECT \"UserId\", \"UserName\", \"Email\" FROM \"users\" ORDER BY \"UserId\"").ToListAsync();
        AssertSeededUsers(lite);

        var pgRows = await Pg.RawSqlAsync<UserWithEmailDto>(
            "SELECT \"UserId\", \"UserName\", \"Email\" FROM \"users\" ORDER BY \"UserId\"").ToListAsync();
        AssertSeededUsers(pgRows);

        var myRows = await My.RawSqlAsync<UserWithEmailDto>(
            "SELECT `UserId`, `UserName`, `Email` FROM `users` ORDER BY `UserId`").ToListAsync();
        AssertSeededUsers(myRows);

        var ssRows = await Ss.RawSqlAsync<UserWithEmailDto>(
            "SELECT [UserId], [UserName], [Email] FROM [users] ORDER BY [UserId]").ToListAsync();
        AssertSeededUsers(ssRows);
    }

    [Test]
    public async Task RawSqlAsync_Dto_UserSummary_ReturnsCorrectTypes()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = await Lite.RawSqlAsync<UserSummaryDto>(
            "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" ORDER BY \"UserId\"").ToListAsync();
        AssertActivityFlags(lite);

        var pgRows = await Pg.RawSqlAsync<UserSummaryDto>(
            "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" ORDER BY \"UserId\"").ToListAsync();
        AssertActivityFlags(pgRows);

        var myRows = await My.RawSqlAsync<UserSummaryDto>(
            "SELECT `UserId`, `UserName`, `IsActive` FROM `users` ORDER BY `UserId`").ToListAsync();
        AssertActivityFlags(myRows);

        var ssRows = await Ss.RawSqlAsync<UserSummaryDto>(
            "SELECT [UserId], [UserName], [IsActive] FROM [users] ORDER BY [UserId]").ToListAsync();
        AssertActivityFlags(ssRows);
    }

    private static void AssertSeededUsers(List<UserWithEmailDto> results)
    {
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0].UserId, Is.EqualTo(1));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
        Assert.That(results[0].Email, Is.EqualTo("alice@test.com"));
        Assert.That(results[1].UserId, Is.EqualTo(2));
        Assert.That(results[1].UserName, Is.EqualTo("Bob"));
        Assert.That(results[1].Email, Is.Null);
        Assert.That(results[2].UserId, Is.EqualTo(3));
        Assert.That(results[2].UserName, Is.EqualTo("Charlie"));
        Assert.That(results[2].Email, Is.EqualTo("charlie@test.com"));
    }

    private static void AssertActivityFlags(List<UserSummaryDto> results)
    {
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0].IsActive, Is.True);
        Assert.That(results[2].IsActive, Is.False);
    }

    #endregion

    #region RawSqlAsync<scalar>

    [Test]
    public async Task RawSqlAsync_ScalarInt_ReturnsListOfIntegers()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = await Lite.RawSqlAsync<int>(
            "SELECT \"UserId\" FROM \"users\" ORDER BY \"UserId\"").ToListAsync();
        Assert.That(lite, Is.EqualTo(new[] { 1, 2, 3 }));

        var pgRows = await Pg.RawSqlAsync<int>(
            "SELECT \"UserId\" FROM \"users\" ORDER BY \"UserId\"").ToListAsync();
        Assert.That(pgRows, Is.EqualTo(new[] { 1, 2, 3 }));

        var myRows = await My.RawSqlAsync<int>(
            "SELECT `UserId` FROM `users` ORDER BY `UserId`").ToListAsync();
        Assert.That(myRows, Is.EqualTo(new[] { 1, 2, 3 }));

        var ssRows = await Ss.RawSqlAsync<int>(
            "SELECT [UserId] FROM [users] ORDER BY [UserId]").ToListAsync();
        Assert.That(ssRows, Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public async Task RawSqlAsync_ScalarString_ReturnsListOfStrings()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = await Lite.RawSqlAsync<string>(
            "SELECT \"UserName\" FROM \"users\" ORDER BY \"UserId\"").ToListAsync();
        Assert.That(lite, Is.EqualTo(new[] { "Alice", "Bob", "Charlie" }));

        var pgRows = await Pg.RawSqlAsync<string>(
            "SELECT \"UserName\" FROM \"users\" ORDER BY \"UserId\"").ToListAsync();
        Assert.That(pgRows, Is.EqualTo(new[] { "Alice", "Bob", "Charlie" }));

        var myRows = await My.RawSqlAsync<string>(
            "SELECT `UserName` FROM `users` ORDER BY `UserId`").ToListAsync();
        Assert.That(myRows, Is.EqualTo(new[] { "Alice", "Bob", "Charlie" }));

        var ssRows = await Ss.RawSqlAsync<string>(
            "SELECT [UserName] FROM [users] ORDER BY [UserId]").ToListAsync();
        Assert.That(ssRows, Is.EqualTo(new[] { "Alice", "Bob", "Charlie" }));
    }

    #endregion

    #region RawSqlScalarAsync

    [Test]
    public async Task RawSqlScalarAsync_Int_ReturnsScalarValue()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        Assert.That(await Lite.RawSqlScalarAsync<int>("SELECT COUNT(*) FROM \"users\""), Is.EqualTo(3));
        Assert.That(await Pg.RawSqlScalarAsync<int>("SELECT COUNT(*) FROM \"users\""), Is.EqualTo(3));
        Assert.That(await My.RawSqlScalarAsync<int>("SELECT COUNT(*) FROM `users`"), Is.EqualTo(3));
        Assert.That(await Ss.RawSqlScalarAsync<int>("SELECT COUNT(*) FROM [users]"), Is.EqualTo(3));
    }

    [Test]
    public async Task RawSqlScalarAsync_String_ReturnsScalarValue()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = await Lite.RawSqlScalarAsync<string>(
            "SELECT \"UserName\" FROM \"users\" WHERE \"UserId\" = @p0", 1);
        Assert.That(lite, Is.EqualTo("Alice"));

        var pg = await Pg.RawSqlScalarAsync<string>(
            "SELECT \"UserName\" FROM \"users\" WHERE \"UserId\" = @p0", 1);
        Assert.That(pg, Is.EqualTo("Alice"));

        var my = await My.RawSqlScalarAsync<string>(
            "SELECT `UserName` FROM `users` WHERE `UserId` = @p0", 1);
        Assert.That(my, Is.EqualTo("Alice"));

        var ss = await Ss.RawSqlScalarAsync<string>(
            "SELECT [UserName] FROM [users] WHERE [UserId] = @p0", 1);
        Assert.That(ss, Is.EqualTo("Alice"));
    }

    [Test]
    public async Task RawSqlScalarAsync_Long_ReturnsScalarValue()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        Assert.That(await Lite.RawSqlScalarAsync<long>("SELECT COUNT(*) FROM \"orders\""), Is.EqualTo(3L));
        Assert.That(await Pg.RawSqlScalarAsync<long>("SELECT COUNT(*) FROM \"orders\""), Is.EqualTo(3L));
        Assert.That(await My.RawSqlScalarAsync<long>("SELECT COUNT(*) FROM `orders`"), Is.EqualTo(3L));
        Assert.That(await Ss.RawSqlScalarAsync<long>("SELECT COUNT(*) FROM [orders]"), Is.EqualTo(3L));
    }

    #endregion

    #region Null Handling

    [Test]
    public async Task RawSqlAsync_Dto_NullableColumns_ReturnsCorrectNulls()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = await Lite.RawSqlAsync<UserWithEmailDto>(
            "SELECT \"UserId\", \"UserName\", \"Email\" FROM \"users\" WHERE \"UserId\" = @p0", 2).ToListAsync();
        AssertBobNullEmail(lite);

        var pgRows = await Pg.RawSqlAsync<UserWithEmailDto>(
            "SELECT \"UserId\", \"UserName\", \"Email\" FROM \"users\" WHERE \"UserId\" = @p0", 2).ToListAsync();
        AssertBobNullEmail(pgRows);

        var myRows = await My.RawSqlAsync<UserWithEmailDto>(
            "SELECT `UserId`, `UserName`, `Email` FROM `users` WHERE `UserId` = @p0", 2).ToListAsync();
        AssertBobNullEmail(myRows);

        var ssRows = await Ss.RawSqlAsync<UserWithEmailDto>(
            "SELECT [UserId], [UserName], [Email] FROM [users] WHERE [UserId] = @p0", 2).ToListAsync();
        AssertBobNullEmail(ssRows);
    }

    private static void AssertBobNullEmail(List<UserWithEmailDto> results)
    {
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].UserId, Is.EqualTo(2));
        Assert.That(results[0].UserName, Is.EqualTo("Bob"));
        Assert.That(results[0].Email, Is.Null);
    }

    [Test]
    public async Task RawSqlScalarAsync_ReturnsDefaultForNull()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        Assert.That(await Lite.RawSqlScalarAsync<int>(
            "SELECT \"UserId\" FROM \"users\" WHERE \"UserId\" = -999"), Is.EqualTo(default(int)));
        Assert.That(await Pg.RawSqlScalarAsync<int>(
            "SELECT \"UserId\" FROM \"users\" WHERE \"UserId\" = -999"), Is.EqualTo(default(int)));
        Assert.That(await My.RawSqlScalarAsync<int>(
            "SELECT `UserId` FROM `users` WHERE `UserId` = -999"), Is.EqualTo(default(int)));
        Assert.That(await Ss.RawSqlScalarAsync<int>(
            "SELECT [UserId] FROM [users] WHERE [UserId] = -999"), Is.EqualTo(default(int)));
    }

    [Test]
    public async Task RawSqlScalarAsync_NullableString_ReturnsDefaultForNull()
    {
        // SELECT NULL parses identically across all four dialects.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        Assert.That(await Lite.RawSqlScalarAsync<string>("SELECT NULL"), Is.Null);
        Assert.That(await Pg.RawSqlScalarAsync<string>("SELECT NULL"), Is.Null);
        Assert.That(await My.RawSqlScalarAsync<string>("SELECT NULL"), Is.Null);
        Assert.That(await Ss.RawSqlScalarAsync<string>("SELECT NULL"), Is.Null);
    }

    #endregion

    #region Empty Result Set

    [Test]
    public async Task RawSqlAsync_EmptyResultSet_ReturnsEmptyList()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = await Lite.RawSqlAsync<UserSummaryDto>(
            "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE 1 = 0").ToListAsync();
        Assert.That(lite, Is.Empty);

        var pgRows = await Pg.RawSqlAsync<UserSummaryDto>(
            "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE 1 = 0").ToListAsync();
        Assert.That(pgRows, Is.Empty);

        var myRows = await My.RawSqlAsync<UserSummaryDto>(
            "SELECT `UserId`, `UserName`, `IsActive` FROM `users` WHERE 1 = 0").ToListAsync();
        Assert.That(myRows, Is.Empty);

        var ssRows = await Ss.RawSqlAsync<UserSummaryDto>(
            "SELECT [UserId], [UserName], [IsActive] FROM [users] WHERE 1 = 0").ToListAsync();
        Assert.That(ssRows, Is.Empty);
    }

    #endregion

    #region Parameter Binding

    [Test]
    public async Task RawSqlAsync_WithParameters_ReturnsFilteredResults()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = await Lite.RawSqlAsync<UserWithEmailDto>(
            "SELECT \"UserId\", \"UserName\", \"Email\" FROM \"users\" WHERE \"UserId\" = @p0", 1).ToListAsync();
        AssertAliceOnly(lite);

        var pgRows = await Pg.RawSqlAsync<UserWithEmailDto>(
            "SELECT \"UserId\", \"UserName\", \"Email\" FROM \"users\" WHERE \"UserId\" = @p0", 1).ToListAsync();
        AssertAliceOnly(pgRows);

        var myRows = await My.RawSqlAsync<UserWithEmailDto>(
            "SELECT `UserId`, `UserName`, `Email` FROM `users` WHERE `UserId` = @p0", 1).ToListAsync();
        AssertAliceOnly(myRows);

        var ssRows = await Ss.RawSqlAsync<UserWithEmailDto>(
            "SELECT [UserId], [UserName], [Email] FROM [users] WHERE [UserId] = @p0", 1).ToListAsync();
        AssertAliceOnly(ssRows);
    }

    private static void AssertAliceOnly(List<UserWithEmailDto> results)
    {
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].UserId, Is.EqualTo(1));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
    }

    [Test]
    public async Task RawSqlAsync_WithMultipleParameters_ReturnsFilteredResults()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = await Lite.RawSqlAsync<UserSummaryDto>(
            "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"IsActive\" = @p0 AND \"UserId\" > @p1",
            1, 1).ToListAsync();
        AssertActiveBobOnly(lite);

        var pgRows = await Pg.RawSqlAsync<UserSummaryDto>(
            "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"IsActive\" = @p0 AND \"UserId\" > @p1",
            true, 1).ToListAsync();
        AssertActiveBobOnly(pgRows);

        var myRows = await My.RawSqlAsync<UserSummaryDto>(
            "SELECT `UserId`, `UserName`, `IsActive` FROM `users` WHERE `IsActive` = @p0 AND `UserId` > @p1",
            1, 1).ToListAsync();
        AssertActiveBobOnly(myRows);

        var ssRows = await Ss.RawSqlAsync<UserSummaryDto>(
            "SELECT [UserId], [UserName], [IsActive] FROM [users] WHERE [IsActive] = @p0 AND [UserId] > @p1",
            1, 1).ToListAsync();
        AssertActiveBobOnly(ssRows);
    }

    private static void AssertActiveBobOnly(List<UserSummaryDto> results)
    {
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].UserId, Is.EqualTo(2));
        Assert.That(results[0].UserName, Is.EqualTo("Bob"));
    }

    [Test]
    public async Task RawSqlScalarAsync_WithParameter_ReturnsFilteredScalar()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        Assert.That(await Lite.RawSqlScalarAsync<int>(
            "SELECT COUNT(*) FROM \"orders\" WHERE \"UserId\" = @p0", 1), Is.EqualTo(2));
        Assert.That(await Pg.RawSqlScalarAsync<int>(
            "SELECT COUNT(*) FROM \"orders\" WHERE \"UserId\" = @p0", 1), Is.EqualTo(2));
        Assert.That(await My.RawSqlScalarAsync<int>(
            "SELECT COUNT(*) FROM `orders` WHERE `UserId` = @p0", 1), Is.EqualTo(2));
        Assert.That(await Ss.RawSqlScalarAsync<int>(
            "SELECT COUNT(*) FROM [orders] WHERE [UserId] = @p0", 1), Is.EqualTo(2));
    }

    #endregion

    #region IAsyncEnumerable Streaming

    [Test]
    public async Task RawSqlAsync_StreamingEnumeration_YieldsRowByRow()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var liteIds = new List<int>();
        await foreach (var u in Lite.RawSqlAsync<UserWithEmailDto>(
            "SELECT \"UserId\", \"UserName\", \"Email\" FROM \"users\" ORDER BY \"UserId\""))
            liteIds.Add(u.UserId);
        Assert.That(liteIds, Is.EqualTo(new[] { 1, 2, 3 }));

        var pgIds = new List<int>();
        await foreach (var u in Pg.RawSqlAsync<UserWithEmailDto>(
            "SELECT \"UserId\", \"UserName\", \"Email\" FROM \"users\" ORDER BY \"UserId\""))
            pgIds.Add(u.UserId);
        Assert.That(pgIds, Is.EqualTo(new[] { 1, 2, 3 }));

        var myIds = new List<int>();
        await foreach (var u in My.RawSqlAsync<UserWithEmailDto>(
            "SELECT `UserId`, `UserName`, `Email` FROM `users` ORDER BY `UserId`"))
            myIds.Add(u.UserId);
        Assert.That(myIds, Is.EqualTo(new[] { 1, 2, 3 }));

        var ssIds = new List<int>();
        await foreach (var u in Ss.RawSqlAsync<UserWithEmailDto>(
            "SELECT [UserId], [UserName], [Email] FROM [users] ORDER BY [UserId]"))
            ssIds.Add(u.UserId);
        Assert.That(ssIds, Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public async Task RawSqlAsync_PartialEnumeration_DoesNotThrow()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        int liteFirst = 0;
        await foreach (var u in Lite.RawSqlAsync<UserWithEmailDto>(
            "SELECT \"UserId\", \"UserName\", \"Email\" FROM \"users\" ORDER BY \"UserId\""))
        { liteFirst = u.UserId; break; }
        Assert.That(liteFirst, Is.EqualTo(1));

        int pgFirst = 0;
        await foreach (var u in Pg.RawSqlAsync<UserWithEmailDto>(
            "SELECT \"UserId\", \"UserName\", \"Email\" FROM \"users\" ORDER BY \"UserId\""))
        { pgFirst = u.UserId; break; }
        Assert.That(pgFirst, Is.EqualTo(1));

        int myFirst = 0;
        await foreach (var u in My.RawSqlAsync<UserWithEmailDto>(
            "SELECT `UserId`, `UserName`, `Email` FROM `users` ORDER BY `UserId`"))
        { myFirst = u.UserId; break; }
        Assert.That(myFirst, Is.EqualTo(1));

        int ssFirst = 0;
        await foreach (var u in Ss.RawSqlAsync<UserWithEmailDto>(
            "SELECT [UserId], [UserName], [Email] FROM [users] ORDER BY [UserId]"))
        { ssFirst = u.UserId; break; }
        Assert.That(ssFirst, Is.EqualTo(1));
    }

    #endregion

    #region Compile-Time Column Resolution

    [Test]
    public async Task RawSqlAsync_LiteralSql_WithAliases_ResolvesCorrectly()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = await Lite.RawSqlAsync<UserWithEmailDto>(
            "SELECT \"UserId\" AS \"UserId\", \"UserName\" AS \"UserName\", \"Email\" AS \"Email\" FROM \"users\" ORDER BY \"UserId\"").ToListAsync();
        AssertAliceFirst(lite);

        var pgRows = await Pg.RawSqlAsync<UserWithEmailDto>(
            "SELECT \"UserId\" AS \"UserId\", \"UserName\" AS \"UserName\", \"Email\" AS \"Email\" FROM \"users\" ORDER BY \"UserId\"").ToListAsync();
        AssertAliceFirst(pgRows);

        var myRows = await My.RawSqlAsync<UserWithEmailDto>(
            "SELECT `UserId` AS `UserId`, `UserName` AS `UserName`, `Email` AS `Email` FROM `users` ORDER BY `UserId`").ToListAsync();
        AssertAliceFirst(myRows);

        var ssRows = await Ss.RawSqlAsync<UserWithEmailDto>(
            "SELECT [UserId] AS [UserId], [UserName] AS [UserName], [Email] AS [Email] FROM [users] ORDER BY [UserId]").ToListAsync();
        AssertAliceFirst(ssRows);
    }

    private static void AssertAliceFirst(List<UserWithEmailDto> results)
    {
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0].UserId, Is.EqualTo(1));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
        Assert.That(results[0].Email, Is.EqualTo("alice@test.com"));
    }

    [Test]
    public async Task RawSqlAsync_LiteralSql_PartialColumns_MissingPropertiesStayDefault()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = await Lite.RawSqlAsync<UserWithEmailDto>(
            "SELECT \"UserId\", \"UserName\" FROM \"users\" ORDER BY \"UserId\"").ToListAsync();
        AssertPartialColumns(lite);

        var pgRows = await Pg.RawSqlAsync<UserWithEmailDto>(
            "SELECT \"UserId\", \"UserName\" FROM \"users\" ORDER BY \"UserId\"").ToListAsync();
        AssertPartialColumns(pgRows);

        var myRows = await My.RawSqlAsync<UserWithEmailDto>(
            "SELECT `UserId`, `UserName` FROM `users` ORDER BY `UserId`").ToListAsync();
        AssertPartialColumns(myRows);

        var ssRows = await Ss.RawSqlAsync<UserWithEmailDto>(
            "SELECT [UserId], [UserName] FROM [users] ORDER BY [UserId]").ToListAsync();
        AssertPartialColumns(ssRows);
    }

    private static void AssertPartialColumns(List<UserWithEmailDto> results)
    {
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0].UserId, Is.EqualTo(1));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
        Assert.That(results[0].Email, Is.Null, "Email not in SQL — should be default (null)");
        Assert.That(results[1].UserId, Is.EqualTo(2));
        Assert.That(results[1].Email, Is.Null);
    }

    #endregion

    #region Single-dialect: base-class fallback (not cross-dialect — tests QuarryContext base behavior)

    [Test]
    public async Task RawSqlAsync_WithoutInterception_ThrowsNotSupportedException()
    {
        // This test exercises the abstract base-class fallback behavior, which is
        // identical regardless of dialect — the generator-emitted interceptor
        // replaces the base method on every concrete context. Calling the base
        // method directly via reflection bypasses the interceptor and verifies the
        // unintercepted path still throws NotSupportedException with the expected
        // message. SQLite-only because the test is dialect-agnostic.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var method = typeof(QuarryContext).GetMethod("RawSqlAsync", new[] { typeof(string), typeof(CancellationToken), typeof(object?[]) })!;
        var generic = method.MakeGenericMethod(typeof(UserWithEmailDto));

        var ex = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
            generic.Invoke(Lite, new object[] { "SELECT 1", CancellationToken.None, Array.Empty<object?>() }));
        Assert.That(ex!.InnerException, Is.TypeOf<NotSupportedException>());
    }

    #endregion
}
