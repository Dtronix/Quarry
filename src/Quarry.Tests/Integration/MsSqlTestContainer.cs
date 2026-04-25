using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace Quarry.Tests.Integration;

/// <summary>
/// Single SQL Server 2022 container shared across the entire test run, plus the
/// DDL + seed helpers that <see cref="QueryTestHarness"/> uses to materialize
/// either the shared <c>quarry_test</c> baseline schema or a per-test opt-out
/// schema.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <see cref="PostgresTestContainer"/>'s shape so a single execution
/// pattern covers both real-provider integration paths. SQL Server lacks
/// <c>SET search_path</c>, so unqualified table references from
/// <see cref="Samples.Ss.SsDb"/> are resolved by connecting as
/// <c>quarry_test_user</c> — a login whose mapped database user has
/// <c>DEFAULT_SCHEMA = quarry_test</c>. The shared container's
/// <c>sa</c> credentials stay reserved for one-time setup work that needs
/// elevated privileges (CREATE LOGIN/USER, CREATE SCHEMA, drops).
/// </para>
/// <para>
/// MS SQL containers cold-start in ~20-30s versus PostgreSQL's ~5s. The lazy
/// singleton amortises the cost across the whole test run; the
/// <c>sp_getapplock</c> baseline gate keeps concurrent test processes from
/// double-running setup.
/// </para>
/// <para>
/// Phase 1 stub: container boot + Docker-unavailable handling are wired up;
/// schema DDL, seed data, and login provisioning are deferred to Phase 2.
/// </para>
/// </remarks>
internal static class MsSqlTestContainer
{
    internal const string BaselineSchemaName = "quarry_test";
    internal const string TestUserName = "quarry_test_user";

    /// <summary>
    /// Fixed test password. SQL Server enforces a default password-complexity
    /// policy on logins; we bypass that with <c>CHECK_POLICY = OFF</c> in the
    /// CREATE LOGIN call so this constant is durable across container
    /// rebuilds. The password value never escapes the test process.
    /// </summary>
    internal const string TestUserPassword = "Quarry-Test-2026!";

    private static readonly SemaphoreSlim _containerLock = new(1, 1);
    private static MsSqlContainer? _container;
    private static string? _dockerUnavailableReason;

    /// <summary>
    /// Returns a connection string authenticated as the <c>sa</c> superuser.
    /// Reserved for setup/teardown work — CREATE SCHEMA, CREATE LOGIN/USER,
    /// owned-schema provisioning. Test harness queries should use
    /// <see cref="GetUserConnectionStringAsync"/> instead.
    /// </summary>
    public static async Task<string> GetSaConnectionStringAsync()
    {
        var container = await GetContainerAsync();
        return container.GetConnectionString();
    }

    /// <summary>
    /// Returns a connection string authenticated as <c>quarry_test_user</c>,
    /// whose default schema is <see cref="BaselineSchemaName"/>. The user is
    /// provisioned on first call to <see cref="EnsureBaselineAsync"/>; calling
    /// this method before <see cref="EnsureBaselineAsync"/> will return a
    /// connection string but opening it will fail with
    /// <c>Login failed for user 'quarry_test_user'</c>.
    /// </summary>
    public static async Task<string> GetUserConnectionStringAsync()
    {
        var saCs = await GetSaConnectionStringAsync();
        // Testcontainers builds an sa connection string of shape
        // "Server=...;User Id=sa;Password=...;TrustServerCertificate=True". We
        // rewrite the User Id and Password fields onto the test user without
        // re-parsing — SqlConnectionStringBuilder is the safe path.
        var builder = new SqlConnectionStringBuilder(saCs)
        {
            UserID = TestUserName,
            Password = TestUserPassword,
        };
        return builder.ConnectionString;
    }

    /// <summary>
    /// Returns the shared container itself, for integration tests that need
    /// to inspect its state (ports, etc.). Prefer
    /// <see cref="GetSaConnectionStringAsync"/> /
    /// <see cref="GetUserConnectionStringAsync"/> for ordinary test setup.
    /// </summary>
    public static async Task<MsSqlContainer> GetContainerAsync()
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
                    var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
                    await container.StartAsync();
                    _container = container;
                }
                catch (Exception ex) when (IsDockerUnavailable(ex))
                {
                    // Cache the reason so later test attempts don't each pay
                    // the full container-probe timeout, and so the developer
                    // sees a single clear message across the run. Mirrors the
                    // PG-side cache.
                    _dockerUnavailableReason =
                        "Docker is not available on this machine — SQL Server-backed tests cannot run. " +
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
    /// Heuristic for "Docker is not installed / not running". Matches
    /// <see cref="PostgresTestContainer.IsDockerUnavailable"/> behaviour so
    /// both providers report the same diagnostic shape.
    /// </summary>
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

    /// <summary>
    /// Phase 1 stub. Phase 2 will implement: take <c>sp_getapplock</c>, check
    /// whether the baseline schema exists, otherwise create the schema, the
    /// <c>quarry_test_user</c> login + database user (with default schema),
    /// then run <c>CreateSchemaObjectsAsync</c> + <c>SeedDataAsync</c>.
    /// </summary>
    public static Task EnsureBaselineAsync()
        => throw new NotImplementedException(
            "Phase 2 of the SQL Server execution-mirror work will implement the baseline " +
            "schema, mapped login, and seed data. Phase 1 only verifies the container boots.");

    /// <summary>
    /// Phase 1 stub. Phase 2 will create a uniquely-named schema with the full
    /// DDL + seed plus a dedicated login whose default schema points at it.
    /// </summary>
    public static Task<string> CreateOwnedSchemaAsync(SqlConnection saConnection)
        => throw new NotImplementedException(
            "Phase 2 of the SQL Server execution-mirror work will implement the owned-schema " +
            "path. Phase 1 only verifies the container boots.");
}
