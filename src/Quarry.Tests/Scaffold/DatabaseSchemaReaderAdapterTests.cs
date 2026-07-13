using System.Linq;
using Microsoft.Data.Sqlite;
using Quarry.Shared.Migration;
using Quarry.Tool.Schema;

namespace Quarry.Tests.Scaffold;

/// <summary>
/// Tests the introspection-metadata -> <see cref="SchemaSnapshot"/> adapter
/// (<see cref="DatabaseSchemaReader.ToSnapshot"/>, step 1b) against a real SQLite
/// database, verifying CLR-type recovery, column-kind inference, FK-action
/// conversion, PK-index skipping, and composite-key derivation.
/// </summary>
[TestFixture]
public class DatabaseSchemaReaderAdapterTests
{
    private string _dbPath = null!;
    private string _connectionString = null!;

    [SetUp]
    public void SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"quarry_adapter_{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_dbPath}";

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE customers (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                first_name TEXT NOT NULL,
                email TEXT,
                is_active INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE orders (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                customer_id INTEGER NOT NULL,
                total REAL NOT NULL DEFAULT 0.0,
                FOREIGN KEY (customer_id) REFERENCES customers(id) ON DELETE CASCADE
            );

            CREATE TABLE order_items (
                order_id INTEGER NOT NULL,
                product_id INTEGER NOT NULL,
                quantity INTEGER NOT NULL DEFAULT 1,
                PRIMARY KEY (order_id, product_id)
            );

            CREATE INDEX idx_orders_customer ON orders(customer_id);
            CREATE UNIQUE INDEX idx_customers_email ON customers(email);
        ";
        cmd.ExecuteNonQuery();
    }

    [TearDown]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    private async Task<SchemaSnapshot> BuildSnapshotAsync()
    {
        var tables = await DatabaseSchemaReader.ReadTablesAsync("sqlite", _connectionString, null, null);
        return DatabaseSchemaReader.ToSnapshot(tables, "sqlite", 1, "InitialFromDatabase");
    }

    [Test]
    public async Task ToSnapshot_SetsVersionNameAndNamingStyle()
    {
        var snapshot = await BuildSnapshotAsync();

        Assert.That(snapshot.Version, Is.EqualTo(1));
        Assert.That(snapshot.Name, Is.EqualTo("InitialFromDatabase"));
        Assert.That(snapshot.Tables.All(t => t.NamingStyle == NamingStyleKind.Exact), Is.True);
    }

    [Test]
    public async Task ToSnapshot_IdentityPrimaryKey_IsPrimaryKeyKindAndIdentity()
    {
        var snapshot = await BuildSnapshotAsync();
        var customers = snapshot.Tables.Single(t => t.TableName == "customers");
        var id = customers.Columns.Single(c => c.Name == "id");

        Assert.That(id.Kind, Is.EqualTo(ColumnKind.PrimaryKey));
        Assert.That(id.IsIdentity, Is.True);
        Assert.That(id.ClrType, Is.Not.Empty);
    }

    [Test]
    public async Task ToSnapshot_TextColumns_MapToStringWithCorrectNullability()
    {
        var snapshot = await BuildSnapshotAsync();
        var customers = snapshot.Tables.Single(t => t.TableName == "customers");

        var firstName = customers.Columns.Single(c => c.Name == "first_name");
        Assert.That(firstName.ClrType, Is.EqualTo("string"));
        Assert.That(firstName.IsNullable, Is.False);
        Assert.That(firstName.Kind, Is.EqualTo(ColumnKind.Standard));

        var email = customers.Columns.Single(c => c.Name == "email");
        Assert.That(email.ClrType, Is.EqualTo("string"));
        Assert.That(email.IsNullable, Is.True);
    }

    [Test]
    public async Task ToSnapshot_DefaultExpression_SetsHasDefault()
    {
        var snapshot = await BuildSnapshotAsync();
        var customers = snapshot.Tables.Single(t => t.TableName == "customers");
        var isActive = customers.Columns.Single(c => c.Name == "is_active");

        Assert.That(isActive.HasDefault, Is.True);
        Assert.That(isActive.DefaultExpression, Is.Not.Null);
    }

    [Test]
    public async Task ToSnapshot_ForeignKeyColumn_IsForeignKeyKindWithCascade()
    {
        var snapshot = await BuildSnapshotAsync();
        var orders = snapshot.Tables.Single(t => t.TableName == "orders");

        var customerId = orders.Columns.Single(c => c.Name == "customer_id");
        Assert.That(customerId.Kind, Is.EqualTo(ColumnKind.ForeignKey));

        var fk = orders.ForeignKeys.Single(f => f.ColumnName == "customer_id");
        Assert.That(fk.ReferencedTable, Is.EqualTo("customers"));
        Assert.That(fk.ReferencedColumn, Is.EqualTo("id"));
        Assert.That(fk.OnDelete, Is.EqualTo(ForeignKeyAction.Cascade));
        Assert.That(fk.ConstraintName, Is.Not.Empty);
    }

    [Test]
    public async Task ToSnapshot_SkipsPrimaryKeyBackingIndexes_KeepsUserIndexes()
    {
        var snapshot = await BuildSnapshotAsync();

        var orders = snapshot.Tables.Single(t => t.TableName == "orders");
        Assert.That(orders.Indexes.Any(i => i.Name == "idx_orders_customer"), Is.True);

        var customers = snapshot.Tables.Single(t => t.TableName == "customers");
        var emailIdx = customers.Indexes.Single(i => i.Name == "idx_customers_email");
        Assert.That(emailIdx.IsUnique, Is.True);

        // No PK-backing (auto) index should leak in as a normal index.
        Assert.That(
            snapshot.Tables.SelectMany(t => t.Indexes).Any(i => i.Name.StartsWith("sqlite_autoindex")),
            Is.False);
    }

    [Test]
    public async Task ToSnapshot_CompositePrimaryKey_PopulatesCompositeKeyColumns()
    {
        var snapshot = await BuildSnapshotAsync();
        var orderItems = snapshot.Tables.Single(t => t.TableName == "order_items");

        Assert.That(orderItems.CompositeKeyColumns, Is.Not.Null);
        Assert.That(orderItems.CompositeKeyColumns!.Count, Is.EqualTo(2));
        Assert.That(orderItems.CompositeKeyColumns, Does.Contain("order_id"));
        Assert.That(orderItems.CompositeKeyColumns, Does.Contain("product_id"));

        var orderId = orderItems.Columns.Single(c => c.Name == "order_id");
        Assert.That(orderId.Kind, Is.EqualTo(ColumnKind.PrimaryKey));
    }
}
