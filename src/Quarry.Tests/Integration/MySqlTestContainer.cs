using System;
using System.Text;
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
/// </remarks>
internal static class MySqlTestContainer
{
    internal const string BaselineDatabaseName = "quarry_test";

    private static readonly SemaphoreSlim _containerLock = new(1, 1);
    private static readonly SemaphoreSlim _baselineLock = new(1, 1);
    private static MySqlContainer? _container;
    private static bool _baselineReady;
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
                    // Grant the Testcontainers-created application user
                    // full server privileges so per-test CREATE DATABASE
                    // works for the useOwnMyDatabase opt-out and for
                    // MySqlMigrationRunnerTests. The default Testcontainers
                    // MySQL user is scoped to MYSQL_DATABASE only, which
                    // would block any CREATE DATABASE call. The
                    // mysql:8.4 entrypoint runs /docker-entrypoint-initdb.d
                    // *.sql files after the database is bootstrapped, and
                    // before the readiness probe returns. Test-only
                    // container, so there is no security concern.
                    var grantSql = Encoding.UTF8.GetBytes(
                        "GRANT ALL PRIVILEGES ON *.* TO 'mysql'@'%' WITH GRANT OPTION;\n" +
                        "FLUSH PRIVILEGES;\n");

                    // Server-wide sql_mode and collation pinning. Two
                    // departures from the default mysql:8.4 server config:
                    //
                    //   * NO_BACKSLASH_ESCAPES added to sql_mode so backslash
                    //     in string literals is taken literally instead of
                    //     as an escape character. Quarry's generator emits
                    //     the same SQL (LIKE '%foo\_bar%' ESCAPE '\') for
                    //     PG / SQLite / SqlServer / MySQL — those three
                    //     other dialects always treat `'\'` as a literal
                    //     backslash. MySQL's default backslash-escape
                    //     handling otherwise fires a 1064 syntax error
                    //     when the generator emits the standard ANSI
                    //     pattern. The other sql_mode flags are the MySQL
                    //     8.4 defaults preserved verbatim so we don't
                    //     accidentally relax STRICT or ONLY_FULL_GROUP_BY.
                    //
                    //   * collation-server pinned to utf8mb4_bin so string
                    //     comparisons (`WHERE col = 'shipped'`,
                    //     `IN ('a', 'b', 'c')`) are case-sensitive. The
                    //     mysql:8.4 default is utf8mb4_0900_ai_ci which is
                    //     case-INsensitive — making PG / SQLite / SqlServer
                    //     return 0 rows for case-mismatched literals while
                    //     MySQL would return matches. utf8mb4_bin gives
                    //     cross-dialect parity.
                    var container = new MySqlBuilder("mysql:8.4")
                        .WithDatabase(BaselineDatabaseName)
                        .WithResourceMapping(grantSql, "/docker-entrypoint-initdb.d/01-grant-all.sql")
                        .WithCommand(
                            "--character-set-server=utf8mb4",
                            "--collation-server=utf8mb4_bin",
                            "--sql-mode=ONLY_FULL_GROUP_BY,STRICT_TRANS_TABLES,NO_ZERO_IN_DATE,NO_ZERO_DATE,ERROR_FOR_DIVISION_BY_ZERO,NO_ENGINE_SUBSTITUTION,NO_BACKSLASH_ESCAPES")
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

    /// <summary>
    /// Creates the shared <c>quarry_test</c> baseline database with all tables
    /// + seed data, idempotently and only once per test process. Safe to call
    /// from many concurrent harness constructors.
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

