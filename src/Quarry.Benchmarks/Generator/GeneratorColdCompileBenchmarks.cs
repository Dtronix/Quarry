using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;

namespace Quarry.Benchmarks.Generator;

[MemoryDiagnoser]
public class GeneratorColdCompileBenchmarks : GeneratorBenchmarkBase
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

    private SyntaxTree[] _trees = null!;

    [GlobalSetup]
    public void Setup()
    {
        var trees = new List<SyntaxTree>(FixtureFiles.Length + 1);
        foreach (var f in FixtureFiles)
            trees.Add(Parse(LoadCorpus(f), f + ".cs"));
        trees.Add(Parse(LoadCorpus("Throughput/Medium"), "Throughput/Medium.cs"));
        _trees = trees.ToArray();
    }

    [Benchmark]
    public int Quarry_GeneratorColdCompile()
    {
        var compilation = BuildCompilation(_trees);
        var result = RunGenerator(compilation);
        return result.GeneratedTrees.Length;
    }
}
