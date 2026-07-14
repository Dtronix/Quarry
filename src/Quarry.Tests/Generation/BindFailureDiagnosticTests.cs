using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using Quarry.Generators;

namespace Quarry.Tests.Generation;

/// <summary>
/// End-to-end tests for the Stage 3 bind-failure path (#311, acceptance criterion 1).
/// A bind exception produces no BoundCallSite to attach an error to; before #311 it was
/// reported to a [ThreadStatic] bag whose entries were drained-and-discarded at
/// orchestrator entry — the QRY900 never surfaced. Bind failures now travel as
/// BindStageResult values to a dedicated report node, so a forced binder exception must
/// surface as a QRY900 compile diagnostic at the failing call site.
/// </summary>
[TestFixture]
public class BindFailureDiagnosticTests
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

    private static ImmutableArray<Diagnostic> RunGenerator(string queryCode)
    {
        var compilation = CreateCompilation(SharedSchema + queryCode);
        var generator = new QuarryGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { generator.AsSourceGenerator() });
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
        return diagnostics;
    }

    private const string QueryCode = @"
public class Svc
{
    private readonly TestApp.TestDbContext _db;
    public Svc(TestApp.TestDbContext db) { _db = db; }
    public async Task Run()
    {
        var items = await _db.Products().Where(p => p.ProductId == 1).ExecuteFetchAllAsync();
    }
}
";

    [Test]
    public void ForcedBindException_SurfacesQRY900()
    {
        try
        {
            Generators.IR.CallSiteBinder.TestThrowOnMethodName = "Where";
            var diags = RunGenerator(QueryCode);
            var qry900 = diags.Where(d => d.Id == "QRY900").ToList();
            Assert.That(qry900, Is.Not.Empty, "a bind-stage exception must surface as a QRY900 compile diagnostic");
            var msg = qry900[0].GetMessage();
            Assert.That(msg, Does.Contain("Bind:"), "message should identify the failing stage");
            Assert.That(msg, Does.Contain("Test-forced bind failure"), "message should carry the exception text");
            Assert.That(qry900[0].Location.GetLineSpan().StartLinePosition.Line, Is.GreaterThan(0),
                "diagnostic should carry the failing call site's location");
        }
        finally
        {
            Generators.IR.CallSiteBinder.TestThrowOnMethodName = null;
        }
    }

    [Test]
    public void NoBindException_NoQRY900()
    {
        var diags = RunGenerator(QueryCode);
        Assert.That(diags.Where(d => d.Id == "QRY900"), Is.Empty,
            "a clean run must not report QRY900");
    }

    [Test]
    public void AllSitesFailBind_GroupLessFile_StillSurfacesQRY900()
    {
        // The group-less hazard: when EVERY site in a file fails bind, no
        // TranslatedCallSite exists for the file, so no FileInterceptorGroup is created
        // and nothing group-driven can report. The dedicated bind-failure output node
        // must still surface QRY900 — the pre-#311 ThreadStatic bag could not (its only
        // drain ran inside group emission).
        var compilation = CreateCompilation(SharedSchema + QueryCode);
        var generator = new QuarryGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { generator.AsSourceGenerator() });
        try
        {
            Generators.IR.CallSiteBinder.TestThrowOnMethodName = "*";
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diags);

            var qry900 = diags.Where(d => d.Id == "QRY900").ToList();
            Assert.That(qry900, Is.Not.Empty,
                "bind failures must surface even when the file produces no interceptor group");

            // Prove the run really was group-less: no interceptor file was generated.
            var interceptorTrees = driver.GetRunResult().GeneratedTrees
                .Where(t => t.FilePath.Contains(".Interceptors."))
                .ToList();
            Assert.That(interceptorTrees, Is.Empty,
                "with every site failing bind, no interceptor group (and no file) should exist");
        }
        finally
        {
            Generators.IR.CallSiteBinder.TestThrowOnMethodName = null;
        }
    }
}
