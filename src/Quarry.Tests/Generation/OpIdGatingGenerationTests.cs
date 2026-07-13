using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using Quarry.Generators;

namespace Quarry.Tests.Generation;

/// <summary>
/// Verifies the single-row Insert terminal gates OpId generation on an enabled logger
/// (#308 item 3). OpId.Next() is an Interlocked.Increment on a shared static; emitting it
/// unconditionally caused cross-core cache-line contention per insert even with logging
/// disabled. The gated form matches the query preamble and batch-insert terminal.
/// </summary>
[TestFixture]
public class OpIdGatingGenerationTests
{
    private static readonly string QuarryCoreAssemblyPath = typeof(Schema).Assembly.Location;
    private static readonly string SystemRuntimeAssemblyPath = typeof(object).Assembly.Location;

    private const string InsertSource = @"
using Quarry;
namespace TestApp;

public class WidgetSchema : Schema
{
    public static string Table => ""widgets"";
    public Key<int> WidgetId => Identity();
    public Col<string> Name { get; }
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Widget> Widgets();
}

public static class Queries
{
    public static async Task Test(TestDbContext db)
    {
        await db.Widgets().Insert(new Widget { Name = ""x"" }).ExecuteNonQueryAsync();
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
    public void SingleRowInsert_GatesOpIdOnLogger()
    {
        var code = RunGeneratorAndGetInterceptors(InsertSource);

        Assert.That(code, Does.Contain("var __opId = __logger != null ? OpId.Next() : 0;"),
            "Insert terminal must gate OpId.Next() on an enabled logger");
        Assert.That(code, Does.Not.Contain("var __opId = OpId.Next();"),
            "Insert terminal must not call OpId.Next() unconditionally");
    }
}
