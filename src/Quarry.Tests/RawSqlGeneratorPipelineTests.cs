using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Quarry.Generators;

namespace Quarry.Tests;

/// <summary>
/// Generator pipeline tests for RawSqlAsync and RawSqlScalarAsync interception.
/// Compiles real C# source through the generator and verifies interceptor output.
/// </summary>
[TestFixture]
public class RawSqlGeneratorPipelineTests
{
    private static readonly string QuarryCoreAssemblyPath = typeof(Schema).Assembly.Location;
    private static readonly string SystemRuntimeAssemblyPath = typeof(object).Assembly.Location;

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

        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Collections.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.Expressions.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "netstandard.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Threading.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Threading.Tasks.dll")));

        return CSharpCompilation.Create(
            "TestAssembly",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));
    }

    private static (ImmutableArray<Diagnostic> Diagnostics, GeneratorDriverRunResult Result) RunGeneratorWithDiagnostics(
        CSharpCompilation compilation)
    {
        var generator = new QuarryGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);
        return (diagnostics, driver.GetRunResult());
    }

    private static string? GetInterceptorsCode(GeneratorDriverRunResult result)
    {
        var interceptorsFile = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains("Interceptors"));
        return interceptorsFile?.GetText().ToString();
    }

    #region RawSqlAsync Discovery Tests

    [Test]
    public void RawSqlAsync_CallSite_IsDiscovered_And_GeneratesInterceptor()
    {
        var source = @"
using Quarry;
using System.Threading.Tasks;

namespace TestApp;

public class UserDto
{
    public int UserId { get; set; }
    public string UserName { get; set; } = null!;
}

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

public class Service
{
    public async Task Test(TestDbContext db)
    {
        var results = await db.RawSqlAsync<UserDto>(""SELECT UserId, UserName FROM users"");
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        var code = GetInterceptorsCode(result);
        Assert.That(code, Is.Not.Null, "Should generate interceptors file");
        Assert.That(code, Does.Contain("RawSqlAsyncWithReader"),
            "Should generate RawSqlAsync interceptor calling RawSqlAsyncWithReader");
        Assert.That(code, Does.Contain("UserDto"),
            "Generated interceptor should reference the DTO type");
    }

    [Test]
    public void RawSqlScalarAsync_CallSite_IsDiscovered_And_GeneratesInterceptor()
    {
        var source = @"
using Quarry;
using System.Threading.Tasks;

namespace TestApp;

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

public class Service
{
    public async Task Test(TestDbContext db)
    {
        var count = await db.RawSqlScalarAsync<int>(""SELECT COUNT(*) FROM users"");
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        var code = GetInterceptorsCode(result);
        Assert.That(code, Is.Not.Null, "Should generate interceptors file");
        Assert.That(code, Does.Contain("RawSqlScalarAsyncWithConverter"),
            "Should generate RawSqlScalarAsync interceptor calling RawSqlScalarAsyncWithConverter");
    }

    #endregion

    #region Non-Generic RawSql Ignored

    [Test]
    public void RawSqlNonQueryAsync_DoesNot_GenerateInterceptor()
    {
        var source = @"
using Quarry;
using System.Threading.Tasks;

namespace TestApp;

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

public class Service
{
    public async Task Test(TestDbContext db)
    {
        await db.RawSqlNonQueryAsync(""DELETE FROM users WHERE UserId = @p0"", 1);
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        var code = GetInterceptorsCode(result);
        // Either no interceptors file at all, or it doesn't contain RawSqlNonQuery
        if (code != null)
        {
            Assert.That(code, Does.Not.Contain("RawSqlNonQuery"),
                "RawSqlNonQueryAsync should not produce an interceptor");
        }
    }

    #endregion

    #region Unresolvable Generic T

    [Test]
    public void RawSqlAsync_UnresolvableGenericT_DoesNot_GenerateInterceptor()
    {
        var source = @"
using Quarry;
using System.Threading.Tasks;

namespace TestApp;

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

public class Service
{
    public async Task<System.Collections.Generic.List<T>> QueryGeneric<T>(TestDbContext db, string sql)
    {
        // T is an open generic — generator cannot resolve concrete type
        return await db.RawSqlAsync<T>(sql);
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        // Should not error
        Assert.That(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), Is.False,
            "Generator should not error on unresolvable generic T");

        var code = GetInterceptorsCode(result);
        // Either no interceptors file, or no RawSqlAsync interceptor for the generic call
        if (code != null)
        {
            Assert.That(code, Does.Not.Contain("RawSqlAsyncWithReader"),
                "Should not generate interceptor for unresolvable generic T");
        }
    }

    #endregion

    #region Both Overloads Detected

    [Test]
    public void RawSqlAsync_BothOverloads_WithAndWithoutCancellationToken_Detected()
    {
        var source = @"
using Quarry;
using System.Threading;
using System.Threading.Tasks;

namespace TestApp;

public class UserDto
{
    public int UserId { get; set; }
    public string UserName { get; set; } = null!;
}

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

public class Service
{
    public async Task Test(TestDbContext db, CancellationToken ct)
    {
        // Overload without CancellationToken
        var r1 = await db.RawSqlAsync<UserDto>(""SELECT UserId, UserName FROM users"");
        // Overload with CancellationToken
        var r2 = await db.RawSqlAsync<UserDto>(""SELECT UserId, UserName FROM users"", ct);
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        var code = GetInterceptorsCode(result);
        Assert.That(code, Is.Not.Null, "Should generate interceptors file");

        // Count the number of RawSqlAsyncWithReader interceptor methods
        var interceptorCount = CountOccurrences(code!, "RawSqlAsyncWithReader");
        Assert.That(interceptorCount, Is.GreaterThanOrEqualTo(2),
            "Should generate interceptors for both overloads");
    }

    #endregion

    #region Empty Properties — CS1522 Guard

    [Test]
    public void RawSqlAsync_DtoWithNoPublicSetters_DoesNotEmitEmptySwitch()
    {
        // When T is a DTO whose properties all lack public setters, the property list
        // is empty. The emitter must not produce an empty switch block (CS1522).
        var source = @"
using Quarry;
using System.Threading.Tasks;

namespace TestApp;

public class ReadOnlyDto
{
    public int Id { get; }
    public string Name { get; } = null!;
}

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

public class Service
{
    public async Task Test(TestDbContext db)
    {
        var results = await db.RawSqlAsync<ReadOnlyDto>(""SELECT Id, Name FROM users"");
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        var code = GetInterceptorsCode(result);
        Assert.That(code, Is.Not.Null, "Should generate interceptors file");
        Assert.That(code, Does.Contain("static _ => new ReadOnlyDto()"),
            "Should emit a one-liner lambda discarding the reader when there are no settable properties");
        Assert.That(code, Does.Not.Contain("switch (r.GetName(i))"),
            "Should not emit a switch block when there are no settable properties");
    }

    #endregion

    #region Entity T Enrichment

    [Test]
    public void RawSqlAsync_EntityT_IsDiscovered_And_GeneratesInterceptor()
    {
        // When T matches a Pipeline 1 entity, the generator should still produce an
        // interceptor. Entity enrichment promotes DTO→Entity kind and maps schema metadata
        // onto the originally discovered properties. In a pipeline test the generated entity
        // class doesn't exist as concrete C# during discovery, so the initial property list
        // may be empty — but the interceptor itself must still be emitted.
        var source = @"
using Quarry;
using System.Threading.Tasks;

namespace TestApp;

public enum OrderPriority { Low = 0, Normal = 1, High = 2 }

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
    public Col<OrderPriority> Priority { get; }
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Order> Orders();
}

public class Service
{
    public async Task Test(TestDbContext db)
    {
        var orders = await db.RawSqlAsync<Order>(""SELECT * FROM orders"");
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        var code = GetInterceptorsCode(result);
        Assert.That(code, Is.Not.Null, "Should generate interceptors file");

        // Interceptor should be generated for the entity type
        Assert.That(code, Does.Contain("RawSqlAsyncWithReader"),
            "Should generate RawSqlAsync interceptor for entity T");
        Assert.That(code, Does.Contain("Task<List<Order>>"),
            "Interceptor return type should use the entity type Order");
        Assert.That(code, Does.Contain("new Order()"),
            "Interceptor reader should construct the entity");

        // Entity enrichment should produce a full property-reading switch, not a no-op
        Assert.That(code, Does.Contain("switch (r.GetName(i))"),
            "Should generate switch-based reader for enriched entity type");
        Assert.That(code, Does.Contain("case \"OrderId\""),
            "Should generate switch case for OrderId column");
        Assert.That(code, Does.Contain("case \"Total\""),
            "Should generate switch case for Total column");
        Assert.That(code, Does.Contain("case \"Priority\""),
            "Should generate switch case for Priority column");
        Assert.That(code, Does.Contain("(global::TestApp.OrderPriority)r.GetInt32(i)"),
            "Should generate enum cast for Priority column");
        Assert.That(code, Does.Contain("case \"UserId\""),
            "Should generate switch case for FK UserId column");
        Assert.That(code, Does.Contain("EntityRef<User, int>"),
            "Should wrap FK column with EntityRef from ColumnInfo metadata");
        Assert.That(code, Does.Not.Contain("static _ => new Order()"),
            "Should NOT emit no-op reader delegate for enriched entity");
    }

    [Test]
    public void RawSqlAsync_EntityT_SimpleColumns_GeneratesPropertySwitch()
    {
        var source = @"
using Quarry;
using System.Threading.Tasks;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
    public Col<string?> Email { get; }
    public Col<bool> IsActive => Default(true);
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public class Service
{
    public async Task Test(TestDbContext db)
    {
        var users = await db.RawSqlAsync<User>(""SELECT * FROM users"");
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        var code = GetInterceptorsCode(result);
        Assert.That(code, Is.Not.Null, "Should generate interceptors file");

        Assert.That(code, Does.Contain("switch (r.GetName(i))"),
            "Should generate switch-based reader for entity type");
        Assert.That(code, Does.Contain("case \"UserId\""),
            "Should generate switch case for UserId");
        Assert.That(code, Does.Contain("case \"UserName\""),
            "Should generate switch case for UserName");
        Assert.That(code, Does.Contain("case \"Email\""),
            "Should generate switch case for Email");
        Assert.That(code, Does.Contain("case \"IsActive\""),
            "Should generate switch case for IsActive");
        Assert.That(code, Does.Not.Contain("static _ => new User()"),
            "Should NOT emit no-op reader delegate");
    }

    [Test]
    public void RawSqlAsync_EntityT_WithCustomTypeMapping_GeneratesFromDbReader()
    {
        // Custom type mappings are a schema-level concern stored in ColumnInfo, not in the
        // type symbol. After ResolveRawSqlTypeInfo discovers properties from ITypeSymbol,
        // PatchWithColumnMetadata enriches them with ColumnInfo metadata (CustomTypeMappingClass,
        // DbReaderMethodName) from the EntityRegistry. This produces FromDb calls in the reader.
        var source = @"
using Quarry;
using System.Threading.Tasks;

namespace TestApp;

public readonly struct Money
{
    public decimal Amount { get; }
    public Money(decimal amount) => Amount = amount;
}

public class MoneyMapping : TypeMapping<Money, decimal>
{
    public override decimal ToDb(Money value) => value.Amount;
    public override Money FromDb(decimal value) => new(value);
}

public class AccountSchema : Schema
{
    public static string Table => ""accounts"";
    public Key<int> AccountId => Identity();
    public Col<string> AccountName => Length(100);
    public Col<Money> Balance => Mapped<Money, MoneyMapping>();
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Account> Accounts();
}

public class Service
{
    public async Task Test(TestDbContext db)
    {
        var accounts = await db.RawSqlAsync<Account>(""SELECT * FROM accounts"");
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        var code = GetInterceptorsCode(result);
        Assert.That(code, Is.Not.Null, "Should generate interceptors file");

        Assert.That(code, Does.Contain("switch (r.GetName(i))"),
            "Should generate switch-based reader for enriched entity type");
        Assert.That(code, Does.Contain("case \"AccountId\""),
            "Should generate switch case for AccountId");
        Assert.That(code, Does.Contain("case \"AccountName\""),
            "Should generate switch case for AccountName");
        Assert.That(code, Does.Contain("case \"Balance\""),
            "Should generate switch case for Balance");
        Assert.That(code, Does.Contain("MoneyMapping"),
            "Should reference MoneyMapping in the reader delegate");
        Assert.That(code, Does.Contain("FromDb"),
            "Should call FromDb for custom type mapping conversion");
        Assert.That(code, Does.Not.Contain("static _ => new Account()"),
            "Should NOT emit no-op reader delegate");
    }

    [Test]
    public void RawSqlAsync_ConcreteDto_WithProperties_GeneratesPropertySwitch()
    {
        // When T is a concrete DTO that exists in the source, the generator discovers
        // its properties via the semantic model and generates a switch-based reader.
        var source = @"
using Quarry;
using System.Threading.Tasks;

namespace TestApp;

public class UserDto
{
    public int UserId { get; set; }
    public string UserName { get; set; } = null!;
    public string? Email { get; set; }
}

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

public class Service
{
    public async Task Test(TestDbContext db)
    {
        var results = await db.RawSqlAsync<UserDto>(""SELECT UserId, UserName, Email FROM users"");
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        var code = GetInterceptorsCode(result);
        Assert.That(code, Is.Not.Null, "Should generate interceptors file");

        // Verify property switch cases are generated for each DTO property
        Assert.That(code, Does.Contain("case \"UserId\""),
            "Should generate switch case for UserId property");
        Assert.That(code, Does.Contain("case \"UserName\""),
            "Should generate switch case for UserName property");
        Assert.That(code, Does.Contain("case \"Email\""),
            "Should generate switch case for Email property");
    }

    #endregion

    #region Helpers

    private static int CountOccurrences(string source, string substring)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(substring, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += substring.Length;
        }
        return count;
    }

    #endregion
}
