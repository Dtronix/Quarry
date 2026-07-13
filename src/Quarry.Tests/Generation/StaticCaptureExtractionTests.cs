using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using Quarry.Generators;

namespace Quarry.Tests.Generation;

/// <summary>
/// Verifies the clause interceptor omits the dead `var __target = func.Target!;` read when
/// every extracted capture is a static field (#308 item 6c). __target (the display-class read)
/// is only needed for instance captures; when all extractors are static the read is dead.
/// </summary>
[TestFixture]
public class StaticCaptureExtractionTests
{
    private static readonly string QuarryCoreAssemblyPath = typeof(Schema).Assembly.Location;
    private static readonly string SystemRuntimeAssemblyPath = typeof(object).Assembly.Location;

    // A WHERE clause whose only captured value is a static field. The static field is mutable
    // so it can't be inlined as a constant — it is read at runtime via a static-field extractor
    // (emitted as `__ExtractVar_...(null!)`), which does not need the func.Target display class.
    private const string StaticCaptureSource = @"
using Quarry;
namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName { get; }
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public static class Queries
{
    private static string SearchTerm = ""lic"";

    public static void Test(TestDbContext db)
    {
        _ = db.Users().Where(u => u.UserName.Contains(SearchTerm)).Select(u => u.UserName).ToDiagnostics();
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
    public void AllStaticCaptures_OmitDeadTargetRead()
    {
        var code = RunGeneratorAndGetInterceptors(StaticCaptureSource);

        // The static field must still be extracted (with a null target)…
        Assert.That(code, Does.Contain("null!"),
            "Static-field extraction should pass a null target");
        // …but the display-class read must not be emitted when no extractor needs it.
        Assert.That(code, Does.Not.Contain("var __target ="),
            "func.Target must not be read when every extractor is a static field");
    }
}
