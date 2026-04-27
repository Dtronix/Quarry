using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using Quarry.Internal;
using Quarry.Logging;
using Quarry.Tests.Integration;
using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;


/// <summary>
/// Cross-dialect logging tests. <see cref="LogsmithOutput.Logger"/> is a
/// process-wide singleton, so the fixture is <see cref="NonParallelizable"/>;
/// each test clears the recording logger between dialects via
/// <c>_logger.Clear()</c> and asserts the per-dialect log shape independently.
///
/// Some tests intentionally remain SQLite-only:
/// - Tests that depend on the inline <c>widgets</c> table (`Sensitive*`,
///   `InsertSensitiveColumn_*`): the harness baselines for Pg/My/Ss don't seed
///   widgets. The sensitive-redaction code path is dialect-independent — it
///   runs in <see cref="QuarryContext"/> before the SQL is built — so verifying
///   on SQLite is sufficient. Test explicitly creates a per-test SQLite
///   connection.
/// </summary>
[TestFixture]
[NonParallelizable]
internal partial class CrossDialectLoggingTests
{
    private RecordingLogsmithLogger _logger = null!;

    [GeneratedRegex(@"\[(\d+)\]")]
    private static partial Regex OpIdPattern();

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _logger = new RecordingLogsmithLogger();
        LogsmithOutput.Logger = _logger;
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        LogsmithOutput.Logger = null;
    }

    [SetUp]
    public void SetUp()
    {
        _logger.MinimumLevel = LogLevel.Trace;
        _logger.CategoryOverrides.Clear();
        _logger.Clear();
    }

    private static long ExtractOpId(string message)
    {
        var match = OpIdPattern().Match(message);
        Assert.That(match.Success, Is.True, $"Expected opId pattern [N] in message: {message}");
        return long.Parse(match.Groups[1].Value);
    }

    private void ReconfigureLogger(LogLevel minimumLevel, Dictionary<string, LogLevel>? categoryOverrides = null)
    {
        _logger.MinimumLevel = minimumLevel;
        _logger.CategoryOverrides.Clear();
        if (categoryOverrides != null)
            foreach (var kvp in categoryOverrides)
                _logger.CategoryOverrides[kvp.Key] = kvp.Value;
        _logger.Clear();
    }

    private void ResetLogger()
    {
        _logger.MinimumLevel = LogLevel.Trace;
        _logger.CategoryOverrides.Clear();
    }

    #region Query Logging (Quarry.Query)

    private void AssertQuerySqlAndCompletion(string dialect, int expectedRowCount)
    {
        var queryEntries = _logger.Entries.Where(e => e.Category == "Quarry.Query").ToList();
        Assert.That(queryEntries, Has.Count.GreaterThanOrEqualTo(2), $"{dialect}: expected ≥2 Quarry.Query entries");
        Assert.That(queryEntries[0].Level, Is.EqualTo(LogLevel.Debug), $"{dialect}: SQL log entry");
        Assert.That(queryEntries[0].Message, Does.Contain("SQL:"));
        Assert.That(queryEntries[0].Message, Does.Contain("SELECT"));
        Assert.That(queryEntries[1].Level, Is.EqualTo(LogLevel.Debug), $"{dialect}: completion log entry");
        Assert.That(queryEntries[1].Message, Does.Contain("Fetched"));
        Assert.That(queryEntries[1].Message, Does.Contain(expectedRowCount.ToString()));
    }

    [Test]
    public async Task FetchAll_LogsSqlAndCompletion()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        _logger.Clear();
        await Lite.Users().Select(u => (u.UserId, u.UserName)).ExecuteFetchAllAsync();
        AssertQuerySqlAndCompletion("Lite", 3);

        _logger.Clear();
        await Pg.Users().Select(u => (u.UserId, u.UserName)).ExecuteFetchAllAsync();
        AssertQuerySqlAndCompletion("Pg", 3);

        _logger.Clear();
        await My.Users().Select(u => (u.UserId, u.UserName)).ExecuteFetchAllAsync();
        AssertQuerySqlAndCompletion("My", 3);

        _logger.Clear();
        await Ss.Users().Select(u => (u.UserId, u.UserName)).ExecuteFetchAllAsync();
        AssertQuerySqlAndCompletion("Ss", 3);
    }

    private void AssertSingleOpIdInQueryEntries(string dialect)
    {
        var queryEntries = _logger.Entries.Where(e => e.Category == "Quarry.Query").ToList();
        Assert.That(queryEntries, Has.Count.GreaterThanOrEqualTo(2), $"{dialect}: expected ≥2 entries");
        var opIds = queryEntries.Select(e => ExtractOpId(e.Message)).Distinct().ToList();
        Assert.That(opIds, Has.Count.EqualTo(1), $"{dialect}: all entries from one operation should share opId");
    }

    [Test]
    public async Task FetchAll_AllEntriesShareSameOpId()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        _logger.Clear();
        await Lite.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        AssertSingleOpIdInQueryEntries("Lite");

        _logger.Clear();
        await Pg.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        AssertSingleOpIdInQueryEntries("Pg");

        _logger.Clear();
        await My.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        AssertSingleOpIdInQueryEntries("My");

        _logger.Clear();
        await Ss.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        AssertSingleOpIdInQueryEntries("Ss");
    }

    private void AssertTwoQueriesDistinctOpIds(string dialect)
    {
        var sqlEntries = _logger.Entries
            .Where(e => e.Category == "Quarry.Query" && e.Message.Contains("SQL:"))
            .ToList();
        Assert.That(sqlEntries, Has.Count.EqualTo(2), $"{dialect}: expected 2 SQL log entries");
        var opId1 = ExtractOpId(sqlEntries[0].Message);
        var opId2 = ExtractOpId(sqlEntries[1].Message);
        Assert.That(opId1, Is.Not.EqualTo(opId2), $"{dialect}: distinct queries should have distinct opIds");
    }

    [Test]
    public async Task TwoQueries_GetDistinctOpIds()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        _logger.Clear();
        await Lite.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        await Lite.Users().Select(u => u.UserId).ExecuteFetchAllAsync();
        AssertTwoQueriesDistinctOpIds("Lite");

        _logger.Clear();
        await Pg.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        await Pg.Users().Select(u => u.UserId).ExecuteFetchAllAsync();
        AssertTwoQueriesDistinctOpIds("Pg");

        _logger.Clear();
        await My.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        await My.Users().Select(u => u.UserId).ExecuteFetchAllAsync();
        AssertTwoQueriesDistinctOpIds("My");

        _logger.Clear();
        await Ss.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        await Ss.Users().Select(u => u.UserId).ExecuteFetchAllAsync();
        AssertTwoQueriesDistinctOpIds("Ss");
    }

    private void AssertCompletionRowCount(string dialect, string expectedCount)
    {
        var completionEntry = _logger.Entries
            .FirstOrDefault(e => e.Category == "Quarry.Query" && e.Message.Contains("Fetched"));
        Assert.That(completionEntry, Is.Not.Null, $"{dialect}: completion entry expected");
        Assert.That(completionEntry!.Message, Does.Contain(expectedCount));
    }

    [Test]
    public async Task FetchFirst_LogsCompletion()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        _logger.Clear();
        await Lite.Users().Select(u => u.UserName).ExecuteFetchFirstAsync();
        AssertCompletionRowCount("Lite", "1");

        _logger.Clear();
        await Pg.Users().Select(u => u.UserName).ExecuteFetchFirstAsync();
        AssertCompletionRowCount("Pg", "1");

        _logger.Clear();
        await My.Users().Select(u => u.UserName).ExecuteFetchFirstAsync();
        AssertCompletionRowCount("My", "1");

        _logger.Clear();
        await Ss.Users().Select(u => u.UserName).ExecuteFetchFirstAsync();
        AssertCompletionRowCount("Ss", "1");
    }

    [Test]
    public async Task FetchFirstOrDefault_NoRows_LogsZeroCount()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        _logger.Clear();
        await Lite.Users().Where(u => u.UserId == -999).Select(u => u.UserName).ExecuteFetchFirstOrDefaultAsync();
        AssertCompletionRowCount("Lite", "0");

        _logger.Clear();
        await Pg.Users().Where(u => u.UserId == -999).Select(u => u.UserName).ExecuteFetchFirstOrDefaultAsync();
        AssertCompletionRowCount("Pg", "0");

        _logger.Clear();
        await My.Users().Where(u => u.UserId == -999).Select(u => u.UserName).ExecuteFetchFirstOrDefaultAsync();
        AssertCompletionRowCount("My", "0");

        _logger.Clear();
        await Ss.Users().Where(u => u.UserId == -999).Select(u => u.UserName).ExecuteFetchFirstOrDefaultAsync();
        AssertCompletionRowCount("Ss", "0");
    }

    private void AssertScalarLog(string dialect, string expectedValue, string category = "Quarry.Query")
    {
        var scalarEntry = _logger.Entries
            .FirstOrDefault(e => e.Category == category && e.Message.Contains("Scalar result"));
        Assert.That(scalarEntry, Is.Not.Null, $"{dialect}: scalar log entry expected in {category}");
        Assert.That(scalarEntry!.Message, Does.Contain(expectedValue));
    }

    [Test]
    public async Task ExecuteScalar_LogsResult()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        _logger.Clear();
        await Lite.Users().Select(u => Sql.Count()).ExecuteScalarAsync<int>();
        AssertScalarLog("Lite", "3");

        _logger.Clear();
        await Pg.Users().Select(u => Sql.Count()).ExecuteScalarAsync<int>();
        AssertScalarLog("Pg", "3");

        _logger.Clear();
        await My.Users().Select(u => Sql.Count()).ExecuteScalarAsync<int>();
        AssertScalarLog("My", "3");

        _logger.Clear();
        await Ss.Users().Select(u => Sql.Count()).ExecuteScalarAsync<int>();
        AssertScalarLog("Ss", "3");
    }

    #endregion

    #region Raw SQL Modification Logging (Quarry.RawSql)

    private void AssertRawSqlNonQueryShape(string dialect, string expectedSqlKeyword)
    {
        var rawEntries = _logger.Entries.Where(e => e.Category == "Quarry.RawSql").ToList();
        Assert.That(rawEntries, Has.Count.GreaterThanOrEqualTo(2), $"{dialect}: ≥2 Quarry.RawSql entries");
        Assert.That(rawEntries[0].Message, Does.Contain("SQL:"));
        Assert.That(rawEntries[0].Message, Does.Contain(expectedSqlKeyword));
        Assert.That(rawEntries[1].Message, Does.Contain("NonQuery"));
    }

    [Test]
    public async Task RawInsert_LogsSqlAndCompletion()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Use DateTime (not string) for CreatedAt — Pg's TIMESTAMP column rejects string-typed
        // parameters with code 42804. SQLite/MySQL/SqlServer all accept DateTime; the test is
        // about logging shape, not the parameter wire format.
        var createdAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        _logger.Clear();
        await Lite.RawSqlNonQueryAsync(
            "INSERT INTO \"users\" (\"UserName\", \"IsActive\", \"CreatedAt\") VALUES (@p0, @p1, @p2)",
            "TestInsertLog", 1, createdAt);
        AssertRawSqlNonQueryShape("Lite", "INSERT");
        Assert.That(_logger.Entries.Last(e => e.Category == "Quarry.RawSql").Message, Does.Contain("affected"));

        _logger.Clear();
        await Pg.RawSqlNonQueryAsync(
            "INSERT INTO \"users\" (\"UserName\", \"IsActive\", \"CreatedAt\") VALUES (@p0, @p1, @p2)",
            "TestInsertLog", true, createdAt);
        AssertRawSqlNonQueryShape("Pg", "INSERT");

        _logger.Clear();
        await My.RawSqlNonQueryAsync(
            "INSERT INTO `users` (`UserName`, `IsActive`, `CreatedAt`) VALUES (@p0, @p1, @p2)",
            "TestInsertLog", 1, createdAt);
        AssertRawSqlNonQueryShape("My", "INSERT");

        _logger.Clear();
        await Ss.RawSqlNonQueryAsync(
            "INSERT INTO [users] ([UserName], [IsActive], [CreatedAt]) VALUES (@p0, @p1, @p2)",
            "TestInsertLog", 1, createdAt);
        AssertRawSqlNonQueryShape("Ss", "INSERT");
    }

    [Test]
    public async Task RawUpdate_LogsSqlAndCompletion()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        _logger.Clear();
        await Lite.RawSqlNonQueryAsync(
            "UPDATE \"users\" SET \"Email\" = @p0 WHERE \"UserId\" = @p1", "updated@test.com", 3);
        AssertRawSqlNonQueryShape("Lite", "UPDATE");

        _logger.Clear();
        await Pg.RawSqlNonQueryAsync(
            "UPDATE \"users\" SET \"Email\" = @p0 WHERE \"UserId\" = @p1", "updated@test.com", 3);
        AssertRawSqlNonQueryShape("Pg", "UPDATE");

        _logger.Clear();
        await My.RawSqlNonQueryAsync(
            "UPDATE `users` SET `Email` = @p0 WHERE `UserId` = @p1", "updated@test.com", 3);
        AssertRawSqlNonQueryShape("My", "UPDATE");

        _logger.Clear();
        await Ss.RawSqlNonQueryAsync(
            "UPDATE [users] SET [Email] = @p0 WHERE [UserId] = @p1", "updated@test.com", 3);
        AssertRawSqlNonQueryShape("Ss", "UPDATE");
    }

    [Test]
    public async Task RawDelete_LogsSqlAndCompletion()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        _logger.Clear();
        await Lite.RawSqlNonQueryAsync(
            "DELETE FROM \"users\" WHERE \"UserId\" = @p0", 3);
        AssertRawSqlNonQueryShape("Lite", "DELETE");

        _logger.Clear();
        await Pg.RawSqlNonQueryAsync(
            "DELETE FROM \"users\" WHERE \"UserId\" = @p0", 3);
        AssertRawSqlNonQueryShape("Pg", "DELETE");

        _logger.Clear();
        await My.RawSqlNonQueryAsync(
            "DELETE FROM `users` WHERE `UserId` = @p0", 3);
        AssertRawSqlNonQueryShape("My", "DELETE");

        _logger.Clear();
        await Ss.RawSqlNonQueryAsync(
            "DELETE FROM [users] WHERE [UserId] = @p0", 3);
        AssertRawSqlNonQueryShape("Ss", "DELETE");
    }

    #endregion

    #region Raw SQL Logging (Quarry.RawSql)

    private void AssertRawSqlSelectShape(string dialect)
    {
        var rawEntries = _logger.Entries.Where(e => e.Category == "Quarry.RawSql").ToList();
        Assert.That(rawEntries, Has.Count.GreaterThanOrEqualTo(2), $"{dialect}: ≥2 Quarry.RawSql entries");
        Assert.That(rawEntries[0].Message, Does.Contain("SQL:"));
        Assert.That(rawEntries[0].Message, Does.Contain("SELECT 1"));
        Assert.That(rawEntries[1].Message, Does.Contain("NonQuery"));
    }

    [Test]
    public async Task RawSqlNonQuery_LogsSqlAndCompletion()
    {
        // "SELECT 1" parses identically across all four dialects.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        _logger.Clear();
        await Lite.RawSqlNonQueryAsync("SELECT 1");
        AssertRawSqlSelectShape("Lite");

        _logger.Clear();
        await Pg.RawSqlNonQueryAsync("SELECT 1");
        AssertRawSqlSelectShape("Pg");

        _logger.Clear();
        await My.RawSqlNonQueryAsync("SELECT 1");
        AssertRawSqlSelectShape("My");

        _logger.Clear();
        await Ss.RawSqlNonQueryAsync("SELECT 1");
        AssertRawSqlSelectShape("Ss");
    }

    [Test]
    public async Task RawSqlScalar_LogsResult()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        _logger.Clear();
        await Lite.RawSqlScalarAsync<int>("SELECT COUNT(*) FROM \"users\"");
        AssertScalarLog("Lite", "3", "Quarry.RawSql");

        _logger.Clear();
        await Pg.RawSqlScalarAsync<int>("SELECT COUNT(*) FROM \"users\"");
        AssertScalarLog("Pg", "3", "Quarry.RawSql");

        _logger.Clear();
        await My.RawSqlScalarAsync<int>("SELECT COUNT(*) FROM `users`");
        AssertScalarLog("My", "3", "Quarry.RawSql");

        _logger.Clear();
        await Ss.RawSqlScalarAsync<int>("SELECT COUNT(*) FROM [users]");
        AssertScalarLog("Ss", "3", "Quarry.RawSql");
    }

    #endregion

    #region Connection Logging (Quarry.Connection)

    private void AssertOpenedLog(string dialect)
    {
        var connEntries = _logger.Entries.Where(e => e.Category == "Quarry.Connection").ToList();
        Assert.That(connEntries.Any(e => e.Message.Contains("opened")), Is.True,
            $"{dialect}: expected an 'opened' connection log");
        Assert.That(connEntries.First(e => e.Message.Contains("opened")).Level,
            Is.EqualTo(LogLevel.Information));
    }

    [Test]
    public async Task ClosedConnection_LogsOpened()
    {
        // For each dialect, build a context around a fresh CLOSED connection. Quarry's
        // EnsureConnectionOpenAsync should log "opened" when the runtime opens it.
        //
        // NOTE: These connections bypass the harness's transaction-rollback isolation —
        // each connects to the container's default DB/schema directly. Keep these tests
        // limited to "SELECT 1"; any INSERT/UPDATE/DELETE here would leak state across
        // shared containers between tests.
        _logger.Clear();
        await using (var conn = new SqliteConnection("Data Source=:memory:"))
        {
            await using var db = new TestDbContext(conn);
            await db.RawSqlNonQueryAsync("SELECT 1");
        }
        AssertOpenedLog("Lite");

        _logger.Clear();
        var pgCs = await PostgresTestContainer.GetConnectionStringAsync();
        await using (var conn = new NpgsqlConnection(pgCs))
        {
            await using var db = new Pg.PgDb(conn);
            await db.RawSqlNonQueryAsync("SELECT 1");
        }
        AssertOpenedLog("Pg");

        _logger.Clear();
        var myCs = await MySqlTestContainer.GetConnectionStringAsync();
        await using (var conn = new MySqlConnection(myCs))
        {
            await using var db = new My.MyDb(conn);
            await db.RawSqlNonQueryAsync("SELECT 1");
        }
        AssertOpenedLog("My");

        _logger.Clear();
        var ssCs = await MsSqlTestContainer.GetUserConnectionStringAsync();
        await using (var conn = new SqlConnection(ssCs))
        {
            await using var db = new Ss.SsDb(conn);
            await db.RawSqlNonQueryAsync("SELECT 1");
        }
        AssertOpenedLog("Ss");
    }

    private void AssertNoOpenedLog(string dialect)
    {
        var openedEntries = _logger.Entries
            .Where(e => e.Category == "Quarry.Connection" && e.Message.Contains("opened"))
            .ToList();
        Assert.That(openedEntries, Is.Empty, $"{dialect}: pre-opened connection should not log 'opened'");
    }

    [Test]
    public async Task PreOpenedConnection_DoesNotLogOpened()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        _logger.Clear();
        await Lite.RawSqlNonQueryAsync("SELECT 1");
        AssertNoOpenedLog("Lite");

        _logger.Clear();
        await Pg.RawSqlNonQueryAsync("SELECT 1");
        AssertNoOpenedLog("Pg");

        _logger.Clear();
        await My.RawSqlNonQueryAsync("SELECT 1");
        AssertNoOpenedLog("My");

        _logger.Clear();
        await Ss.RawSqlNonQueryAsync("SELECT 1");
        AssertNoOpenedLog("Ss");
    }

    private void AssertSingleClosedLog(string dialect)
    {
        var closedEntries = _logger.Entries
            .Where(e => e.Category == "Quarry.Connection" && e.Message.Contains("closed"))
            .ToList();
        Assert.That(closedEntries, Has.Count.EqualTo(1), $"{dialect}: expected exactly one 'closed' log");
        Assert.That(closedEntries[0].Level, Is.EqualTo(LogLevel.Information));
    }

    [Test]
    public async Task ClosedConnection_Dispose_LogsClosed()
    {
        // Open a fresh connection per dialect, do work, dispose — verify the close is logged.
        //
        // NOTE: Same un-isolated connections as ClosedConnection_LogsOpened. Keep limited
        // to "SELECT 1" to avoid cross-test state leakage on the shared containers.
        _logger.Clear();
        var liteConn = new SqliteConnection("Data Source=:memory:");
        var liteDb = new TestDbContext(liteConn);
        await liteDb.RawSqlNonQueryAsync("SELECT 1");
        _logger.Clear();
        liteDb.Dispose();
        AssertSingleClosedLog("Lite");
        liteConn.Dispose();

        _logger.Clear();
        var pgCs = await PostgresTestContainer.GetConnectionStringAsync();
        var pgConn = new NpgsqlConnection(pgCs);
        var pgDb = new Pg.PgDb(pgConn);
        await pgDb.RawSqlNonQueryAsync("SELECT 1");
        _logger.Clear();
        pgDb.Dispose();
        AssertSingleClosedLog("Pg");
        pgConn.Dispose();

        _logger.Clear();
        var myCs = await MySqlTestContainer.GetConnectionStringAsync();
        var myConn = new MySqlConnection(myCs);
        var myDb = new My.MyDb(myConn);
        await myDb.RawSqlNonQueryAsync("SELECT 1");
        _logger.Clear();
        myDb.Dispose();
        AssertSingleClosedLog("My");
        myConn.Dispose();

        _logger.Clear();
        var ssCs = await MsSqlTestContainer.GetUserConnectionStringAsync();
        var ssConn = new SqlConnection(ssCs);
        var ssDb = new Ss.SsDb(ssConn);
        await ssDb.RawSqlNonQueryAsync("SELECT 1");
        _logger.Clear();
        ssDb.Dispose();
        AssertSingleClosedLog("Ss");
        ssConn.Dispose();
    }

    #endregion

    #region Parameter Logging (Quarry.Parameters)

    private void AssertSingleParamLog(string dialect, string expectedValueSubstring)
    {
        var paramEntries = _logger.Entries.Where(e => e.Category == "Quarry.Parameters").ToList();
        Assert.That(paramEntries, Has.Count.GreaterThanOrEqualTo(1), $"{dialect}: ≥1 parameter log");
        Assert.That(paramEntries[0].Level, Is.EqualTo(LogLevel.Trace));
        Assert.That(paramEntries[0].Message, Does.Contain("@p0"));
        Assert.That(paramEntries[0].Message, Does.Contain(expectedValueSubstring));
    }

    [Test]
    public async Task QueryWithParameters_LogsParameterValues()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var userId = 1;

        _logger.Clear();
        await Lite.Users().Where(u => u.UserId == userId).Select(u => u.UserName).ExecuteFetchAllAsync();
        AssertSingleParamLog("Lite", "1");

        _logger.Clear();
        await Pg.Users().Where(u => u.UserId == userId).Select(u => u.UserName).ExecuteFetchAllAsync();
        AssertSingleParamLog("Pg", "1");

        _logger.Clear();
        await My.Users().Where(u => u.UserId == userId).Select(u => u.UserName).ExecuteFetchAllAsync();
        AssertSingleParamLog("My", "1");

        _logger.Clear();
        await Ss.Users().Where(u => u.UserId == userId).Select(u => u.UserName).ExecuteFetchAllAsync();
        AssertSingleParamLog("Ss", "1");
    }

    private void AssertTwoParamsLogged(string dialect)
    {
        var paramEntries = _logger.Entries.Where(e => e.Category == "Quarry.Parameters").ToList();
        Assert.That(paramEntries, Has.Count.EqualTo(2), $"{dialect}: expected 2 parameter logs");
        Assert.That(paramEntries[0].Message, Does.Contain("@p0"));
        Assert.That(paramEntries[0].Message, Does.Contain("1"));
        Assert.That(paramEntries[1].Message, Does.Contain("@p1"));
        Assert.That(paramEntries[1].Message, Does.Contain("Alice"));
    }

    [Test]
    public async Task QueryWithMultipleParameters_LogsAllParameters()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        _logger.Clear();
        await Lite.RawSqlNonQueryAsync(
            "SELECT * FROM \"users\" WHERE \"UserId\" = @p0 AND \"UserName\" = @p1", 1, "Alice");
        AssertTwoParamsLogged("Lite");

        _logger.Clear();
        await Pg.RawSqlNonQueryAsync(
            "SELECT * FROM \"users\" WHERE \"UserId\" = @p0 AND \"UserName\" = @p1", 1, "Alice");
        AssertTwoParamsLogged("Pg");

        _logger.Clear();
        await My.RawSqlNonQueryAsync(
            "SELECT * FROM `users` WHERE `UserId` = @p0 AND `UserName` = @p1", 1, "Alice");
        AssertTwoParamsLogged("My");

        _logger.Clear();
        await Ss.RawSqlNonQueryAsync(
            "SELECT * FROM [users] WHERE [UserId] = @p0 AND [UserName] = @p1", 1, "Alice");
        AssertTwoParamsLogged("Ss");
    }

    private void AssertParamShareOpIdWithQuery(string dialect)
    {
        var queryEntry = _logger.Entries.First(e => e.Category == "Quarry.Query" && e.Message.Contains("SQL:"));
        var paramEntry = _logger.Entries.First(e => e.Category == "Quarry.Parameters");
        Assert.That(ExtractOpId(queryEntry.Message), Is.EqualTo(ExtractOpId(paramEntry.Message)),
            $"{dialect}: query and parameter logs should share opId");
    }

    [Test]
    public async Task ParameterLogs_ShareOpIdWithQueryLog()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var userId = 1;

        _logger.Clear();
        await Lite.Users().Where(u => u.UserId == userId).Select(u => u.UserName).ExecuteFetchAllAsync();
        AssertParamShareOpIdWithQuery("Lite");

        _logger.Clear();
        await Pg.Users().Where(u => u.UserId == userId).Select(u => u.UserName).ExecuteFetchAllAsync();
        AssertParamShareOpIdWithQuery("Pg");

        _logger.Clear();
        await My.Users().Where(u => u.UserId == userId).Select(u => u.UserName).ExecuteFetchAllAsync();
        AssertParamShareOpIdWithQuery("My");

        _logger.Clear();
        await Ss.Users().Where(u => u.UserId == userId).Select(u => u.UserName).ExecuteFetchAllAsync();
        AssertParamShareOpIdWithQuery("Ss");
    }

    #endregion

    #region Sensitive Column Redaction (SQLite-only — see class docstring)

    [Test]
    public async Task SensitiveColumn_LogsRedactedValue()
    {
        // SQLite-only — provider-independent path, see class docstring.
        // Sensitive-redaction runs in QuarryContext before SQL is built (ParameterLog.BoundSensitive
        // emits regardless of provider), so single-dialect verification is sufficient.
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await CreateAndSeedWidgetsAsync(conn);
        _logger.Clear();
        await using var db = new TestDbContext(conn);

        var secret = "super-secret-value";
        await db.Widgets().Where(w => w.Secret == secret).Select(w => w.WidgetName).ExecuteFetchAllAsync();

        var paramEntries = _logger.Entries.Where(e => e.Category == "Quarry.Parameters").ToList();
        Assert.That(paramEntries, Has.Count.GreaterThanOrEqualTo(1));
        var sensitiveEntry = paramEntries.First();
        Assert.That(sensitiveEntry.Message, Does.Not.Contain("super-secret-value"));
        Assert.That(sensitiveEntry.Message, Does.Contain("SENSITIVE"));
    }

    [Test]
    public async Task SensitiveColumn_DoesNotLeakValueAtAnyLevel()
    {
        // SQLite-only — provider-independent path, see class docstring.
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await CreateAndSeedWidgetsAsync(conn);
        _logger.Clear();
        await using var db = new TestDbContext(conn);

        var secret = "super-secret-value";
        await db.Widgets().Where(w => w.Secret == secret).Select(w => w.WidgetName).ExecuteFetchAllAsync();

        Assert.That(_logger.Entries, Has.None.Matches<RecordingLogsmithLogger.LogRecord>(
            e => e.Message.Contains("super-secret-value")));
    }

    [Test]
    public async Task InsertSensitiveColumn_LogsRedactedValue()
    {
        // SQLite-only — provider-independent path, see class docstring.
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await CreateAndSeedWidgetsAsync(conn);
        _logger.Clear();
        await using var db = new TestDbContext(conn);

        var id = Guid.NewGuid();
        await db.Widgets()
            .Insert(new Widget { WidgetId = id, WidgetName = "TestWidget", Secret = "insert-secret-123" })
            .ExecuteNonQueryAsync();

        var paramEntries = _logger.Entries.Where(e => e.Category == "Quarry.Parameters").ToList();
        Assert.That(paramEntries, Has.Some.Matches<RecordingLogsmithLogger.LogRecord>(
            e => e.Message.Contains("SENSITIVE")));
        Assert.That(_logger.Entries, Has.None.Matches<RecordingLogsmithLogger.LogRecord>(
            e => e.Message.Contains("insert-secret-123")));
    }

    private static async Task CreateAndSeedWidgetsAsync(SqliteConnection conn)
    {
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE "widgets" (
                    "WidgetId" TEXT NOT NULL PRIMARY KEY,
                    "WidgetName" TEXT NOT NULL,
                    "Secret" TEXT NOT NULL
                )
                """;
            await cmd.ExecuteNonQueryAsync();
        }
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO "widgets" ("WidgetId", "WidgetName", "Secret") VALUES
                    ('00000000-0000-0000-0000-000000000001', 'Gizmo', 'super-secret-value')
                """;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    #endregion

    #region Slow Query Detection (Quarry.Execution)

    private void AssertSlowQueryWarning(string dialect)
    {
        var slowEntries = _logger.Entries
            .Where(e => e.Category == "Quarry.Execution" && e.Level == LogLevel.Warning)
            .ToList();
        Assert.That(slowEntries, Has.Count.EqualTo(1), $"{dialect}: expected exactly one slow-query warning");
        Assert.That(slowEntries[0].Message, Does.Contain("Slow query"));
    }

    [Test]
    public async Task SlowQueryThresholdZero_EmitsWarning()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        Lite.SlowQueryThreshold = TimeSpan.Zero;
        Pg.SlowQueryThreshold = TimeSpan.Zero;
        My.SlowQueryThreshold = TimeSpan.Zero;
        Ss.SlowQueryThreshold = TimeSpan.Zero;

        _logger.Clear();
        await Lite.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        AssertSlowQueryWarning("Lite");

        _logger.Clear();
        await Pg.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        AssertSlowQueryWarning("Pg");

        _logger.Clear();
        await My.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        AssertSlowQueryWarning("My");

        _logger.Clear();
        await Ss.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        AssertSlowQueryWarning("Ss");
    }

    private void AssertNoExecutionLog(string dialect)
    {
        var entries = _logger.Entries.Where(e => e.Category == "Quarry.Execution").ToList();
        Assert.That(entries, Is.Empty, $"{dialect}: expected no Quarry.Execution entries");
    }

    [Test]
    public async Task SlowQueryThresholdNull_NoWarning()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        Lite.SlowQueryThreshold = null;
        Pg.SlowQueryThreshold = null;
        My.SlowQueryThreshold = null;
        Ss.SlowQueryThreshold = null;

        _logger.Clear();
        await Lite.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        AssertNoExecutionLog("Lite");

        _logger.Clear();
        await Pg.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        AssertNoExecutionLog("Pg");

        _logger.Clear();
        await My.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        AssertNoExecutionLog("My");

        _logger.Clear();
        await Ss.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        AssertNoExecutionLog("Ss");
    }

    [Test]
    public async Task SlowQueryThresholdLarge_NoWarningForFastQuery()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        Lite.SlowQueryThreshold = TimeSpan.FromMinutes(10);
        Pg.SlowQueryThreshold = TimeSpan.FromMinutes(10);
        My.SlowQueryThreshold = TimeSpan.FromMinutes(10);
        Ss.SlowQueryThreshold = TimeSpan.FromMinutes(10);

        _logger.Clear();
        await Lite.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        AssertNoExecutionLog("Lite");

        _logger.Clear();
        await Pg.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        AssertNoExecutionLog("Pg");

        _logger.Clear();
        await My.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        AssertNoExecutionLog("My");

        _logger.Clear();
        await Ss.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        AssertNoExecutionLog("Ss");
    }

    private void AssertSlowQueryWarningContent(string dialect)
    {
        var slowEntry = _logger.Entries
            .First(e => e.Category == "Quarry.Execution" && e.Level == LogLevel.Warning);
        Assert.That(slowEntry.Message, Does.Contain("ms"));
        Assert.That(slowEntry.Message, Does.Contain("SELECT"));
    }

    [Test]
    public async Task SlowQueryWarning_ContainsElapsedTimeAndSql()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        Lite.SlowQueryThreshold = TimeSpan.Zero;
        Pg.SlowQueryThreshold = TimeSpan.Zero;
        My.SlowQueryThreshold = TimeSpan.Zero;
        Ss.SlowQueryThreshold = TimeSpan.Zero;

        _logger.Clear();
        await Lite.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        AssertSlowQueryWarningContent("Lite");

        _logger.Clear();
        await Pg.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        AssertSlowQueryWarningContent("Pg");

        _logger.Clear();
        await My.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        AssertSlowQueryWarningContent("My");

        _logger.Clear();
        await Ss.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        AssertSlowQueryWarningContent("Ss");
    }

    [Test]
    public async Task SlowQueryScalar_EmitsWarning()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        Lite.SlowQueryThreshold = TimeSpan.Zero;
        Pg.SlowQueryThreshold = TimeSpan.Zero;
        My.SlowQueryThreshold = TimeSpan.Zero;
        Ss.SlowQueryThreshold = TimeSpan.Zero;

        _logger.Clear();
        await Lite.Users().Select(u => Sql.Count()).ExecuteScalarAsync<int>();
        AssertSlowQueryWarning("Lite");

        _logger.Clear();
        await Pg.Users().Select(u => Sql.Count()).ExecuteScalarAsync<int>();
        AssertSlowQueryWarning("Pg");

        _logger.Clear();
        await My.Users().Select(u => Sql.Count()).ExecuteScalarAsync<int>();
        AssertSlowQueryWarning("My");

        _logger.Clear();
        await Ss.Users().Select(u => Sql.Count()).ExecuteScalarAsync<int>();
        AssertSlowQueryWarning("Ss");
    }

    #endregion

    #region Level Gating

    [Test]
    public async Task MinimumLevelWarning_SuppressesDebugLogs()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        ReconfigureLogger(LogLevel.Warning);
        try
        {
            await Lite.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
            Assert.That(_logger.Entries.Where(e => e.Level == LogLevel.Debug), Is.Empty,
                "Lite: Debug entries should be suppressed");

            ReconfigureLogger(LogLevel.Warning);
            await Pg.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
            Assert.That(_logger.Entries.Where(e => e.Level == LogLevel.Debug), Is.Empty,
                "Pg: Debug entries should be suppressed");

            ReconfigureLogger(LogLevel.Warning);
            await My.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
            Assert.That(_logger.Entries.Where(e => e.Level == LogLevel.Debug), Is.Empty,
                "My: Debug entries should be suppressed");

            ReconfigureLogger(LogLevel.Warning);
            await Ss.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
            Assert.That(_logger.Entries.Where(e => e.Level == LogLevel.Debug), Is.Empty,
                "Ss: Debug entries should be suppressed");
        }
        finally { ResetLogger(); }
    }

    [Test]
    public async Task CategoryLevelNone_SuppressesQueryLogsOnly()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        Lite.SlowQueryThreshold = TimeSpan.Zero;
        Pg.SlowQueryThreshold = TimeSpan.Zero;
        My.SlowQueryThreshold = TimeSpan.Zero;
        Ss.SlowQueryThreshold = TimeSpan.Zero;

        try
        {
            ReconfigureLogger(LogLevel.Trace, new Dictionary<string, LogLevel>
            { ["Quarry.Query"] = LogLevel.None });
            await Lite.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
            Assert.That(_logger.Entries.Where(e => e.Category == "Quarry.Query"), Is.Empty, "Lite: Query suppressed");
            Assert.That(_logger.Entries.Where(e => e.Category == "Quarry.Execution"), Is.Not.Empty, "Lite: Execution still works");

            ReconfigureLogger(LogLevel.Trace, new Dictionary<string, LogLevel>
            { ["Quarry.Query"] = LogLevel.None });
            await Pg.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
            Assert.That(_logger.Entries.Where(e => e.Category == "Quarry.Query"), Is.Empty, "Pg: Query suppressed");
            Assert.That(_logger.Entries.Where(e => e.Category == "Quarry.Execution"), Is.Not.Empty, "Pg: Execution still works");

            ReconfigureLogger(LogLevel.Trace, new Dictionary<string, LogLevel>
            { ["Quarry.Query"] = LogLevel.None });
            await My.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
            Assert.That(_logger.Entries.Where(e => e.Category == "Quarry.Query"), Is.Empty, "My: Query suppressed");
            Assert.That(_logger.Entries.Where(e => e.Category == "Quarry.Execution"), Is.Not.Empty, "My: Execution still works");

            ReconfigureLogger(LogLevel.Trace, new Dictionary<string, LogLevel>
            { ["Quarry.Query"] = LogLevel.None });
            await Ss.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
            Assert.That(_logger.Entries.Where(e => e.Category == "Quarry.Query"), Is.Empty, "Ss: Query suppressed");
            Assert.That(_logger.Entries.Where(e => e.Category == "Quarry.Execution"), Is.Not.Empty, "Ss: Execution still works");
        }
        finally { ResetLogger(); }
    }

    [Test]
    public async Task CategoryLevelDebug_SuppressesTraceParameterLogs()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var userId = 1;

        try
        {
            ReconfigureLogger(LogLevel.Debug);
            await Lite.Users().Where(u => u.UserId == userId).Select(u => u.UserName).ExecuteFetchAllAsync();
            Assert.That(_logger.Entries.Where(e => e.Category == "Quarry.Parameters"), Is.Empty, "Lite: Trace params suppressed");
            Assert.That(_logger.Entries.Where(e => e.Category == "Quarry.Query"), Is.Not.Empty, "Lite: Debug query still works");

            ReconfigureLogger(LogLevel.Debug);
            await Pg.Users().Where(u => u.UserId == userId).Select(u => u.UserName).ExecuteFetchAllAsync();
            Assert.That(_logger.Entries.Where(e => e.Category == "Quarry.Parameters"), Is.Empty, "Pg: Trace params suppressed");
            Assert.That(_logger.Entries.Where(e => e.Category == "Quarry.Query"), Is.Not.Empty, "Pg: Debug query still works");

            ReconfigureLogger(LogLevel.Debug);
            await My.Users().Where(u => u.UserId == userId).Select(u => u.UserName).ExecuteFetchAllAsync();
            Assert.That(_logger.Entries.Where(e => e.Category == "Quarry.Parameters"), Is.Empty, "My: Trace params suppressed");
            Assert.That(_logger.Entries.Where(e => e.Category == "Quarry.Query"), Is.Not.Empty, "My: Debug query still works");

            ReconfigureLogger(LogLevel.Debug);
            await Ss.Users().Where(u => u.UserId == userId).Select(u => u.UserName).ExecuteFetchAllAsync();
            Assert.That(_logger.Entries.Where(e => e.Category == "Quarry.Parameters"), Is.Empty, "Ss: Trace params suppressed");
            Assert.That(_logger.Entries.Where(e => e.Category == "Quarry.Query"), Is.Not.Empty, "Ss: Debug query still works");
        }
        finally { ResetLogger(); }
    }

    #endregion

    #region RawSql Parameter-Name Convention

    [Test]
    public async Task RawSql_ParameterName_IsAlwaysAtPN_AcrossDialects()
    {
        // The RawSql runtime always assigns `param.ParameterName = "@pN"` (see
        // QuarryContext.RawSqlScalarAsyncWithConverter, QuarryContext.cs:469). Every supported
        // provider accepts that named-parameter form: SQLite/SqlClient natively, Npgsql by
        // rewriting "@name" markers to positional internally, MySqlConnector via its named-
        // parameter mode. If the runtime convention ever changes (e.g., switching to native
        // positional binding on Npgsql 11+), this test pins the contract — every dialect
        // logs the parameter under the literal `@p0` name. Lives here (not in
        // CrossDialectRawSqlTests) because it depends on the singleton recording logger that
        // requires this fixture's [NonParallelizable] guard.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        _logger.Clear();
        await Lite.RawSqlScalarAsync<int>(
            "SELECT COUNT(*) FROM \"users\" WHERE \"UserId\" = @p0", 1);
        Assert.That(
            _logger.Entries.Any(e => e.Category == "Quarry.Parameters" && e.Message.Contains("@p0")),
            Is.True, "Lite should log parameter as @p0");

        _logger.Clear();
        await Pg.RawSqlScalarAsync<int>(
            "SELECT COUNT(*) FROM \"users\" WHERE \"UserId\" = @p0", 1);
        Assert.That(
            _logger.Entries.Any(e => e.Category == "Quarry.Parameters" && e.Message.Contains("@p0")),
            Is.True, "Pg should log parameter as @p0 (Npgsql rewrites @name to positional internally)");

        _logger.Clear();
        await My.RawSqlScalarAsync<int>(
            "SELECT COUNT(*) FROM `users` WHERE `UserId` = @p0", 1);
        Assert.That(
            _logger.Entries.Any(e => e.Category == "Quarry.Parameters" && e.Message.Contains("@p0")),
            Is.True, "My should log parameter as @p0");

        _logger.Clear();
        await Ss.RawSqlScalarAsync<int>(
            "SELECT COUNT(*) FROM [users] WHERE [UserId] = @p0", 1);
        Assert.That(
            _logger.Entries.Any(e => e.Category == "Quarry.Parameters" && e.Message.Contains("@p0")),
            Is.True, "Ss should log parameter as @p0");
    }

    #endregion

    #region Async Enumerable Logging

    [Test]
    public async Task ToAsyncEnumerable_LogsSqlAndCompletion()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        _logger.Clear();
        var liteCount = 0;
        await foreach (var name in Lite.Users().Select(u => u.UserName).ToAsyncEnumerable())
            liteCount++;
        AssertQuerySqlAndCompletion("Lite", liteCount);

        _logger.Clear();
        var pgCount = 0;
        await foreach (var name in Pg.Users().Select(u => u.UserName).ToAsyncEnumerable())
            pgCount++;
        AssertQuerySqlAndCompletion("Pg", pgCount);

        _logger.Clear();
        var myCount = 0;
        await foreach (var name in My.Users().Select(u => u.UserName).ToAsyncEnumerable())
            myCount++;
        AssertQuerySqlAndCompletion("My", myCount);

        _logger.Clear();
        var ssCount = 0;
        await foreach (var name in Ss.Users().Select(u => u.UserName).ToAsyncEnumerable())
            ssCount++;
        AssertQuerySqlAndCompletion("Ss", ssCount);
    }

    #endregion
}
