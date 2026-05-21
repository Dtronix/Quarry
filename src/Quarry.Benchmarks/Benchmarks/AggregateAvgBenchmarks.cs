using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SqlKata;
using SqlKata.Compilers;

namespace Quarry.Benchmarks.Benchmarks;

public class AggregateAvgBenchmarks : BenchmarkBase
{
    [Benchmark(Baseline = true)]
    public async Task<double> Raw_Avg()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = "SELECT AVG(Total) FROM orders";
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToDouble(result);
    }

    [Benchmark]
    public async Task<double> Dapper_Avg()
    {
        return await Connection.ExecuteScalarAsync<double>("SELECT AVG(Total) FROM orders");
    }

    [Benchmark]
    public async Task<double> EfCore_Avg()
    {
        return await EfContext.Orders.AsNoTracking().AverageAsync(o => o.Total);
    }

    [Benchmark]
    public async Task<double> Quarry_Avg()
    {
        return await QuarryDb.Orders()
            .Select(o => Sql.Avg(o.Total))
            .ExecuteScalarAsync<double>();
    }

    [Benchmark]
    public async Task<double> SqlKata_Avg()
    {
        var query = new Query("orders").AsAverage("Total");
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
