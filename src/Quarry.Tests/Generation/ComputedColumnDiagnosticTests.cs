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
/// equivalent filter. The Action&lt;T&gt; lambda form (<c>Set(p =&gt; p.X = v)</c>) is
/// the surface API users could reach if init-only enforcement on generated entities
/// was bypassed; QRY075 backstops it. The POCO form (<c>Set(new T { ... })</c>)
/// already filters computed columns via UpdateInfo, so QRY075 must stay silent.
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

    [Test]
    public void QRY075_UpdateSetAction_AssignToComputedColumn_Reports()
    {
        // The generator runs on the parse tree before C# semantic analysis
        // surfaces the init-only assignment error, so the synthesized lambda
        // body `p.DiscountedPrice = 99m` reaches the analyzer's UpdateSetAction
        // hook. QRY075 must fire with the schema's property name and entity
        // name in the message. (The synthesized compilation also produces a CS
        // error for the init-only assignment; we only assert the generator
        // diagnostic here.)
        var (diags, _, _) = RunGenerator(@"
public class Svc
{
    private readonly TestApp.TestDbContext _db;
    public Svc(TestApp.TestDbContext db) { _db = db; }
    public async Task Run()
    {
        await _db.Products().Update().Set(p => p.DiscountedPrice = 99m).Where(p => p.ProductId == 1).ExecuteNonQueryAsync();
    }
}
");
        var qry075 = diags.Where(d => d.Id == "QRY075").ToList();
        Assert.That(qry075, Is.Not.Empty, "Update().Set lambda assigning a Computed<T>() column must report QRY075");
        var msg = qry075[0].GetMessage();
        Assert.That(msg, Does.Contain("DiscountedPrice"), "Diagnostic should name the offending column");
        Assert.That(msg, Does.Contain("Product"), "Diagnostic should name the offending entity");
    }

    [Test]
    public void QRY075_UpdateSetAction_AssignToWritableColumn_DoesNotReport()
    {
        // Sanity check the negative case: assigning a writable column from the
        // same Action lambda must not trigger QRY075. Guards against the hook
        // over-firing on every UpdateSetAction.
        var (diags, _, _) = RunGenerator(@"
public class Svc
{
    private readonly TestApp.TestDbContext _db;
    public Svc(TestApp.TestDbContext db) { _db = db; }
    public async Task Run()
    {
        await _db.Products().Update().Set(p => p.Price = 99m).Where(p => p.ProductId == 1).ExecuteNonQueryAsync();
    }
}
");
        var qry075 = diags.Where(d => d.Id == "QRY075").ToList();
        Assert.That(qry075, Is.Empty, "Update().Set lambda assigning a writable column must not report QRY075");
    }
}
