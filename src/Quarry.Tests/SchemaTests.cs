namespace Quarry.Tests;

/// <summary>
/// Tests for Schema base class inheritance and column modifier methods.
/// </summary>
public class SchemaTests
{
    // Test schema with default naming style
    private class TestUserSchema : Schema
    {
        public static string Table => "users";
        public static string SchemaName => "public";

        public Key<int> UserId => Identity();
        public Col<string> UserName => Length(100);
        public Col<string?> Email => Length(255)!;
        public Col<bool> IsActive => Default(true);
        public Col<DateTime> CreatedAt => Default(() => DateTime.UtcNow);
        public Col<decimal> Balance => Precision(18, 2);
#pragma warning disable CS0649 // Field is never assigned to
        public Col<int> Flags;
#pragma warning restore CS0649
    }

    // Test schema with snake_case naming
    private class SnakeCaseSchema : Schema
    {
        public static string Table => "snake_table";

        protected override NamingStyle NamingStyle => NamingStyle.SnakeCase;

        public Key<int> UserId => Identity();
    }

    // Test schema with GUID key
    private class GuidKeySchema : Schema
    {
        public static string Table => "products";

        public Key<Guid> ProductId => ClientGenerated();
    }

    // Test schema with computed column
    private class ComputedColumnSchema : Schema
    {
        public static string Table => "order_items";

        public Key<int> Id => Identity();
        public Col<decimal> LineTotal => Computed<decimal>();
    }

    // Test schema with foreign key
    private class OrderSchema : Schema
    {
        public static string Table => "orders";

        public Key<int> OrderId => Identity();
        public Ref<TestUserSchema, int> UserId => ForeignKey<TestUserSchema, int>();
    }

    // Test schema with one-to-many
    private class UserWithOrdersSchema : Schema
    {
        public static string Table => "users";

        public Key<int> UserId => Identity();
        public Many<OrderSchema> Orders => HasMany<OrderSchema>(o => o.OrderId);
    }

    [Test]
    public void Schema_CanBeInherited()
    {
        var schema = new TestUserSchema();

        Assert.That(schema, Is.InstanceOf<Schema>());
    }

    [Test]
    public void Schema_TablePropertyCanBeAccessed()
    {
        Assert.That(TestUserSchema.Table, Is.EqualTo("users"));
    }

    [Test]
    public void Schema_SchemaNamePropertyCanBeAccessed()
    {
        Assert.That(TestUserSchema.SchemaName, Is.EqualTo("public"));
    }

    [Test]
    public void Schema_IdentityMethod_ReturnsKey()
    {
        var schema = new TestUserSchema();

        // Verifies the fluent API compiles and produces a valid Key<int>
        Assert.That(schema.UserId, Is.EqualTo(default(Key<int>)));
    }

    [Test]
    public void Schema_LengthMethod_ReturnsCol()
    {
        var schema = new TestUserSchema();

        Assert.That(schema.UserName, Is.EqualTo(default(Col<string>)));
    }

    [Test]
    public void Schema_PrecisionMethod_ReturnsCol()
    {
        var schema = new TestUserSchema();

        Assert.That(schema.Balance, Is.EqualTo(default(Col<decimal>)));
    }

    [Test]
    public void Schema_DefaultMethod_ReturnsCol()
    {
        var schema = new TestUserSchema();

        Assert.That(schema.IsActive, Is.EqualTo(default(Col<bool>)));
    }

    [Test]
    public void Schema_DefaultFactoryMethod_ReturnsCol()
    {
        var schema = new TestUserSchema();

        Assert.That(schema.CreatedAt, Is.EqualTo(default(Col<DateTime>)));
    }

    [Test]
    public void Schema_UnconfiguredCol_IsDefault()
    {
        var schema = new TestUserSchema();

        Assert.That(schema.Flags, Is.EqualTo(default(Col<int>)));
    }

    [Test]
    public void Schema_NamingStyleDefault_IsExact()
    {
        // We can't directly test the protected property, but we verify
        // that the default schema compiles and works correctly
        var schema = new TestUserSchema();
        Assert.That(schema, Is.Not.Null);
    }

    [Test]
    public void Schema_CanOverrideNamingStyle()
    {
        // This test verifies the snake case schema compiles
        // The actual naming conversion is tested by the generator
        var schema = new SnakeCaseSchema();
        Assert.That(schema, Is.Not.Null);
    }

    [Test]
    public void Schema_ClientGeneratedGuidKey_Works()
    {
        var schema = new GuidKeySchema();

        Assert.That(schema.ProductId, Is.EqualTo(default(Key<Guid>)));
    }

    [Test]
    public void Schema_ComputedColumn_Works()
    {
        var schema = new ComputedColumnSchema();

        Assert.That(schema.LineTotal, Is.EqualTo(default(Col<decimal>)));
    }

    [Test]
    public void Schema_ForeignKey_ReturnsRef()
    {
        var schema = new OrderSchema();

        // Ref has default values when created via ForeignKey
        Assert.That(schema.UserId.Id, Is.EqualTo(0));
        Assert.That(schema.UserId.Value, Is.Null);
    }

    [Test]
    public void Schema_HasMany_ReturnsMany()
    {
        var schema = new UserWithOrdersSchema();

        Assert.That(schema.Orders, Is.EqualTo(default(Many<OrderSchema>)));
    }
}
