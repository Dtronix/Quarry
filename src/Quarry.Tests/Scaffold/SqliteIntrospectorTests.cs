using Microsoft.Data.Sqlite;
using Quarry.Shared.Scaffold;

namespace Quarry.Tests.Scaffold;

[TestFixture]
public class SqliteIntrospectorTests
{
    private string _dbPath = null!;
    private string _connectionString = null!;

    [SetUp]
    public void SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"quarry_test_{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_dbPath}";

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE customers (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                first_name TEXT NOT NULL,
                last_name TEXT NOT NULL,
                email TEXT,
                is_active INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE orders (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                customer_id INTEGER NOT NULL,
                order_date TEXT NOT NULL,
                total REAL NOT NULL DEFAULT 0.0,
                FOREIGN KEY (customer_id) REFERENCES customers(id) ON DELETE CASCADE
            );

            CREATE TABLE order_items (
                order_id INTEGER NOT NULL,
                product_id INTEGER NOT NULL,
                quantity INTEGER NOT NULL DEFAULT 1,
                PRIMARY KEY (order_id, product_id),
                FOREIGN KEY (order_id) REFERENCES orders(id),
                FOREIGN KEY (product_id) REFERENCES products(id)
            );

            CREATE TABLE products (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                price REAL NOT NULL
            );

            CREATE INDEX idx_orders_customer ON orders(customer_id);
            CREATE UNIQUE INDEX idx_orders_date_customer ON orders(order_date, customer_id);
        ";
        cmd.ExecuteNonQuery();
    }

    [TearDown]
    public void TearDown()
    {
        // Clear SQLite connection pool to release file locks
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    [Test]
    public async Task GetTablesAsync_ReturnsAllTables()
    {
        using var introspector = await SqliteIntrospector.CreateAsync(_connectionString);
        var tables = await introspector.GetTablesAsync(null);

        Assert.That(tables.Select(t => t.Name),
            Is.EquivalentTo(new[] { "customers", "orders", "order_items", "products" }));
    }

    [Test]
    public async Task GetColumnsAsync_ReturnsCorrectColumns()
    {
        using var introspector = await SqliteIntrospector.CreateAsync(_connectionString);
        var columns = await introspector.GetColumnsAsync("customers", null);

        Assert.That(columns, Has.Count.EqualTo(5));
        Assert.Multiple(() =>
        {
            Assert.That(columns[0].Name, Is.EqualTo("id"));
            Assert.That(columns[0].DataType, Is.EqualTo("INTEGER"));
            Assert.That(columns[0].IsIdentity, Is.True);
            Assert.That(columns[0].IsNullable, Is.False);

            Assert.That(columns[1].Name, Is.EqualTo("first_name"));
            Assert.That(columns[1].DataType, Is.EqualTo("TEXT"));
            Assert.That(columns[1].IsNullable, Is.False);

            Assert.That(columns[3].Name, Is.EqualTo("email"));
            Assert.That(columns[3].IsNullable, Is.True);

            Assert.That(columns[4].Name, Is.EqualTo("is_active"));
            Assert.That(columns[4].DefaultExpression, Is.EqualTo("1"));
        });
    }

    [Test]
    public async Task GetPrimaryKeyAsync_SingleColumn_ReturnsCorrectly()
    {
        using var introspector = await SqliteIntrospector.CreateAsync(_connectionString);
        var pk = await introspector.GetPrimaryKeyAsync("customers", null);

        Assert.That(pk, Is.Not.Null);
        Assert.That(pk!.Columns, Is.EqualTo(new[] { "id" }));
    }

    [Test]
    public async Task GetPrimaryKeyAsync_CompositeKey_ReturnsBothColumns()
    {
        using var introspector = await SqliteIntrospector.CreateAsync(_connectionString);
        var pk = await introspector.GetPrimaryKeyAsync("order_items", null);

        Assert.That(pk, Is.Not.Null);
        Assert.That(pk!.Columns, Has.Count.EqualTo(2));
        Assert.That(pk.Columns, Is.EquivalentTo(new[] { "order_id", "product_id" }));
    }

    [Test]
    public async Task GetForeignKeysAsync_ReturnsForeignKeys()
    {
        using var introspector = await SqliteIntrospector.CreateAsync(_connectionString);
        var fks = await introspector.GetForeignKeysAsync("orders", null);

        Assert.That(fks, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(fks[0].ColumnName, Is.EqualTo("customer_id"));
            Assert.That(fks[0].ReferencedTable, Is.EqualTo("customers"));
            Assert.That(fks[0].ReferencedColumn, Is.EqualTo("id"));
            Assert.That(fks[0].OnDelete, Is.EqualTo("CASCADE"));
        });
    }

    [Test]
    public async Task GetForeignKeysAsync_MultipleFks_ReturnsAll()
    {
        using var introspector = await SqliteIntrospector.CreateAsync(_connectionString);
        var fks = await introspector.GetForeignKeysAsync("order_items", null);

        Assert.That(fks, Has.Count.EqualTo(2));
        Assert.That(fks.Select(fk => fk.ColumnName), Is.EquivalentTo(new[] { "order_id", "product_id" }));
    }

    [Test]
    public async Task GetIndexesAsync_ReturnsIndexes()
    {
        using var introspector = await SqliteIntrospector.CreateAsync(_connectionString);
        var indexes = await introspector.GetIndexesAsync("orders", null);

        // Should have idx_orders_customer (non-unique) and idx_orders_date_customer (unique)
        // Plus possibly the autoindex for PK
        var namedIndexes = indexes.Where(i => !i.IsPrimaryKey).ToList();

        Assert.That(namedIndexes, Has.Count.GreaterThanOrEqualTo(2));

        var customerIdx = namedIndexes.FirstOrDefault(i => i.Name == "idx_orders_customer");
        Assert.That(customerIdx, Is.Not.Null);
        Assert.That(customerIdx!.IsUnique, Is.False);
        Assert.That(customerIdx.Columns, Is.EqualTo(new[] { "customer_id" }));

        var compositeIdx = namedIndexes.FirstOrDefault(i => i.Name == "idx_orders_date_customer");
        Assert.That(compositeIdx, Is.Not.Null);
        Assert.That(compositeIdx!.IsUnique, Is.True);
        Assert.That(compositeIdx.Columns, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task EndToEnd_FullScaffoldWorkflow()
    {
        using var introspector = await SqliteIntrospector.CreateAsync(_connectionString);

        var tables = await introspector.GetTablesAsync(null);
        tables = TableFilter.Apply(tables, "*");

        Assert.That(tables, Has.Count.EqualTo(4));

        // Verify we can introspect all tables without errors
        foreach (var table in tables)
        {
            var columns = await introspector.GetColumnsAsync(table.Name, null);
            var pk = await introspector.GetPrimaryKeyAsync(table.Name, null);
            var fks = await introspector.GetForeignKeysAsync(table.Name, null);
            var indexes = await introspector.GetIndexesAsync(table.Name, null);

            Assert.That(columns, Has.Count.GreaterThan(0), $"Table {table.Name} should have columns");

            // Type mapping should work for all columns
            foreach (var col in columns)
            {
                var isPk = pk?.Columns.Any(c => c.Equals(col.Name, StringComparison.OrdinalIgnoreCase)) ?? false;
                var result = ReverseTypeMapper.MapSqlType(col.DataType, "sqlite", col.Name, col.IsNullable, col.IsIdentity, isPk);
                Assert.That(result.ClrType, Is.Not.Null.And.Not.Empty, $"Type mapping failed for {table.Name}.{col.Name}");
            }
        }
    }
}
