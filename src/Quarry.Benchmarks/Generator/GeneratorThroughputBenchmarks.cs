using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;
using Quarry.Benchmarks.GeneratorHarness;

namespace Quarry.Benchmarks.Generator;

[MemoryDiagnoser]
public class GeneratorThroughputBenchmarks : GeneratorBenchmarkBase
{
    private SyntaxTree[] _smallFull = null!;
    private SyntaxTree[] _mediumFull = null!;
    private SyntaxTree[] _largeFull = null!;

    [GlobalSetup]
    public void Setup()
    {
        _smallFull = ParseFixturePlus("Throughput/Small");
        _mediumFull = ParseFixturePlus("Throughput/Medium");
        _largeFull = ParseFixturePlus("Throughput/Large");
    }

    [Benchmark]
    public int Quarry_Throughput_Small() => RunWith(_smallFull);

    [Benchmark]
    public int Quarry_Throughput_Medium() => RunWith(_mediumFull);

    [Benchmark]
    public int Quarry_Throughput_Large() => RunWith(_largeFull);

    private static int RunWith(SyntaxTree[] trees)
    {
        var compilation = BuildCompilation(trees);
        var result = RunGenerator(compilation);
        return result.GeneratedTrees.Length;
    }
}
