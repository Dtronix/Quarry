using Microsoft.Data.Sqlite;
using Quarry.Internal;
using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests;


/// <summary>
/// Self-contained, disposable test harness that provides all dialect contexts and database connections.
/// Lite is backed by a real SQLite in-memory connection (SQL verification + execution).
/// Pg, My, Ss are on MockDbConnection (SQL verification only).
/// Each test creates its own harness — no shared mutable state, fully parallelizable.
/// </summary>
internal sealed class QueryTestHarness : IAsyncDisposable
{
    private readonly SqliteConnection _sqliteConnection;

    /// <summary>SQLite context on a real in-memory connection (SQL verification + execution).</summary>
    public TestDbContext Lite { get; }

    /// <summary>PostgreSQL context on MockDbConnection (SQL verification only).</summary>
    public Pg.PgDb Pg { get; }

    /// <summary>MySQL context on MockDbConnection (SQL verification only).</summary>
    public My.MyDb My { get; }

    /// <summary>SQL Server context on MockDbConnection (SQL verification only).</summary>
    public Ss.SsDb Ss { get; }

    /// <summary>The shared MockDbConnection backing Pg/My/Ss — exposed for tests that inspect executed SQL.</summary>
    public MockDbConnection MockConnection { get; }

    private QueryTestHarness(SqliteConnection sqliteConnection, MockDbConnection mockConnection)
    {
        _sqliteConnection = sqliteConnection;
        MockConnection = mockConnection;
        Lite = new TestDbContext(sqliteConnection);
        Pg = new Pg.PgDb(mockConnection);
        My = new My.MyDb(mockConnection);
        Ss = new Ss.SsDb(mockConnection);
    }

    /// <summary>
    /// Creates a fresh harness with an isolated SQLite in-memory database, seeded with default schema and data.
    /// </summary>
    public static async Task<QueryTestHarness> CreateAsync()
    {
        var sqliteConnection = new SqliteConnection("Data Source=:memory:");
        await sqliteConnection.OpenAsync();

        var mockConnection = new MockDbConnection();
        var harness = new QueryTestHarness(sqliteConnection, mockConnection);

        // FK enforcement is off by default so DELETE tests don't need to worry about
        // dependent-row ordering.  Tests that specifically verify FK behavior can enable
        // it via SqlAsync("PRAGMA foreign_keys = ON").
        await harness.SqlAsync("PRAGMA foreign_keys = OFF");
        await harness.CreateSchema();
        await harness.SeedData();

        return harness;
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
        My.Dispose();
        Pg.Dispose();
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
