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

    /// <summary>
    /// Exposes the protected static surface of <see cref="GeneratorBenchmarkBase"/> so
    /// tests can drive it without instantiating a real benchmark class.
    /// </summary>
    private sealed class HarnessProxy : GeneratorBenchmarkBase
    {
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
