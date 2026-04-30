using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;

namespace Quarry.Benchmarks.Generator;

[MemoryDiagnoser]
public class GeneratorThroughputBenchmarks : GeneratorBenchmarkBase
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
    private SyntaxTree _smallTree = null!;
    private SyntaxTree _mediumTree = null!;
    private SyntaxTree _largeTree = null!;

    [GlobalSetup]
    public void Setup()
    {
        _fixtureTrees = FixtureFiles
            .Select(f => Parse(LoadCorpus(f), f + ".cs"))
            .ToArray();

        _smallTree = Parse(LoadCorpus("Throughput/Small"), "Throughput/Small.cs");
        _mediumTree = Parse(LoadCorpus("Throughput/Medium"), "Throughput/Medium.cs");
        _largeTree = Parse(LoadCorpus("Throughput/Large"), "Throughput/Large.cs");
    }

    [Benchmark]
    public int Quarry_Throughput_Small() => RunWith(_smallTree);

    [Benchmark]
    public int Quarry_Throughput_Medium() => RunWith(_mediumTree);

    [Benchmark]
    public int Quarry_Throughput_Large() => RunWith(_largeTree);

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
