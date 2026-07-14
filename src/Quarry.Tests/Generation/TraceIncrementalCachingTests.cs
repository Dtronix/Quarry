using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using Quarry.Generators;

namespace Quarry.Tests.Generation;

/// <summary>
/// Warm-run trace persistence (#311, acceptance criterion 3). Trace lines used to be
/// read from the [ThreadStatic] TraceCapture at emission time — a different pipeline
/// node than the orchestrator that produced them, so a cached orchestrator result plus
/// a re-running emission (every keystroke recombines with CompilationProvider) read
/// whatever the current thread happened to hold. Trace lines are now captured onto
/// <c>AssembledPlan.TraceLines</c> inside the orchestrator; cached groups carry them,
/// so <c>// [Trace]</c> comments survive incremental runs regardless of thread.
/// </summary>
[TestFixture]
public class TraceIncrementalCachingTests
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

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}
";

    private const string TracedServiceSource = @"
using Quarry;
using TestApp;
using System.Threading.Tasks;

namespace TestApp.Services;

public class TracedService
{
    public async Task DoWork(TestDbContext db)
    {
        await db.Users()
            .Where(u => u.UserId == 1)
            .Trace()
            .Select(u => (u.UserId, u.UserName))
            .ExecuteFetchAllAsync();
    }
}
";

    private const string HelperSource = @"
namespace TestApp.Services;

public static class Helper
{
    public static int Add(int a, int b) => a + b;
}
";

    private const string HelperSourceModified = @"
namespace TestApp.Services;

public static class Helper
{
    // warm-run edit: touches the compilation without touching any Quarry call site
    public static int Add(int a, int b) => a + b;
    public static int Sub(int a, int b) => a - b;
}
";

    private static readonly CSharpParseOptions ParseOptions =
        new(LanguageVersion.Latest, preprocessorSymbols: new[] { "QUARRY_TRACE" });

    private static CSharpCompilation CreateCompilation(params (string Source, string Path)[] files)
    {
        var syntaxTrees = files.Select(f =>
            CSharpSyntaxTree.ParseText(f.Source, ParseOptions, path: f.Path)).ToList();

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

    private static string GetTracedInterceptorSource(GeneratorDriverRunResult result)
    {
        var tree = result.GeneratedTrees.FirstOrDefault(t =>
            t.FilePath.Contains(".Interceptors.") && t.GetText().ToString().Contains("TracedService"));
        Assert.That(tree, Is.Not.Null, "an interceptor file for TracedService must be generated");
        return tree!.GetText().ToString();
    }

    [Test]
    public void TraceComments_SurviveWarmRun_AfterUnrelatedFileEdit()
    {
        var compilation = CreateCompilation(
            (SharedSource, "Shared.cs"),
            (TracedServiceSource, "TracedService.cs"),
            (HelperSource, "Helper.cs"));

        var generator = new QuarryGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { generator.AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(
                disabledOutputs: IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));

        // Cold run: traces must be present.
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        var coldSource = GetTracedInterceptorSource(driver.GetRunResult());
        Assert.That(coldSource, Does.Contain("// [Trace]"), "cold run must emit trace comments");
        Assert.That(coldSource, Does.Contain("// [Trace] ChainAnalysis"));
        Assert.That(coldSource, Does.Contain("// [Trace] Assembly"));

        // The discriminating assertion for the #311 lifecycle: the orchestrator now
        // captures trace lines onto AssembledPlan.TraceLines and clears the
        // ThreadStatic in a finally, so nothing may linger after a run. The pre-#311
        // code left the buffer populated for the emission node to read — this
        // assertion fails on that code.
        Assert.That(Generators.IR.TraceCapture.IsEmpty, Is.True,
            "TraceCapture must be empty after a run — trace state may not survive the orchestrator call");

        // Belt and braces for the warm-run check below: even if a future change leaks
        // state again, the warm run must not depend on it. (Note: the warm-run check
        // alone would NOT catch a revert to emission-time TraceCapture reads in this
        // single-threaded driver — the cold run's plan mutation would satisfy it. The
        // IsEmpty assertion above is what pins the lifecycle.)
        Generators.IR.TraceCapture.Clear();

        // Warm run: edit a file with no Quarry call sites. The compilation changes (so
        // the emission output re-runs) but every TranslatedCallSite is value-equal, so
        // the orchestrator result — including the traced chain's group — stays cached.
        var oldTree = compilation.SyntaxTrees.First(t => t.FilePath == "Helper.cs");
        var newTree = CSharpSyntaxTree.ParseText(HelperSourceModified, ParseOptions, path: "Helper.cs");
        var modifiedCompilation = compilation.ReplaceSyntaxTree(oldTree, newTree);

        driver = driver.RunGeneratorsAndUpdateCompilation(modifiedCompilation, out _, out _);
        var warmSource = GetTracedInterceptorSource(driver.GetRunResult());

        Assert.That(warmSource, Does.Contain("// [Trace]"),
            "trace comments must survive an incremental run whose traced chain was cached");
        Assert.That(warmSource, Does.Contain("// [Trace] ChainAnalysis"));
        Assert.That(warmSource, Does.Contain("// [Trace] Assembly"));
    }
}
