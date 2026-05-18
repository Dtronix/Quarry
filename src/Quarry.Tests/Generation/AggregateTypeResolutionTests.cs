using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using Quarry.Generators;

namespace Quarry.Tests.Generation;

/// <summary>
/// Regression tests for <c>ProjectionAnalyzer.ResolveAggregateClrType</c>.
/// </summary>
/// <remarks>
/// In Stage 1 (UsageSiteDiscovery) the Quarry-generated entity class does not yet
/// exist in the SemanticModel, so an aggregate argument like <c>o.Total</c> is an
/// ErrorType expression. The pre-fix resolver let Roslyn's overload resolution
/// against the Error-typed argument silently pick the <c>Sql.Sum(decimal)</c>
/// candidate and return <c>decimal</c> as the result CLR type. The fix consults
/// the schema-driven column lookup first, which is authoritative for direct
/// entity-property access independent of SemanticModel state.
/// </remarks>
[TestFixture]
public class AggregateTypeResolutionTests
{
    private static readonly string QuarryCoreAssemblyPath = typeof(Schema).Assembly.Location;
    private static readonly string SystemRuntimeAssemblyPath = typeof(object).Assembly.Location;

    private static CSharpCompilation CreateCompilation(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions);

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
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));
    }

    private static string RunAndGetInterceptors(CSharpCompilation compilation)
    {
        var generator = new QuarryGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { generator.AsSourceGenerator() });
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        var result = driver.GetRunResult();

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "expected an interceptors file to be generated");
        return interceptorsTree!.GetText().ToString();
    }

    private static string Source(string columnType) => $@"
using Quarry;
using System.Threading.Tasks;

namespace TestApp;

public class OrderSchema : Schema
{{
    public static string Table => ""orders"";
    public Key<int> OrderId => Identity();
    public Col<{columnType}> Total {{ get; }}
}}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{{
    public partial IEntityAccessor<Order> Orders();
}}

public static class Queries
{{
    public static async Task<{columnType}> Test(TestDbContext db)
    {{
        return await db.Orders().Select(o => Sql.Sum(o.Total)).ExecuteScalarAsync<{columnType}>();
    }}
}}
";

    private static string AvgSource(string columnType, string resultType) => $@"
using Quarry;
using System.Threading.Tasks;

namespace TestApp;

public class OrderSchema : Schema
{{
    public static string Table => ""orders"";
    public Key<int> OrderId => Identity();
    public Col<{columnType}> Total {{ get; }}
}}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{{
    public partial IEntityAccessor<Order> Orders();
}}

public static class Queries
{{
    public static async Task<{resultType}> Test(TestDbContext db)
    {{
        return await db.Orders().Select(o => Sql.Avg(o.Total)).ExecuteScalarAsync<{resultType}>();
    }}
}}
";

    // ── Sum: the carrier's TResult must match the column CLR type. ──

    [Test]
    public void Sum_OverDoubleColumn_ResolvesToDouble()
    {
        var code = RunAndGetInterceptors(CreateCompilation(Source("double")));

        Assert.That(code, Does.Contain("IQueryBuilder<Order, double>"),
            "carrier interface must use double when column is Col<double>");
        Assert.That(code, Does.Not.Contain("IQueryBuilder<Order, decimal>"),
            "must NOT fall back to decimal default for a Col<double> column");
    }

    [Test]
    public void Sum_OverDecimalColumn_ResolvesToDecimal()
    {
        var code = RunAndGetInterceptors(CreateCompilation(Source("decimal")));

        Assert.That(code, Does.Contain("IQueryBuilder<Order, decimal>"),
            "carrier interface must use decimal when column is Col<decimal>");
    }

    [Test]
    public void Sum_OverIntColumn_ResolvesToInt()
    {
        var code = RunAndGetInterceptors(CreateCompilation(Source("int")));

        Assert.That(code, Does.Contain("IQueryBuilder<Order, int>"),
            "carrier interface must use int when column is Col<int>");
        Assert.That(code, Does.Not.Contain("IQueryBuilder<Order, decimal>"),
            "must NOT fall back to decimal default for a Col<int> column");
    }

    [Test]
    public void Sum_OverLongColumn_ResolvesToLong()
    {
        var code = RunAndGetInterceptors(CreateCompilation(Source("long")));

        Assert.That(code, Does.Contain("IQueryBuilder<Order, long>"),
            "carrier interface must use long when column is Col<long>");
        Assert.That(code, Does.Not.Contain("IQueryBuilder<Order, decimal>"),
            "must NOT fall back to decimal default for a Col<long> column");
    }

    // ── Avg: tests Sql.Avg's resolution path, which shares the same defaultType("decimal"). ──

    [Test]
    public void Avg_OverDoubleColumn_ResolvesToDouble()
    {
        var code = RunAndGetInterceptors(CreateCompilation(AvgSource("double", "double")));

        Assert.That(code, Does.Contain("IQueryBuilder<Order, double>"),
            "Avg over a Col<double> column must resolve to double");
        Assert.That(code, Does.Not.Contain("IQueryBuilder<Order, decimal>"),
            "must NOT fall back to decimal default for a Col<double> column");
    }
}
