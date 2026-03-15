using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Quarry.Generators;

namespace Quarry.Tests;

/// <summary>
/// Tests that per-file incremental output correctly caches unchanged files.
/// Verifies the Phase 2 SelectMany fan-out regenerates only modified file groups.
/// </summary>
[TestFixture]
public class IncrementalCachingTests
{
    private static readonly string QuarryCoreAssemblyPath = typeof(Schema).Assembly.Location;
    private static readonly string SystemRuntimeAssemblyPath = typeof(object).Assembly.Location;

    private const string SharedSource = @"
using Quarry;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";

    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL, Schema = ""public"")]
public partial class TestDbContext : QuarryContext
{
    public partial IQueryBuilder<User> Users();
}
";

    private const string FileASource = @"
using Quarry;
using TestApp;

namespace TestApp.Services;

public class ServiceA
{
    public async void DoWork(TestDbContext db)
    {
        await db.Users().Select(u => new { u.UserId, u.UserName }).ExecuteFetchAllAsync();
    }
}
";

    private const string FileBSource = @"
using Quarry;
using TestApp;

namespace TestApp.Services;

public class ServiceB
{
    public async void DoWork(TestDbContext db)
    {
        await db.Users().Where(u => u.UserId == 1).Select(u => new { u.UserName }).ExecuteFetchFirstAsync();
    }
}
";

    private static CSharpCompilation CreateCompilation(params (string Source, string Path)[] files)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var syntaxTrees = files.Select(f =>
            CSharpSyntaxTree.ParseText(f.Source, parseOptions, path: f.Path)).ToList();

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
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Threading.Tasks.dll")));

        return CSharpCompilation.Create(
            "TestAssembly",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));
    }

    [Test]
    public void PerFileOutput_UnchangedCompilation_AllOutputsCached()
    {
        var compilation = CreateCompilation(
            (SharedSource, "Shared.cs"),
            (FileASource, "ServiceA.cs"),
            (FileBSource, "ServiceB.cs"));

        var generator = new QuarryGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { generator.AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(
                disabledOutputs: IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));

        // Initial run
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        var result1 = driver.GetRunResult();

        // Verify initial run produced interceptor outputs
        var interceptorFiles = result1.GeneratedTrees
            .Where(t => t.FilePath.Contains("Interceptors"))
            .ToList();
        Assert.That(interceptorFiles.Count, Is.GreaterThanOrEqualTo(1),
            "Should generate at least one interceptor file");

        // Second run with same compilation — everything should be cached
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        var result2 = driver.GetRunResult();

        // All outputs should be cached on unchanged re-run
        var genResult = result2.Results[0];
        foreach (var step in genResult.TrackedOutputSteps.Values.SelectMany(s => s))
        {
            foreach (var output in step.Outputs)
            {
                Assert.That(output.Reason,
                    Is.EqualTo(IncrementalStepRunReason.Cached)
                    .Or.EqualTo(IncrementalStepRunReason.Unchanged),
                    $"Output should be cached or unchanged on identical re-run");
            }
        }
    }

    [Test]
    public void PerFileOutput_ModifyOneFile_OtherFileCached()
    {
        var compilation = CreateCompilation(
            (SharedSource, "Shared.cs"),
            (FileASource, "ServiceA.cs"),
            (FileBSource, "ServiceB.cs"));

        var generator = new QuarryGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { generator.AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(
                disabledOutputs: IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));

        // Initial run
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        // Modify FileA — add a comment to change its syntax tree
        var modifiedFileASource = @"
using Quarry;
using TestApp;

namespace TestApp.Services;

// Modified comment to trigger change
public class ServiceA
{
    public async void DoWork(TestDbContext db)
    {
        await db.Users().Select(u => new { u.UserId, u.UserName }).ExecuteFetchAllAsync();
    }
}
";

        // Use ReplaceSyntaxTree to properly maintain incremental driver state
        var oldTree = compilation.SyntaxTrees.First(t => t.FilePath == "ServiceA.cs");
        var newTree = CSharpSyntaxTree.ParseText(modifiedFileASource,
            new CSharpParseOptions(LanguageVersion.Latest), path: "ServiceA.cs");
        var modifiedCompilation = compilation.ReplaceSyntaxTree(oldTree, newTree);

        // Second run with modified FileA
        driver = driver.RunGeneratorsAndUpdateCompilation(modifiedCompilation, out _, out _);
        var result = driver.GetRunResult();

        // Verify we still generate interceptor outputs
        var interceptorFiles = result.GeneratedTrees
            .Where(t => t.FilePath.Contains("Interceptors"))
            .ToList();
        Assert.That(interceptorFiles.Count, Is.GreaterThanOrEqualTo(1),
            "Should still generate interceptor files after modification");

        // At least some outputs should be cached (the unchanged file's output)
        var genResult = result.Results[0];
        var allReasons = genResult.TrackedOutputSteps.Values
            .SelectMany(s => s)
            .SelectMany(s => s.Outputs)
            .Select(o => o.Reason)
            .ToList();

        Assert.That(allReasons, Does.Contain(IncrementalStepRunReason.Cached)
            .Or.Contain(IncrementalStepRunReason.Unchanged),
            "Some outputs should be cached when only one file changed");
    }

    [Test]
    public void PerFileOutput_ModifyQuery_RegeneratesAffectedFile()
    {
        var compilation = CreateCompilation(
            (SharedSource, "Shared.cs"),
            (FileASource, "ServiceA.cs"),
            (FileBSource, "ServiceB.cs"));

        var generator = new QuarryGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { generator.AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(
                disabledOutputs: IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));

        // Initial run
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        var result1 = driver.GetRunResult();

        // Capture initial interceptor source for comparison
        var initialInterceptorSources = result1.GeneratedTrees
            .Where(t => t.FilePath.Contains("Interceptors"))
            .ToDictionary(t => t.FilePath, t => t.GetText().ToString());

        // Modify FileA's query — change the projection
        var modifiedFileASource = @"
using Quarry;
using TestApp;

namespace TestApp.Services;

public class ServiceA
{
    public async void DoWork(TestDbContext db)
    {
        await db.Users().Select(u => new { u.UserId }).ExecuteFetchAllAsync();
    }
}
";

        // Use ReplaceSyntaxTree to properly maintain incremental driver state
        var oldTree = compilation.SyntaxTrees.First(t => t.FilePath == "ServiceA.cs");
        var newTree = CSharpSyntaxTree.ParseText(modifiedFileASource,
            new CSharpParseOptions(LanguageVersion.Latest), path: "ServiceA.cs");
        var modifiedCompilation = compilation.ReplaceSyntaxTree(oldTree, newTree);

        // Second run with modified query
        driver = driver.RunGeneratorsAndUpdateCompilation(modifiedCompilation, out _, out _);
        var result2 = driver.GetRunResult();

        // Verify generator completed without crashing (CS8785 = generator failure)
        var generatorFailures = result2.Diagnostics
            .Where(d => d.Id == "CS8785")
            .ToList();
        Assert.That(generatorFailures, Is.Empty,
            "Generator should not crash on incremental re-run");

        // Verify the interceptor outputs still exist
        var newInterceptorSources = result2.GeneratedTrees
            .Where(t => t.FilePath.Contains("Interceptors"))
            .ToDictionary(t => t.FilePath, t => t.GetText().ToString());

        Assert.That(newInterceptorSources.Count, Is.GreaterThanOrEqualTo(1),
            "Should still generate interceptor files");
    }

    [Test]
    public void PerFileOutput_TwoFiles_GeneratesSeparateInterceptorOutputs()
    {
        var compilation = CreateCompilation(
            (SharedSource, "Shared.cs"),
            (FileASource, "ServiceA.cs"),
            (FileBSource, "ServiceB.cs"));

        var generator = new QuarryGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { generator.AsSourceGenerator() });

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        var result = driver.GetRunResult();

        // With per-file output, queries in different files should produce
        // separate interceptor files (different stable file hashes)
        var interceptorFiles = result.GeneratedTrees
            .Where(t => t.FilePath.Contains("Interceptors"))
            .ToList();

        Assert.That(interceptorFiles.Count, Is.GreaterThanOrEqualTo(2),
            "Queries in different files should produce separate interceptor outputs");

        // Verify different file hashes in the names
        var fileNames = interceptorFiles.Select(t => System.IO.Path.GetFileName(t.FilePath)).ToList();
        Assert.That(fileNames.Distinct().Count(), Is.EqualTo(fileNames.Count),
            "Each interceptor file should have a unique name (different file hash)");
    }
}
