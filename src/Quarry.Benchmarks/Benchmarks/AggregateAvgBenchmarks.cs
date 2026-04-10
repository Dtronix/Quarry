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
    public async Task<decimal> Raw_Avg()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = "SELECT AVG(Total) FROM orders";
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToDecimal(result);
    }

    [Benchmark]
    public async Task<decimal> Dapper_Avg()
    {
        return await Connection.ExecuteScalarAsync<decimal>("SELECT AVG(Total) FROM orders");
    }

    [Benchmark]
    public async Task<decimal> EfCore_Avg()
    {
        return await EfContext.Orders.AsNoTracking().AverageAsync(o => o.Total);
    }

    [Benchmark]
    public async Task<decimal> Quarry_Avg()
    {
        return await QuarryDb.Orders()
            .Select(o => Sql.Avg(o.Total))
            .ExecuteScalarAsync<decimal>();
    }

    [Benchmark]
    public async Task<decimal> SqlKata_Avg()
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
        return Convert.ToDecimal(result);
    }
}
