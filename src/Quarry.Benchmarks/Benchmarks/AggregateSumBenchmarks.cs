using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SqlKata;
using SqlKata.Compilers;

namespace Quarry.Benchmarks.Benchmarks;

public class AggregateSumBenchmarks : BenchmarkBase
{
    [Benchmark(Baseline = true)]
    public async Task<double> Raw_Sum()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = "SELECT SUM(Total) FROM orders";
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToDouble(result);
    }

    [Benchmark]
    public async Task<double> Dapper_Sum()
    {
        return await Connection.ExecuteScalarAsync<double>("SELECT SUM(Total) FROM orders");
    }

    [Benchmark]
    public async Task<double> EfCore_Sum()
    {
        return await EfContext.Orders.AsNoTracking().SumAsync(o => o.Total);
    }

    [Benchmark]
    public async Task<double> Quarry_Sum()
    {
        return await QuarryDb.Orders()
            .Select(o => Sql.Sum(o.Total))
            .ExecuteScalarAsync<double>();
    }

    [Benchmark]
    public async Task<double> SqlKata_Sum()
    {
        var query = new Query("orders").AsSum("Total");
        var compiled = SqlKataCompiler.Compile(query);

        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = compiled.Sql;
        foreach (var binding in compiled.Bindings)
        {
            cmd.Parameters.AddWithValue($"@p{cmd.Parameters.Count}", binding);
        }
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToDouble(result);
    }
}
