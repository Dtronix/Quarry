using Quarry;
using Quarry.Tests.Samples;

namespace Quarry.Tests.Integration;


/// <summary>
/// SQLite integration tests for RawSqlAsync and RawSqlScalarAsync.
/// Executes against a real in-memory SQLite database via QueryTestHarness.
/// Not cross-dialect — raw SQL bypasses the query builder and uses hand-written SQL strings.
/// </summary>
[TestFixture]
internal class RawSqlIntegrationTests
{
    #region RawSqlAsync<DTO> Tests

    [Test]
    public async Task RawSqlAsync_Dto_ReturnsPopulatedResults()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var results = await Lite.RawSqlAsync<UserWithEmailDto>(
            "SELECT \"UserId\", \"UserName\", \"Email\" FROM \"users\" ORDER BY \"UserId\"").ToListAsync();

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

    [Test]
    public async Task RawSqlAsync_Dto_UserSummary_ReturnsCorrectTypes()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var results = await Lite.RawSqlAsync<UserSummaryDto>(
            "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" ORDER BY \"UserId\"").ToListAsync();

        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0].IsActive, Is.True);
        Assert.That(results[2].IsActive, Is.False);
    }

    #endregion

    #region RawSqlAsync<scalar> Tests

    [Test]
    public async Task RawSqlAsync_ScalarInt_ReturnsListOfIntegers()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var results = await Lite.RawSqlAsync<int>(
            "SELECT \"UserId\" FROM \"users\" ORDER BY \"UserId\"").ToListAsync();

        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0], Is.EqualTo(1));
        Assert.That(results[1], Is.EqualTo(2));
        Assert.That(results[2], Is.EqualTo(3));
    }

    [Test]
    public async Task RawSqlAsync_ScalarString_ReturnsListOfStrings()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var results = await Lite.RawSqlAsync<string>(
            "SELECT \"UserName\" FROM \"users\" ORDER BY \"UserId\"").ToListAsync();

        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0], Is.EqualTo("Alice"));
        Assert.That(results[1], Is.EqualTo("Bob"));
        Assert.That(results[2], Is.EqualTo("Charlie"));
    }

    #endregion

    #region RawSqlScalarAsync Tests

    [Test]
    public async Task RawSqlScalarAsync_Int_ReturnsScalarValue()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var count = await Lite.RawSqlScalarAsync<int>(
            "SELECT COUNT(*) FROM \"users\"");

        Assert.That(count, Is.EqualTo(3));
    }

    [Test]
    public async Task RawSqlScalarAsync_String_ReturnsScalarValue()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var name = await Lite.RawSqlScalarAsync<string>(
            "SELECT \"UserName\" FROM \"users\" WHERE \"UserId\" = @p0", 1);

        Assert.That(name, Is.EqualTo("Alice"));
    }

    [Test]
    public async Task RawSqlScalarAsync_Long_ReturnsScalarValue()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var count = await Lite.RawSqlScalarAsync<long>(
            "SELECT COUNT(*) FROM \"orders\"");

        Assert.That(count, Is.EqualTo(3L));
    }

    #endregion

    #region Null Handling Tests

    [Test]
    public async Task RawSqlAsync_Dto_NullableColumns_ReturnsCorrectNulls()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var results = await Lite.RawSqlAsync<UserWithEmailDto>(
            "SELECT \"UserId\", \"UserName\", \"Email\" FROM \"users\" WHERE \"UserId\" = @p0", 2).ToListAsync();

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

        var result = await Lite.RawSqlScalarAsync<int>(
            "SELECT \"UserId\" FROM \"users\" WHERE \"UserId\" = -999");

        Assert.That(result, Is.EqualTo(default(int)));
    }

    [Test]
    public async Task RawSqlScalarAsync_NullableString_ReturnsDefaultForNull()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var result = await Lite.RawSqlScalarAsync<string>(
            "SELECT NULL");

        Assert.That(result, Is.Null);
    }

    #endregion

    #region Empty Result Set Tests

    [Test]
    public async Task RawSqlAsync_EmptyResultSet_ReturnsEmptyList()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var results = await Lite.RawSqlAsync<UserSummaryDto>(
            "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE 1 = 0").ToListAsync();

        Assert.That(results, Is.Not.Null);
        Assert.That(results, Is.Empty);
    }

    #endregion

    #region Parameter Binding Tests

    [Test]
    public async Task RawSqlAsync_WithParameters_ReturnsFilteredResults()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var results = await Lite.RawSqlAsync<UserWithEmailDto>(
            "SELECT \"UserId\", \"UserName\", \"Email\" FROM \"users\" WHERE \"UserId\" = @p0", 1).ToListAsync();

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].UserId, Is.EqualTo(1));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
    }

    [Test]
    public async Task RawSqlAsync_WithMultipleParameters_ReturnsFilteredResults()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var results = await Lite.RawSqlAsync<UserSummaryDto>(
            "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"IsActive\" = @p0 AND \"UserId\" > @p1",
            1, 1).ToListAsync();

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].UserId, Is.EqualTo(2));
        Assert.That(results[0].UserName, Is.EqualTo("Bob"));
    }

    [Test]
    public async Task RawSqlScalarAsync_WithParameter_ReturnsFilteredScalar()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var count = await Lite.RawSqlScalarAsync<int>(
            "SELECT COUNT(*) FROM \"orders\" WHERE \"UserId\" = @p0", 1);

        Assert.That(count, Is.EqualTo(2));
    }

    #endregion

    #region NotSupportedException Fallback Test

    [Test]
    public async Task RawSqlAsync_WithoutInterception_ThrowsNotSupportedException()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Call the base QuarryContext.RawSqlAsync<T> via reflection to bypass the generated interceptor.
        // This verifies the fallback body throws NotSupportedException.
        var method = typeof(QuarryContext).GetMethod("RawSqlAsync", new[] { typeof(string), typeof(CancellationToken), typeof(object?[]) })!;
        var generic = method.MakeGenericMethod(typeof(UserWithEmailDto));

        var ex = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
            generic.Invoke(Lite, new object[] { "SELECT 1", CancellationToken.None, Array.Empty<object?>() }));
        Assert.That(ex!.InnerException, Is.TypeOf<NotSupportedException>());
    }

    #endregion

    #region IAsyncEnumerable Streaming Tests

    [Test]
    public async Task RawSqlAsync_StreamingEnumeration_YieldsRowByRow()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var userIds = new List<int>();
        await foreach (var user in Lite.RawSqlAsync<UserWithEmailDto>(
            "SELECT \"UserId\", \"UserName\", \"Email\" FROM \"users\" ORDER BY \"UserId\""))
        {
            userIds.Add(user.UserId);
        }

        Assert.That(userIds, Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public async Task RawSqlAsync_PartialEnumeration_DoesNotThrow()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        int firstUserId = 0;
        await foreach (var user in Lite.RawSqlAsync<UserWithEmailDto>(
            "SELECT \"UserId\", \"UserName\", \"Email\" FROM \"users\" ORDER BY \"UserId\""))
        {
            firstUserId = user.UserId;
            break; // Only consume first row
        }

        Assert.That(firstUserId, Is.EqualTo(1));
    }

    #endregion

    #region Compile-Time Column Resolution Tests

    [Test]
    public async Task RawSqlAsync_LiteralSql_WithAliases_ResolvesCorrectly()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Uses AS aliases to match DTO property names — compile-time resolution via aliases
        var results = await Lite.RawSqlAsync<UserWithEmailDto>(
            "SELECT \"UserId\" AS \"UserId\", \"UserName\" AS \"UserName\", \"Email\" AS \"Email\" FROM \"users\" ORDER BY \"UserId\"").ToListAsync();

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

        // SQL selects only UserId and UserName — Email is not in the SQL
        // The compile-time resolver should skip Email, leaving it at default (null)
        var results = await Lite.RawSqlAsync<UserWithEmailDto>(
            "SELECT \"UserId\", \"UserName\" FROM \"users\" ORDER BY \"UserId\"").ToListAsync();

        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0].UserId, Is.EqualTo(1));
        Assert.That(results[0].UserName, Is.EqualTo("Alice"));
        Assert.That(results[0].Email, Is.Null, "Email not in SQL — should be default (null)");
        Assert.That(results[1].UserId, Is.EqualTo(2));
        Assert.That(results[1].Email, Is.Null);
    }

    #endregion
}
