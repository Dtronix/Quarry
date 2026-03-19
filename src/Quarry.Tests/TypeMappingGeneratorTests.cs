using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Quarry.Generators;

namespace Quarry.Tests;

/// <summary>
/// Layers 1 + 2: SchemaParser Mapped&lt;&gt; extraction and QRY003 diagnostic tests.
/// Uses compilation-based testing with inline source code.
/// </summary>
[TestFixture]
public class TypeMappingGeneratorTests
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

        return CSharpCompilation.Create(
            "TestAssembly",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));
    }

    private static GeneratorDriverRunResult RunGenerator(CSharpCompilation compilation)
    {
        var generator = new QuarryGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() });
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    private static (GeneratorDriverRunResult Result, IReadOnlyList<Diagnostic> Diagnostics) RunGeneratorWithDiagnostics(CSharpCompilation compilation)
    {
        var generator = new QuarryGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() });
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
        return (driver.GetRunResult(), diagnostics);
    }

    private const string MoneyAndMappingSource = @"
using Quarry;

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
";

    #region Layer 1: SchemaParser – Mapped<> Extraction

    [Test]
    public void Generator_WithMappedColumn_GeneratesEntityProperty()
    {
        var source = MoneyAndMappingSource + @"
public class AccountSchema : Schema
{
    public static string Table => ""accounts"";
    public Key<int> AccountId => Identity();
    public Col<Money> Balance => Mapped<Money, MoneyMapping>();
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Account> Accounts();
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var entitySource = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("Account.g.cs"));
        Assert.That(entitySource, Is.Not.Null, "Should generate Account.g.cs");

        var entityCode = entitySource!.GetText().ToString();
        Assert.That(entityCode, Does.Contain("public partial class Account"));
        Assert.That(entityCode, Does.Contain("Money Balance"));
    }

    [Test]
    public void Generator_WithChainedMappedLength_ParsesBothModifiers()
    {
        // Mapped<> chained with other modifiers
        var source = MoneyAndMappingSource + @"
public class AccountSchema : Schema
{
    public static string Table => ""accounts"";
    public Key<int> AccountId => Identity();
    public Col<Money> Balance => Mapped<Money, MoneyMapping>();
    public Col<string> AccountName => Length(100);
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Account> Accounts();
}
";

        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var entitySource = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("Account.g.cs"));
        Assert.That(entitySource, Is.Not.Null);

        var entityCode = entitySource!.GetText().ToString();
        Assert.That(entityCode, Does.Contain("Money Balance"));
        Assert.That(entityCode, Does.Contain("string AccountName"));
    }

    #endregion

    #region Layer 2: QRY003 Diagnostic

    [Test]
    public void Generator_UnmappedCustomType_EmitsQRY003()
    {
        var source = @"
using Quarry;

namespace TestApp;

public readonly struct Money
{
    public decimal Amount { get; }
    public Money(decimal amount) => Amount = amount;
}

public class AccountSchema : Schema
{
    public static string Table => ""accounts"";
    public Key<int> AccountId => Identity();
    public Col<Money> Balance { get; }
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Account> Accounts();
}
";

        var compilation = CreateCompilation(source);
        var (_, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qry003 = diagnostics.Where(d => d.Id == "QRY003").ToList();
        Assert.That(qry003, Has.Count.GreaterThan(0),
            "Should emit QRY003 for unmapped custom type");
        Assert.That(qry003[0].GetMessage(), Does.Contain("Balance"),
            "QRY003 should mention the column name");
    }

    [Test]
    public void Generator_MappedCustomType_DoesNotEmitQRY003()
    {
        var source = MoneyAndMappingSource + @"
public class AccountSchema : Schema
{
    public static string Table => ""accounts"";
    public Key<int> AccountId => Identity();
    public Col<Money> Balance => Mapped<Money, MoneyMapping>();
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Account> Accounts();
}
";

        var compilation = CreateCompilation(source);
        var (_, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qry003 = diagnostics.Where(d => d.Id == "QRY003").ToList();
        Assert.That(qry003, Has.Count.EqualTo(0),
            "Should not emit QRY003 when Mapped<> is used");
    }

    [Test]
    public void Generator_EnumColumn_DoesNotEmitQRY003()
    {
        var source = @"
using Quarry;

namespace TestApp;

public enum Status { Active, Inactive }

public class ItemSchema : Schema
{
    public static string Table => ""items"";
    public Key<int> ItemId => Identity();
    public Col<Status> Status { get; }
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Item> Items();
}
";

        var compilation = CreateCompilation(source);
        var (_, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qry003 = diagnostics.Where(d => d.Id == "QRY003").ToList();
        Assert.That(qry003, Has.Count.EqualTo(0),
            "Should not emit QRY003 for enum types");
    }

    [Test]
    public void Generator_KnownFallbackTypes_DoNotEmitQRY003()
    {
        var source = @"
using Quarry;
using System;

namespace TestApp;

public class EventSchema : Schema
{
    public static string Table => ""events"";
    public Key<int> EventId => Identity();
    public Col<DateTimeOffset> OccurredAt { get; }
    public Col<TimeSpan> Duration { get; }
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Event> Events();
}
";

        var compilation = CreateCompilation(source);
        var (_, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qry003 = diagnostics.Where(d => d.Id == "QRY003").ToList();
        Assert.That(qry003, Has.Count.EqualTo(0),
            "Should not emit QRY003 for known fallback types like DateTimeOffset and TimeSpan");
    }

    [Test]
    public void Generator_PrimitiveTypes_DoNotEmitQRY003()
    {
        var source = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> Name => Length(100);
    public Col<bool> IsActive { get; }
    public Col<decimal> Balance { get; }
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}
";

        var compilation = CreateCompilation(source);
        var (_, diagnostics) = RunGeneratorWithDiagnostics(compilation);

        var qry003 = diagnostics.Where(d => d.Id == "QRY003").ToList();
        Assert.That(qry003, Has.Count.EqualTo(0),
            $"Should not emit QRY003 for primitive types but got: {string.Join("; ", qry003.Select(d => d.GetMessage()))}");
    }

    #endregion
}
