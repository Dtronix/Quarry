using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Quarry.Migration;

namespace Quarry.Migration.Tests;

[TestFixture]
public class SchemaResolverTests
{
    private static Compilation CreateCompilation(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);

        // Minimal Quarry types needed for schema resolution
        var quarryTypes = CSharpSyntaxTree.ParseText(@"
namespace Quarry
{
    public abstract class Schema
    {
        protected virtual NamingStyle NamingStyle => NamingStyle.Exact;
        protected static ColumnBuilder<T> Identity<T>() => default;
        protected static ColumnBuilder<T> Length<T>(int maxLength) => default;
        protected static ColumnBuilder<T> Precision<T>(int precision, int scale) => default;
        protected static ColumnBuilder<T> Default<T>(T value) => default;
        protected static ColumnBuilder<T> Default<T>(System.Func<T> factory) => default;
        protected static ColumnBuilder<T> MapTo<T>(string columnName) => default;
        protected static ColumnBuilder<T> Mapped<T, TMapping>() => default;
        protected static RefBuilder<TEntity, TKey> ForeignKey<TEntity, TKey>() where TEntity : Schema => default;
    }

    public enum NamingStyle { Exact = 0, SnakeCase = 1, CamelCase = 2, LowerCase = 3 }

    public readonly struct Col<T>
    {
        public static implicit operator Col<T>(ColumnBuilder<T> builder) => default;
    }

    public readonly struct Key<T>
    {
        public static implicit operator Key<T>(ColumnBuilder<T> builder) => default;
    }

    public readonly struct Ref<TEntity, TKey> where TEntity : Schema
    {
        public TKey Id { get; init; }
        public static implicit operator Ref<TEntity, TKey>(RefBuilder<TEntity, TKey> builder) => default;
    }

    public readonly struct ColumnBuilder<T>
    {
        public ColumnBuilder<T> Identity() => default;
        public ColumnBuilder<T> Length(int maxLength) => default;
        public ColumnBuilder<T> Precision(int precision, int scale) => default;
        public ColumnBuilder<T> Default(T value) => default;
        public ColumnBuilder<T> MapTo(string columnName) => default;
        public ColumnBuilder<T> Mapped<TMapping>() => default;
    }

    public readonly struct RefBuilder<TEntity, TKey> where TEntity : Schema
    {
        public RefBuilder<TEntity, TKey> ForeignKey() => default;
        public RefBuilder<TEntity, TKey> MapTo(string columnName) => default;
    }
}
");

        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Func<>).Assembly.Location),
        };

        // Add runtime assemblies for System.Runtime
        var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var runtimeRef = MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Runtime.dll"));

        return CSharpCompilation.Create(
            "TestAssembly",
            new[] { tree, quarryTypes },
            references.Append(runtimeRef),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    [Test]
    public void Resolve_BasicSchema_ExtractsTableAndColumns()
    {
        var compilation = CreateCompilation(@"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";

    public Key<int> UserId => Identity<int>();
    public Col<string> UserName => Length<string>(100);
    public Col<bool> IsActive => Default(true);
}
");

        var resolver = new SchemaResolver();
        var map = resolver.Resolve(compilation);

        Assert.That(map.TryGetEntity("users", out var entity), Is.True);
        Assert.That(entity.ClassName, Is.EqualTo("UserSchema"));
        Assert.That(entity.AccessorName, Is.EqualTo("Users"));
        Assert.That(entity.TryGetProperty("UserId", out var prop1), Is.True);
        Assert.That(prop1, Is.EqualTo("UserId"));
        Assert.That(entity.TryGetProperty("UserName", out var prop2), Is.True);
        Assert.That(prop2, Is.EqualTo("UserName"));
        Assert.That(entity.TryGetProperty("IsActive", out var prop3), Is.True);
        Assert.That(prop3, Is.EqualTo("IsActive"));
    }

    [Test]
    public void Resolve_CaseInsensitiveTableLookup()
    {
        var compilation = CreateCompilation(@"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity<int>();
}
");

        var resolver = new SchemaResolver();
        var map = resolver.Resolve(compilation);

        Assert.That(map.TryGetEntity("USERS", out _), Is.True);
        Assert.That(map.TryGetEntity("Users", out _), Is.True);
    }

    [Test]
    public void Resolve_CaseInsensitiveColumnLookup()
    {
        var compilation = CreateCompilation(@"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity<int>();
}
");

        var resolver = new SchemaResolver();
        var map = resolver.Resolve(compilation);

        Assert.That(map.TryGetEntity("users", out var entity), Is.True);
        Assert.That(entity.TryGetProperty("userid", out var prop), Is.True);
        Assert.That(prop, Is.EqualTo("UserId"));
    }

    [Test]
    public void Resolve_SnakeCaseNamingStyle()
    {
        var compilation = CreateCompilation(@"
using Quarry;

public class EmployeeSchema : Schema
{
    public static string Table => ""employees"";
    protected override NamingStyle NamingStyle => NamingStyle.SnakeCase;

    public Key<int> EmployeeId => Identity<int>();
    public Col<string> FirstName => Length<string>(50);
    public Col<string> LastName => Length<string>(50);
}
");

        var resolver = new SchemaResolver();
        var map = resolver.Resolve(compilation);

        Assert.That(map.TryGetEntity("employees", out var entity), Is.True);
        // SnakeCase: EmployeeId → employee_id
        Assert.That(entity.TryGetProperty("employee_id", out var prop1), Is.True);
        Assert.That(prop1, Is.EqualTo("EmployeeId"));
        Assert.That(entity.TryGetProperty("first_name", out var prop2), Is.True);
        Assert.That(prop2, Is.EqualTo("FirstName"));
    }

    [Test]
    public void Resolve_MapToOverridesNamingStyle()
    {
        var compilation = CreateCompilation(@"
using Quarry;

public class AccountSchema : Schema
{
    public static string Table => ""accounts"";

    public Key<int> AccountId => Identity<int>();
    public Col<decimal> CreditLimit => Precision<decimal>(18, 2).MapTo(""credit_limit"");
}
");

        var resolver = new SchemaResolver();
        var map = resolver.Resolve(compilation);

        Assert.That(map.TryGetEntity("accounts", out var entity), Is.True);
        // MapTo takes priority
        Assert.That(entity.TryGetProperty("credit_limit", out var prop), Is.True);
        Assert.That(prop, Is.EqualTo("CreditLimit"));
        // Default naming (Exact) for AccountId
        Assert.That(entity.TryGetProperty("AccountId", out _), Is.True);
    }

    [Test]
    public void Resolve_ForeignKeyColumns()
    {
        var compilation = CreateCompilation(@"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity<int>();
}

public class OrderSchema : Schema
{
    public static string Table => ""orders"";
    public Key<int> OrderId => Identity<int>();
    public Ref<UserSchema, int> UserId => ForeignKey<UserSchema, int>();
    public Col<decimal> Total => Precision<decimal>(18, 2);
}
");

        var resolver = new SchemaResolver();
        var map = resolver.Resolve(compilation);

        Assert.That(map.TryGetEntity("orders", out var entity), Is.True);
        Assert.That(entity.TryGetProperty("OrderId", out _), Is.True);
        Assert.That(entity.TryGetProperty("UserId", out _), Is.True);
        Assert.That(entity.TryGetProperty("Total", out _), Is.True);
    }

    [Test]
    public void Resolve_MultipleSchemas()
    {
        var compilation = CreateCompilation(@"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity<int>();
}

public class OrderSchema : Schema
{
    public static string Table => ""orders"";
    public Key<int> OrderId => Identity<int>();
}

public class ProductSchema : Schema
{
    public static string Table => ""products"";
    public Key<int> ProductId => Identity<int>();
}
");

        var resolver = new SchemaResolver();
        var map = resolver.Resolve(compilation);

        Assert.That(map.TryGetEntity("users", out _), Is.True);
        Assert.That(map.TryGetEntity("orders", out _), Is.True);
        Assert.That(map.TryGetEntity("products", out _), Is.True);
    }

    [Test]
    public void Resolve_NoTableProperty_SkipsSchema()
    {
        var compilation = CreateCompilation(@"
using Quarry;

public class BrokenSchema : Schema
{
    public Key<int> Id => Identity<int>();
}
");

        var resolver = new SchemaResolver();
        var map = resolver.Resolve(compilation);

        Assert.That(map.Entities.Count(), Is.EqualTo(0));
    }

    [Test]
    public void Resolve_AbstractSchema_Skipped()
    {
        var compilation = CreateCompilation(@"
using Quarry;

public abstract class BaseSchema : Schema
{
    public static string Table => ""base"";
    public Key<int> Id => Identity<int>();
}
");

        var resolver = new SchemaResolver();
        var map = resolver.Resolve(compilation);

        Assert.That(map.Entities.Count(), Is.EqualTo(0));
    }

    [Test]
    public void Resolve_AutoPropertyColumns()
    {
        var compilation = CreateCompilation(@"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity<int>();
    public Col<string?> Email { get; }
    public Col<System.DateTime?> LastLogin { get; }
}
");

        var resolver = new SchemaResolver();
        var map = resolver.Resolve(compilation);

        Assert.That(map.TryGetEntity("users", out var entity), Is.True);
        Assert.That(entity.TryGetProperty("Email", out _), Is.True);
        Assert.That(entity.TryGetProperty("LastLogin", out _), Is.True);
    }

    [Test]
    public void Resolve_SchemaName_Extracted()
    {
        var compilation = CreateCompilation(@"
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public static string SchemaName => ""dbo"";
    public Key<int> UserId => Identity<int>();
}
");

        var resolver = new SchemaResolver();
        var map = resolver.Resolve(compilation);

        Assert.That(map.TryGetEntity("users", out var entity), Is.True);
        Assert.That(entity.SchemaName, Is.EqualTo("dbo"));
    }

    [Test]
    public void DeriveAccessorName_StripsSchemaSuffix()
    {
        Assert.That(SchemaResolver.DeriveAccessorName("UserSchema"), Is.EqualTo("Users"));
        Assert.That(SchemaResolver.DeriveAccessorName("OrderSchema"), Is.EqualTo("Orders"));
        Assert.That(SchemaResolver.DeriveAccessorName("OrderItemSchema"), Is.EqualTo("OrderItems"));
    }

    [Test]
    public void DeriveAccessorName_AlreadyPlural()
    {
        Assert.That(SchemaResolver.DeriveAccessorName("AddressSchema"), Is.EqualTo("Address"));
    }

    [Test]
    public void DeriveAccessorName_NoSchemaSuffix()
    {
        Assert.That(SchemaResolver.DeriveAccessorName("Product"), Is.EqualTo("Products"));
    }

    [Test]
    public void ApplyNamingStyle_SnakeCase()
    {
        Assert.That(SchemaResolver.ToSnakeCase("UserId"), Is.EqualTo("user_id"));
        Assert.That(SchemaResolver.ToSnakeCase("FirstName"), Is.EqualTo("first_name"));
        Assert.That(SchemaResolver.ToSnakeCase("IsActive"), Is.EqualTo("is_active"));
    }

    [Test]
    public void ApplyNamingStyle_CamelCase()
    {
        Assert.That(SchemaResolver.ToCamelCase("UserId"), Is.EqualTo("userId"));
        Assert.That(SchemaResolver.ToCamelCase("FirstName"), Is.EqualTo("firstName"));
    }

    [Test]
    public void ApplyNamingStyle_LowerCase()
    {
        Assert.That(SchemaResolver.ApplyNamingStyle("UserId", NamingStyle.LowerCase), Is.EqualTo("userid"));
    }

    [Test]
    public void ApplyNamingStyle_Exact()
    {
        Assert.That(SchemaResolver.ApplyNamingStyle("UserId", NamingStyle.Exact), Is.EqualTo("UserId"));
    }

    [Test]
    public void Resolve_CamelCaseNamingStyle()
    {
        var compilation = CreateCompilation(@"
using Quarry;

public class ItemSchema : Schema
{
    public static string Table => ""items"";
    protected override NamingStyle NamingStyle => NamingStyle.CamelCase;

    public Key<int> ItemId => Identity<int>();
    public Col<string> ItemName => Length<string>(100);
}
");

        var resolver = new SchemaResolver();
        var map = resolver.Resolve(compilation);

        Assert.That(map.TryGetEntity("items", out var entity), Is.True);
        Assert.That(entity.TryGetProperty("itemId", out var prop1), Is.True);
        Assert.That(prop1, Is.EqualTo("ItemId"));
        Assert.That(entity.TryGetProperty("itemName", out var prop2), Is.True);
        Assert.That(prop2, Is.EqualTo("ItemName"));
    }
}
