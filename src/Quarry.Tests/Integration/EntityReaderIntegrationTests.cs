using Microsoft.Data.Sqlite;
using Quarry.Tests.Samples;

namespace Quarry.Tests.Integration;

#pragma warning disable QRY001

/// <summary>
/// End-to-end SQLite integration tests for EntityReader&lt;T&gt; custom materialization.
/// Verifies that entities annotated with [EntityReader] are materialized via the custom reader
/// instead of the auto-generated ordinal-based reader.
/// </summary>
[TestFixture]
internal class EntityReaderIntegrationTests
{
    private SqliteConnection _connection = null!;
    private TestDbContext _db = null!;

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
        _db = new TestDbContext(_connection);
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
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
            CREATE TABLE "products" (
                "ProductId" INTEGER PRIMARY KEY,
                "ProductName" TEXT NOT NULL,
                "Price" REAL NOT NULL,
                "Description" TEXT
            )
            """);
    }

    private async Task SeedData()
    {
        await ExecuteSqlAsync("""
            INSERT INTO "products" ("ProductId", "ProductName", "Price", "Description") VALUES
                (1, 'Widget',  29.99, 'A fine widget'),
                (2, 'Gadget',  49.50, NULL),
                (3, 'Doohickey', 9.95, 'Budget option')
            """);
    }

    #region Select(u => u) — Identity Entity Projection

    [Test]
    public async Task Select_IdentityProjection_UsesCustomReader()
    {
        var results = await _db.Products()
            .Where(p => p.ProductId == 1)
            .Select(p => p)
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(1));

        // DisplayLabel is only set by the custom ProductReader — the generated reader
        // would never populate it since it's not a schema column.
        Assert.That(results[0].DisplayLabel, Is.EqualTo("[Widget] $29.99"),
            "DisplayLabel proves the custom EntityReader ran");
        Assert.That(results[0].ProductId, Is.EqualTo(1));
        Assert.That(results[0].ProductName, Is.EqualTo("Widget"));
        Assert.That(results[0].Price, Is.EqualTo(29.99m));
        Assert.That(results[0].Description, Is.EqualTo("A fine widget"));
    }

    [Test]
    public async Task Select_IdentityProjection_HandlesNullColumns()
    {
        var results = await _db.Products()
            .Where(p => p.ProductId == 2)
            .Select(p => p)
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].ProductName, Is.EqualTo("Gadget"));
        Assert.That(results[0].Description, Is.Null,
            "Custom reader should handle null columns correctly");
        Assert.That(results[0].DisplayLabel, Is.EqualTo("[Gadget] $49.50"));
    }

    [Test]
    public async Task Select_IdentityProjection_MultipleRows_AllHaveDisplayLabel()
    {
        var results = await _db.Products()
            .Where(p => p.ProductId <= 3)
            .Select(p => p)
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(3));
        foreach (var product in results)
        {
            Assert.That(product.DisplayLabel, Is.Not.Empty,
                $"Product {product.ProductId} should have DisplayLabel set by custom reader");
            Assert.That(product.DisplayLabel, Does.StartWith("["),
                "DisplayLabel should follow the custom reader's format");
        }
    }

    #endregion

    #region Select(p => p) with Where — Filtered Entity Projection

    [Test]
    public async Task Select_IdentityWithWhere_UsesCustomReader()
    {
        // Filter to seeded rows only to avoid interference from round-trip insert test
        var results = await _db.Products()
            .Where(p => p.ProductId == 1)
            .Select(p => p)
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].DisplayLabel, Is.Not.Empty,
            "Filtered product should have DisplayLabel from custom reader");
        Assert.That(results[0].ProductName, Is.EqualTo("Widget"));
    }

    #endregion

    #region Tuple/DTO Projections — Custom Reader Should NOT Apply

    [Test]
    public async Task Select_TupleProjection_DoesNotUseCustomReader()
    {
        var results = await _db.Products()
            .Where(p => p.ProductId <= 3)
            .Select(p => (p.ProductId, p.ProductName, p.Price))
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0].ProductId, Is.EqualTo(1));
        Assert.That(results[0].ProductName, Is.EqualTo("Widget"));
        Assert.That(results[0].Price, Is.EqualTo(29.99m));
    }

    [Test]
    public async Task Select_SingleColumn_DoesNotUseCustomReader()
    {
        var results = await _db.Products()
            .Where(p => p.ProductId <= 3)
            .Select(p => p.ProductName)
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results, Does.Contain("Widget"));
        Assert.That(results, Does.Contain("Gadget"));
        Assert.That(results, Does.Contain("Doohickey"));
    }

    #endregion

    #region ExecuteFetchFirst / ExecuteFetchFirstOrDefault

    [Test]
    public async Task ExecuteFetchFirst_UsesCustomReader()
    {
        var product = await _db.Products()
            .Where(p => p.ProductId == 1)
            .Select(p => p)
            .ExecuteFetchFirstAsync();

        Assert.That(product.DisplayLabel, Is.EqualTo("[Widget] $29.99"),
            "ExecuteFetchFirst should use the custom reader");
    }

    [Test]
    public async Task ExecuteFetchFirstOrDefault_UsesCustomReader()
    {
        var product = await _db.Products()
            .Where(p => p.ProductId == 3)
            .Select(p => p)
            .ExecuteFetchFirstOrDefaultAsync();

        Assert.That(product, Is.Not.Null);
        Assert.That(product!.DisplayLabel, Is.EqualTo("[Doohickey] $9.95"),
            "ExecuteFetchFirstOrDefault should use the custom reader");
    }

    #endregion

    #region Insert + Select Round-Trip

    [Test]
    public async Task RoundTrip_InsertThenSelectEntity_UsesCustomReader()
    {
        await _db.Insert(new Product
        {
            ProductName = "Thingamajig",
            Price = 199.99m,
            Description = "Premium item"
        }).ExecuteNonQueryAsync();

        var results = await _db.Products()
            .Where(p => p.ProductName == "Thingamajig")
            .Select(p => p)
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Price, Is.EqualTo(199.99m));
        Assert.That(results[0].Description, Is.EqualTo("Premium item"));
        Assert.That(results[0].DisplayLabel, Is.EqualTo("[Thingamajig] $199.99"),
            "Round-tripped entity should be materialized by custom reader");
    }

    #endregion
}
