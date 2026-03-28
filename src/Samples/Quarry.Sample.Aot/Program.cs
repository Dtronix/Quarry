using Microsoft.Data.Sqlite;
using Quarry.Sample.Aot.Data;

// ── bootstrap ────────────────────────────────────────────────────────
await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();

await using var db = new AotDb(connection);
await db.MigrateAsync(connection);

await SeedAsync(db);

// ── scenarios ────────────────────────────────────────────────────────
var results = new List<(string Name, bool Passed)>();

results.Add(await SelectWithCapturedLocal(db));
results.Add(await WhereWithEnumParameter(db));
results.Add(await WhereWithNullableCheck(db));
results.Add(await WhereWithBoolColumn(db));
results.Add(await WhereWithStringOperations(db));
results.Add(await CollectionContains(db));
results.Add(await JoinProductCategory(db));
results.Add(await NavigationSubqueryAny(db));
results.Add(await NavigationSubqueryCount(db));
results.Add(await InsertAndReturnIdentity(db));
results.Add(await UpdateWithCapturedValue(db));
results.Add(await DeleteWithCapturedValue(db));
results.Add(await CustomTypeMappingRoundTrip(db));
results.Add(await SelectWithDtoProjection(db));
results.Add(await SelectWithTupleProjection(db));

// ── report ───────────────────────────────────────────────────────────
int passed = 0, failed = 0;
Console.WriteLine();
foreach (var (name, ok) in results)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}");
    if (ok) passed++; else failed++;
}

Console.WriteLine();
Console.WriteLine($"{passed} passed, {failed} failed out of {results.Count} scenarios.");
return failed > 0 ? 1 : 0;

// ═══════════════════════════════════════════════════════════════════════
// Seed data
// ═══════════════════════════════════════════════════════════════════════
static async Task SeedAsync(AotDb db)
{
    // Categories
    await db.Categories().Insert(new Category { Name = "Electronics" }).ExecuteNonQueryAsync();
    await db.Categories().Insert(new Category { Name = "Clothing" }).ExecuteNonQueryAsync();
    await db.Categories().Insert(new Category { Name = "Books" }).ExecuteNonQueryAsync();

    // Products
    await db.Products().Insert(new Product
    {
        Name = "Widget A",
        Price = new Money(19.99m),
        IsActive = true,
        Priority = Priority.High,
        Description = "A fine widget",
        CategoryId = 1,
        CreatedAt = DateTime.UtcNow
    }).ExecuteNonQueryAsync();

    await db.Products().Insert(new Product
    {
        Name = "Widget B",
        Price = new Money(29.99m),
        IsActive = true,
        Priority = Priority.Medium,
        Description = null,
        CategoryId = 1,
        CreatedAt = DateTime.UtcNow
    }).ExecuteNonQueryAsync();

    await db.Products().Insert(new Product
    {
        Name = "Gadget C",
        Price = new Money(9.99m),
        IsActive = false,
        Priority = Priority.Low,
        Description = "Budget gadget",
        CategoryId = 2,
        CreatedAt = DateTime.UtcNow
    }).ExecuteNonQueryAsync();

    await db.Products().Insert(new Product
    {
        Name = "Book D",
        Price = new Money(14.99m),
        IsActive = true,
        Priority = Priority.Critical,
        Description = "Must read",
        CategoryId = 3,
        CreatedAt = DateTime.UtcNow
    }).ExecuteNonQueryAsync();

    // Order items
    await db.OrderItems().Insert(new OrderItem { Quantity = 10, UnitPrice = 19.99m, ProductId = 1 }).ExecuteNonQueryAsync();
    await db.OrderItems().Insert(new OrderItem { Quantity = 2, UnitPrice = 19.99m, ProductId = 1 }).ExecuteNonQueryAsync();
    await db.OrderItems().Insert(new OrderItem { Quantity = 1, UnitPrice = 29.99m, ProductId = 2 }).ExecuteNonQueryAsync();
}

// ═══════════════════════════════════════════════════════════════════════
// Scenarios
// ═══════════════════════════════════════════════════════════════════════

// 1. Captured local → FieldInfo cache on carrier
static async Task<(string, bool)> SelectWithCapturedLocal(AotDb db)
{
    int minId = 2;
    var rows = await db.Products()
        .Where(p => p.ProductId > minId)
        .Select(p => p.Name)
        .ExecuteFetchAllAsync();
    return ("Select with captured local", rows.Count == 2);
}

// 2. Enum closure → underlying type cast
static async Task<(string, bool)> WhereWithEnumParameter(AotDb db)
{
    var prio = Priority.High;
    var rows = await db.Products()
        .Where(p => p.Priority == prio)
        .Select(p => p.Name)
        .ExecuteFetchAllAsync();
    return ("Where with enum parameter", rows.Count == 1 && rows[0] == "Widget A");
}

// 3. Nullable check → IS NOT NULL
static async Task<(string, bool)> WhereWithNullableCheck(AotDb db)
{
    var rows = await db.Products()
        .Where(p => p.Description != null)
        .Select(p => p.Name)
        .ExecuteFetchAllAsync();
    return ("Where with nullable check", rows.Count == 3);
}

// 4. Bare bool → col = 1
static async Task<(string, bool)> WhereWithBoolColumn(AotDb db)
{
    var rows = await db.Products()
        .Where(p => p.IsActive)
        .Select(p => p.Name)
        .ExecuteFetchAllAsync();
    return ("Where with bool column", rows.Count == 3);
}

