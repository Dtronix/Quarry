using Quarry.Benchmarks.Generator;

namespace Quarry.Tests.Generation;

[TestFixture]
public class GeneratorBenchmarkHarnessTests
{
    private const string TrivialSource = @"
using Quarry;

namespace BenchHarnessTest;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL, Schema = ""public"")]
public partial class HarnessDb : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}
";

    [Test]
    public void RunGenerator_WithHarnessReferences_ProducesEntityClass()
    {
        var tree = HarnessProxy.Parse(TrivialSource, "TrivialSource.cs");
        var compilation = HarnessProxy.BuildCompilation(new[] { tree });

        var result = HarnessProxy.RunGenerator(compilation);

        Assert.That(result.GeneratedTrees.Length, Is.GreaterThan(0),
            "Harness reference set must be sufficient for the generator to emit output. " +
            "Zero generated trees means BuildReferences is missing a runtime assembly the " +
            "generator needs, which would silently make every benchmark a no-op.");

        var entityTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("User.g.cs"));
        Assert.That(entityTree, Is.Not.Null, "Expected entity class User.g.cs to be generated.");
    }

    [Test]
    public void Fixture_Compiles_AndGeneratesEntityClasses()
    {
        var fixtureFiles = new[]
        {
            "Fixture/UserSchema",
            "Fixture/OrderSchema",
            "Fixture/OrderItemSchema",
            "Fixture/ProductSchema",
            "Fixture/AddressSchema",
            "Fixture/BenchDbContext",
        };

        var trees = fixtureFiles
            .Select(name => HarnessProxy.Parse(HarnessProxy.LoadCorpus(name), name + ".cs"))
            .ToArray();

        var compilation = HarnessProxy.BuildCompilation(trees);
        var result = HarnessProxy.RunGenerator(compilation);

        var generatedFileNames = result.GeneratedTrees
            .Select(t => System.IO.Path.GetFileName(t.FilePath))
            .ToArray();

        foreach (var entity in new[] { "User.g.cs", "Order.g.cs", "OrderItem.g.cs", "Product.g.cs", "Address.g.cs" })
        {
            Assert.That(generatedFileNames, Has.Some.EndsWith(entity),
                $"Expected entity class ending in {entity} from the fixture corpus. " +
                $"Got: {string.Join(", ", generatedFileNames)}");
        }
    }

    [TestCase("Throughput/Small", 10)]
    [TestCase("Throughput/Medium", 50)]
    [TestCase("Throughput/Large", 200)]
    public void Throughput_Corpora_CompileCleanly_AndProduceInterceptors(string corpus, int expectedQueryCount)
    {
        var trees = new[]
        {
            "Fixture/UserSchema",
            "Fixture/OrderSchema",
            "Fixture/OrderItemSchema",
            "Fixture/ProductSchema",
            "Fixture/AddressSchema",
            "Fixture/BenchDbContext",
            corpus,
        }.Select(name => HarnessProxy.Parse(HarnessProxy.LoadCorpus(name), name + ".cs")).ToArray();

        var compilation = HarnessProxy.BuildCompilation(trees);
        var result = HarnessProxy.RunGenerator(compilation);

        // Post-generation: include generated trees, then check error diagnostics.
        // Pre-generation errors like CS8795 (partial method needs implementation)
        // are expected — the generator supplies the implementations.
        var withGenerated = compilation.AddSyntaxTrees(result.GeneratedTrees);
        var errors = withGenerated.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .ToArray();
        Assert.That(errors, Is.Empty,
            $"Corpus '{corpus}' must compile cleanly after generator runs. Errors:\n" +
            string.Join("\n", errors.Select(d => d.ToString())));

        var corpusSource = HarnessProxy.LoadCorpus(corpus);
        var actualQueryCount = System.Text.RegularExpressions.Regex.Matches(
            corpusSource, @"public static object Q\d+\(BenchDb db\)").Count;
        Assert.That(actualQueryCount, Is.EqualTo(expectedQueryCount),
            $"Corpus '{corpus}' should declare exactly {expectedQueryCount} Q-methods.");

        var interceptorFiles = result.GeneratedTrees
            .Count(t => t.FilePath.Contains("Interceptors", StringComparison.Ordinal));
        Assert.That(interceptorFiles, Is.GreaterThan(0),
            $"Corpus '{corpus}' should drive at least one interceptor file generation.");
    }

    [TestCase("PipelineSplit/SchemaOnly", false, false)]
    [TestCase("PipelineSplit/PlusQueries", true, false)]
    [TestCase("PipelineSplit/PlusMigrations", true, true)]
    public void PipelineSplit_Corpora_FireExpectedPipelines(string corpus, bool expectInterceptors, bool expectMigrationOutput)
    {
        var trees = new[]
        {
            "Fixture/UserSchema",
            "Fixture/OrderSchema",
            "Fixture/OrderItemSchema",
            "Fixture/ProductSchema",
            "Fixture/AddressSchema",
            "Fixture/BenchDbContext",
            corpus,
        }.Select(name => HarnessProxy.Parse(HarnessProxy.LoadCorpus(name), name + ".cs")).ToArray();

        var compilation = HarnessProxy.BuildCompilation(trees);
        var result = HarnessProxy.RunGenerator(compilation);

        var withGenerated = compilation.AddSyntaxTrees(result.GeneratedTrees);
        var errors = withGenerated.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .ToArray();
        Assert.That(errors, Is.Empty,
            $"Corpus '{corpus}' must compile cleanly after generator runs. Errors:\n" +
            string.Join("\n", errors.Select(d => d.ToString())));

        var generatedFileNames = result.GeneratedTrees
            .Select(t => System.IO.Path.GetFileName(t.FilePath))
            .ToArray();

        // Pipeline 1 always fires — entity classes for the fixture types.
        foreach (var entity in new[] { "User.g.cs", "Order.g.cs" })
            Assert.That(generatedFileNames, Has.Some.EndsWith(entity),
                $"Pipeline 1 (Schema/Entity) must always emit {entity}.");

        var hasInterceptor = result.GeneratedTrees
            .Any(t => t.FilePath.Contains("Interceptors", StringComparison.Ordinal));
        Assert.That(hasInterceptor, Is.EqualTo(expectInterceptors),
            $"Corpus '{corpus}' interceptor expectation. Generated files: " +
            string.Join(", ", generatedFileNames));

        var hasMigrateAsync = result.GeneratedTrees
            .Any(t => t.GetText().ToString().Contains("MigrateAsync", StringComparison.Ordinal));
        Assert.That(hasMigrateAsync, Is.EqualTo(expectMigrationOutput),
            $"Corpus '{corpus}' migration-output expectation. Generated files: " +
            string.Join(", ", generatedFileNames));
    }

    /// <summary>
    /// Exposes the protected static surface of <see cref="GeneratorBenchmarkBase"/> so
    /// tests can drive it without instantiating a real benchmark class.
    /// </summary>
    private sealed class HarnessProxy : GeneratorBenchmarkBase
    {
        public static new string LoadCorpus(string relativePath) =>
            GeneratorBenchmarkBase.LoadCorpus(relativePath);

        public static new Microsoft.CodeAnalysis.SyntaxTree Parse(string source, string path) =>
            GeneratorBenchmarkBase.Parse(source, path);

        public static new Microsoft.CodeAnalysis.CSharp.CSharpCompilation BuildCompilation(
            IEnumerable<Microsoft.CodeAnalysis.SyntaxTree> trees) =>
            GeneratorBenchmarkBase.BuildCompilation(trees);

        public static new Microsoft.CodeAnalysis.GeneratorDriverRunResult RunGenerator(
            Microsoft.CodeAnalysis.CSharp.CSharpCompilation compilation) =>
            GeneratorBenchmarkBase.RunGenerator(compilation);
    }
}
