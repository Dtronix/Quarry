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

    /// <summary>
    /// sp_getapplock resource name used to gate baseline-schema creation across
    /// concurrent test processes that share the same container (rare, but the
    /// PG side guards it the same way and the cost is negligible).
    /// </summary>
    private const string BaselineApplockResource = "quarry_test_baseline";

    /// <summary>
    /// Fixed list of tables created by <see cref="CreateSchemaObjectsAsync"/>,
    /// in dependency-free order. Used by owned-schema teardown to drop tables
    /// before the schema (SQL Server has no <c>DROP SCHEMA CASCADE</c>).
    /// </summary>
    private static readonly string[] _allTables =
    [
        "shipments", "user_addresses", "addresses", "warehouses",
        "products", "events", "accounts", "order_items", "orders", "users",
    ];

    private static readonly SemaphoreSlim _containerLock = new(1, 1);
    private static readonly SemaphoreSlim _baselineLock = new(1, 1);
    private static MsSqlContainer? _container;
    private static bool _baselineReady;
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
    /// Creates the shared <c>quarry_test</c> schema, the
    /// <c>quarry_test_user</c> login + database user mapped to that schema,
    /// and all tables + seed data, idempotently and only once per test
    /// process. Safe to call from many concurrent harness constructors.
    /// </summary>
    /// <remarks>
    /// Uses <c>sp_getapplock</c> in <c>Session</c> ownership mode so the lock
    /// covers all the DDL + seed work without requiring everything to run
    /// inside a single transaction (some DDL — notably <c>CREATE LOGIN</c> —
    /// has implicit-commit semantics that interact poorly with explicit
    /// transactions). The lock is released before we close the connection, or
    /// implicitly when the connection closes.
    /// </remarks>
    public static async Task EnsureBaselineAsync()
    {
        if (_baselineReady)
            return;

        await _baselineLock.WaitAsync();
        try
        {
            if (_baselineReady)
                return;

            var saCs = await GetSaConnectionStringAsync();
            await using var conn = new SqlConnection(saCs);
            await conn.OpenAsync();

            await AcquireApplockAsync(conn, BaselineApplockResource);
            try
            {
                // Probe: is the schema already populated by a concurrent
                // process?
                var alreadyReady = await SchemaHasUsersTableAsync(conn, BaselineSchemaName);
                if (!alreadyReady)
                {
                    await ProvisionLoginAndUserAsync(conn, TestUserName, TestUserPassword, BaselineSchemaName);
                    await ExecAsync(conn, $"CREATE SCHEMA [{BaselineSchemaName}] AUTHORIZATION [{TestUserName}]");
                    await CreateSchemaObjectsAsync(conn, BaselineSchemaName);
                    await SeedDataAsync(conn, BaselineSchemaName);
                }
                else
                {
                    // Another process did the work but our static state still
                    // says not-ready. The login + user already exist; nothing
                    // to do but mark ready.
                }
            }
            finally
            {
                await ReleaseApplockAsync(conn, BaselineApplockResource);
            }

            _baselineReady = true;
        }
        finally
        {
            _baselineLock.Release();
        }
    }

    /// <summary>
    /// Creates a uniquely-named schema with the full DDL + seed, plus a
    /// dedicated short-lived login whose mapped user has that schema as its
    /// default. Returns the metadata needed to connect as that user and to
    /// drop everything at end-of-test.
    /// </summary>
    /// <remarks>
    /// SQL Server has no <c>SET search_path</c> equivalent, so the only way
    /// to make unqualified <c>[users]</c> references from <see cref="Samples.Ss.SsDb"/>
    /// resolve to the owned schema is to log in as a user whose default
    /// schema points there. The per-harness login is dropped on dispose
    /// (<see cref="DropOwnedSchemaAsync"/>).
    /// </remarks>
    public static async Task<OwnedSchemaInfo> CreateOwnedSchemaAsync(SqlConnection saConnection)
    {
        var schema = "test_" + Guid.NewGuid().ToString("N").Substring(0, 12);
        var user = schema + "_u";
        var password = TestUserPassword;

        await ProvisionLoginAndUserAsync(saConnection, user, password, schema);
        await ExecAsync(saConnection, $"CREATE SCHEMA [{schema}] AUTHORIZATION [{user}]");
        await CreateSchemaObjectsAsync(saConnection, schema);
        await SeedDataAsync(saConnection, schema);

        return new OwnedSchemaInfo(schema, user, password);
    }

    /// <summary>
    /// Tears down a schema previously created by <see cref="CreateOwnedSchemaAsync"/>:
    /// drops all known tables in the schema, then the schema, then the
    /// database user, then the server-level login. Each step is wrapped so a
    /// best-effort failure (lingering connection, etc.) doesn't mask the
    /// caller's own exception.
    /// </summary>
    public static async Task DropOwnedSchemaAsync(SqlConnection saConnection, OwnedSchemaInfo info)
    {
        // Tables first — SQL Server rejects DROP SCHEMA on a non-empty schema
        // and has no CASCADE.
        foreach (var table in _allTables)
        {
            try { await ExecAsync(saConnection, $"DROP TABLE IF EXISTS [{info.Schema}].[{table}]"); }
            catch { /* best-effort */ }
        }
        try { await ExecAsync(saConnection, $"DROP VIEW IF EXISTS [{info.Schema}].[Order]"); } catch { }
        try { await ExecAsync(saConnection, $"DROP SCHEMA IF EXISTS [{info.Schema}]"); } catch { }
        try { await ExecAsync(saConnection, $"DROP USER IF EXISTS [{info.User}]"); } catch { }
        // DROP LOGIN can fail if there's still an open connection authenticated
        // as this login. Force-close any pooled connections for that user
        // first; SqlConnection.ClearAllPools is the blunt-but-effective
        // hammer.
        try { SqlConnection.ClearAllPools(); } catch { }
        try { await ExecAsync(saConnection, $"DROP LOGIN [{info.User}]"); } catch { }
    }

    /// <summary>
    /// Information needed to connect as the per-harness owned-schema user and
    /// to clean up the schema/user/login at end-of-test.
    /// </summary>
    /// <param name="Schema">Schema name (e.g. <c>test_abc123def456</c>).</param>
    /// <param name="User">Login + database-user name; same identifier on both sides.</param>
    /// <param name="Password">Password for the login.</param>
    public readonly record struct OwnedSchemaInfo(string Schema, string User, string Password);

    /// <summary>
    /// Builds a connection string that authenticates as <paramref name="info"/>'s
    /// login. Uses the same server/port/encrypt settings as the sa connection
    /// string, only the credentials change.
    /// </summary>
    public static async Task<string> GetOwnedSchemaConnectionStringAsync(OwnedSchemaInfo info)
    {
        var saCs = await GetSaConnectionStringAsync();
        var builder = new SqlConnectionStringBuilder(saCs)
        {
            UserID = info.User,
            Password = info.Password,
        };
        return builder.ConnectionString;
    }

    private static async Task ProvisionLoginAndUserAsync(SqlConnection conn, string login, string password, string defaultSchema)
    {
        // CREATE LOGIN affects the server (master.sys.server_principals); the
        // db user maps the login into the current database with a default
        // schema. CHECK_POLICY = OFF lets us use a fixed test password without
        // tripping Windows password-complexity rules. db_owner role gives the
        // user enough privilege to issue DDL/DML inside its own schema.
        await ExecAsync(conn, $@"
            IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = '{login}')
                CREATE LOGIN [{login}] WITH PASSWORD = '{password}', CHECK_POLICY = OFF");
        await ExecAsync(conn, $@"
            IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = '{login}')
                CREATE USER [{login}] FOR LOGIN [{login}] WITH DEFAULT_SCHEMA = [{defaultSchema}]");
        await ExecAsync(conn, $"ALTER ROLE [db_owner] ADD MEMBER [{login}]");
    }

    private static async Task AcquireApplockAsync(SqlConnection conn, string resource)
    {
        // Session-owned applock so the DDL below can run outside any
        // transaction without losing the lock — some DDL has implicit commit
        // semantics. Released explicitly in the finally block.
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            DECLARE @r INT;
            EXEC @r = sp_getapplock
                @Resource = '{resource}',
                @LockMode = 'Exclusive',
                @LockOwner = 'Session',
                @LockTimeout = 60000;
            IF @r < 0 THROW 50000, 'Failed to acquire applock {resource}', 1;";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task ReleaseApplockAsync(SqlConnection conn, string resource)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"EXEC sp_releaseapplock @Resource = '{resource}', @LockOwner = 'Session'";
        try { await cmd.ExecuteNonQueryAsync(); }
        catch { /* best-effort; closing the connection releases session locks */ }
    }

    private static async Task<bool> SchemaHasUsersTableAsync(SqlConnection conn, string schema)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT 1 FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE s.name = @s AND t.name = 'users'";
        var p = cmd.CreateParameter();
        p.ParameterName = "@s";
        p.Value = schema;
        cmd.Parameters.Add(p);
        var result = await cmd.ExecuteScalarAsync();
        return result is not null && result is not DBNull;
    }

    private static async Task ExecAsync(SqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// DDL port of <see cref="QueryTestHarness.CreateSchema"/> for SQL Server.
    /// Column types match what <c>Quarry.Migration.SqlTypeMapper.MapSqlServer</c>
    /// emits for each CLR type, because <c>Microsoft.Data.SqlClient</c> is
    /// strict about <c>SqlDbType</c>-vs-column-type mismatches at parameter
    /// binding time.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><description>Primary keys use <c>INT IDENTITY(1,1) PRIMARY KEY</c>. Seed inserts use <c>SET IDENTITY_INSERT [tbl] ON</c> to supply explicit IDs; SQL Server auto-tracks the high-water mark, so no <c>setval</c>-equivalent is needed.</description></item>
    ///   <item><description><c>Col&lt;decimal&gt;</c> / <c>Col&lt;Money&gt;</c> columns use <c>DECIMAL(18, 2)</c> per <c>SqlTypeMapper.MapSqlServer</c>.</description></item>
    ///   <item><description><c>Col&lt;bool&gt;</c> columns use <c>BIT</c>. Default values are <c>1</c>/<c>0</c>, not <c>TRUE</c>/<c>FALSE</c>.</description></item>
    ///   <item><description><c>Col&lt;DateTime&gt;</c> uses <c>DATETIME2</c>; <c>Col&lt;DateTimeOffset&gt;</c> uses <c>DATETIMEOFFSET</c>.</description></item>
    ///   <item><description>Identifiers are square-bracket quoted to match the SqlServer manifest's emission shape.</description></item>
    ///   <item><description>FOREIGN KEY constraints from the SQLite source are intentionally omitted, mirroring the PG port: SQLite does not enforce FKs by default, and the test suite would otherwise need to re-order delete/mutate operations.</description></item>
    ///   <item><description>Computed column on <c>products.DiscountedPrice</c> uses <c>AS (...) PERSISTED</c> — SQL Server's analogue of PG's <c>GENERATED ALWAYS AS (...) STORED</c>.</description></item>
    /// </list>
    /// </remarks>
    private static async Task CreateSchemaObjectsAsync(SqlConnection conn, string schema)
    {
        await ExecAsync(conn, $@"CREATE TABLE [{schema}].[users] (
            [UserId] INT IDENTITY(1,1) PRIMARY KEY,
            [UserName] NVARCHAR(MAX) NOT NULL,
            [Email] NVARCHAR(MAX) NULL,
            [IsActive] BIT NOT NULL DEFAULT 1,
            [CreatedAt] DATETIME2 NOT NULL,
            [LastLogin] DATETIME2 NULL
        )");

        await ExecAsync(conn, $@"CREATE TABLE [{schema}].[orders] (
            [OrderId] INT IDENTITY(1,1) PRIMARY KEY,
            [UserId] INT NOT NULL,
            [Total] DECIMAL(18, 2) NOT NULL,
            [Status] NVARCHAR(MAX) NOT NULL,
            [Priority] INT NOT NULL DEFAULT 1,
            [OrderDate] DATETIME2 NOT NULL,
            [Notes] NVARCHAR(MAX) NULL
        )");

        await ExecAsync(conn, $@"CREATE TABLE [{schema}].[order_items] (
            [OrderItemId] INT IDENTITY(1,1) PRIMARY KEY,
            [OrderId] INT NOT NULL,
            [ProductName] NVARCHAR(MAX) NOT NULL,
            [Quantity] INT NOT NULL,
            [UnitPrice] DECIMAL(18, 2) NOT NULL,
            [LineTotal] DECIMAL(18, 2) NOT NULL
        )");

        await ExecAsync(conn, $@"CREATE TABLE [{schema}].[accounts] (
            [AccountId] INT IDENTITY(1,1) PRIMARY KEY,
            [UserId] INT NOT NULL,
            [AccountName] NVARCHAR(MAX) NOT NULL,
            [Balance] DECIMAL(18, 2) NOT NULL,
            [credit_limit] DECIMAL(18, 2) NOT NULL DEFAULT 0,
            [IsActive] BIT NOT NULL DEFAULT 1
        )");

        await ExecAsync(conn, $@"CREATE TABLE [{schema}].[events] (
            [EventId] INT IDENTITY(1,1) PRIMARY KEY,
            [EventName] NVARCHAR(MAX) NOT NULL,
            [ScheduledAt] DATETIMEOFFSET NOT NULL,
            [CancelledAt] DATETIMEOFFSET NULL
        )");

        await ExecAsync(conn, $@"CREATE TABLE [{schema}].[addresses] (
            [AddressId] INT IDENTITY(1,1) PRIMARY KEY,
            [City] NVARCHAR(MAX) NOT NULL,
            [Street] NVARCHAR(MAX) NOT NULL,
            [ZipCode] NVARCHAR(MAX) NULL
        )");

        await ExecAsync(conn, $@"CREATE TABLE [{schema}].[user_addresses] (
            [UserAddressId] INT IDENTITY(1,1) PRIMARY KEY,
            [UserId] INT NOT NULL,
            [AddressId] INT NOT NULL
        )");

        await ExecAsync(conn, $@"CREATE TABLE [{schema}].[warehouses] (
            [WarehouseId] INT IDENTITY(1,1) PRIMARY KEY,
            [WarehouseName] NVARCHAR(MAX) NOT NULL,
            [Region] NVARCHAR(MAX) NOT NULL
        )");

        await ExecAsync(conn, $@"CREATE TABLE [{schema}].[shipments] (
            [ShipmentId] INT IDENTITY(1,1) PRIMARY KEY,
            [OrderId] INT NOT NULL,
            [WarehouseId] INT NOT NULL,
            [ReturnWarehouseId] INT NULL,
            [ShipDate] DATETIME2 NOT NULL
        )");

        await ExecAsync(conn, $@"CREATE TABLE [{schema}].[products] (
            [ProductId] INT IDENTITY(1,1) PRIMARY KEY,
            [ProductName] NVARCHAR(MAX) NOT NULL,
            [Price] DECIMAL(18, 2) NOT NULL,
            [Description] NVARCHAR(MAX) NULL,
            [DiscountedPrice] AS ([Price] * 0.9) PERSISTED
        )");

        // CREATE VIEW must be the only statement in its batch — separate
        // command keeps that invariant satisfied.
        await ExecAsync(conn, $"CREATE VIEW [{schema}].[Order] AS SELECT * FROM [{schema}].[orders]");
    }

    /// <summary>
    /// Seed data port of <see cref="QueryTestHarness.SeedData"/>. Rows use the
    /// same explicit primary keys as the SQLite seed; <c>SET IDENTITY_INSERT</c>
    /// is toggled around each table's insert because SQL Server otherwise
    /// rejects explicit values for <c>IDENTITY</c> columns. SQL Server tracks
    /// the high-water mark internally, so no PG-style <c>setval</c> step is
    /// needed at the end.
    /// </summary>
    private static async Task SeedDataAsync(SqlConnection conn, string schema)
    {
        await SeedTableAsync(conn, schema, "users", $@"
            INSERT INTO [{schema}].[users] ([UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin]) VALUES
                (1, 'Alice',   'alice@test.com',   1, '2024-01-15T00:00:00', '2024-06-01T00:00:00'),
                (2, 'Bob',     NULL,               1, '2024-02-20T00:00:00', NULL),
                (3, 'Charlie', 'charlie@test.com', 0, '2024-03-10T00:00:00', '2024-05-15T00:00:00')");

        await SeedTableAsync(conn, schema, "orders", $@"
            INSERT INTO [{schema}].[orders] ([OrderId], [UserId], [Total], [Status], [Priority], [OrderDate], [Notes]) VALUES
                (1, 1, 250.00, 'Shipped', 2, '2024-06-01T00:00:00', 'Express'),
                (2, 1, 75.50,  'Pending', 1, '2024-06-15T00:00:00', NULL),
                (3, 2, 150.00, 'Shipped', 3, '2024-07-01T00:00:00', NULL)");

        await SeedTableAsync(conn, schema, "order_items", $@"
            INSERT INTO [{schema}].[order_items] ([OrderItemId], [OrderId], [ProductName], [Quantity], [UnitPrice], [LineTotal]) VALUES
                (1, 1, 'Widget', 2, 125.00, 250.00),
                (2, 2, 'Gadget', 1, 75.50,  75.50),
                (3, 3, 'Widget', 3, 50.00,  150.00)");

        await SeedTableAsync(conn, schema, "accounts", $@"
            INSERT INTO [{schema}].[accounts] ([AccountId], [UserId], [AccountName], [Balance], [credit_limit], [IsActive]) VALUES
                (1, 1, 'Savings',  1000.50, 5000.00, 1),
                (2, 1, 'Checking', 250.75,  1000.00, 1),
                (3, 2, 'Savings',  500.00,  2000.00, 0)");

        await SeedTableAsync(conn, schema, "events", $@"
            INSERT INTO [{schema}].[events] ([EventId], [EventName], [ScheduledAt], [CancelledAt]) VALUES
                (1, 'Launch', '2024-06-15T10:30:00+00:00', NULL),
                (2, 'Review', '2024-07-01T14:00:00+02:00', '2024-06-28T09:00:00+02:00')");

        await SeedTableAsync(conn, schema, "addresses", $@"
            INSERT INTO [{schema}].[addresses] ([AddressId], [City], [Street], [ZipCode]) VALUES
                (1, 'Portland', '123 Main St', '97201'),
                (2, 'Seattle', '456 Oak Ave', '98101')");

        await SeedTableAsync(conn, schema, "user_addresses", $@"
            INSERT INTO [{schema}].[user_addresses] ([UserAddressId], [UserId], [AddressId]) VALUES
                (1, 1, 1),
                (2, 1, 2),
                (3, 2, 1)");

        await SeedTableAsync(conn, schema, "products", $@"
            INSERT INTO [{schema}].[products] ([ProductId], [ProductName], [Price], [Description]) VALUES
                (1, 'Widget',  29.99, 'A fine widget'),
                (2, 'Gadget',  49.50, NULL),
                (3, 'Doohickey', 9.95, 'Budget option')");

        await SeedTableAsync(conn, schema, "warehouses", $@"
            INSERT INTO [{schema}].[warehouses] ([WarehouseId], [WarehouseName], [Region]) VALUES
                (1, 'West Coast Hub', 'US'),
                (2, 'EU Central', 'EU')");

        await SeedTableAsync(conn, schema, "shipments", $@"
            INSERT INTO [{schema}].[shipments] ([ShipmentId], [OrderId], [WarehouseId], [ReturnWarehouseId], [ShipDate]) VALUES
                (1, 1, 1, 2, '2024-06-02T00:00:00'),
                (2, 3, 2, NULL, '2024-07-02T00:00:00')");
    }

    private static async Task SeedTableAsync(SqlConnection conn, string schema, string table, string insertSql)
    {
        // IDENTITY_INSERT is per-session and only one table can have it ON at a
        // time, so we toggle around each insert. The trailing OFF is critical
        // — the next table's seed would fail otherwise.
        await ExecAsync(conn, $"SET IDENTITY_INSERT [{schema}].[{table}] ON");
        try
        {
            await ExecAsync(conn, insertSql);
        }
        finally
        {
            await ExecAsync(conn, $"SET IDENTITY_INSERT [{schema}].[{table}] OFF");
        }
    }
}
