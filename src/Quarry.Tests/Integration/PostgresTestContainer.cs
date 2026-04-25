using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Quarry.Tests.Integration;

/// <summary>
/// Single PostgreSQL 17 container shared across the entire test run, plus the
/// DDL + seed helpers that <see cref="QueryTestHarness"/> uses to materialize
/// either the shared <c>quarry_test</c> baseline schema or a per-test opt-out
/// schema.
/// </summary>
/// <remarks>
/// <para>
/// Centralised here so that all integration tests route through the same
/// container instance — starting one PG container per test class would add
/// multi-second overhead per class. Works across multiple test assemblies
/// because the state lives in this type's static fields, scoped to the
/// process that loads the assembly (NUnit runs all tests in one process
/// unless configured otherwise).
/// </para>
/// <para>
/// The baseline schema (<c>quarry_test</c>) is created exactly once per test
/// process in <see cref="EnsureBaselineAsync"/>. Each harness opens its own
/// <see cref="NpgsqlConnection"/> from Npgsql's pool, sets
/// <c>search_path</c> to that schema, and wraps the test in a transaction
/// that rolls back at dispose — giving per-test isolation without per-test
/// DDL cost. Tests that need their own schema (migration runner, transaction-
/// behaviour tests) call <see cref="CreateOwnedSchemaAsync"/> instead.
/// </para>
/// </remarks>
internal static class PostgresTestContainer
{
    internal const string BaselineSchemaName = "quarry_test";

