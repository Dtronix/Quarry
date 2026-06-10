using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using Quarry.Generators;

namespace Quarry.Tests.Generation;

/// <summary>
/// Verifies that MySQL carrier bind blocks are emitted in SQL-text order when a renderer
/// emits placeholders out of chain order (#303). The DistinctOrderBy wrap hoists the
/// ORDER BY expression (param P1) textually before the WHERE (param P0); MySqlConnector
/// binds the Nth '?' to the Nth added parameter, so the P1 bind block must precede P0's.
/// Flat chains and named-placeholder dialects keep chain-order emission byte-identically.
/// </summary>
[TestFixture]
public class MySqlBindOrderGenerationTests
{
    private static readonly string QuarryCoreAssemblyPath = typeof(Schema).Assembly.Location;
    private static readonly string SystemRuntimeAssemblyPath = typeof(object).Assembly.Location;

    private const string SourceTemplate = @"
using Quarry;
namespace TestApp;

public class OrderSchema : Schema
{
    public static string Table => ""orders"";
    public Key<int> OrderId => Identity();
    public Col<int> UserId { get; }
    public Col<decimal> Total { get; }
}

[QuarryContext(Dialect = SqlDialect.{DIALECT})]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Order> Orders();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        decimal threshold = 100.00m;
        decimal bias = 10000.00m;
        var totals = await db.Orders()
            .Where(o => o.Total > threshold)
            .OrderBy(o => o.Total + bias)
            {DISTINCT}
            .Select(o => o.Total)
            .ExecuteFetchAllAsync();
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

    private static string BuildSource(string dialect, bool distinct)
        => SourceTemplate
            .Replace("{DIALECT}", dialect)
            .Replace("{DISTINCT}", distinct ? ".Distinct()" : "");

    [Test]
    public void WrapChain_MySQL_EmitsBindBlocksInSqlTextOrder()
    {
        var code = RunGeneratorAndGetInterceptors(BuildSource("MySQL", distinct: true));

        // The wrap shape: hoisted ORDER BY '?' (slot 1) textually precedes the WHERE '?'
        // (slot 0), with markers fully rewritten to bare '?'.
        Assert.That(code, Does.Contain("(`Total` + ?) AS `_o0`"),
            "Wrap path should hoist the ORDER BY expression into the inner SELECT");
        Assert.That(code, Does.Contain("WHERE `Total` > ?"),
            "WHERE placeholder should be bare '?'");
        Assert.That(code, Does.Not.Contain("{__Q"),
            "Bind-order markers must never leak into generated source");

        var p0 = code.IndexOf("var __p0 = __cmd.CreateParameter();", StringComparison.Ordinal);
        var p1 = code.IndexOf("var __p1 = __cmd.CreateParameter();", StringComparison.Ordinal);
        Assert.That(p0, Is.GreaterThanOrEqualTo(0), "P0 bind block should exist");
        Assert.That(p1, Is.GreaterThanOrEqualTo(0), "P1 bind block should exist");
        Assert.That(p1, Is.LessThan(p0),
            "MySQL wrap chain must bind P1 (hoisted ORDER BY param, 1st '?') before P0 " +
            "(WHERE param, 2nd '?') — SQL-text order, not chain order (#303)");
    }

    [Test]
    public void FlatChain_MySQL_KeepsChainOrderBinding()
    {
        var code = RunGeneratorAndGetInterceptors(BuildSource("MySQL", distinct: false));

        Assert.That(code, Does.Not.Contain("{__Q"),
            "Bind-order markers must never leak into generated source");

        var p0 = code.IndexOf("var __p0 = __cmd.CreateParameter();", StringComparison.Ordinal);
        var p1 = code.IndexOf("var __p1 = __cmd.CreateParameter();", StringComparison.Ordinal);
        Assert.That(p0, Is.GreaterThanOrEqualTo(0), "P0 bind block should exist");
        Assert.That(p1, Is.GreaterThanOrEqualTo(0), "P1 bind block should exist");
        Assert.That(p0, Is.LessThan(p1),
            "Flat MySQL chain renders placeholders in chain order; binding must stay " +
            "in GlobalIndex order (identity — byte-identical to pre-#303 emission)");
    }

    [Test]
    public void WrapChain_PostgreSQL_KeepsChainOrderBinding()
    {
        var code = RunGeneratorAndGetInterceptors(BuildSource("PostgreSQL", distinct: true));

        // PG's $N placeholders carry identity, so the wrap needs no bind reordering:
        // the hoisted ORDER BY expression renders with its post-body slot ($2). The SQL
        // lives in a C# verbatim string, so each '"' is doubled in the generated source.
        Assert.That(code, Does.Contain("(\"\"Total\"\" + $2) AS \"\"_o0\"\""),
            "PG wrap should pre-allocate the hoisted ORDER BY param at its global slot");

        var p0 = code.IndexOf("var __p0 = __cmd.CreateParameter();", StringComparison.Ordinal);
        var p1 = code.IndexOf("var __p1 = __cmd.CreateParameter();", StringComparison.Ordinal);
        Assert.That(p0, Is.GreaterThanOrEqualTo(0), "P0 bind block should exist");
        Assert.That(p1, Is.GreaterThanOrEqualTo(0), "P1 bind block should exist");
        Assert.That(p0, Is.LessThan(p1), "Named/positional-indexed dialects keep chain-order binding");
    }
}
