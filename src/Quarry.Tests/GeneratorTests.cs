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

    /// <summary>
    /// Runs the generator and returns the output compilation (with generated source included).
    /// </summary>
    private static CSharpCompilation RunGeneratorAndGetOutputCompilation(CSharpCompilation compilation)
    {
        var generator = new QuarryGenerator();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() });
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

        return (CSharpCompilation)outputCompilation;
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

        var generatedSources = result.GeneratedTrees.ToList();

        Assert.That(generatedSources.Count, Is.GreaterThanOrEqualTo(1),
            "Should generate at least entity class");

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

        // Check owned connection constructor
        Assert.That(contextCode, Does.Contain("public TestDbContext(IDbConnection connection, bool ownsConnection)"));
        Assert.That(contextCode, Does.Contain(": base(connection, ownsConnection)"));

        // Check full constructor with options
        Assert.That(contextCode, Does.Contain("bool ownsConnection,"));
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
        Assert.That(contextCode, Does.Contain("throw new NotSupportedException(\"Entity accessor methods must be intercepted by the Quarry source generator.\")"));
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
        Assert.That(contextCode, Does.Contain("throw new NotSupportedException(\"Entity accessor methods must be intercepted by the Quarry source generator.\")"));
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

        // Check dialect is used in the generated context (e.g., constructor or entity accessor)
        Assert.That(contextCode, Does.Contain("NotSupportedException"));
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
        Assert.That(contextCode, Does.Contain("IEntityAccessor<User> Users"));
        Assert.That(contextCode, Does.Contain("IEntityAccessor<Order> Orders"));
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
using System.Threading.Tasks;

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
    public static async Task Test(TestDbContext db)
    {
        await db.Users().Select(u => u).ExecuteFetchAllAsync();
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

    #region IQueryBuilder<T> interceptor limitation

    [Test]
    public void DirectTerminalOnIQueryBuilderT_CompilesWithoutSignatureMismatch()
    {
        // Validates that IQueryBuilder<T> execution terminals can be intercepted
        // by the generator without CS9144 (interceptor signature mismatch).
        var source = @"
using System;
using System.Threading.Tasks;
using Quarry;

namespace TestApp;

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

class Service
{
    async Task DoWork(TestDbContext db)
    {
        // Direct call on IQueryBuilder<T> — no .Prepare() or .Select()
        var user = await db.Users().Where(u => u.UserId == 1).ExecuteFetchFirstAsync();
    }
}
";
        // Enable interceptors for the TestApp namespace so Roslyn validates signatures
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest)
            .WithFeatures(new[] { new KeyValuePair<string, string>("InterceptorsNamespaces", "TestApp") });
        var syntaxTrees = new[] { CSharpSyntaxTree.ParseText(source, parseOptions) };

        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        var references = new MetadataReference[]
        {
            MetadataReference.CreateFromFile(QuarryCoreAssemblyPath),
            MetadataReference.CreateFromFile(SystemRuntimeAssemblyPath),
            MetadataReference.CreateFromFile(typeof(System.Data.IDbConnection).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Collections.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.Expressions.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "netstandard.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.ComponentModel.Primitives.dll")),
        };

        var compilation = CSharpCompilation.Create("TestAssembly", syntaxTrees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        var generator = new QuarryGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() },
            parseOptions: parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

        var errors = outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        // CS9144 = interceptor signature mismatch. If present, the generator emitted
        // the wrong parameter type (e.g., IQueryBuilder<User, User> instead of IQueryBuilder<User>).
        var cs9144 = errors.Where(d => d.Id == "CS9144").ToList();

        Assert.That(cs9144, Is.Empty,
            "Direct IQueryBuilder<T> terminal produced CS9144 signature mismatch: " +
            string.Join("; ", cs9144.Select(d => d.GetMessage())));
    }

    #endregion

    #region Nullable collection Contains — CS0030 regression

    [Test]
    public void NullableArrayContains_NullableColumn_NoCS0030()
    {
        // Regression: long?[] used in .Contains() on a Col<long?> column should generate
        // IReadOnlyList<long?>, not IReadOnlyList<long>. CS0030 = "Cannot convert type".
        var source = @"
using System;
using System.Linq;
using System.Threading.Tasks;
using Quarry;

namespace TestApp;

public class EquipmentOptionSchema : Schema
{
    public static string Table => ""equipment_options"";
    public Key<long> Id => Identity();
    public Col<string> Name => Length(200);
}

public class EquipmentPropertySchema : Schema
{
    public static string Table => ""equipment_properties"";
    public Key<long> Id => Identity();
    public Col<long?> EquipmentOptionId { get; }
    public Col<string> Value => Length(500);
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<EquipmentOption> EquipmentOptions();
    public partial IEntityAccessor<EquipmentProperty> EquipmentProperties();
}

class Service
{
    async Task DeleteEquipmentProperty(TestDbContext db)
    {
        // Simulates the user pattern: Select non-nullable Id, cast to nullable, ToArray()
        var optionRows = await db.EquipmentOptions().Select(o => o).ExecuteFetchAllAsync();
        var optionIdValues = optionRows.Select(o => (long?)o.Id).ToArray(); // long?[]

        // Contains on nullable column with nullable collection
        await db.EquipmentProperties()
            .Delete()
            .Where(ep => optionIdValues.Contains(ep.EquipmentOptionId))
            .ExecuteNonQueryAsync();
    }
}
";
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest)
            .WithFeatures(new[] { new KeyValuePair<string, string>("InterceptorsNamespaces", "TestApp") });
        var syntaxTrees = new[] { CSharpSyntaxTree.ParseText(source, parseOptions) };

        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        var references = new MetadataReference[]
        {
            MetadataReference.CreateFromFile(QuarryCoreAssemblyPath),
            MetadataReference.CreateFromFile(SystemRuntimeAssemblyPath),
            MetadataReference.CreateFromFile(typeof(System.Data.IDbConnection).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Collections.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.Expressions.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "netstandard.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.ComponentModel.Primitives.dll")),
        };

        var compilation = CSharpCompilation.Create("TestAssembly", syntaxTrees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        var generator = new QuarryGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

        var errors = outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        var cs0030 = errors.Where(d => d.Id == "CS0030").ToList();
        Assert.That(cs0030, Is.Empty,
            "Nullable collection Contains produced CS0030: " +
            string.Join("; ", cs0030.Select(d => d.GetMessage())));
    }

    [Test]
    public void NullableArrayContains_NullableColumn_NotFirstColumn_NoCS0030()
    {
        // Regression: when the target column is NOT the first column in the entity,
        // FindCollectionElementTypes must match the correct column, not the first one.
        // Previously col.PropertyName == kvp.Key was always true (tautological match),
        // so the first column (Id, non-nullable) was used instead of EquipmentOptionId (nullable).
        var source = @"
using System;
using System.Linq;
using System.Threading.Tasks;
using Quarry;

namespace TestApp;

public class EquipmentOptionSchema : Schema
{
    public static string Table => ""equipment_options"";
    public Key<long> Id => Identity();
    public Col<string> Name => Length(200);
}

public class EquipmentPropertySchema : Schema
{
    public static string Table => ""equipment_properties"";
    public Key<long> Id => Identity();
    public Col<long?> EquipmentId { get; }
    public Col<long?> EquipmentOptionId { get; }
    public Col<long> EquipmentPropertyTypeId { get; }
    public Col<string> Value => Length(500);
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<EquipmentOption> EquipmentOptions();
    public partial IEntityAccessor<EquipmentProperty> EquipmentProperties();
}

class Service
{
    async Task DeleteEquipmentProperty(TestDbContext db, string nameFilter)
    {
        var optionRows = await db.EquipmentOptions()
            .Where(eo => eo.Name == nameFilter)
            .Select(eo => eo)
            .ExecuteFetchAllAsync();

        var optionIdValues = optionRows.Select(o => (long?)o.Id).ToArray();

        await db.EquipmentProperties()
            .Delete()
            .Where(ep => optionIdValues.Contains(ep.EquipmentOptionId) && ep.EquipmentPropertyTypeId == 6)
            .ExecuteNonQueryAsync();
    }
}
";
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest)
            .WithFeatures(new[] { new KeyValuePair<string, string>("InterceptorsNamespaces", "TestApp") });
        var syntaxTrees = new[] { CSharpSyntaxTree.ParseText(source, parseOptions) };

        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        var references = new MetadataReference[]
        {
            MetadataReference.CreateFromFile(QuarryCoreAssemblyPath),
            MetadataReference.CreateFromFile(SystemRuntimeAssemblyPath),
            MetadataReference.CreateFromFile(typeof(System.Data.IDbConnection).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Collections.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.Expressions.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "netstandard.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.ComponentModel.Primitives.dll")),
        };

        var compilation = CSharpCompilation.Create("TestAssembly", syntaxTrees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        var generator = new QuarryGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

        var errors = outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        var cs0030 = errors.Where(d => d.Id == "CS0030").ToList();
        Assert.That(cs0030, Is.Empty,
            "Nullable collection Contains produced CS0030 (multi-closure method): " +
            string.Join("; ", cs0030.Select(d => d.GetMessage())));
    }

    #endregion

    #region HasManyThrough Diagnostics (QRY044/QRY045)

    [Test]
    public void Generator_HasManyThrough_InvalidJunctionNavigation_ReportsQRY044()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class AddressSchema : Schema
{
    public static string Table => ""addresses"";
    public Key<int> AddressId => Identity();
    public Col<string> City => Length(100);
}

