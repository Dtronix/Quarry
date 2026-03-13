using Quarry.Shared.Scaffold;

namespace Quarry.Tests.Scaffold;

[TestFixture]
public class TableFilterTests
{
    private static List<TableMetadata> MakeTables(params string[] names)
    {
        return names.Select(n => new TableMetadata(n, null)).ToList();
    }

    [Test]
    public void Apply_NoPattern_ReturnsAllNonSystemTables()
    {
        var tables = MakeTables("users", "orders", "products");
        var result = TableFilter.Apply(tables, null);
        Assert.That(result, Has.Count.EqualTo(3));
    }

    [Test]
    public void Apply_WildcardPattern_ReturnsAll()
    {
        var tables = MakeTables("users", "orders");
        var result = TableFilter.Apply(tables, "*");
        Assert.That(result, Has.Count.EqualTo(2));
    }

    [Test]
    public void Apply_GlobPattern_FiltersCorrectly()
    {
        var tables = MakeTables("users", "user_roles", "orders", "order_items", "products");
        var result = TableFilter.Apply(tables, "user*,order*");
        Assert.That(result.Select(t => t.Name), Is.EquivalentTo(new[] { "users", "user_roles", "orders", "order_items" }));
    }

    [Test]
    public void Apply_ExcludePattern_ExcludesMatches()
    {
        var tables = MakeTables("users", "user_logs", "user_roles");
        var result = TableFilter.Apply(tables, "user*,!user_logs");
        Assert.That(result.Select(t => t.Name), Is.EquivalentTo(new[] { "users", "user_roles" }));
    }

    [Test]
    public void Apply_AutoExcludesSystemTables()
    {
        var tables = MakeTables("users", "__EFMigrationsHistory", "__quarry_migrations", "sqlite_sequence", "pg_stats");
        var result = TableFilter.Apply(tables, "*");
        Assert.That(result.Select(t => t.Name), Is.EquivalentTo(new[] { "users" }));
    }

    [Test]
    public void Apply_AutoExcludesMigrationFrameworkTables()
    {
        var tables = MakeTables("users", "schema_migrations", "flyway_schema_history", "_prisma_migrations");
        var result = TableFilter.Apply(tables, "*");
        Assert.That(result.Select(t => t.Name), Is.EquivalentTo(new[] { "users" }));
    }

    [Test]
    public void Apply_ExcludeOnlyPattern_IncludesAllExceptExcluded()
    {
        var tables = MakeTables("users", "orders", "audit_log");
        var result = TableFilter.Apply(tables, "!audit_log");
        Assert.That(result.Select(t => t.Name), Is.EquivalentTo(new[] { "users", "orders" }));
    }
}
