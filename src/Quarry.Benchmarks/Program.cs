using BenchmarkDotNet.Running;
using Quarry.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, new BenchmarkConfig());
