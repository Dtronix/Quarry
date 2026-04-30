using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Quarry.Generators;

namespace Quarry.Benchmarks.Generator;

public abstract class GeneratorBenchmarkBase
{
    private const string CorpusResourcePrefix = "Quarry.Benchmarks.Corpora.v1.";

    private static readonly Lazy<IReadOnlyList<MetadataReference>> ReferencesCache =
        new(BuildReferencesImpl, isThreadSafe: true);

    protected static IReadOnlyList<MetadataReference> References => ReferencesCache.Value;

    protected static string LoadCorpus(string resourceName)
    {
        var asm = typeof(GeneratorBenchmarkBase).Assembly;
        var fullName = CorpusResourcePrefix + resourceName;
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
