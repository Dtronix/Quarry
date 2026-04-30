using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;

namespace Quarry.Benchmarks.Generator;

[MemoryDiagnoser]
public class GeneratorPipelineSplitBenchmarks : GeneratorBenchmarkBase
{
    private static readonly string[] FixtureFiles =
    {
        "Fixture/UserSchema",
        "Fixture/OrderSchema",
        "Fixture/OrderItemSchema",
        "Fixture/ProductSchema",
        "Fixture/AddressSchema",
        "Fixture/BenchDbContext",
    };

    private SyntaxTree[] _fixtureTrees = null!;
    private SyntaxTree _schemaOnlyTree = null!;
    private SyntaxTree _plusQueriesTree = null!;
    private SyntaxTree _plusMigrationsTree = null!;

    [GlobalSetup]
    public void Setup()
    {
        _fixtureTrees = FixtureFiles
            .Select(f => Parse(LoadCorpus(f), f + ".cs"))
            .ToArray();

        _schemaOnlyTree = Parse(LoadCorpus("PipelineSplit/SchemaOnly"), "PipelineSplit/SchemaOnly.cs");
        _plusQueriesTree = Parse(LoadCorpus("PipelineSplit/PlusQueries"), "PipelineSplit/PlusQueries.cs");
        _plusMigrationsTree = Parse(LoadCorpus("PipelineSplit/PlusMigrations"), "PipelineSplit/PlusMigrations.cs");
    }

    [Benchmark]
    public int Quarry_Pipeline_SchemaOnly() => RunWith(_schemaOnlyTree);

    [Benchmark]
    public int Quarry_Pipeline_PlusQueries() => RunWith(_plusQueriesTree);

    [Benchmark]
    public int Quarry_Pipeline_PlusMigrations() => RunWith(_plusMigrationsTree);

    private int RunWith(SyntaxTree corpus)
    {
        var trees = new SyntaxTree[_fixtureTrees.Length + 1];
        Array.Copy(_fixtureTrees, trees, _fixtureTrees.Length);
        trees[_fixtureTrees.Length] = corpus;

        var compilation = BuildCompilation(trees);
        var result = RunGenerator(compilation);
        return result.GeneratedTrees.Length;
    }
}
