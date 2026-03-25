using System.Text.RegularExpressions;
using Logsmith;
using Logsmith.Sinks;
using Microsoft.Data.Sqlite;
using Quarry.Internal;
using Quarry.Logging;
using Quarry.Tests.Samples;

namespace Quarry.Tests.Integration;


/// <summary>
/// Integration tests for Logsmith logging.
/// </summary>
/// <remarks>
/// These tests are NonParallelizable because LogManager is a process-wide singleton.
/// Each test clears the recording sink before execution.
/// </remarks>
[TestFixture]
[NonParallelizable]
internal partial class LoggingIntegrationTests
{
    private SqliteConnection _connection = null!;
    private RecordingSink _sink = null!;

    [GeneratedRegex(@"\[(\d+)\]")]
    private static partial Regex OpIdPattern();

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _sink = new RecordingSink();
        LogManager.Initialize(c =>
        {
            c.MinimumLevel = LogLevel.Trace;
            c.AddSink(_sink);
        });

        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        await ExecuteSqlAsync("""
            CREATE TABLE "users" (
                "UserId" INTEGER PRIMARY KEY,
                "UserName" TEXT NOT NULL,
                "Email" TEXT,
                "IsActive" INTEGER NOT NULL DEFAULT 1,
                "CreatedAt" TEXT NOT NULL,
                "LastLogin" TEXT
            )
            """);

        await ExecuteSqlAsync("""
            CREATE TABLE "orders" (
                "OrderId" INTEGER PRIMARY KEY,
                "UserId" INTEGER NOT NULL,
                "Total" REAL NOT NULL,
                "Status" TEXT NOT NULL,
                "OrderDate" TEXT NOT NULL,
                "Notes" TEXT,
                FOREIGN KEY ("UserId") REFERENCES "users"("UserId")
            )
            """);

        await ExecuteSqlAsync("""
            INSERT INTO "users" ("UserId", "UserName", "Email", "IsActive", "CreatedAt", "LastLogin") VALUES
                (1, 'Alice',   'alice@test.com',   1, '2024-01-15 00:00:00', '2024-06-01 00:00:00'),
                (2, 'Bob',     NULL,               1, '2024-02-20 00:00:00', NULL),
                (3, 'Charlie', 'charlie@test.com', 0, '2024-03-10 00:00:00', '2024-05-15 00:00:00')
            """);

