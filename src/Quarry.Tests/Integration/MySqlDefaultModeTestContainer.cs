using System;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;
using Testcontainers.MySql;

namespace Quarry.Tests.Integration;

/// <summary>
/// Sibling of <see cref="MySqlTestContainer"/> that boots a MySQL 8.4 container
/// with the stock <c>sql_mode</c> — specifically WITHOUT <c>NO_BACKSLASH_ESCAPES</c>.
/// Used by <see cref="MySqlBackslashEscapesIntegrationTests"/> to prove the
/// generator's MySqlBackslashEscapes = true emit path produces SQL that parses
/// correctly on real default-mode MySQL.
/// </summary>
/// <remarks>
/// <para>
/// Kept minimal: a single <c>users</c> table with three seeded rows is enough
/// for the focused LIKE-escape regression tests. The main test suite uses
/// <see cref="MySqlTestContainer"/> with <c>NO_BACKSLASH_ESCAPES</c> set, which
/// remains the default coverage path. This container exists solely to verify
/// the default-<c>sql_mode</c> emit shape works against a real MySQL server.
/// </para>
/// </remarks>
internal static class MySqlDefaultModeTestContainer
{
    internal const string DatabaseName = "quarry_test_default";

    private static readonly SemaphoreSlim _containerLock = new(1, 1);
    private static readonly SemaphoreSlim _baselineLock = new(1, 1);
    private static MySqlContainer? _container;
    private static bool _baselineReady;
    private static string? _dockerUnavailableReason;

    public static async Task<string> GetConnectionStringAsync()
    {
        var container = await GetContainerAsync();
        return container.GetConnectionString();
    }

    public static async Task<MySqlContainer> GetContainerAsync()
    {
        if (_dockerUnavailableReason is not null)
            Assert.Ignore(_dockerUnavailableReason);

        if (_container is not null)
            return _container;

        await _containerLock.WaitAsync();
        try
        {
            if (_dockerUnavailableReason is not null)
                Assert.Ignore(_dockerUnavailableReason);

            if (_container is null)
            {
                try
                {
                    // Stock MySQL 8.4 sql_mode: ONLY_FULL_GROUP_BY, STRICT_TRANS_TABLES,
                    // NO_ZERO_IN_DATE, NO_ZERO_DATE, ERROR_FOR_DIVISION_BY_ZERO,
                    // NO_ENGINE_SUBSTITUTION. Note the explicit absence of
                    // NO_BACKSLASH_ESCAPES — backslash IS a string-literal escape
                    // character on this container, exactly as on a default-config
                    // production MySQL server.
                    var container = new MySqlBuilder("mysql:8.4")
                        .WithDatabase(DatabaseName)
                        .WithCommand(
                            "--character-set-server=utf8mb4",
                            "--collation-server=utf8mb4_bin",
                            "--sql-mode=ONLY_FULL_GROUP_BY,STRICT_TRANS_TABLES,NO_ZERO_IN_DATE,NO_ZERO_DATE,ERROR_FOR_DIVISION_BY_ZERO,NO_ENGINE_SUBSTITUTION")
                        .Build();
                    await container.StartAsync();
                    _container = container;
                }
                catch (Exception ex) when (IsDockerUnavailable(ex))
                {
                    _dockerUnavailableReason =
                        "Docker is not available on this machine — MySQL-backed tests cannot run. " +
                        "Install Docker Desktop (Windows/macOS) or a Docker engine (Linux) to run " +
                        "the Quarry test suite. " +
                        $"Underlying error: {ex.GetType().Name}: {ex.Message}";
                    Assert.Ignore(_dockerUnavailableReason);
                }
            }
            return _container!;
        }
        finally
        {
            _containerLock.Release();
        }
    }

    /// <summary>
    /// Creates the <c>users</c> table with a small set of rows containing
    /// LIKE-meta characters (<c>_</c>, <c>%</c>) and a literal backslash in
    /// the <c>UserName</c> column. Idempotent and safe to call concurrently.
    /// </summary>
    public static async Task EnsureBaselineAsync()
    {
        if (_baselineReady)
            return;

        await _baselineLock.WaitAsync();
        try
        {
            if (_baselineReady)
                return;

            var cs = await GetConnectionStringAsync();
            await using var conn = new MySqlConnection(cs);
            await conn.OpenAsync();

            const string LockName = "quarry_test_default_baseline";
            await ExecAsync(conn, $"SELECT GET_LOCK('{LockName}', 60);");
            try
            {
                var alreadyReady = await TableExistsAsync(conn, DatabaseName, "users");
                if (!alreadyReady)
                {
                    await ExecAsync(conn, $"USE `{DatabaseName}`;");
                    await ExecAsync(conn, @"CREATE TABLE `users` (
                        `UserId` INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                        `UserName` TEXT NOT NULL
                    );");
                    // Three rows. UserName values exercise:
                    //   - underscore (LIKE meta): "user_name"
                    //   - percent    (LIKE meta): "50% off"
                    //   - backslash  (string-literal escape on default sql_mode): "a\b"
                    // The SeedData helper writes these via parameter binding so the
                    // backslash arrives in the column value as a literal backslash
                    // regardless of the server's sql_mode setting.
                    await using var seed = conn.CreateCommand();
                    seed.CommandText = @"INSERT INTO `users` (`UserId`, `UserName`) VALUES
                        (1, @u1),
                        (2, @u2),
                        (3, @u3);";
                    var p1 = seed.CreateParameter(); p1.ParameterName = "@u1"; p1.Value = "user_name"; seed.Parameters.Add(p1);
                    var p2 = seed.CreateParameter(); p2.ParameterName = "@u2"; p2.Value = "50% off"; seed.Parameters.Add(p2);
                    var p3 = seed.CreateParameter(); p3.ParameterName = "@u3"; p3.Value = "a\\b";    seed.Parameters.Add(p3);
                    await seed.ExecuteNonQueryAsync();
                }
            }
            finally
            {
                await ExecAsync(conn, $"SELECT RELEASE_LOCK('{LockName}');");
            }

            _baselineReady = true;
        }
        finally
        {
            _baselineLock.Release();
        }
    }

    private static async Task<bool> TableExistsAsync(MySqlConnection conn, string database, string table)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT 1 FROM information_schema.tables WHERE table_schema = @db AND table_name = @tb";
        var p1 = cmd.CreateParameter(); p1.ParameterName = "@db"; p1.Value = database; cmd.Parameters.Add(p1);
        var p2 = cmd.CreateParameter(); p2.ParameterName = "@tb"; p2.Value = table;    cmd.Parameters.Add(p2);
        var result = await cmd.ExecuteScalarAsync();
        return result is not null && result is not DBNull;
    }

    private static async Task ExecAsync(MySqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static bool IsDockerUnavailable(Exception ex)
    {
        for (var cur = ex; cur is not null; cur = cur.InnerException!)
        {
            var typeName = cur.GetType().FullName ?? "";
            var message = cur.Message ?? "";
            if (typeName.Contains("Docker", StringComparison.Ordinal) ||
                typeName.Contains("Testcontainers", StringComparison.Ordinal))
                return true;
            if (message.Contains("docker", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("daemon", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("named pipe", StringComparison.OrdinalIgnoreCase))
                return true;
            if (cur.InnerException is null) break;
        }
        return false;
    }
}
