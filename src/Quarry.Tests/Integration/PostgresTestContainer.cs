using System;
using System.Threading;
using System.Threading.Tasks;
using Testcontainers.PostgreSql;

namespace Quarry.Tests.Integration;

/// <summary>
/// Single PostgreSQL 17 container shared across the entire test run.
/// Tests get a connection string from <see cref="GetConnectionStringAsync"/>;
/// the container is started lazily on first access and reused until the
/// test process exits.
/// </summary>
/// <remarks>
/// Centralised here so that all integration tests route through the same
/// container instance — starting one PG container per test class would add
/// multi-second overhead per class. Works across multiple test assemblies
/// because the state lives in this type's static field, scoped to the
/// process that loads the assembly (NUnit runs all tests in one process
/// unless configured otherwise).
/// </remarks>
internal static class PostgresTestContainer
{
    private static readonly SemaphoreSlim _lock = new(1, 1);
    private static PostgreSqlContainer? _container;

    /// <summary>
    /// Returns a connection string pointing at the shared container. Starts
    /// the container on first call; subsequent calls reuse the running one.
    /// Thread-safe.
    /// </summary>
    public static async Task<string> GetConnectionStringAsync()
    {
        var container = await GetContainerAsync();
        return container.GetConnectionString();
    }

    /// <summary>
    /// Returns the shared container itself, for integration tests that need
    /// to inspect its state (ports, etc.). Prefer <see cref="GetConnectionStringAsync"/>
    /// for ordinary test setup.
    /// </summary>
    public static async Task<PostgreSqlContainer> GetContainerAsync()
    {
        if (_container is not null)
            return _container;

        await _lock.WaitAsync();
        try
        {
            if (_container is null)
            {
                var container = new PostgreSqlBuilder("postgres:17-alpine").Build();
                await container.StartAsync();
                _container = container;
            }
            return _container;
        }
        finally
        {
            _lock.Release();
        }
    }
}
