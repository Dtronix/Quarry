using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;
using Quarry.Benchmarks.GeneratorHarness;

namespace Quarry.Benchmarks.Generator;

[MemoryDiagnoser]
public class GeneratorPipelineSplitBenchmarks : GeneratorBenchmarkBase
{
    private SyntaxTree[] _schemaOnlyFull = null!;
    private SyntaxTree[] _plusQueriesFull = null!;
    private SyntaxTree[] _plusMigrationsFull = null!;

    [GlobalSetup]
    public void Setup()
    {
        _schemaOnlyFull = ParseFixturePlus("PipelineSplit/SchemaOnly");
        _plusQueriesFull = ParseFixturePlus("PipelineSplit/PlusQueries");
        _plusMigrationsFull = ParseFixturePlus("PipelineSplit/PlusMigrations");
    }

    [Benchmark]
    public int Quarry_Pipeline_SchemaOnly() => RunWith(_schemaOnlyFull);

    [Benchmark]
    public int Quarry_Pipeline_PlusQueries() => RunWith(_plusQueriesFull);

    [Benchmark]
    public int Quarry_Pipeline_PlusMigrations() => RunWith(_plusMigrationsFull);

    private static int RunWith(SyntaxTree[] trees)
    {
        var compilation = BuildCompilation(trees);
        var result = RunGenerator(compilation);
        return result.GeneratedTrees.Length;
    }
}
