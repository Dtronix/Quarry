using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Quarry.Benchmarks.Benchmarks;

public class AggregateBenchmarks : BenchmarkBase
{
    // --- COUNT ---

    [Benchmark(Baseline = true)]
    public async Task<int> Raw_Count()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM users";
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    [Benchmark]
    public async Task<int> Dapper_Count()
    {
        return await Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM users");
    }

    [Benchmark]
    public async Task<int> EfCore_Count()
    {
        return await EfContext.Users.AsNoTracking().CountAsync();
    }

    [Benchmark]
    public async Task<int> Quarry_Count()
    {
        return await QuarryDb.Users
            .Select(u => Sql.Count())
            .ExecuteScalarAsync<int>();
    }

    // --- SUM ---

    [Benchmark]
    public async Task<decimal> Raw_Sum()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = "SELECT SUM(Total) FROM orders";
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToDecimal(result);
    }

    [Benchmark]
    public async Task<decimal> Dapper_Sum()
    {
        return await Connection.ExecuteScalarAsync<decimal>("SELECT SUM(Total) FROM orders");
    }

    [Benchmark]
    public async Task<decimal> EfCore_Sum()
    {
        return await EfContext.Orders.AsNoTracking().SumAsync(o => o.Total);
    }

    [Benchmark]
    public async Task<decimal> Quarry_Sum()
    {
        return await QuarryDb.Orders
            .Select(o => Sql.Sum(o.Total))
            .ExecuteScalarAsync<decimal>();
    }

    // --- AVG ---

    [Benchmark]
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
        return await QuarryDb.Orders
            .Select(o => Sql.Avg(o.Total))
            .ExecuteScalarAsync<decimal>();
    }
}
