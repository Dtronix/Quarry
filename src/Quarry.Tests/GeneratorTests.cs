using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Quarry.Generators;

namespace Quarry.Tests;

/// <summary>
/// Tests for the QuarryGenerator source generator.
/// </summary>
[TestFixture]
public class GeneratorTests
{
    private static readonly string QuarryCoreAssemblyPath = typeof(Schema).Assembly.Location;
    private static readonly string SystemRuntimeAssemblyPath = typeof(object).Assembly.Location;

    /// <summary>
    /// Creates a compilation with the given source code and necessary references.
    /// </summary>
    private static CSharpCompilation CreateCompilation(params string[] sources)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var syntaxTrees = sources.Select(s => CSharpSyntaxTree.ParseText(s, parseOptions)).ToList();

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(QuarryCoreAssemblyPath),
            MetadataReference.CreateFromFile(SystemRuntimeAssemblyPath),
            MetadataReference.CreateFromFile(typeof(System.Data.IDbConnection).Assembly.Location),
        };

        // Add netstandard/runtime references
        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Collections.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.Expressions.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "netstandard.dll")));

        return CSharpCompilation.Create(
            "TestAssembly",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));
    }

    /// <summary>
    /// Runs the generator on the given compilation and returns the results.
    /// </summary>
    private static GeneratorDriverRunResult RunGenerator(CSharpCompilation compilation)
    {
        var generator = new QuarryGenerator();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() });
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        return driver.GetRunResult();
    }

    /// <summary>
    /// Runs the generator and returns both the result and all diagnostics (input + generator).
    /// </summary>
    private static (GeneratorDriverRunResult Result, IReadOnlyList<Diagnostic> Diagnostics) RunGeneratorWithDiagnostics(CSharpCompilation compilation)
    {
        var generator = new QuarryGenerator();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() });
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        return (driver.GetRunResult(), diagnostics);
    }

    [Test]
    public void Generator_WithValidContext_GeneratesEntityClass()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";

    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL, Schema = ""public"")]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        // Should generate User.g.cs and TestDbContext.Metadata.g.cs
        var generatedSources = result.GeneratedTrees.ToList();

        Assert.That(generatedSources.Count, Is.GreaterThanOrEqualTo(2),
            "Should generate at least entity class and metadata file");

        var entitySource = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("User.g.cs"));
        Assert.That(entitySource, Is.Not.Null, "Should generate User.g.cs");

        var entityCode = entitySource!.GetText().ToString();
        Assert.That(entityCode, Does.Contain("public partial class User"));
        Assert.That(entityCode, Does.Contain("public int UserId"));
        Assert.That(entityCode, Does.Contain("public string UserName"));
    }

    [Test]
    public void Generator_WithNullableColumn_GeneratesNullableProperty()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";

    public Key<int> UserId => Identity();
    public Col<string?> Email => Length(255);
    public Col<DateTime?> LastLogin { get; }
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var entitySource = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("User.g.cs"));
        Assert.That(entitySource, Is.Not.Null);

        var entityCode = entitySource!.GetText().ToString();
        Assert.That(entityCode, Does.Contain("public string? Email"));
        Assert.That(entityCode, Does.Contain("public DateTime? LastLogin"));
    }

    [Test]
    public void Generator_WithComputedColumn_GeneratesInitOnlyProperty()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class OrderItemSchema : Schema
{
    public static string Table => ""order_items"";

    public Key<int> OrderItemId => Identity();
    public Col<int> Quantity { get; }
    public Col<decimal> UnitPrice => Precision(18, 2);
    public Col<decimal> LineTotal => Computed<decimal>();
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<OrderItem> OrderItems();
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var entitySource = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("OrderItem.g.cs"));
        Assert.That(entitySource, Is.Not.Null);

        var entityCode = entitySource!.GetText().ToString();
        Assert.That(entityCode, Does.Contain("public decimal LineTotal { get; init; }"));
        Assert.That(entityCode, Does.Contain("public int Quantity { get; set; }"));
    }

    [Test]
    public void Generator_WithForeignKey_GeneratesRefProperty()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
}

public class OrderSchema : Schema
{
    public static string Table => ""orders"";