// 5. String Contains/StartsWith → LIKE with closure capture
static async Task<(string, bool)> WhereWithStringOperations(AotDb db)
{
    var prefix = "Widget";
    var rows = await db.Products()
        .Where(p => p.Name.StartsWith(prefix))
        .Select(p => p.Name)
        .ExecuteFetchAllAsync();
    return ("Where with string operations", rows.Count == 2);
}

// 6. Collection Contains → IN clause, ExpressionHelper.ExtractContainsCollection
static async Task<(string, bool)> CollectionContains(AotDb db)
{
    var ids = new[] { 1, 3 };
    var rows = await db.Products()
        .Where(p => ids.Contains(p.ProductId))
        .Select(p => p.Name)
        .ExecuteFetchAllAsync();
    return ("Collection Contains (IN clause)", rows.Count == 2);
}

// 7. Join → JoinedCarrierBase
static async Task<(string, bool)> JoinProductCategory(AotDb db)
{
    var rows = await db.Products()
        .Join<Category>((p, c) => p.CategoryId.Id == c.CategoryId)
        .Select((p, c) => (p.ProductId, c.Name))
        .ExecuteFetchAllAsync();
    return ("Join Product-Category", rows.Count == 4);
}

// 8. Navigation subquery Any → EXISTS
static async Task<(string, bool)> NavigationSubqueryAny(AotDb db)
{
    int minQty = 5;
    var rows = await db.Products()
        .Where(p => p.OrderItems.Any(oi => oi.Quantity > minQty))
        .Select(p => p.Name)
        .ExecuteFetchAllAsync();
    return ("Navigation subquery (Any)", rows.Count == 1 && rows[0] == "Widget A");
}

// 9. Navigation subquery Count → scalar COUNT
static async Task<(string, bool)> NavigationSubqueryCount(AotDb db)
{
    var rows = await db.Products()
        .Where(p => p.OrderItems.Count() > 0)
        .Select(p => p.Name)
        .ExecuteFetchAllAsync();
    return ("Navigation subquery (Count)", rows.Count == 2);
}

// 10. Insert + ExecuteScalar → identity return
static async Task<(string, bool)> InsertAndReturnIdentity(AotDb db)
{
    var id = await db.Categories().Insert(new Category { Name = "Test Category" }).ExecuteScalarAsync<int>();
    return ("Insert + ExecuteScalar identity", id == 4);
}

// 11. Update with captured value → UpdateSetAction FieldInfo pattern
static async Task<(string, bool)> UpdateWithCapturedValue(AotDb db)
{
    var newName = "Updated Widget A";
    int targetId = 1;
    await db.Products().Update()
        .Set(p => p.Name = newName)
        .Where(p => p.ProductId == targetId)
        .ExecuteNonQueryAsync();

    var name = await db.Products()
        .Where(p => p.ProductId == targetId)
        .Select(p => p.Name)
        .ExecuteFetchFirstAsync();

    // Restore original
    var originalName = "Widget A";
    await db.Products().Update()
        .Set(p => p.Name = originalName)
        .Where(p => p.ProductId == targetId)
        .ExecuteNonQueryAsync();

    return ("Update with captured value", name == "Updated Widget A");
}

// 12. Delete with captured value → DeleteCarrier with closure
static async Task<(string, bool)> DeleteWithCapturedValue(AotDb db)
{
    // Insert a disposable row
    var id = await db.Products().Insert(new Product
    {
        Name = "ToDelete",
        Price = new Money(0m),
        IsActive = false,
        Priority = Priority.Low,
        CategoryId = 1,
        CreatedAt = DateTime.UtcNow
    }).ExecuteScalarAsync<int>();

    await db.Products().Delete().Where(p => p.ProductId == id).ExecuteNonQueryAsync();

    var remaining = await db.Products()
        .Where(p => p.ProductId == id)
        .Select(p => p.ProductId)
        .ExecuteFetchAllAsync();
    return ("Delete with captured value", remaining.Count == 0);
}

// 13. Custom TypeMapping round-trip → MoneyMapping.ToDb / FromDb
static async Task<(string, bool)> CustomTypeMappingRoundTrip(AotDb db)
{
    var product = await db.Products()
        .Where(p => p.ProductId == 1)
        .Select(p => p)
        .ExecuteFetchFirstAsync();
    return ("Custom TypeMapping round-trip", product.Price.Amount == 19.99m);
}

// 14. DTO projection
static async Task<(string, bool)> SelectWithDtoProjection(AotDb db)
{
    var rows = await db.Products()
        .Where(p => p.IsActive)
        .Select(p => new ProductSummary { Name = p.Name, Price = p.Price })
        .ExecuteFetchAllAsync();
    return ("Select with DTO projection", rows.Count == 3 && rows[0].Name.Length > 0);
}

// 15. Tuple projection
static async Task<(string, bool)> SelectWithTupleProjection(AotDb db)
{
    var rows = await db.Categories()
        .Where(c => c.CategoryId > 0)
        .Select(c => (c.CategoryId, c.Name))
        .OrderBy(c => c.Name)
        .ExecuteFetchAllAsync();
    return ("Select with tuple projection", rows.Count >= 3 && rows[0].Name == "Books");
}

// ═══════════════════════════════════════════════════════════════════════
// DTO for projection scenario
// ═══════════════════════════════════════════════════════════════════════
public class ProductSummary
{
    public string Name { get; set; } = "";
    public Money Price { get; set; }
}
