using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using Quarry.Generators;

namespace Quarry.Tests.Generation;

/// <summary>
/// Generator-driver tests for QRY075: Update().Set targeting a computed column
/// must produce a compile-time diagnostic.
///
/// Computed columns are populated by the database from a stored expression
/// (Computed&lt;T&gt;()); INSERT silently filters them via InsertInfo, but UPDATE has no
/// equivalent filter. Without QRY075 the SQL emitter would render
/// <c>SET "Computed" = @p0</c> and the database engine would reject the statement
/// at execution time. This test fixture asserts the diagnostic fires for the three
/// Update SET surface forms (typed lambda, Action&lt;T&gt; lambda, POCO) and stays silent
/// for non-computed columns.
/// </summary>
[TestFixture]
public class ComputedColumnDiagnosticTests
{
    private static readonly string QuarryCoreAssemblyPath = typeof(Schema).Assembly.Location;
    private static readonly string SystemRuntimeAssemblyPath = typeof(object).Assembly.Location;

    private const string SharedSchema = @"
using Quarry;
using System;
using System.Threading.Tasks;
namespace TestApp;

public class ProductSchema : Schema
{
    public static string Table => ""products"";
    public Key<int> ProductId => Identity();
    public Col<string> ProductName => Length(200);
    public Col<decimal> Price => Precision(18, 2);
    public Col<decimal> DiscountedPrice => Computed<decimal>();
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Product> Products();
}
";

    private static CSharpCompilation CreateCompilation(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var syntaxTrees = new[] { CSharpSyntaxTree.ParseText(source, parseOptions) };

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

    private static (ImmutableArray<Diagnostic> Diagnostics, IReadOnlyList<string> GeneratedFiles, string InterceptorSource) RunGenerator(string queryCode)
    {
        var source = SharedSchema + queryCode;
        var compilation = CreateCompilation(source);
        var generator = new QuarryGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() });
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
        var run = driver.GetRunResult();
        var files = run.GeneratedTrees.Select(t => Path.GetFileName(t.FilePath)).ToList();
        var interceptorTree = run.GeneratedTrees.FirstOrDefault(t => t.FilePath.Contains(".Interceptors."));
        var interceptorSource = interceptorTree?.GetText().ToString() ?? "";
        return (diagnostics, files, interceptorSource);
    }

    [Test]
    public void QRY075_UpdateSet_TypedLambda_OnComputedColumn_Reports()
    {
        var (diags, files, interceptorSource) = RunGenerator(@"
public class Svc
{
    private readonly TestApp.TestDbContext _db;
    public Svc(TestApp.TestDbContext db) { _db = db; }
    public async Task Run()
    {
        await _db.Products().Update().Set(p => p.DiscountedPrice, 99m).Where(p => p.ProductId == 1).ExecuteNonQueryAsync();
    }
}
");
        // DEBUG: dump every diagnostic and generated file so we can see the pipeline output.
        foreach (var d in diags)
            TestContext.Progress.WriteLine($"[DIAG] {d.Id}: {d.GetMessage()}");
        foreach (var f in files)
            TestContext.Progress.WriteLine($"[FILE] {f}");
        TestContext.Progress.WriteLine("[INTERCEPTOR-SRC]\n" + interceptorSource);

        var qry075 = diags.Where(d => d.Id == "QRY075").ToList();
        Assert.That(qry075, Is.Not.Empty, "Update().Set on a Computed<T>() column must report QRY075");
        var msg = qry075[0].GetMessage();
        Assert.That(msg, Does.Contain("DiscountedPrice"));
        Assert.That(msg, Does.Contain("Product"));
    }

    [Test]
    public void QRY075_UpdateSet_NonComputedColumn_DoesNotReport()
    {
        var (diags, files, interceptorSource) = RunGenerator(@"
public class Svc
{
    private readonly TestApp.TestDbContext _db;
    public Svc(TestApp.TestDbContext db) { _db = db; }
    public async Task Run()
    {
        await _db.Products().Update().Set(p => p.Price, 99m).Where(p => p.ProductId == 1).ExecuteNonQueryAsync();
    }
}
");
        foreach (var d in diags)
            TestContext.Progress.WriteLine($"[DIAG-NC] {d.Id}: {d.GetMessage()}");
        foreach (var f in files)
            TestContext.Progress.WriteLine($"[FILE-NC] {f}");
        var qry075 = diags.Where(d => d.Id == "QRY075").ToList();
        Assert.That(qry075, Is.Empty, "Update().Set on a non-computed column must not report QRY075");
    }

    [Test]
    public void QRY075_UpdateSetPoco_DoesNotReport_BecauseInsertInfoFiltersComputed()
    {
        // UpdateSetPoco builds SET terms from UpdateInfo.Columns, which already filters
        // computed columns at construction time. So the POCO form can never produce a
        // SET against a computed column, and no QRY075 should fire.
        var (diags, files, interceptorSource) = RunGenerator(@"
public class Svc
{
    private readonly TestApp.TestDbContext _db;
    public Svc(TestApp.TestDbContext db) { _db = db; }
    public async Task Run()
    {
        await _db.Products().Update().Set(new Product { ProductName = ""x"", Price = 10m }).Where(p => p.ProductId == 1).ExecuteNonQueryAsync();
    }
}
");
        var qry075 = diags.Where(d => d.Id == "QRY075").ToList();
        Assert.That(qry075, Is.Empty, "POCO Update().Set must not report QRY075 — UpdateInfo already filters computed columns");
    }
}
