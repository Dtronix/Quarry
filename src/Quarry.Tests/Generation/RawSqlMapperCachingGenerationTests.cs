using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using Quarry.Generators;

namespace Quarry.Tests.Generation;

/// <summary>
/// Verifies RawSql readers reference cached mapper fields instead of allocating a custom
/// TypeMapping instance per row per column (#308 item 5). The chain path already caches
/// mappers in static fields; the RawSql readers used to emit `new {Mapper}().FromDb(...)`
/// inside the per-row Read.
/// </summary>
[TestFixture]
public class RawSqlMapperCachingGenerationTests
{
    private static readonly string QuarryCoreAssemblyPath = typeof(Schema).Assembly.Location;
    private static readonly string SystemRuntimeAssemblyPath = typeof(object).Assembly.Location;

    private const string MappedEntitySource = @"
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

public class AccountSchema : Schema
{
    public static string Table => ""accounts"";
    public Key<int> AccountId => Identity();
    public Col<Money> Balance => Mapped<Money, MoneyMapping>();
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Account> Accounts();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        var rows = await db.RawSqlAsync<Account>(""SELECT AccountId, Balance FROM accounts"");
    }
}
";

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

    private static string RunGeneratorAndGetInterceptors(string source)
    {
        var generator = new QuarryGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { generator.AsSourceGenerator() });
        driver = driver.RunGeneratorsAndUpdateCompilation(CreateCompilation(source), out _, out _);
        var result = driver.GetRunResult();

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");
        return interceptorsTree!.GetText().ToString();
    }

    [Test]
    public void RawSqlReader_UsesCachedMapper_NotPerRowAllocation()
    {
        var code = RunGeneratorAndGetInterceptors(MappedEntitySource);

        // The mapper must actually be applied to the Balance column.
        Assert.That(code, Does.Contain(".FromDb("),
            "Balance column should be materialized through the MoneyMapping.FromDb mapper");

        // No per-row allocation: the old form was `new MoneyMapping().FromDb(...)`, whose
        // `().FromDb(` signature must not appear. The cached form is `_mapper_....FromDb(...)`.
        Assert.That(code, Does.Not.Contain("().FromDb("),
            "RawSql reader must not allocate a mapper instance per row (new Mapping().FromDb)");

        // A cached mapper field must be referenced (either the struct's own field or the
        // file-scope cached field).
        Assert.That(code, Does.Contain("_mapper_"),
            "RawSql reader should reference a cached mapper field");
    }
}