    public Key<int> OrderId => Identity();
    public Ref<UserSchema, int> UserId => ForeignKey<UserSchema, int>();
    public Col<decimal> Total => Precision(18, 2);
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Order> Orders();
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var orderSource = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("Order.g.cs"));
        Assert.That(orderSource, Is.Not.Null);

        var orderCode = orderSource!.GetText().ToString();
        Assert.That(orderCode, Does.Contain("public EntityRef<User, int> UserId"));
    }

    [Test]
    public void Generator_WithNavigation_GeneratesNavigationListProperty()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
    public Many<OrderSchema> Orders => HasMany<OrderSchema>(o => o.UserId);
}

public class OrderSchema : Schema
{
    public static string Table => ""orders"";
    public Key<int> OrderId => Identity();
    public Ref<UserSchema, int> UserId => ForeignKey<UserSchema, int>();
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Order> Orders();
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var userSource = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("User.g.cs"));
        Assert.That(userSource, Is.Not.Null);

        var userCode = userSource!.GetText().ToString();
        Assert.That(userCode, Does.Contain("public NavigationList<Order> Orders"));
    }

    [Test]
    public void Generator_GeneratesMetadataFile()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL, Schema = ""public"")]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var metadataSource = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("TestDbContext.Metadata.g.cs"));
        Assert.That(metadataSource, Is.Not.Null);

        var metadataCode = metadataSource!.GetText().ToString();
        Assert.That(metadataCode, Does.Contain("internal static class UserMetadata"));
        Assert.That(metadataCode, Does.Contain("TableName = \"users\""));
        Assert.That(metadataCode, Does.Contain("public static class UserId"));
        Assert.That(metadataCode, Does.Contain("public static class UserName"));
    }

    [Test]
    public void Generator_WithSnakeCaseNaming_AppliesConvention()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    protected override NamingStyle NamingStyle => NamingStyle.SnakeCase;

    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
    public Col<DateTime> CreatedAt { get; }
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var metadataSource = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("TestDbContext.Metadata.g.cs"));
        Assert.That(metadataSource, Is.Not.Null);

        var metadataCode = metadataSource!.GetText().ToString();
        Assert.That(metadataCode, Does.Contain("Name = \"user_id\""));
        Assert.That(metadataCode, Does.Contain("Name = \"user_name\""));
        Assert.That(metadataCode, Does.Contain("Name = \"created_at\""));
    }

    [Test]
    public void Generator_WithMapTo_OverridesColumnName()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => MapTo<string>(""user_display_name"");
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var metadataSource = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("TestDbContext.Metadata.g.cs"));
        Assert.That(metadataSource, Is.Not.Null);

        var metadataCode = metadataSource!.GetText().ToString();
        Assert.That(metadataCode, Does.Contain("Name = \"user_display_name\""));
    }

    [Test]
    public void Generator_WithNoContext_GeneratesNothing()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
}

// No [QuarryContext] attribute
public class TestDbContext
{
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        Assert.That(result.GeneratedTrees.Length, Is.EqualTo(0),
            "Should not generate anything without QuarryContext attribute");
    }

    [Test]
    [TestCase(SqlDialect.SQLite, "\\\"users\\\"")]
    [TestCase(SqlDialect.PostgreSQL, "\\\"users\\\"")]
    [TestCase(SqlDialect.MySQL, "`users`")]
    [TestCase(SqlDialect.SqlServer, "[users]")]
    public void Generator_QuotesIdentifiersPerDialect(SqlDialect dialect, string expectedQuotedTable)
    {
        var source = $@"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
}}