            // Use a MySQL named-lock to make the baseline setup safe across
            // concurrent test processes sharing one container. GET_LOCK
            // blocks until we hold the lock; RELEASE_LOCK releases it. The
            // 60s timeout is generous — DDL+seed completes in well under a
            // second, but a slow Docker startup combined with multiple
            // processes can push first-process DDL out toward several
            // seconds. Any other process entering EnsureBaselineAsync
            // concurrently will wait here and then observe the ready
            // database.
            const string BaselineLockName = "quarry_test_baseline";
            await ExecAsync(conn, $"SELECT GET_LOCK('{BaselineLockName}', 60);");
            try
            {
                // Probe whether the baseline is already populated — if the
                // `users` table exists in the baseline database another
                // process already ran setup and seed; don't repeat the work.
                var alreadyReady = await TableExistsAsync(conn, BaselineDatabaseName, "users");
                if (!alreadyReady)
                {
                    await ExecAsync(conn, $"USE `{BaselineDatabaseName}`;");
                    await CreateSchemaObjectsAsync(conn);
                    await SeedDataAsync(conn);
                }
            }
            finally
            {
                await ExecAsync(conn, $"SELECT RELEASE_LOCK('{BaselineLockName}');");
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

    /// <summary>
    /// Creates a uniquely-named database with the full DDL + seed, for a
    /// single harness instance that needs its own isolated database (e.g.
    /// transactional tests, migration runner tests). Returns the database
    /// name so the caller can <c>DROP DATABASE</c> on dispose. The supplied
    /// connection switches to the new database via <c>USE</c>.
    /// </summary>
    public static async Task<string> CreateOwnedDatabaseAsync(MySqlConnection connection)
    {
        var database = "test_" + Guid.NewGuid().ToString("N").Substring(0, 12);
        await ExecAsync(connection, $"CREATE DATABASE `{database}`;");
        await ExecAsync(connection, $"USE `{database}`;");
        await CreateSchemaObjectsAsync(connection);
        await SeedDataAsync(connection);
        return database;
    }

    private static async Task ExecAsync(MySqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// DDL port of <see cref="QueryTestHarness.CreateSchema"/> for MySQL.
    /// Column types match what <c>Quarry.Migration.SqlTypeMapper.MapMySql</c>
    /// emits for each CLR type, because the harness DDL is supposed to mirror
    /// what the migration emits in production. Caller is expected to have
    /// switched the active database with <c>USE</c> before calling this.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><description>Primary keys use <c>INT NOT NULL AUTO_INCREMENT PRIMARY KEY</c>; explicit-PK seed inserts advance the AUTO_INCREMENT counter automatically (no <c>setval</c> aftercare needed, unlike PG).</description></item>
    ///   <item><description><c>Col&lt;decimal&gt;</c> / <c>Col&lt;Money&gt;</c>→decimal columns use <c>DECIMAL(18, 2)</c>. Per <c>SqlTypeMapper.MapMySql</c> at <c>SqlTypeMapper.cs:90</c>.</description></item>
    ///   <item><description><c>Col&lt;bool&gt;</c> columns use <c>TINYINT(1)</c>. Per <c>SqlTypeMapper.MapMySql</c> at <c>SqlTypeMapper.cs:86</c>. <c>MySqlConnector.TreatTinyAsBoolean</c> is true by default, so <c>reader.GetBoolean(i)</c> materialises correctly and <c>WHERE col = TRUE</c> binds the literal <c>1</c>/<c>0</c>.</description></item>
    ///   <item><description><c>Col&lt;DateTime&gt;</c> and <c>Col&lt;DateTimeOffset&gt;</c> both use <c>DATETIME</c>. Per <c>SqlTypeMapper.MapMySql</c> at <c>SqlTypeMapper.cs:93-94</c>. <b>DTO offset semantics are not preserved</b> — this matches what <c>SqlTypeMapper</c> emits in production. If a test asserts on an offset value, expect to triage in Phase 5.</description></item>
    ///   <item><description>Identifiers use backtick quoting (MySQL native) instead of PG's double-quotes.</description></item>
    ///   <item><description>FOREIGN KEY constraints from the SQLite source are intentionally omitted. SQLite does not enforce FKs unless <c>PRAGMA foreign_keys = ON</c>, which the harness does not set. MySQL with InnoDB does enforce FKs, so omitting them mirrors Lite's effective behaviour and keeps DELETE-by-where-clause tests passing — same rationale as the PG harness.</description></item>
    ///   <item><description>Mixed-case identifiers (e.g. table view <c>`Order`</c>) are preserved by the default Linux container's <c>lower_case_table_names=0</c>, which matches the case-sensitive identifiers Quarry emits.</description></item>
    /// </list>
    /// </remarks>
    private static async Task CreateSchemaObjectsAsync(MySqlConnection conn)
    {
        await ExecAsync(conn, @"CREATE TABLE `users` (
            `UserId` INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
            `UserName` TEXT NOT NULL,
            `Email` TEXT,
            `IsActive` TINYINT(1) NOT NULL DEFAULT 1,
            `CreatedAt` DATETIME NOT NULL,
            `LastLogin` DATETIME
        );");

        await ExecAsync(conn, @"CREATE TABLE `orders` (
            `OrderId` INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
            `UserId` INT NOT NULL,
            `Total` DECIMAL(18, 2) NOT NULL,
            `Status` TEXT NOT NULL,
            `Priority` INT NOT NULL DEFAULT 1,
            `OrderDate` DATETIME NOT NULL,
            `Notes` TEXT
        );");

        await ExecAsync(conn, @"CREATE TABLE `order_items` (
            `OrderItemId` INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
            `OrderId` INT NOT NULL,
            `ProductName` TEXT NOT NULL,
            `Quantity` INT NOT NULL,
            `UnitPrice` DECIMAL(18, 2) NOT NULL,
            `LineTotal` DECIMAL(18, 2) NOT NULL
        );");

        await ExecAsync(conn, @"CREATE TABLE `accounts` (
            `AccountId` INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
            `UserId` INT NOT NULL,
            `AccountName` TEXT NOT NULL,
            `Balance` DECIMAL(18, 2) NOT NULL,
            `credit_limit` DECIMAL(18, 2) NOT NULL DEFAULT 0,
            `IsActive` TINYINT(1) NOT NULL DEFAULT 1
        );");

        await ExecAsync(conn, @"CREATE TABLE `events` (
            `EventId` INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
            `EventName` TEXT NOT NULL,
            `ScheduledAt` DATETIME NOT NULL,
            `CancelledAt` DATETIME
        );");

        await ExecAsync(conn, @"CREATE TABLE `addresses` (
            `AddressId` INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
            `City` TEXT NOT NULL,
            `Street` TEXT NOT NULL,
            `ZipCode` TEXT
        );");

        await ExecAsync(conn, @"CREATE TABLE `user_addresses` (
            `UserAddressId` INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
            `UserId` INT NOT NULL,
            `AddressId` INT NOT NULL
        );");

        await ExecAsync(conn, @"CREATE TABLE `warehouses` (
            `WarehouseId` INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
            `WarehouseName` TEXT NOT NULL,
            `Region` TEXT NOT NULL
        );");

        await ExecAsync(conn, @"CREATE TABLE `shipments` (
            `ShipmentId` INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
            `OrderId` INT NOT NULL,
            `WarehouseId` INT NOT NULL,
            `ReturnWarehouseId` INT NULL,
            `ShipDate` DATETIME NOT NULL
        );");

        await ExecAsync(conn, @"CREATE TABLE `products` (
            `ProductId` INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
            `ProductName` TEXT NOT NULL,
            `Price` DECIMAL(18, 2) NOT NULL,
            `Description` TEXT,
            `DiscountedPrice` DECIMAL(18, 2) GENERATED ALWAYS AS (`Price` * 0.9) STORED
        );");

        await ExecAsync(conn, @"CREATE VIEW `Order` AS SELECT * FROM `orders`;");
    }

    /// <summary>
    /// Seed data port of <see cref="QueryTestHarness.SeedData"/>. Rows use the
    /// same explicit primary keys as the SQLite seed. After explicit-PK
    /// inserts MySQL advances the AUTO_INCREMENT counter past the highest
    /// seeded value automatically; no post-seed sequence-fixup is required
    /// (unlike PG's IDENTITY columns).
    /// </summary>
    /// <remarks>
    /// Caller is expected to have switched the active database with
    /// <c>USE</c> before calling this. <c>events.ScheduledAt</c> /
    /// <c>CancelledAt</c> values are seeded with offset suffixes stripped
    /// because <c>DATETIME</c> has no offset awareness. If a test asserts
    /// on the offset, expect to triage in Phase 5.
    /// </remarks>
    private static async Task SeedDataAsync(MySqlConnection conn)
    {
        await ExecAsync(conn, @"INSERT INTO `users` (`UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin`) VALUES
            (1, 'Alice',   'alice@test.com',   1, '2024-01-15 00:00:00', '2024-06-01 00:00:00'),
            (2, 'Bob',     NULL,               1, '2024-02-20 00:00:00', NULL),
            (3, 'Charlie', 'charlie@test.com', 0, '2024-03-10 00:00:00', '2024-05-15 00:00:00');");

        await ExecAsync(conn, @"INSERT INTO `orders` (`OrderId`, `UserId`, `Total`, `Status`, `Priority`, `OrderDate`, `Notes`) VALUES
            (1, 1, 250.00, 'Shipped', 2, '2024-06-01 00:00:00', 'Express'),
            (2, 1, 75.50,  'Pending', 1, '2024-06-15 00:00:00', NULL),
            (3, 2, 150.00, 'Shipped', 3, '2024-07-01 00:00:00', NULL);");

        await ExecAsync(conn, @"INSERT INTO `order_items` (`OrderItemId`, `OrderId`, `ProductName`, `Quantity`, `UnitPrice`, `LineTotal`) VALUES
            (1, 1, 'Widget', 2, 125.00, 250.00),
            (2, 2, 'Gadget', 1, 75.50,  75.50),
            (3, 3, 'Widget', 3, 50.00,  150.00);");

        await ExecAsync(conn, @"INSERT INTO `accounts` (`AccountId`, `UserId`, `AccountName`, `Balance`, `credit_limit`, `IsActive`) VALUES
            (1, 1, 'Savings',  1000.50, 5000.00, 1),
            (2, 1, 'Checking', 250.75,  1000.00, 1),
            (3, 2, 'Savings',  500.00,  2000.00, 0);");

        await ExecAsync(conn, @"INSERT INTO `events` (`EventId`, `EventName`, `ScheduledAt`, `CancelledAt`) VALUES
            (1, 'Launch', '2024-06-15 10:30:00', NULL),
            (2, 'Review', '2024-07-01 14:00:00', '2024-06-28 09:00:00');");

        await ExecAsync(conn, @"INSERT INTO `addresses` (`AddressId`, `City`, `Street`, `ZipCode`) VALUES
            (1, 'Portland', '123 Main St', '97201'),
            (2, 'Seattle', '456 Oak Ave', '98101');");

        await ExecAsync(conn, @"INSERT INTO `user_addresses` (`UserAddressId`, `UserId`, `AddressId`) VALUES
            (1, 1, 1),
            (2, 1, 2),
            (3, 2, 1);");

        await ExecAsync(conn, @"INSERT INTO `products` (`ProductId`, `ProductName`, `Price`, `Description`) VALUES
            (1, 'Widget',  29.99, 'A fine widget'),
            (2, 'Gadget',  49.50, NULL),
            (3, 'Doohickey', 9.95, 'Budget option');");

        await ExecAsync(conn, @"INSERT INTO `warehouses` (`WarehouseId`, `WarehouseName`, `Region`) VALUES
            (1, 'West Coast Hub', 'US'),
            (2, 'EU Central', 'EU');");

        await ExecAsync(conn, @"INSERT INTO `shipments` (`ShipmentId`, `OrderId`, `WarehouseId`, `ReturnWarehouseId`, `ShipDate`) VALUES
            (1, 1, 1, 2, '2024-06-02 00:00:00'),
            (2, 3, 2, NULL, '2024-07-02 00:00:00');");
    }
}