    private static readonly SemaphoreSlim _containerLock = new(1, 1);
    private static readonly SemaphoreSlim _baselineLock = new(1, 1);
    private static PostgreSqlContainer? _container;
    private static bool _baselineReady;
    private static string? _dockerUnavailableReason;

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
                    var container = new PostgreSqlBuilder("postgres:17-alpine").Build();
                    await container.StartAsync();
                    _container = container;
                }
                catch (Exception ex) when (IsDockerUnavailable(ex))
                {
                    // Cache the reason so later test attempts don't each pay
                    // the full container-probe timeout, and so the developer
                    // sees a single clear message across the run.
                    _dockerUnavailableReason =
                        "Docker is not available on this machine — PG-backed tests cannot run. " +
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
    /// probe failed. The common signals: exception type from the ducktyped
    /// Docker client (Docker.DotNet), or a message mentioning the Docker
    /// endpoint / daemon not being reachable.
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
    /// Creates the shared <c>quarry_test</c> schema with all tables + seed
    /// data, idempotently and only once per test process. Safe to call from
    /// many concurrent harness constructors.
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
            await using var conn = new NpgsqlConnection(cs);
            await conn.OpenAsync();

            // Use a PostgreSQL advisory lock to make the baseline setup
            // safe across concurrent test processes sharing one container.
            // The magic number is arbitrary but must be stable across
            // processes; any int64 will do. `pg_advisory_lock` blocks until
            // we hold the lock; `pg_advisory_unlock` releases it at the end.
            // Any other process entering EnsureBaselineAsync concurrently
            // will wait here and then observe the ready schema.
            const long BaselineLockKey = 0x51554152_52595445L; // 'QUARRYTE'
            await ExecAsync(conn, $"SELECT pg_advisory_lock({BaselineLockKey});");
            try
            {
                // Probe whether the baseline is already populated — if the
                // `users` table exists in the baseline schema another process
                // already ran setup and seed; don't repeat the work.
                var alreadyReady = await TableExistsAsync(conn, BaselineSchemaName, "users");
                if (!alreadyReady)
                {
                    await ExecAsync(conn, $"CREATE SCHEMA IF NOT EXISTS \"{BaselineSchemaName}\";");
                    await CreateSchemaObjectsAsync(conn, BaselineSchemaName);
                    await SeedDataAsync(conn, BaselineSchemaName);
                }
            }
            finally
            {
                await ExecAsync(conn, $"SELECT pg_advisory_unlock({BaselineLockKey});");
            }

            _baselineReady = true;
        }
        finally
        {
            _baselineLock.Release();
        }
    }

    private static async Task<bool> TableExistsAsync(NpgsqlConnection conn, string schema, string table)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT 1 FROM information_schema.tables WHERE table_schema = @schema AND table_name = @table";
        var p1 = cmd.CreateParameter(); p1.ParameterName = "schema"; p1.Value = schema; cmd.Parameters.Add(p1);
        var p2 = cmd.CreateParameter(); p2.ParameterName = "table";  p2.Value = table;  cmd.Parameters.Add(p2);
        var result = await cmd.ExecuteScalarAsync();
        return result is not null && result is not DBNull;
    }

    /// <summary>
    /// Creates a uniquely-named schema with the full DDL + seed, for a single
    /// harness instance that needs its own isolated schema (e.g. transactional
    /// tests, migration runner tests). Returns the schema name so the caller
    /// can <c>DROP SCHEMA ... CASCADE</c> on dispose.
    /// </summary>
    public static async Task<string> CreateOwnedSchemaAsync(NpgsqlConnection connection)
    {
        var schema = "test_" + Guid.NewGuid().ToString("N").Substring(0, 12);
        await ExecAsync(connection, $"CREATE SCHEMA \"{schema}\";");
        await CreateSchemaObjectsAsync(connection, schema);
        await SeedDataAsync(connection, schema);
        return schema;
    }

    private static async Task ExecAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// DDL port of <see cref="QueryTestHarness.CreateSchema"/> for PostgreSQL.
    /// Differences vs the SQLite source:
    /// <list type="bullet">
    ///   <item><description>Primary keys use <c>GENERATED BY DEFAULT AS IDENTITY</c> so seed rows can supply explicit IDs and later inserts auto-generate.</description></item>
    ///   <item><description>Money / decimal-backed columns (Quarry schemas declare <c>Col&lt;decimal&gt;</c> or <c>Col&lt;Money&gt;</c>→decimal) use <c>NUMERIC(18, 2)</c>. SQLite's <c>REAL</c> was lenient for decimal materialisation; PG's Npgsql provider refuses to read <c>double precision</c> as <c>System.Decimal</c>, so the PG column must actually be <c>NUMERIC</c>.</description></item>
    ///   <item><description>All identifiers stay quoted to match the SQLite source's case-sensitive naming.</description></item>
    /// </list>
    /// </summary>
    private static async Task CreateSchemaObjectsAsync(NpgsqlConnection conn, string schema)
    {
        var q = $"\"{schema}\"";

        await ExecAsync(conn, $@"CREATE TABLE {q}.""users"" (
            ""UserId"" INTEGER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
            ""UserName"" TEXT NOT NULL,
            ""Email"" TEXT,
            ""IsActive"" INTEGER NOT NULL DEFAULT 1,
            ""CreatedAt"" TEXT NOT NULL,
            ""LastLogin"" TEXT
        );");

        await ExecAsync(conn, $@"CREATE TABLE {q}.""orders"" (
            ""OrderId"" INTEGER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
            ""UserId"" INTEGER NOT NULL,
            ""Total"" NUMERIC(18, 2) NOT NULL,
            ""Status"" TEXT NOT NULL,
            ""Priority"" INTEGER NOT NULL DEFAULT 1,
            ""OrderDate"" TEXT NOT NULL,
            ""Notes"" TEXT,
            FOREIGN KEY (""UserId"") REFERENCES {q}.""users""(""UserId"")
        );");

        await ExecAsync(conn, $@"CREATE TABLE {q}.""order_items"" (
            ""OrderItemId"" INTEGER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
            ""OrderId"" INTEGER NOT NULL,
            ""ProductName"" TEXT NOT NULL,
            ""Quantity"" INTEGER NOT NULL,
            ""UnitPrice"" NUMERIC(18, 2) NOT NULL,
            ""LineTotal"" NUMERIC(18, 2) NOT NULL,
            FOREIGN KEY (""OrderId"") REFERENCES {q}.""orders""(""OrderId"")
        );");

        await ExecAsync(conn, $@"CREATE TABLE {q}.""accounts"" (
            ""AccountId"" INTEGER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
            ""UserId"" INTEGER NOT NULL,
            ""AccountName"" TEXT NOT NULL,
            ""Balance"" NUMERIC(18, 2) NOT NULL,
            ""credit_limit"" NUMERIC(18, 2) NOT NULL DEFAULT 0,
            ""IsActive"" INTEGER NOT NULL DEFAULT 1,
            FOREIGN KEY (""UserId"") REFERENCES {q}.""users""(""UserId"")
        );");

        await ExecAsync(conn, $@"CREATE TABLE {q}.""events"" (
            ""EventId"" INTEGER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
            ""EventName"" TEXT NOT NULL,
            ""ScheduledAt"" TEXT NOT NULL,
            ""CancelledAt"" TEXT
        );");

        await ExecAsync(conn, $@"CREATE TABLE {q}.""addresses"" (
            ""AddressId"" INTEGER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
            ""City"" TEXT NOT NULL,
            ""Street"" TEXT NOT NULL,
            ""ZipCode"" TEXT
        );");

        await ExecAsync(conn, $@"CREATE TABLE {q}.""user_addresses"" (
            ""UserAddressId"" INTEGER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
            ""UserId"" INTEGER NOT NULL,
            ""AddressId"" INTEGER NOT NULL,
            FOREIGN KEY (""UserId"") REFERENCES {q}.""users""(""UserId""),
            FOREIGN KEY (""AddressId"") REFERENCES {q}.""addresses""(""AddressId"")
        );");

        await ExecAsync(conn, $@"CREATE TABLE {q}.""warehouses"" (
            ""WarehouseId"" INTEGER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
            ""WarehouseName"" TEXT NOT NULL,
            ""Region"" TEXT NOT NULL
        );");

        await ExecAsync(conn, $@"CREATE TABLE {q}.""shipments"" (
            ""ShipmentId"" INTEGER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
            ""OrderId"" INTEGER NOT NULL,
            ""WarehouseId"" INTEGER NOT NULL,
            ""ReturnWarehouseId"" INTEGER NULL,
            ""ShipDate"" TEXT NOT NULL,
            FOREIGN KEY (""OrderId"") REFERENCES {q}.""orders""(""OrderId""),
            FOREIGN KEY (""WarehouseId"") REFERENCES {q}.""warehouses""(""WarehouseId""),
            FOREIGN KEY (""ReturnWarehouseId"") REFERENCES {q}.""warehouses""(""WarehouseId"")
        );");

        await ExecAsync(conn, $@"CREATE TABLE {q}.""products"" (
            ""ProductId"" INTEGER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
            ""ProductName"" TEXT NOT NULL,
            ""Price"" NUMERIC(18, 2) NOT NULL,
            ""Description"" TEXT,
            ""DiscountedPrice"" NUMERIC(18, 2) GENERATED ALWAYS AS (""Price"" * 0.9) STORED
        );");

        await ExecAsync(conn, $@"CREATE VIEW {q}.""Order"" AS SELECT * FROM {q}.""orders"";");
    }

    /// <summary>
    /// Seed data port of <see cref="QueryTestHarness.SeedData"/>. Rows use the
    /// same explicit primary keys as the SQLite seed. After each table is
    /// seeded with explicit IDs the IDENTITY sequence is advanced past the
    /// highest seeded value so subsequent auto-generating inserts don't
    /// collide.
    /// </summary>
    private static async Task SeedDataAsync(NpgsqlConnection conn, string schema)
    {
        var q = $"\"{schema}\"";

        await ExecAsync(conn, $@"INSERT INTO {q}.""users"" (""UserId"", ""UserName"", ""Email"", ""IsActive"", ""CreatedAt"", ""LastLogin"") VALUES
            (1, 'Alice',   'alice@test.com',   1, '2024-01-15 00:00:00', '2024-06-01 00:00:00'),
            (2, 'Bob',     NULL,               1, '2024-02-20 00:00:00', NULL),
            (3, 'Charlie', 'charlie@test.com', 0, '2024-03-10 00:00:00', '2024-05-15 00:00:00');");

        await ExecAsync(conn, $@"INSERT INTO {q}.""orders"" (""OrderId"", ""UserId"", ""Total"", ""Status"", ""Priority"", ""OrderDate"", ""Notes"") VALUES
            (1, 1, 250.00, 'Shipped', 2, '2024-06-01 00:00:00', 'Express'),
            (2, 1, 75.50,  'Pending', 1, '2024-06-15 00:00:00', NULL),
            (3, 2, 150.00, 'Shipped', 3, '2024-07-01 00:00:00', NULL);");

        await ExecAsync(conn, $@"INSERT INTO {q}.""order_items"" (""OrderItemId"", ""OrderId"", ""ProductName"", ""Quantity"", ""UnitPrice"", ""LineTotal"") VALUES
            (1, 1, 'Widget', 2, 125.00, 250.00),
            (2, 2, 'Gadget', 1, 75.50,  75.50),
            (3, 3, 'Widget', 3, 50.00,  150.00);");

        await ExecAsync(conn, $@"INSERT INTO {q}.""accounts"" (""AccountId"", ""UserId"", ""AccountName"", ""Balance"", ""credit_limit"", ""IsActive"") VALUES
            (1, 1, 'Savings',  1000.50, 5000.00, 1),
            (2, 1, 'Checking', 250.75,  1000.00, 1),
            (3, 2, 'Savings',  500.00,  2000.00, 0);");

        await ExecAsync(conn, $@"INSERT INTO {q}.""events"" (""EventId"", ""EventName"", ""ScheduledAt"", ""CancelledAt"") VALUES
            (1, 'Launch', '2024-06-15 10:30:00+00:00', NULL),
            (2, 'Review', '2024-07-01 14:00:00+02:00', '2024-06-28 09:00:00+02:00');");

        await ExecAsync(conn, $@"INSERT INTO {q}.""addresses"" (""AddressId"", ""City"", ""Street"", ""ZipCode"") VALUES
            (1, 'Portland', '123 Main St', '97201'),
            (2, 'Seattle', '456 Oak Ave', '98101');");

        await ExecAsync(conn, $@"INSERT INTO {q}.""user_addresses"" (""UserAddressId"", ""UserId"", ""AddressId"") VALUES
            (1, 1, 1),
            (2, 1, 2),
            (3, 2, 1);");

        await ExecAsync(conn, $@"INSERT INTO {q}.""products"" (""ProductId"", ""ProductName"", ""Price"", ""Description"") VALUES
            (1, 'Widget',  29.99, 'A fine widget'),
            (2, 'Gadget',  49.50, NULL),
            (3, 'Doohickey', 9.95, 'Budget option');");

        await ExecAsync(conn, $@"INSERT INTO {q}.""warehouses"" (""WarehouseId"", ""WarehouseName"", ""Region"") VALUES
            (1, 'West Coast Hub', 'US'),
            (2, 'EU Central', 'EU');");

        await ExecAsync(conn, $@"INSERT INTO {q}.""shipments"" (""ShipmentId"", ""OrderId"", ""WarehouseId"", ""ReturnWarehouseId"", ""ShipDate"") VALUES
            (1, 1, 1, 2, '2024-06-02 00:00:00'),
            (2, 3, 2, NULL, '2024-07-02 00:00:00');");

        // Advance each IDENTITY sequence past the highest explicitly-seeded
        // ID. With BY DEFAULT AS IDENTITY, explicit inserts do not advance
        // the sequence — the next auto-generating insert would otherwise
        // collide with a seeded row.
        foreach (var (table, pk) in new[]
        {
            ("users", "UserId"), ("orders", "OrderId"), ("order_items", "OrderItemId"),
            ("accounts", "AccountId"), ("events", "EventId"), ("addresses", "AddressId"),
            ("user_addresses", "UserAddressId"), ("warehouses", "WarehouseId"),
            ("shipments", "ShipmentId"), ("products", "ProductId"),
        })
        {
            await ExecAsync(conn, $@"SELECT setval(
                pg_get_serial_sequence('{q}.""{table}""', '{pk}'),
                (SELECT COALESCE(MAX(""{pk}""), 0) + 1 FROM {q}.""{table}""),
                false);");
        }
    }
}
