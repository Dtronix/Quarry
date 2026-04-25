using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using Quarry.Internal;
using Quarry.Tests.Integration;
using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests;


/// <summary>
/// Self-contained, disposable test harness that provides all dialect contexts and database connections.
/// Lite is backed by a real SQLite in-memory connection (SQL verification + execution).
/// Pg is backed by a real Npgsql connection to a shared PostgreSQL 17 container
/// (SQL verification + execution). My is backed by a real MySqlConnector connection
/// to a shared MySQL 8.4 container (SQL verification + execution). Ss is on
/// MockDbConnection (SQL verification only — out of scope for the real-provider
/// migration that motivated Pg and My moving off the mock).
/// Each test creates its own harness — no shared mutable state, fully parallelizable.
/// </summary>
internal sealed class QueryTestHarness : IAsyncDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly NpgsqlConnection _npgsqlConnection;
    private readonly NpgsqlTransaction? _npgsqlTransaction;
    private readonly string? _ownedPgSchema;
    private readonly MySqlConnection _mysqlConnection;
    private readonly MySqlTransaction? _mysqlTransaction;
    private readonly string? _ownedMyDatabase;

    /// <summary>SQLite context on a real in-memory connection (SQL verification + execution).</summary>
    public TestDbContext Lite { get; }

    /// <summary>PostgreSQL context on a real <see cref="NpgsqlConnection"/> attached to the shared Testcontainers PG 17 container (SQL verification + execution).</summary>
    public Pg.PgDb Pg { get; }

    /// <summary>MySQL context on a real <see cref="MySqlConnection"/> attached to the shared Testcontainers MySQL 8.4 container (SQL verification + execution).</summary>
    public My.MyDb My { get; }

    /// <summary>SQL Server context on MockDbConnection (SQL verification only).</summary>
    public Ss.SsDb Ss { get; }

    /// <summary>The shared MockDbConnection backing Ss — exposed for tests that inspect executed SQL.</summary>
    public MockDbConnection MockConnection { get; }

    private QueryTestHarness(
        SqliteConnection sqliteConnection,
        MockDbConnection mockConnection,
        NpgsqlConnection npgsqlConnection,
        NpgsqlTransaction? npgsqlTransaction,
        string? ownedPgSchema,
        MySqlConnection mysqlConnection,
        MySqlTransaction? mysqlTransaction,
        string? ownedMyDatabase)
    {
        _sqliteConnection = sqliteConnection;
        MockConnection = mockConnection;
        _npgsqlConnection = npgsqlConnection;
        _npgsqlTransaction = npgsqlTransaction;
        _ownedPgSchema = ownedPgSchema;
        _mysqlConnection = mysqlConnection;
        _mysqlTransaction = mysqlTransaction;
        _ownedMyDatabase = ownedMyDatabase;
        Lite = new TestDbContext(sqliteConnection);
        Pg = new Pg.PgDb(npgsqlConnection);
        My = new My.MyDb(mysqlConnection);
        Ss = new Ss.SsDb(mockConnection);
    }

    /// <summary>
    /// Creates a fresh harness with an isolated SQLite in-memory database and
    /// PG + MySQL connections to the shared containers, all seeded with the
    /// default schema and data.
    /// </summary>
    /// <param name="useOwnPgSchema">
    /// When <c>false</c> (default), the PG connection sets <c>search_path</c>
    /// to the shared <c>quarry_test</c> baseline schema and wraps the test in
    /// a transaction that rolls back on dispose — near-zero per-test overhead.
    /// When <c>true</c>, a uniquely-named PG schema is created with its own
    /// DDL + seed, <c>search_path</c> is set to that schema, and it is
    /// dropped on dispose. Use this for tests that issue their own
    /// <c>BEGIN</c>/<c>COMMIT</c> (e.g. <see cref="Quarry.Migration.MigrationRunner"/>
    /// tests), or that depend on COMMIT-visible state.
    /// </param>
    /// <param name="useOwnMyDatabase">
    /// MySQL parallel of <paramref name="useOwnPgSchema"/>. When <c>false</c>
    /// (default), the MySQL connection switches to the shared <c>quarry_test</c>
    /// baseline database and wraps the test in a transaction that rolls back
    /// on dispose. When <c>true</c>, a uniquely-named database is created with
    /// its own DDL + seed and dropped on dispose.
    /// </param>
    public static async Task<QueryTestHarness> CreateAsync(
        bool useOwnPgSchema = false,
        bool useOwnMyDatabase = false)
    {
        // Track every opened resource so a partial-setup throw can roll
        // them all back — previously a transient PG connection refusal or
        // a mid-setup DDL failure would leak the SQLite + Npgsql + mock
        // connections for the rest of the process.
        SqliteConnection? sqliteConnection = null;
        MockDbConnection? mockConnection = null;
        NpgsqlConnection? npgsqlConnection = null;
        NpgsqlTransaction? npgsqlTransaction = null;
        string? ownedPgSchema = null;
        MySqlConnection? mysqlConnection = null;
        MySqlTransaction? mysqlTransaction = null;
        string? ownedMyDatabase = null;

        try
        {
            sqliteConnection = new SqliteConnection("Data Source=:memory:");
            await sqliteConnection.OpenAsync();

            // FK enforcement is off by default so DELETE tests don't need to worry about
            // dependent-row ordering.  Tests that specifically verify FK behavior can enable
            // it via SqlAsync("PRAGMA foreign_keys = ON").
            await using (var pragma = sqliteConnection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA foreign_keys = OFF";
                await pragma.ExecuteNonQueryAsync();
            }

            mockConnection = new MockDbConnection();

            // PG setup — ensure baseline first (no-op after the first harness).
            await PostgresTestContainer.EnsureBaselineAsync();
            var pgCs = await PostgresTestContainer.GetConnectionStringAsync();
            npgsqlConnection = new NpgsqlConnection(pgCs);
            await npgsqlConnection.OpenAsync();

            if (useOwnPgSchema)
            {
                ownedPgSchema = await PostgresTestContainer.CreateOwnedSchemaAsync(npgsqlConnection);
                await SetPgSearchPathAsync(npgsqlConnection, ownedPgSchema);
            }
            else
            {
                await SetPgSearchPathAsync(npgsqlConnection, PostgresTestContainer.BaselineSchemaName);
                npgsqlTransaction = (NpgsqlTransaction)await npgsqlConnection.BeginTransactionAsync();
            }

            // MySQL setup — ensure baseline first (no-op after the first harness).
            await MySqlTestContainer.EnsureBaselineAsync();
            var myCs = await MySqlTestContainer.GetConnectionStringAsync();
            mysqlConnection = new MySqlConnection(myCs);
            await mysqlConnection.OpenAsync();

            if (useOwnMyDatabase)
            {
                ownedMyDatabase = await MySqlTestContainer.CreateOwnedDatabaseAsync(mysqlConnection);
            }
            else
            {
                await SetMyDatabaseAsync(mysqlConnection, MySqlTestContainer.BaselineDatabaseName);
                mysqlTransaction = (MySqlTransaction)await mysqlConnection.BeginTransactionAsync();
            }

            var harness = new QueryTestHarness(
                sqliteConnection, mockConnection,
                npgsqlConnection, npgsqlTransaction, ownedPgSchema,
                mysqlConnection, mysqlTransaction, ownedMyDatabase);

            await harness.CreateSchema();
            await harness.SeedData();

            return harness;
        }
        catch
        {
            // Best-effort unwind — preserve the original exception but don't
            // leak connections. Each dispose is wrapped because an error in
            // an earlier step is more informative than a cascade in teardown.
            if (mysqlTransaction is not null)
            {
                try { await mysqlTransaction.RollbackAsync(); } catch { }
                try { await mysqlTransaction.DisposeAsync(); } catch { }
            }
            if (ownedMyDatabase is not null && mysqlConnection?.State == System.Data.ConnectionState.Open)
            {
                try
                {
                    await using var drop = mysqlConnection.CreateCommand();
                    drop.CommandText = $"DROP DATABASE IF EXISTS `{ownedMyDatabase}`;";
                    await drop.ExecuteNonQueryAsync();
                }
                catch { }
            }
            if (mysqlConnection is not null)
                try { await mysqlConnection.DisposeAsync(); } catch { }
            if (npgsqlTransaction is not null)
            {
                try { await npgsqlTransaction.RollbackAsync(); } catch { }
                try { await npgsqlTransaction.DisposeAsync(); } catch { }
            }
            if (ownedPgSchema is not null && npgsqlConnection?.State == System.Data.ConnectionState.Open)
            {
                try
                {
                    await using var drop = npgsqlConnection.CreateCommand();
                    drop.CommandText = $"DROP SCHEMA IF EXISTS \"{ownedPgSchema}\" CASCADE;";
                    await drop.ExecuteNonQueryAsync();
                }
                catch { }
            }
            if (npgsqlConnection is not null)
                try { await npgsqlConnection.DisposeAsync(); } catch { }
            mockConnection?.Dispose();
            if (sqliteConnection is not null)
                try { await sqliteConnection.DisposeAsync(); } catch { }
            throw;
        }
    }

    private static async Task SetPgSearchPathAsync(NpgsqlConnection conn, string schema)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SET search_path TO \"{schema}\";";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task SetMyDatabaseAsync(MySqlConnection conn, string database)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"USE `{database}`;";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Execute raw SQL against the live SQLite connection.
    /// Used by tests that need custom seed data or schema modifications.
    /// </summary>
    public async Task SqlAsync(string sql)
    {
        await using var cmd = _sqliteConnection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    public void Deconstruct(out TestDbContext lite, out Pg.PgDb pg, out My.MyDb my, out Ss.SsDb ss)
    {
        lite = Lite;
        pg = Pg;
        my = My;
        ss = Ss;
    }

    /// <summary>
    /// Asserts exact SQL string equality for all 4 dialects. Reports all failures at once.
    /// </summary>
    public static void AssertDialects(
        string sqliteActual, string pgActual, string mysqlActual, string ssActual,
        string sqlite, string pg, string mysql, string ss)
    {
        Assert.Multiple(() =>
        {
            Assert.That(sqliteActual, Is.EqualTo(sqlite), "SQLite");
            Assert.That(pgActual, Is.EqualTo(pg), "PostgreSQL");
            Assert.That(mysqlActual, Is.EqualTo(mysql), "MySQL");
            Assert.That(ssActual, Is.EqualTo(ss), "SqlServer");
        });
    }

    /// <summary>
    /// Asserts exact SQL equality for all 4 dialects via QueryDiagnostics. Reports all failures at once.
    /// </summary>
    public static void AssertDialects(
        QueryDiagnostics sqliteDiag, QueryDiagnostics pgDiag,
        QueryDiagnostics mysqlDiag, QueryDiagnostics ssDiag,
        string sqlite, string pg, string mysql, string ss)
    {
        Assert.Multiple(() =>
        {
            Assert.That(sqliteDiag.Sql, Is.EqualTo(sqlite), "SQLite");
            Assert.That(pgDiag.Sql, Is.EqualTo(pg), "PostgreSQL");
            Assert.That(mysqlDiag.Sql, Is.EqualTo(mysql), "MySQL");
            Assert.That(ssDiag.Sql, Is.EqualTo(ss), "SqlServer");
        });
    }

    public async ValueTask DisposeAsync()
    {
        Ss.Dispose();

        // MySQL teardown. Mirrors the PG teardown ordering and rationale —
        // ROLLBACK for the transactional path, DROP DATABASE for the owned
        // path. Run on the raw connection rather than through My.Dispose
        // because My's dispose only disposes the context wrapper.
        My.Dispose();
        if (_mysqlTransaction is not null)
        {
            try { await _mysqlTransaction.RollbackAsync(); }
            catch { /* connection may already be closed; ignore */ }
            await _mysqlTransaction.DisposeAsync();
        }
        if (_ownedMyDatabase is not null && _mysqlConnection.State == System.Data.ConnectionState.Open)
        {
            try
            {
                await using var drop = _mysqlConnection.CreateCommand();
                drop.CommandText = $"DROP DATABASE IF EXISTS `{_ownedMyDatabase}`;";
                await drop.ExecuteNonQueryAsync();
            }
            catch { /* best-effort; avoids masking the test's own failure */ }
        }
        await _mysqlConnection.DisposeAsync();

        // PG teardown. For the transactional path, ROLLBACK discards any
        // changes the test made. For the owned-schema path, DROP SCHEMA
        // CASCADE wipes the schema entirely. We run teardown on the raw
        // connection rather than through Pg.Dispose because Pg's dispose
        // only disposes the context wrapper — the connection it was handed
        // is ours to manage.
        Pg.Dispose();
        if (_npgsqlTransaction is not null)
        {
            try { await _npgsqlTransaction.RollbackAsync(); }
            catch { /* connection may already be closed; ignore */ }
            await _npgsqlTransaction.DisposeAsync();
        }
        if (_ownedPgSchema is not null && _npgsqlConnection.State == System.Data.ConnectionState.Open)
        {
            try
            {
                await using var drop = _npgsqlConnection.CreateCommand();
                drop.CommandText = $"DROP SCHEMA IF EXISTS \"{_ownedPgSchema}\" CASCADE;";
                await drop.ExecuteNonQueryAsync();
            }
            catch { /* best-effort; avoids masking the test's own failure */ }
        }
        await _npgsqlConnection.DisposeAsync();

        Lite.Dispose();
        MockConnection.Dispose();
        await _sqliteConnection.DisposeAsync();
    }

    private async Task CreateSchema()
    {
        await SqlAsync("""
            CREATE TABLE "users" (
                "UserId" INTEGER PRIMARY KEY,
                "UserName" TEXT NOT NULL,
                "Email" TEXT,
                "IsActive" INTEGER NOT NULL DEFAULT 1,
                "CreatedAt" TEXT NOT NULL,
                "LastLogin" TEXT
            )
            """);

        await SqlAsync("""
            CREATE TABLE "orders" (
                "OrderId" INTEGER PRIMARY KEY,
                "UserId" INTEGER NOT NULL,
                "Total" REAL NOT NULL,
                "Status" TEXT NOT NULL,
                "Priority" INTEGER NOT NULL DEFAULT 1,
                "OrderDate" TEXT NOT NULL,
                "Notes" TEXT,
                FOREIGN KEY ("UserId") REFERENCES "users"("UserId")
            )
            """);

        await SqlAsync("""
            CREATE TABLE "order_items" (
                "OrderItemId" INTEGER PRIMARY KEY,
                "OrderId" INTEGER NOT NULL,
                "ProductName" TEXT NOT NULL,
                "Quantity" INTEGER NOT NULL,
                "UnitPrice" REAL NOT NULL,
                "LineTotal" REAL NOT NULL,
                FOREIGN KEY ("OrderId") REFERENCES "orders"("OrderId")
            )
            """);

        await SqlAsync("""
            CREATE TABLE "accounts" (
                "AccountId" INTEGER PRIMARY KEY,
                "UserId" INTEGER NOT NULL,
                "AccountName" TEXT NOT NULL,
                "Balance" REAL NOT NULL,
                "credit_limit" REAL NOT NULL DEFAULT 0,
                "IsActive" INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY ("UserId") REFERENCES "users"("UserId")
            )
            """);

        await SqlAsync("""
            CREATE TABLE "events" (
                "EventId" INTEGER PRIMARY KEY,
                "EventName" TEXT NOT NULL,
                "ScheduledAt" TEXT NOT NULL,
                "CancelledAt" TEXT
            )
            """);

        await SqlAsync("""
            CREATE TABLE "addresses" (
                "AddressId" INTEGER PRIMARY KEY,
                "City" TEXT NOT NULL,
                "Street" TEXT NOT NULL,
                "ZipCode" TEXT
            )
            """);

        await SqlAsync("""
            CREATE TABLE "user_addresses" (
                "UserAddressId" INTEGER PRIMARY KEY,
                "UserId" INTEGER NOT NULL,
                "AddressId" INTEGER NOT NULL,
                FOREIGN KEY ("UserId") REFERENCES "users"("UserId"),
                FOREIGN KEY ("AddressId") REFERENCES "addresses"("AddressId")
            )
            """);

        await SqlAsync("""
            CREATE TABLE "warehouses" (
                "WarehouseId" INTEGER PRIMARY KEY,
                "WarehouseName" TEXT NOT NULL,
                "Region" TEXT NOT NULL
            )
            """);

        await SqlAsync("""
            CREATE TABLE "shipments" (
                "ShipmentId" INTEGER PRIMARY KEY,
                "OrderId" INTEGER NOT NULL,
                "WarehouseId" INTEGER NOT NULL,
                "ReturnWarehouseId" INTEGER NULL,
                "ShipDate" TEXT NOT NULL,
                FOREIGN KEY ("OrderId") REFERENCES "orders"("OrderId"),
                FOREIGN KEY ("WarehouseId") REFERENCES "warehouses"("WarehouseId"),
                FOREIGN KEY ("ReturnWarehouseId") REFERENCES "warehouses"("WarehouseId")
            )
            """);

        await SqlAsync("""
            CREATE TABLE "products" (
                "ProductId" INTEGER PRIMARY KEY,
                "ProductName" TEXT NOT NULL,
                "Price" REAL NOT NULL,
                "Description" TEXT,
                "DiscountedPrice" REAL GENERATED ALWAYS AS ("Price" * 0.9) STORED
            )
            """);

        // View to alias "orders" as "Order" for join table name compatibility.
        await SqlAsync("""
            CREATE VIEW "Order" AS SELECT * FROM "orders"
            """);
    }

    private async Task SeedData()
    {
        await SqlAsync("""
            INSERT INTO "users" ("UserId", "UserName", "Email", "IsActive", "CreatedAt", "LastLogin") VALUES
                (1, 'Alice',   'alice@test.com',   1, '2024-01-15 00:00:00', '2024-06-01 00:00:00'),
                (2, 'Bob',     NULL,               1, '2024-02-20 00:00:00', NULL),
                (3, 'Charlie', 'charlie@test.com', 0, '2024-03-10 00:00:00', '2024-05-15 00:00:00')
            """);

        await SqlAsync("""
            INSERT INTO "orders" ("OrderId", "UserId", "Total", "Status", "Priority", "OrderDate", "Notes") VALUES
                (1, 1, 250.00, 'Shipped', 2, '2024-06-01 00:00:00', 'Express'),
                (2, 1, 75.50,  'Pending', 1, '2024-06-15 00:00:00', NULL),
                (3, 2, 150.00, 'Shipped', 3, '2024-07-01 00:00:00', NULL)
            """);

        await SqlAsync("""
            INSERT INTO "order_items" ("OrderItemId", "OrderId", "ProductName", "Quantity", "UnitPrice", "LineTotal") VALUES
                (1, 1, 'Widget', 2, 125.00, 250.00),
                (2, 2, 'Gadget', 1, 75.50,  75.50),
                (3, 3, 'Widget', 3, 50.00,  150.00)
            """);

        await SqlAsync("""
            INSERT INTO "accounts" ("AccountId", "UserId", "AccountName", "Balance", "credit_limit", "IsActive") VALUES
                (1, 1, 'Savings',  1000.50, 5000.00, 1),
                (2, 1, 'Checking', 250.75,  1000.00, 1),
                (3, 2, 'Savings',  500.00,  2000.00, 0)
            """);

        await SqlAsync("""
            INSERT INTO "events" ("EventId", "EventName", "ScheduledAt", "CancelledAt") VALUES
                (1, 'Launch', '2024-06-15 10:30:00+00:00', NULL),
                (2, 'Review', '2024-07-01 14:00:00+02:00', '2024-06-28 09:00:00+02:00')
            """);

        await SqlAsync("""
            INSERT INTO "addresses" ("AddressId", "City", "Street", "ZipCode") VALUES
                (1, 'Portland', '123 Main St', '97201'),
                (2, 'Seattle', '456 Oak Ave', '98101')
            """);

        await SqlAsync("""
            INSERT INTO "user_addresses" ("UserAddressId", "UserId", "AddressId") VALUES
                (1, 1, 1),
                (2, 1, 2),
                (3, 2, 1)
            """);

        await SqlAsync("""
            INSERT INTO "products" ("ProductId", "ProductName", "Price", "Description") VALUES
                (1, 'Widget',  29.99, 'A fine widget'),
                (2, 'Gadget',  49.50, NULL),
                (3, 'Doohickey', 9.95, 'Budget option')
            """);

        await SqlAsync("""
            INSERT INTO "warehouses" ("WarehouseId", "WarehouseName", "Region") VALUES
                (1, 'West Coast Hub', 'US'),
                (2, 'EU Central', 'EU')
            """);

        await SqlAsync("""
            INSERT INTO "shipments" ("ShipmentId", "OrderId", "WarehouseId", "ReturnWarehouseId", "ShipDate") VALUES
                (1, 1, 1, 2, '2024-06-02 00:00:00'),
                (2, 3, 2, NULL, '2024-07-02 00:00:00')
            """);
    }
}
