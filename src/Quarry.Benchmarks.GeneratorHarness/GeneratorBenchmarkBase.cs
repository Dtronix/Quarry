using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Quarry.Generators;

namespace Quarry.Benchmarks.GeneratorHarness;

public abstract class GeneratorBenchmarkBase
{
    private const string CorpusResourcePrefix = "Quarry.Benchmarks.GeneratorHarness.Corpora.v1.";

    /// <summary>
    /// Shared fixture file list (logical paths under Corpora/v1/). All generator
    /// benchmarks parse these on top of their own corpus.
    /// </summary>
    protected static readonly IReadOnlyList<string> FixtureFiles = new[]
    {
        "Fixture/UserSchema",
        "Fixture/OrderSchema",
        "Fixture/OrderItemSchema",
        "Fixture/ProductSchema",
        "Fixture/AddressSchema",
        "Fixture/BenchDbContext",
    };

    private static readonly Lazy<IReadOnlyList<MetadataReference>> ReferencesCache =
        new(BuildReferencesImpl, isThreadSafe: true);

    protected static IReadOnlyList<MetadataReference> References => ReferencesCache.Value;

    /// <summary>
    /// Parses every fixture file plus the given extra corpus into a single SyntaxTree[].
    /// Hoist this from GlobalSetup so per-iter measurement covers only compilation + driver run.
    /// </summary>
    protected static SyntaxTree[] ParseFixturePlus(string extraCorpusRelativePath)
    {
        var trees = new SyntaxTree[FixtureFiles.Count + 1];
        for (var i = 0; i < FixtureFiles.Count; i++)
            trees[i] = Parse(LoadCorpus(FixtureFiles[i]), FixtureFiles[i] + ".cs");
        trees[FixtureFiles.Count] = Parse(
            LoadCorpus(extraCorpusRelativePath),
            extraCorpusRelativePath + ".cs");
        return trees;
    }

    protected static string LoadCorpus(string relativePath)
    {
        var asm = typeof(GeneratorBenchmarkBase).Assembly;
        var fullName = CorpusResourcePrefix + relativePath.Replace('/', '.') + ".cs";
        using var stream = asm.GetManifestResourceStream(fullName)
            ?? throw new InvalidOperationException(
                $"Embedded corpus '{fullName}' not found. Available: " +
                string.Join(", ", asm.GetManifestResourceNames()));
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    protected static SyntaxTree Parse(string source, string path) =>
        CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Latest),
            path: path);

    protected static CSharpCompilation BuildCompilation(IEnumerable<SyntaxTree> trees) =>
        CSharpCompilation.Create(
            assemblyName: "GeneratorBench",
            syntaxTrees: trees,
            references: References,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

    protected static GeneratorDriverRunResult RunGenerator(CSharpCompilation compilation)
    {
        var generator = new QuarryGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator.AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    private static IReadOnlyList<MetadataReference> BuildReferencesImpl()
    {
        var refs = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(Schema).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Data.IDbConnection).Assembly.Location),
        };

        var runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();
        foreach (var dll in new[]
        {
            "System.Runtime.dll",
            "System.Collections.dll",
            "System.Linq.dll",
            "System.Linq.Expressions.dll",
            "netstandard.dll",
            "System.Threading.Tasks.dll",
        })
        {
            refs.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, dll)));
        }

        return refs;
    }
}