[QuarryContext(Dialect = SqlDialect.{dialect})]
public partial class TestDbContext : QuarryContext
{{
    public partial IEntityAccessor<User> Users();
}}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var metadataSource = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("TestDbContext.Metadata.g.cs"));
        Assert.That(metadataSource, Is.Not.Null);

        var metadataCode = metadataSource!.GetText().ToString();
        Assert.That(metadataCode, Does.Contain(expectedQuotedTable));
    }

    [Test]
    public void Generator_WithSchema_GeneratesQualifiedTableName()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL, Schema = ""myschema"")]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var metadataSource = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("TestDbContext.Metadata.g.cs"));
        Assert.That(metadataSource, Is.Not.Null);

        var metadataCode = metadataSource!.GetText().ToString();
        // Should contain qualified name with schema
        Assert.That(metadataCode, Does.Contain("\\\"myschema\\\".\\\"users\\\""));
    }

    [Test]
    public void Generator_WithGuidKey_MarksClientGenerated()
    {
        var source = @"
using Quarry;
using System;

namespace TestApp;

public class ProductSchema : Schema
{
    public static string Table => ""products"";
    public Key<Guid> ProductId => ClientGenerated();
    public Col<string> Name => Length(200);
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Product> Products();
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var entitySource = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("Product.g.cs"));
        Assert.That(entitySource, Is.Not.Null);

        var entityCode = entitySource!.GetText().ToString();
        Assert.That(entityCode, Does.Contain("public System.Guid ProductId { get; set; }"));
    }

    [Test]
    public void Generator_MarksColumnMetadataCorrectly()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var metadataSource = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("TestDbContext.Metadata.g.cs"));
        Assert.That(metadataSource, Is.Not.Null);

        var metadataCode = metadataSource!.GetText().ToString();

        // Check primary key metadata
        Assert.That(metadataCode, Does.Contain("IsPrimaryKey = true"));
        Assert.That(metadataCode, Does.Contain("IsIdentity = true"));

        // Check string column metadata
        Assert.That(metadataCode, Does.Contain("MaxLength = 100"));
    }

    [Test]
    public void Generator_GeneratesContextClass()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL, Schema = ""public"")]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var contextSource = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("TestDbContext.g.cs"));
        Assert.That(contextSource, Is.Not.Null, "Should generate TestDbContext.g.cs");

        var contextCode = contextSource!.GetText().ToString();

        // Check constructors
        Assert.That(contextCode, Does.Contain("public TestDbContext(IDbConnection connection)"));
        Assert.That(contextCode, Does.Contain(": base(connection)"));

        // Check full constructor with options
        Assert.That(contextCode, Does.Contain("TimeSpan? defaultTimeout"));
        Assert.That(contextCode, Does.Contain("IsolationLevel? defaultIsolation"));
        Assert.That(contextCode, Does.Not.Contain("onSqlGenerated"));
    }

    [Test]
    public void Generator_GeneratesQueryBuilderProperty()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var contextSource = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("TestDbContext.g.cs"));
        Assert.That(contextSource, Is.Not.Null);

        var contextCode = contextSource!.GetText().ToString();

        // Check property implementation
        Assert.That(contextCode, Does.Contain("public partial IEntityAccessor<User> Users"));
        Assert.That(contextCode, Does.Contain("QueryBuilder<User>.Create(_dialect, \"users\", null, (IQueryExecutionContext)this)"));
    }

    [Test]
    public void Generator_GeneratesQueryBuilderPropertyWithSchema()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL, Schema = ""myschema"")]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var contextSource = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("TestDbContext.g.cs"));
        Assert.That(contextSource, Is.Not.Null);

        var contextCode = contextSource!.GetText().ToString();

        // Check schema name constant
        Assert.That(contextCode, Does.Contain("_schemaName = \"myschema\""));
        // Check property uses schema name
        Assert.That(contextCode, Does.Contain("QueryBuilder<User>.Create(_dialect, \"users\", _schemaName, (IQueryExecutionContext)this)"));
    }

    [Test]
    public void Generator_UsesCorrectDialect()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
}

[QuarryContext(Dialect = SqlDialect.MySQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var contextSource = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("TestDbContext.g.cs"));
        Assert.That(contextSource, Is.Not.Null);

        var contextCode = contextSource!.GetText().ToString();

        // Check dialect field
        Assert.That(contextCode, Does.Contain("SqlDialect.MySQL"));
    }

    [Test]
    public void Generator_WithMultipleEntities_GeneratesMultipleProperties()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
}

public class OrderSchema : Schema
{
    public static string Table => ""orders"";
    public Key<int> OrderId => Identity();
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Order> Orders();
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var contextSource = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("TestDbContext.g.cs"));
        Assert.That(contextSource, Is.Not.Null);

        var contextCode = contextSource!.GetText().ToString();

        // Check both properties
        Assert.That(contextCode, Does.Contain("IQueryBuilder<User> Users"));
        Assert.That(contextCode, Does.Contain("IQueryBuilder<Order> Orders"));
    }

    [Test]
    public void Generator_SkipsNonQuarryContextClasses()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
}

// This class has the attribute but doesn't inherit from QuarryContext
[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class NotAContext
{
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        // Should not generate a context class
        var contextSource = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("NotAContext.g.cs"));
        Assert.That(contextSource, Is.Null, "Should not generate for non-QuarryContext class");
    }

    [Test]
    public void Generator_WithMismatchedTypeMapping_ReportsQRY017()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class Money
{
    public decimal Amount { get; }
    public Money(decimal amount) => Amount = amount;
}