        await ExecuteSqlAsync("""
            INSERT INTO "orders" ("OrderId", "UserId", "Total", "Status", "OrderDate", "Notes") VALUES
                (1, 1, 250.00, 'Shipped', '2024-06-01 00:00:00', 'Express'),
                (2, 1, 75.50,  'Pending', '2024-06-15 00:00:00', NULL),
                (3, 2, 150.00, 'Shipped', '2024-07-01 00:00:00', NULL)
            """);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        LogManager.Reconfigure(c => c.ClearSinks());
        _sink.Dispose();
        await _connection.DisposeAsync();
    }

    [SetUp]
    public void SetUp()
    {
        _sink.Clear();
    }

    private async Task ExecuteSqlAsync(string sql)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static long ExtractOpId(string message)
    {
        var match = OpIdPattern().Match(message);
        Assert.That(match.Success, Is.True, $"Expected opId pattern [N] in message: {message}");
        return long.Parse(match.Groups[1].Value);
    }

    private void ReconfigureLogging(Action<LogConfigBuilder> configure)
    {
        LogManager.Reconfigure(configure);
        _sink.Clear();
    }

    private void ResetLogging()
    {
        LogManager.Reconfigure(c =>
        {
            c.MinimumLevel = LogLevel.Trace;
            c.AddSink(_sink);
        });
    }

    #region Query Logging (Quarry.Query)

    [Test]
    public async Task FetchAll_LogsSqlAndCompletion()
    {
        await using var db = new TestDbContext(_connection);

        await db.Users()
            .Select(u => (u.UserId, u.UserName))
            .ExecuteFetchAllAsync();

        var queryEntries = _sink.Entries
            .Where(e => e.Category == "Quarry.Query")
            .ToList();

        Assert.That(queryEntries, Has.Count.GreaterThanOrEqualTo(2));

        // First entry: SQL generated
        Assert.That(queryEntries[0].Level, Is.EqualTo(LogLevel.Debug));
        Assert.That(queryEntries[0].Message, Does.Contain("SQL:"));
        Assert.That(queryEntries[0].Message, Does.Contain("SELECT"));

        // Second entry: completion with row count
        Assert.That(queryEntries[1].Level, Is.EqualTo(LogLevel.Debug));
        Assert.That(queryEntries[1].Message, Does.Contain("Fetched"));
        Assert.That(queryEntries[1].Message, Does.Contain("3"));
    }

    [Test]
    public async Task FetchAll_AllEntriesShareSameOpId()
    {
        await using var db = new TestDbContext(_connection);

        await db.Users()
            .Select(u => u.UserName)
            .ExecuteFetchAllAsync();

        var queryEntries = _sink.Entries
            .Where(e => e.Category == "Quarry.Query")
            .ToList();

        Assert.That(queryEntries, Has.Count.GreaterThanOrEqualTo(2));

        var opIds = queryEntries.Select(e => ExtractOpId(e.Message)).Distinct().ToList();
        Assert.That(opIds, Has.Count.EqualTo(1), "All entries from one operation should share the same opId");
    }

    [Test]
    public async Task TwoQueries_GetDistinctOpIds()
    {
        await using var db = new TestDbContext(_connection);

        await db.Users().Select(u => u.UserName).ExecuteFetchAllAsync();
        await db.Users().Select(u => u.UserId).ExecuteFetchAllAsync();

        var queryEntries = _sink.Entries
            .Where(e => e.Category == "Quarry.Query")
            .ToList();

        // Extract opIds from the SQL log entries (first entry per query)
        var sqlEntries = queryEntries.Where(e => e.Message.Contains("SQL:")).ToList();
        Assert.That(sqlEntries, Has.Count.EqualTo(2));

        var opId1 = ExtractOpId(sqlEntries[0].Message);
        var opId2 = ExtractOpId(sqlEntries[1].Message);
        Assert.That(opId1, Is.Not.EqualTo(opId2));
    }

    [Test]
    public async Task FetchFirst_LogsCompletion()
    {
        await using var db = new TestDbContext(_connection);

        await db.Users()
            .Select(u => u.UserName)
            .ExecuteFetchFirstAsync();

        var completionEntry = _sink.Entries
            .FirstOrDefault(e => e.Category == "Quarry.Query" && e.Message.Contains("Fetched"));

        Assert.That(completionEntry, Is.Not.Null);
        Assert.That(completionEntry!.Message, Does.Contain("1"));
    }

    [Test]
    public async Task FetchFirstOrDefault_NoRows_LogsZeroCount()
    {
        await using var db = new TestDbContext(_connection);

        await db.Users()
            .Where(u => u.UserId == -999)
            .Select(u => u.UserName)
            .ExecuteFetchFirstOrDefaultAsync();

        var completionEntry = _sink.Entries
            .FirstOrDefault(e => e.Category == "Quarry.Query" && e.Message.Contains("Fetched"));

        Assert.That(completionEntry, Is.Not.Null);
        Assert.That(completionEntry!.Message, Does.Contain("0"));
    }

    [Test]
    public async Task ExecuteScalar_LogsResult()
    {
        await using var db = new TestDbContext(_connection);

        await db.Users()
            .Select(u => Sql.Count())
            .ExecuteScalarAsync<int>();

        var scalarEntry = _sink.Entries
            .FirstOrDefault(e => e.Category == "Quarry.Query" && e.Message.Contains("Scalar result"));

        Assert.That(scalarEntry, Is.Not.Null);
        Assert.That(scalarEntry!.Message, Does.Contain("3"));
    }

    #endregion

    #region Raw SQL Modification Logging (Quarry.RawSql)

    [Test]
    public async Task RawInsert_LogsSqlAndCompletion()
    {
        await using var db = new TestDbContext(_connection);

        await db.RawSqlNonQueryAsync(
            "INSERT INTO \"users\" (\"UserName\", \"IsActive\", \"CreatedAt\") VALUES (@p0, @p1, @p2)",
            "TestInsertLog", 1, "2024-01-01 00:00:00");

        var rawEntries = _sink.Entries
            .Where(e => e.Category == "Quarry.RawSql")
            .ToList();

        Assert.That(rawEntries, Has.Count.GreaterThanOrEqualTo(2));
        Assert.That(rawEntries[0].Message, Does.Contain("SQL:"));
        Assert.That(rawEntries[0].Message, Does.Contain("INSERT"));
        Assert.That(rawEntries[1].Message, Does.Contain("NonQuery"));
        Assert.That(rawEntries[1].Message, Does.Contain("affected"));

        // Clean up
        await ExecuteSqlAsync("DELETE FROM \"users\" WHERE \"UserName\" = 'TestInsertLog'");
    }

    [Test]
    public async Task RawUpdate_LogsSqlAndCompletion()
    {
        await using var db = new TestDbContext(_connection);

        await db.RawSqlNonQueryAsync(
            "UPDATE \"users\" SET \"Email\" = @p0 WHERE \"UserId\" = @p1",
            "updated@test.com", 3);

        var rawEntries = _sink.Entries
            .Where(e => e.Category == "Quarry.RawSql")
            .ToList();

        Assert.That(rawEntries, Has.Count.GreaterThanOrEqualTo(2));
        Assert.That(rawEntries[0].Message, Does.Contain("UPDATE"));
        Assert.That(rawEntries[1].Message, Does.Contain("NonQuery"));

        // Clean up
        await ExecuteSqlAsync("UPDATE \"users\" SET \"Email\" = 'charlie@test.com' WHERE \"UserId\" = 3");
    }

    [Test]
    public async Task RawDelete_LogsSqlAndCompletion()
    {
        await ExecuteSqlAsync("""
            INSERT INTO "users" ("UserId", "UserName", "IsActive", "CreatedAt")
            VALUES (100, 'ToDelete', 1, '2024-01-01 00:00:00')
            """);
        _sink.Clear();

        await using var db = new TestDbContext(_connection);

        await db.RawSqlNonQueryAsync(
            "DELETE FROM \"users\" WHERE \"UserId\" = @p0", 100);

        var rawEntries = _sink.Entries
            .Where(e => e.Category == "Quarry.RawSql")
            .ToList();

        Assert.That(rawEntries, Has.Count.GreaterThanOrEqualTo(2));
        Assert.That(rawEntries[0].Message, Does.Contain("DELETE"));
        Assert.That(rawEntries[1].Message, Does.Contain("NonQuery"));
    }

    #endregion

    #region Raw SQL Logging (Quarry.RawSql)

    [Test]
    public async Task RawSqlNonQuery_LogsSqlAndCompletion()
    {
        await using var db = new TestDbContext(_connection);

        await db.RawSqlNonQueryAsync("SELECT 1");

        var rawEntries = _sink.Entries
            .Where(e => e.Category == "Quarry.RawSql")
            .ToList();

        Assert.That(rawEntries, Has.Count.GreaterThanOrEqualTo(2));
        Assert.That(rawEntries[0].Message, Does.Contain("SQL:"));
        Assert.That(rawEntries[0].Message, Does.Contain("SELECT 1"));
        Assert.That(rawEntries[1].Message, Does.Contain("NonQuery"));
    }

    [Test]
    public async Task RawSqlScalar_LogsResult()
    {
        await using var db = new TestDbContext(_connection);

        await db.RawSqlScalarAsync<int>("SELECT COUNT(*) FROM \"users\"");

        var scalarEntry = _sink.Entries
            .FirstOrDefault(e => e.Category == "Quarry.RawSql" && e.Message.Contains("Scalar result"));

        Assert.That(scalarEntry, Is.Not.Null);
        Assert.That(scalarEntry!.Message, Does.Contain("3"));
    }

    #endregion

    #region Connection Logging (Quarry.Connection)

    [Test]
    public async Task ClosedConnection_LogsOpened()
    {
        await using var freshConnection = new SqliteConnection("Data Source=:memory:");
        // Connection is closed — opening should be logged
        await using var db = new TestDbContext(freshConnection);

        await db.RawSqlNonQueryAsync("SELECT 1");

        var connectionEntries = _sink.Entries
            .Where(e => e.Category == "Quarry.Connection")
            .ToList();

        Assert.That(connectionEntries.Any(e => e.Message.Contains("opened")), Is.True);
        Assert.That(connectionEntries.First(e => e.Message.Contains("opened")).Level,
            Is.EqualTo(LogLevel.Information));
    }

    [Test]
    public async Task PreOpenedConnection_DoesNotLogOpened()
    {
        // _connection is already open from OneTimeSetUp
        await using var db = new TestDbContext(_connection);

        await db.RawSqlNonQueryAsync("SELECT 1");

        var openedEntries = _sink.Entries
            .Where(e => e.Category == "Quarry.Connection" && e.Message.Contains("opened"))
            .ToList();

        Assert.That(openedEntries, Is.Empty);
    }

    [Test]
    public async Task ClosedConnection_Dispose_LogsClosed()
    {
        // Create a context with a closed connection, do work (opens it), then dispose (closes it)
        var conn = new SqliteConnection("Data Source=:memory:");
        var db = new TestDbContext(conn);

        await db.RawSqlNonQueryAsync("SELECT 1");

        _sink.Clear();
        db.Dispose();

        var closedEntries = _sink.Entries
            .Where(e => e.Category == "Quarry.Connection" && e.Message.Contains("closed"))
            .ToList();

        Assert.That(closedEntries, Has.Count.EqualTo(1));
        Assert.That(closedEntries[0].Level, Is.EqualTo(LogLevel.Information));

        conn.Dispose();
    }

    #endregion

    #region Parameter Logging (Quarry.Parameters)

    [Test]
    public async Task QueryWithParameters_LogsParameterValues()
    {
        await using var db = new TestDbContext(_connection);

        // Use a captured variable so the interceptor generates a parameterized query
        var userId = 1;
        await db.Users()
            .Where(u => u.UserId == userId)
            .Select(u => u.UserName)
            .ExecuteFetchAllAsync();

        var paramEntries = _sink.Entries
            .Where(e => e.Category == "Quarry.Parameters")
            .ToList();

        Assert.That(paramEntries, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(paramEntries[0].Level, Is.EqualTo(LogLevel.Trace));
        Assert.That(paramEntries[0].Message, Does.Contain("@p0"));
        Assert.That(paramEntries[0].Message, Does.Contain("1"));
    }

    [Test]
    public async Task QueryWithMultipleParameters_LogsAllParameters()
    {
        await using var db = new TestDbContext(_connection);

        await db.RawSqlNonQueryAsync(
            "SELECT * FROM \"users\" WHERE \"UserId\" = @p0 AND \"UserName\" = @p1",
            1, "Alice");

        var paramEntries = _sink.Entries
            .Where(e => e.Category == "Quarry.Parameters")
            .ToList();

        Assert.That(paramEntries, Has.Count.EqualTo(2));
        Assert.That(paramEntries[0].Message, Does.Contain("@p0"));
        Assert.That(paramEntries[0].Message, Does.Contain("1"));
        Assert.That(paramEntries[1].Message, Does.Contain("@p1"));
        Assert.That(paramEntries[1].Message, Does.Contain("Alice"));
    }

    [Test]
    public async Task ParameterLogs_ShareOpIdWithQueryLog()
    {
        await using var db = new TestDbContext(_connection);

        // Use a captured variable so the interceptor generates a parameterized query
        var userId = 1;
        await db.Users()
            .Where(u => u.UserId == userId)
            .Select(u => u.UserName)
            .ExecuteFetchAllAsync();

        var queryEntry = _sink.Entries
            .First(e => e.Category == "Quarry.Query" && e.Message.Contains("SQL:"));
        var paramEntry = _sink.Entries
            .First(e => e.Category == "Quarry.Parameters");

        var queryOpId = ExtractOpId(queryEntry.Message);
        var paramOpId = ExtractOpId(paramEntry.Message);

        Assert.That(queryOpId, Is.EqualTo(paramOpId));
    }

    #endregion

    #region Slow Query Detection (Quarry.Execution)

    [Test]
    public async Task SlowQueryThresholdZero_EmitsWarning()
    {
        await using var db = new TestDbContext(_connection);
        db.SlowQueryThreshold = TimeSpan.Zero; // Everything is "slow"

        await db.Users()
            .Select(u => u.UserName)
            .ExecuteFetchAllAsync();

        var slowEntries = _sink.Entries
            .Where(e => e.Category == "Quarry.Execution" && e.Level == LogLevel.Warning)
            .ToList();

        Assert.That(slowEntries, Has.Count.EqualTo(1));
        Assert.That(slowEntries[0].Message, Does.Contain("Slow query"));
    }

    [Test]
    public async Task SlowQueryThresholdNull_NoWarning()
    {
        await using var db = new TestDbContext(_connection);
        db.SlowQueryThreshold = null;

        await db.Users()
            .Select(u => u.UserName)
            .ExecuteFetchAllAsync();

        var slowEntries = _sink.Entries
            .Where(e => e.Category == "Quarry.Execution")
            .ToList();

        Assert.That(slowEntries, Is.Empty);
    }

    [Test]
    public async Task SlowQueryThresholdLarge_NoWarningForFastQuery()
    {
        await using var db = new TestDbContext(_connection);
        db.SlowQueryThreshold = TimeSpan.FromMinutes(10);

        await db.Users()
            .Select(u => u.UserName)
            .ExecuteFetchAllAsync();

        var slowEntries = _sink.Entries
            .Where(e => e.Category == "Quarry.Execution")
            .ToList();

        Assert.That(slowEntries, Is.Empty);
    }

    [Test]
    public async Task SlowQueryWarning_ContainsElapsedTimeAndSql()
    {
        await using var db = new TestDbContext(_connection);
        db.SlowQueryThreshold = TimeSpan.Zero;

        await db.Users()
            .Select(u => u.UserName)
            .ExecuteFetchAllAsync();

        var slowEntry = _sink.Entries
            .First(e => e.Category == "Quarry.Execution" && e.Level == LogLevel.Warning);

        Assert.That(slowEntry.Message, Does.Contain("ms"));
        Assert.That(slowEntry.Message, Does.Contain("SELECT"));
    }

    #endregion

    #region Level Gating

    [Test]
    public async Task MinimumLevelWarning_SuppressesDebugLogs()
    {
        ReconfigureLogging(c =>
        {
            c.MinimumLevel = LogLevel.Warning;
            c.AddSink(_sink);
        });

        try
        {
            await using var db = new TestDbContext(_connection);
            await db.Users().Select(u => u.UserName).ExecuteFetchAllAsync();

            var debugEntries = _sink.Entries
                .Where(e => e.Level == LogLevel.Debug)
                .ToList();

            Assert.That(debugEntries, Is.Empty, "Debug entries should be suppressed at Warning minimum level");
        }
        finally
        {
            ResetLogging();
        }
    }

    [Test]
    public async Task CategoryLevelNone_SuppressesQueryLogsOnly()
    {
        ReconfigureLogging(c =>
        {
            c.MinimumLevel = LogLevel.Trace;
            c.SetMinimumLevel("Quarry.Query", LogLevel.None);
            c.AddSink(_sink);
        });

        try
        {
            await using var db = new TestDbContext(_connection);
            db.SlowQueryThreshold = TimeSpan.Zero; // Force slow query warning

            await db.Users().Select(u => u.UserName).ExecuteFetchAllAsync();

            var queryEntries = _sink.Entries
                .Where(e => e.Category == "Quarry.Query")
                .ToList();

            var executionEntries = _sink.Entries
                .Where(e => e.Category == "Quarry.Execution")
                .ToList();

            Assert.That(queryEntries, Is.Empty, "Query logs should be suppressed");
            Assert.That(executionEntries, Is.Not.Empty, "Execution logs should still work");
        }
        finally
        {
            ResetLogging();
        }
    }

    [Test]
    public async Task CategoryLevelDebug_SuppressesTraceParameterLogs()
    {
        ReconfigureLogging(c =>
        {
            c.MinimumLevel = LogLevel.Debug;
            c.AddSink(_sink);
        });

        try
        {
            await using var db = new TestDbContext(_connection);

            // Use a captured variable so the interceptor generates a parameterized query
            var userId = 1;
            await db.Users()
                .Where(u => u.UserId == userId)
                .Select(u => u.UserName)
                .ExecuteFetchAllAsync();

            var paramEntries = _sink.Entries
                .Where(e => e.Category == "Quarry.Parameters")
                .ToList();

            var queryEntries = _sink.Entries
                .Where(e => e.Category == "Quarry.Query")
                .ToList();

            Assert.That(paramEntries, Is.Empty, "Trace-level parameter logs should be suppressed at Debug minimum");
            Assert.That(queryEntries, Is.Not.Empty, "Debug-level query logs should still work");
        }
        finally
        {
            ResetLogging();
        }
    }

    #endregion

    #region Async Enumerable Logging

    [Test]
    public async Task ToAsyncEnumerable_LogsSqlAndCompletion()
    {
        await using var db = new TestDbContext(_connection);

        var count = 0;
        await foreach (var name in db.Users().Select(u => u.UserName).ToAsyncEnumerable())
        {
            count++;
        }

        var queryEntries = _sink.Entries
            .Where(e => e.Category == "Quarry.Query")
            .ToList();

        Assert.That(queryEntries, Has.Count.GreaterThanOrEqualTo(2));
        Assert.That(queryEntries[0].Message, Does.Contain("SQL:"));

        var completionEntry = queryEntries.FirstOrDefault(e => e.Message.Contains("Fetched"));
        Assert.That(completionEntry, Is.Not.Null);
        Assert.That(completionEntry!.Message, Does.Contain(count.ToString()));
    }

    #endregion
}