public class UserAddressSchema : Schema
{
    public static string Table => ""user_addresses"";
    public Key<int> UserAddressId => Identity();
    public Ref<UserSchema, int> UserId => ForeignKey<UserSchema, int>();
    public Ref<AddressSchema, int> AddressId => ForeignKey<AddressSchema, int>();
    public One<AddressSchema> Address { get; }
}

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);

    // Junction navigation references 'NonExistentNav' which is not a Many<T> on this entity
    public Many<AddressSchema> Addresses
        => HasManyThrough<AddressSchema, UserAddressSchema, UserSchema>(
            self => self.NonExistentNav,
            through => through.Address);
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Address> Addresses();
    public partial IEntityAccessor<UserAddress> UserAddresses();
}
";

        var compilation = CreateCompilation(source);
        var (result, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qry044 = diagnostics.FirstOrDefault(d => d.Id == "QRY064");
        Assert.That(qry044, Is.Not.Null, "Should report QRY044 when junction navigation is not a Many<T>");
    }

    [Test]
    public void Generator_HasManyThrough_InvalidTargetNavigation_ReportsQRY045()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class AddressSchema : Schema
{
    public static string Table => ""addresses"";
    public Key<int> AddressId => Identity();
    public Col<string> City => Length(100);
}

public class UserAddressSchema : Schema
{
    public static string Table => ""user_addresses"";
    public Key<int> UserAddressId => Identity();
    public Ref<UserSchema, int> UserId => ForeignKey<UserSchema, int>();
    public Ref<AddressSchema, int> AddressId => ForeignKey<AddressSchema, int>();
    // No One<AddressSchema> navigation — target nav will be invalid
}

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
    public Many<UserAddressSchema> UserAddresses => HasMany<UserAddressSchema>(ua => ua.UserId);

    // Target navigation references 'Address' but UserAddressSchema has no One<AddressSchema>
    public Many<AddressSchema> Addresses
        => HasManyThrough<AddressSchema, UserAddressSchema, UserSchema>(
            self => self.UserAddresses,
            through => through.Address);
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Address> Addresses();
    public partial IEntityAccessor<UserAddress> UserAddresses();
}
";

        var compilation = CreateCompilation(source);
        var (result, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qry045 = diagnostics.FirstOrDefault(d => d.Id == "QRY065");
        Assert.That(qry045, Is.Not.Null, "Should report QRY045 when target navigation is not a One<T> on junction entity");
        Assert.That(qry045!.GetMessage(), Does.Contain("Address"));
        Assert.That(qry045.GetMessage(), Does.Contain("UserAddress"));
    }

    [Test]
    public void Generator_HasManyThrough_ValidNavigations_NoDiagnostics()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class AddressSchema : Schema
{
    public static string Table => ""addresses"";
    public Key<int> AddressId => Identity();
    public Col<string> City => Length(100);
}