public class MoneyMapping : TypeMapping<Money, decimal>
{
    public override decimal ToDb(Money value) => value.Amount;
    public override Money FromDb(decimal value) => new Money(value);
}

public class ItemSchema : Schema
{
    public static string Table => ""items"";
    public Key<int> Id => Identity();
    public Col<string> Price => Mapped<string, MoneyMapping>();
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Item> Items();
}
";

        var compilation = CreateCompilation(source);
        var (result, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qry017 = diagnostics.FirstOrDefault(d => d.Id == "QRY017");
        Assert.That(qry017, Is.Not.Null, "Should report QRY017 for mismatched TypeMapping TCustom");
        Assert.That(qry017!.GetMessage(), Does.Contain("Price"));
        Assert.That(qry017.GetMessage(), Does.Contain("string"));
        Assert.That(qry017.GetMessage(), Does.Contain("Money"));
    }

    [Test]
    public void Generator_WithCorrectTypeMapping_DoesNotReportQRY017()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class Money
{
    public decimal Amount { get; }
    public Money(decimal amount) => Amount = amount;
}

public class MoneyMapping : TypeMapping<Money, decimal>
{
    public override decimal ToDb(Money value) => value.Amount;
    public override Money FromDb(decimal value) => new Money(value);
}

public class ItemSchema : Schema
{
    public static string Table => ""items"";
    public Key<int> Id => Identity();
    public Col<Money> Price => Mapped<Money, MoneyMapping>();
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Item> Items();
}
";

        var compilation = CreateCompilation(source);
        var (result, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qry017 = diagnostics.FirstOrDefault(d => d.Id == "QRY017");
        Assert.That(qry017, Is.Null, "Should not report QRY017 when TypeMapping TCustom matches column type");
    }

    [Test]
    public void Generator_WithDuplicateTypeMappings_ReportsQRY018()
    {
        var source = @"
using Quarry;

namespace TestApp;

public struct Money
{
    public decimal Amount { get; }
    public Money(decimal amount) => Amount = amount;
}

public class MoneyToDecimalMapping : TypeMapping<Money, decimal>
{
    public override decimal ToDb(Money value) => value.Amount;
    public override Money FromDb(decimal value) => new Money(value);
}

public class MoneyToStringMapping : TypeMapping<Money, string>
{
    public override string ToDb(Money value) => value.Amount.ToString();
    public override Money FromDb(string value) => new Money(decimal.Parse(value));
}

public class AccountSchema : Schema
{
    public static string Table => ""accounts"";
    public Key<int> Id => Identity();
    public Col<Money> Balance => Mapped<Money, MoneyToDecimalMapping>();
}

public class OrderSchema : Schema
{
    public static string Table => ""orders"";
    public Key<int> Id => Identity();
    public Col<Money> Total => Mapped<Money, MoneyToStringMapping>();
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Account> Accounts();
    public partial IEntityAccessor<Order> Orders();
}
";

        var compilation = CreateCompilation(source);
        var (result, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qry018 = diagnostics.FirstOrDefault(d => d.Id == "QRY018");
        Assert.That(qry018, Is.Not.Null, "Should report QRY018 for duplicate TypeMapping on same custom type");
        Assert.That(qry018!.GetMessage(), Does.Contain("Money"));
        Assert.That(qry018.GetMessage(), Does.Contain("MoneyToDecimalMapping"));
        Assert.That(qry018.GetMessage(), Does.Contain("MoneyToStringMapping"));
    }

    [Test]
    public void Generator_WithSameTypeMappingOnMultipleColumns_DoesNotReportQRY018()
    {
        var source = @"
using Quarry;

namespace TestApp;

public struct Money
{
    public decimal Amount { get; }
    public Money(decimal amount) => Amount = amount;
}

public class MoneyMapping : TypeMapping<Money, decimal>
{
    public override decimal ToDb(Money value) => value.Amount;
    public override Money FromDb(decimal value) => new Money(value);
}

public class AccountSchema : Schema
{
    public static string Table => ""accounts"";
    public Key<int> Id => Identity();
    public Col<Money> Balance => Mapped<Money, MoneyMapping>();
    public Col<Money> CreditLimit => Mapped<Money, MoneyMapping>();
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Account> Accounts();
}
";

        var compilation = CreateCompilation(source);
        var (result, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qry018 = diagnostics.FirstOrDefault(d => d.Id == "QRY018");
        Assert.That(qry018, Is.Null, "Should not report QRY018 when same TypeMapping class is reused");
    }

    #region EntityReader Diagnostics

    [Test]
    public void Generator_WithValidEntityReader_ReportsQRY026()
    {
        var source = @"
using Quarry;
using System.Data.Common;

namespace TestApp;

public class User
{
    public int UserId { get; set; }
    public string UserName { get; set; } = """";
}

public class UserReader : EntityReader<User>
{
    public override User Read(DbDataReader reader) => new User
    {
        UserId = reader.GetInt32(reader.GetOrdinal(""user_id"")),
        UserName = reader.GetString(reader.GetOrdinal(""user_name"")),
    };
}

[EntityReader(typeof(UserReader))]
public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}
";

        var compilation = CreateCompilation(source);
        var (result, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qry026 = diagnostics.FirstOrDefault(d => d.Id == "QRY026");
        Assert.That(qry026, Is.Not.Null, "Should report QRY026 when valid EntityReader is detected");
        Assert.That(qry026!.GetMessage(), Does.Contain("UserReader"));
        Assert.That(qry026.GetMessage(), Does.Contain("User"));

        var qry027 = diagnostics.FirstOrDefault(d => d.Id == "QRY027");
        Assert.That(qry027, Is.Null, "Should not report QRY027 when EntityReader is valid");
    }

    [Test]
    public void Generator_WithInvalidEntityReader_ReportsQRY027()
    {
        var source = @"
using Quarry;
using System.Data.Common;

namespace TestApp;

public class User
{
    public int UserId { get; set; }
}

public class Order
{
    public int OrderId { get; set; }
}

// Reader for Order, but applied to UserSchema — T mismatch
public class OrderReader : EntityReader<Order>
{
    public override Order Read(DbDataReader reader) => new Order
    {
        OrderId = reader.GetInt32(0),
    };
}

[EntityReader(typeof(OrderReader))]
public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}
";

        var compilation = CreateCompilation(source);
        var (result, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qry027 = diagnostics.FirstOrDefault(d => d.Id == "QRY027");
        Assert.That(qry027, Is.Not.Null, "Should report QRY027 when EntityReader T doesn't match entity");
        Assert.That(qry027!.GetMessage(), Does.Contain("OrderReader"));
        Assert.That(qry027.GetMessage(), Does.Contain("UserSchema"));
    }

    [Test]
    public void Generator_WithValidEntityReader_EmitsReaderDelegation()
    {
        var source = @"
using Quarry;
using System.Data.Common;
using System.Linq;

namespace TestApp;

public class User
{
    public int UserId { get; set; }
    public string UserName { get; set; } = """";
}

public class UserReader : EntityReader<User>
{
    public override User Read(DbDataReader reader) => new User
    {
        UserId = reader.GetInt32(reader.GetOrdinal(""user_id"")),
        UserName = reader.GetString(reader.GetOrdinal(""user_name"")),
    };
}

[EntityReader(typeof(UserReader))]
public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public static class Queries
{
    public static void Test(TestDbContext db)
    {
        db.Users.Select(u => u);
    }
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");

        var interceptorsCode = interceptorsTree!.GetText().ToString();
        Assert.That(interceptorsCode, Does.Contain("_entityReader_TestApp_UserReader"),
            "Should emit cached EntityReader field");
        Assert.That(interceptorsCode, Does.Contain("_entityReader_TestApp_UserReader.Read(r)"),
            "Should delegate to custom reader's Read() method");
        Assert.That(interceptorsCode, Does.Not.Contain("new User"),
            "Should not generate inline object initializer");
    }

    #endregion

    #region Composite Key

    [Test]
    public void Generator_CompositeKey_GeneratesMetadata()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class StudentSchema : Schema
{
    public static string Table => ""students"";
    public Key<int> StudentId => Identity();
    public Col<string> Name { get; }
}

public class CourseSchema : Schema
{
    public static string Table => ""courses"";
    public Key<int> CourseId => Identity();
    public Col<string> Title { get; }
}

public class EnrollmentSchema : Schema
{
    public static string Table => ""enrollments"";
    public Ref<StudentSchema, int> StudentId => ForeignKey<StudentSchema, int>();
    public Ref<CourseSchema, int> CourseId => ForeignKey<CourseSchema, int>();
    public Col<DateTime> EnrolledAt { get; }
    public CompositeKey PK => PrimaryKey(StudentId, CourseId);
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Student> Students();
    public partial IEntityAccessor<Course> Courses();
    public partial IEntityAccessor<Enrollment> Enrollments();
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var metadataSource = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("TestDbContext.Metadata.g.cs"));
        Assert.That(metadataSource, Is.Not.Null);

        var metadataCode = metadataSource!.GetText().ToString();

        // Should emit composite PK arrays
        Assert.That(metadataCode, Does.Contain("PrimaryKeyColumns"));
        Assert.That(metadataCode, Does.Contain("PrimaryKeyProperties"));

        // Should NOT emit single PrimaryKeyColumn for the enrollment entity
        // (it has a composite key, not a single Key<T>)
        Assert.That(metadataCode, Does.Contain("EnrollmentMetadata"));
    }

    [Test]
    public void Generator_CompositeKey_MarksColumnsAsPrimaryKey()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class StudentSchema : Schema
{
    public static string Table => ""students"";
    public Key<int> StudentId => Identity();
}

public class CourseSchema : Schema
{
    public static string Table => ""courses"";
    public Key<int> CourseId => Identity();
}

public class EnrollmentSchema : Schema
{
    public static string Table => ""enrollments"";
    public Ref<StudentSchema, int> StudentId => ForeignKey<StudentSchema, int>();
    public Ref<CourseSchema, int> CourseId => ForeignKey<CourseSchema, int>();
    public CompositeKey PK => PrimaryKey(StudentId, CourseId);
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Student> Students();
    public partial IEntityAccessor<Course> Courses();
    public partial IEntityAccessor<Enrollment> Enrollments();
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var metadataSource = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("TestDbContext.Metadata.g.cs"));
        Assert.That(metadataSource, Is.Not.Null);

        var metadataCode = metadataSource!.GetText().ToString();

        // Extract just the Enrollment metadata section
        var enrollmentIdx = metadataCode.IndexOf("EnrollmentMetadata");
        Assert.That(enrollmentIdx, Is.GreaterThan(-1));
        var enrollmentSection = metadataCode.Substring(enrollmentIdx);

        // Both FK columns that are part of the composite PK should have IsPrimaryKey = true
        // Find StudentId column metadata within Enrollment
        var studentIdIdx = enrollmentSection.IndexOf("public static class StudentId");
        Assert.That(studentIdIdx, Is.GreaterThan(-1));
        // Use enough length to capture the full column metadata block
        var endIdx = enrollmentSection.IndexOf("public static class CourseId");
        var studentIdSection = enrollmentSection.Substring(studentIdIdx, endIdx - studentIdIdx);
        Assert.That(studentIdSection, Does.Contain("IsPrimaryKey = true"));
        Assert.That(studentIdSection, Does.Contain("IsForeignKey = true"));
    }

    [Test]
    public void Generator_CompositeKey_NoSinglePrimaryKeyColumn()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class StudentSchema : Schema
{
    public static string Table => ""students"";
    public Key<int> StudentId => Identity();
}

public class CourseSchema : Schema
{
    public static string Table => ""courses"";
    public Key<int> CourseId => Identity();
}

public class EnrollmentSchema : Schema
{
    public static string Table => ""enrollments"";
    public Ref<StudentSchema, int> StudentId => ForeignKey<StudentSchema, int>();
    public Ref<CourseSchema, int> CourseId => ForeignKey<CourseSchema, int>();
    public CompositeKey PK => PrimaryKey(StudentId, CourseId);
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Student> Students();
    public partial IEntityAccessor<Course> Courses();
    public partial IEntityAccessor<Enrollment> Enrollments();
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var metadataSource = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("TestDbContext.Metadata.g.cs"));
        Assert.That(metadataSource, Is.Not.Null);

        var metadataCode = metadataSource!.GetText().ToString();

        var enrollmentIdx = metadataCode.IndexOf("EnrollmentMetadata");
        var nextEntityIdx = metadataCode.IndexOf("internal static class", enrollmentIdx + 1);
        var enrollmentSection = nextEntityIdx > 0
            ? metadataCode.Substring(enrollmentIdx, nextEntityIdx - enrollmentIdx)
            : metadataCode.Substring(enrollmentIdx);

        // Composite key entity should NOT have single PrimaryKeyColumn
        Assert.That(enrollmentSection, Does.Not.Contain("public const string PrimaryKeyColumn"));
        // But should have PrimaryKeyColumns array
        Assert.That(enrollmentSection, Does.Contain("PrimaryKeyColumns"));
    }

    #endregion
}
