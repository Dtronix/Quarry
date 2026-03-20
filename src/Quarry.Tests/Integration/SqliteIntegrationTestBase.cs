using Microsoft.Data.Sqlite;
using Quarry.Tests.Samples;

namespace Quarry.Tests.Integration;


/// <summary>
/// Base class for SQLite-backed integration tests.
/// </summary>
/// <remarks>
/// Known test infrastructure limitations:
/// <list type="bullet">
/// <item>RIGHT JOIN is not supported by SQLite — use CrossDialectJoinTests for SQL verification.</item>
/// <item>Navigation joins (Join(u => u.Orders)) require compile-time interceptors that resolve FK
///   relationships; the runtime path does not resolve navigation properties.</item>
///<item>Multi-dialect execution testing would require PostgreSQL/MySQL/SQL Server instances.</item>
/// </list>
/// </remarks>
internal abstract class SqliteIntegrationTestBase
{
    private SqliteConnection _connection = null!;
    protected TestDbContext Db = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        await CreateSchema();
        await SeedData();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _connection.DisposeAsync();
    }

    [SetUp]
    public void SetUp()
    {
        Db = new TestDbContext(_connection);
    }

    [TearDown]
    public void TearDown()
    {
        Db.Dispose();
    }

    private async Task ExecuteSqlAsync(string sql)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task CreateSchema()
    {
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

        // View to alias "orders" as "Order" for join table name compatibility.
        // The join interceptor uses the entity class name ("Order") as the table name.
        await ExecuteSqlAsync("""
            CREATE VIEW "Order" AS SELECT * FROM "orders"
            """);
    }

    private async Task SeedData()
    {
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

        await ExecuteSqlAsync("""
            INSERT INTO "order_items" ("OrderItemId", "OrderId", "ProductName", "Quantity", "UnitPrice", "LineTotal") VALUES
                (1, 1, 'Widget', 2, 125.00, 250.00),
                (2, 2, 'Gadget', 1, 75.50,  75.50),
                (3, 3, 'Widget', 3, 50.00,  150.00)
            """);
    }
}
