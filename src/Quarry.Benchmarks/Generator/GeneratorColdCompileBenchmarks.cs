using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;
using Quarry.Benchmarks.GeneratorHarness;

namespace Quarry.Benchmarks.Generator;

[MemoryDiagnoser]
public class GeneratorColdCompileBenchmarks : GeneratorBenchmarkBase
{
    private SyntaxTree[] _trees = null!;

    [GlobalSetup]
    public void Setup() => _trees = ParseFixturePlus("Throughput/Medium");

    [Benchmark]
    public int Quarry_GeneratorColdCompile()
    {
        var compilation = BuildCompilation(_trees);
        var result = RunGenerator(compilation);
        return result.GeneratedTrees.Length;
    }
}
