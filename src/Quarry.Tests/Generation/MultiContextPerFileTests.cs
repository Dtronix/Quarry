using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using Quarry.Generators;

namespace Quarry.Tests.Generation;

/// <summary>
/// Verifies that two <c>[QuarryContext]</c> classes declared in the same source
/// file each get their own interceptor <c>.g.cs</c> output. Per llm.md
/// §"Caching Boundaries", <c>FileInterceptorGroup</c> is keyed by
/// (context class name, source file path), so two contexts in one file must
/// produce two distinct interceptor files. Carrier classes are emitted as
/// <c>file sealed class Chain_N</c>, which makes <c>Chain_0</c> in each file
/// independently scoped — but the test still asserts that the two interceptor
/// files don't share the same generated path.
/// </summary>
[TestFixture]
public class MultiContextPerFileTests
{
    private static readonly string QuarryCoreAssemblyPath = typeof(Schema).Assembly.Location;
    private static readonly string SystemRuntimeAssemblyPath = typeof(object).Assembly.Location;

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

    private static GeneratorDriverRunResult RunGenerator(CSharpCompilation compilation)
    {
        var generator = new QuarryGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() });
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    [Test]
    public void TwoContextsInOneFile_EmitTwoIndependentInterceptorFiles()
    {
        // Two contexts declared in the same syntax tree, each with its own
        // entity accessor and a usage chain that forces carrier emission.
        // The generator must produce two distinct interceptor .g.cs files
        // (one per context class) — not a single merged file.
        const string source = @"
using Quarry;
using System;
using System.Threading.Tasks;
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
    public Col<int> UserId { get; }
    public Col<decimal> Total { get; }
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class FirstDb : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class SecondDb : QuarryContext
{
    public partial IEntityAccessor<Order> Orders();
}

public class Svc
{
    private readonly FirstDb _first;
    private readonly SecondDb _second;
    public Svc(FirstDb first, SecondDb second) { _first = first; _second = second; }
    public async Task RunFirst() => await _first.Users().Where(u => u.UserId > 0).ExecuteFetchAllAsync();
    public async Task RunSecond() => await _second.Orders().Where(o => o.Total > 0m).ExecuteFetchAllAsync();
}
";
        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorTrees = result.GeneratedTrees
            .Where(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"))
            .ToList();

        Assert.That(interceptorTrees, Has.Count.EqualTo(2),
            $"Expected exactly 2 interceptor files (one per context). Got: {string.Join(", ", interceptorTrees.Select(t => Path.GetFileName(t.FilePath)))}");

        var fileNames = interceptorTrees.Select(t => Path.GetFileName(t.FilePath)).ToList();
        Assert.That(fileNames.Any(n => n.StartsWith("FirstDb.Interceptors.")), Is.True,
            $"FirstDb interceptor file missing. Got: {string.Join(", ", fileNames)}");
        Assert.That(fileNames.Any(n => n.StartsWith("SecondDb.Interceptors.")), Is.True,
            $"SecondDb interceptor file missing. Got: {string.Join(", ", fileNames)}");

        // Each interceptor file is compiled with file-scoped accessibility, so
        // each file emits its own Chain_0 carrier. Confirm both files declare
        // the carrier class — proves the two contexts ran through carrier
        // emission independently (not deduplicated against each other).
        foreach (var tree in interceptorTrees)
        {
            var src = tree.GetText().ToString();
            Assert.That(src, Does.Contain("file sealed class Chain_"),
                $"{Path.GetFileName(tree.FilePath)}: expected a file-scoped Chain_ carrier class");
        }
    }
}
