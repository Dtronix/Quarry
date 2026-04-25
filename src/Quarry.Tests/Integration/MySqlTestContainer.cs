using System;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;
using Testcontainers.MySql;

namespace Quarry.Tests.Integration;

/// <summary>
/// Single MySQL 8.4 container shared across the entire test run, plus the DDL
/// + seed helpers that <see cref="QueryTestHarness"/> uses to materialize
/// either the shared <c>quarry_test</c> baseline database or a per-test
/// opt-out database. Direct analogue of <see cref="PostgresTestContainer"/>
/// for MySQL.
/// </summary>
/// <remarks>
/// <para>
/// MySQL has no schemas-as-namespaces concept, so the PG "schema" boundary
/// becomes a "database" boundary here: the baseline lives in a single
/// <c>quarry_test</c> database, and tests opt-in to a per-test database via
/// <see cref="CreateOwnedDatabaseAsync"/>.
/// </para>
/// <para>
/// The baseline schema is created exactly once per test process in
/// <see cref="EnsureBaselineAsync"/>, gated by a MySQL <c>GET_LOCK</c> for
/// cross-process safety (mirrors PG's <c>pg_advisory_lock</c> path). Each
/// harness opens its own <see cref="MySqlConnection"/> from MySqlConnector's
/// pool, switches to that database, and wraps the test in a transaction that
/// rolls back at dispose — giving per-test isolation without per-test DDL
/// cost.
/// </para>
/// <para>
/// This file ships in two phases. Phase 1 (this commit) provides the
/// container lifetime + Docker-unavailable handling so the bootstrap can be
/// proven on CI. Phase 2 fills in the DDL port and seed helpers.
/// </para>
/// </remarks>
internal static class MySqlTestContainer
{
    internal const string BaselineDatabaseName = "quarry_test";

    private static readonly SemaphoreSlim _containerLock = new(1, 1);
    private static MySqlContainer? _container;
    private static string? _dockerUnavailableReason;

    /// <summary>
    /// Returns a connection string pointing at the shared container, with the
    /// default database set to <see cref="BaselineDatabaseName"/>. Starts the
    /// container on first call; subsequent calls reuse the running one.
    /// Thread-safe.
    /// </summary>
    public static async Task<string> GetConnectionStringAsync()
    {
        var container = await GetContainerAsync();
        return container.GetConnectionString();
    }

    /// <summary>
    /// Returns the shared container itself, for integration tests that need
    /// to inspect its state (ports, etc.). Prefer
    /// <see cref="GetConnectionStringAsync"/> for ordinary test setup.
    /// </summary>
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
                    var container = new MySqlBuilder("mysql:8.4")
                        .WithDatabase(BaselineDatabaseName)
                        .Build();
                    await container.StartAsync();
                    _container = container;
                }
                catch (Exception ex) when (IsDockerUnavailable(ex))
                {
                    // Cache the reason so later test attempts don't each pay
                    // the full container-probe timeout, and so the developer
                    // sees a single clear message across the run.
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
    /// Heuristic for "Docker is not installed / not running" — Testcontainers
    /// surfaces a handful of different exception types depending on which
    /// probe failed. Mirrors the PG-side heuristic byte-for-byte.
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
}