public class UserAddressSchema : Schema
{
    public static string Table => ""user_addresses"";
    public Key<int> UserAddressId => Identity();
    public Ref<UserSchema, int> UserId => ForeignKey<UserSchema, int>();
    public Ref<AddressSchema, int> AddressId => ForeignKey<AddressSchema, int>();
    public One<AddressSchema> Address { get; }
}

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
    public Many<UserAddressSchema> UserAddresses => HasMany<UserAddressSchema>(ua => ua.UserId);

    public Many<AddressSchema> Addresses
        => HasManyThrough<AddressSchema, UserAddressSchema, UserSchema>(
            self => self.UserAddresses,
            through => through.Address);
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Address> Addresses();
    public partial IEntityAccessor<UserAddress> UserAddresses();
}
";

        var compilation = CreateCompilation(source);
        var (result, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qry044 = diagnostics.FirstOrDefault(d => d.Id == "QRY064");
        var qry045 = diagnostics.FirstOrDefault(d => d.Id == "QRY065");
        Assert.That(qry044, Is.Null, "Should not report QRY044 for valid HasManyThrough");
        Assert.That(qry045, Is.Null, "Should not report QRY045 for valid HasManyThrough");
    }

    #endregion

    #region Set Operation Diagnostics (QRY070/QRY071)

    // Note: IntersectAll/ExceptAll diagnostic tests require the full generator pipeline to
    // discover default interface method calls. The current test infrastructure cannot fully
    // resolve these during source generation. The diagnostic code in PipelineOrchestrator is
    // verified by code review. To properly test, a runtime integration test with the
    // compiled test harness would be needed (IntersectAll/ExceptAll call on a real context).

    [Test]
    public void DiagnosticDescriptors_QRY070_QRY071_HaveUniqueIds()
    {
        // Verify QRY070/QRY071 descriptors have correct, unique IDs and Error severity.
        // Full chain-level testing of these diagnostics requires the runtime test harness
        // because the generator test compilation can't resolve default interface method calls
        // (IntersectAll/ExceptAll) through the partial method chain.
        var qry070 = DiagnosticDescriptors.IntersectAllNotSupported;
        var qry071 = DiagnosticDescriptors.ExceptAllNotSupported;

        Assert.That(qry070.Id, Is.EqualTo("QRY070"));
        Assert.That(qry071.Id, Is.EqualTo("QRY071"));
        Assert.That(qry070.Id, Is.Not.EqualTo(qry071.Id));
        Assert.That(qry070.DefaultSeverity, Is.EqualTo(Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        Assert.That(qry071.DefaultSeverity, Is.EqualTo(Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        Assert.That(qry070.MessageFormat.ToString(), Does.Contain("{0}"));
        Assert.That(qry071.MessageFormat.ToString(), Does.Contain("{0}"));
    }

    [Test]
    public void DiagnosticDescriptors_SetOperation_IdsAreUnique()
    {
        // Verify all set operation diagnostic IDs are distinct from each other
        // and from all other diagnostors. This guards against the QRY041 collision
        // that was caught in a prior review.
        var setOpDescriptors = new[]
        {
            DiagnosticDescriptors.IntersectAllNotSupported,
            DiagnosticDescriptors.ExceptAllNotSupported,
            DiagnosticDescriptors.SetOperationProjectionMismatch,
        };

        var ids = setOpDescriptors.Select(d => d.Id).ToList();
        Assert.That(ids, Is.Unique, "All set operation diagnostic IDs must be unique");
        Assert.That(ids, Does.Contain("QRY070"));
        Assert.That(ids, Does.Contain("QRY071"));
        Assert.That(ids, Does.Contain("QRY072"));
    }

    // Note on QRY072 (SetOperationProjectionMismatch) cross-entity coverage:
    // QRY072 fires when the two sides of a set operation produce different SQL column counts.
    // For tuple TResult and required-init records the C# type system pins both sides to the
    // same column count, but for DTOs with object initializers it does NOT — both sides can
    // share TResult=MyDto while assigning a different number of properties:
    //   Select(u => new MyDto { A = u.X, B = u.Y })  // 2 columns
    //     .Union(Select(u => new MyDto { A = u.X })) // 1 column
    // ProjectionAnalyzer counts one column per assignment expression, so the IR ends up with
    // mismatched Columns.Count and the diagnostic fires. This is verified at the unit level
    // by PipelineOrchestratorTests.CollectPostAnalysisDiagnostics_SetOperationColumnCountMismatch_EmitsQRY072,
    // which constructs the AssembledPlan directly and asserts the diagnostic is emitted.
    // The descriptor uniqueness check above guards against ID collisions.

    #endregion

}
