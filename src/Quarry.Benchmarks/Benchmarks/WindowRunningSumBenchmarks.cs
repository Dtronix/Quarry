using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quarry.Benchmarks.Infrastructure;
using SqlKata;
using SqlKata.Compilers;

namespace Quarry.Benchmarks.Benchmarks;

public class WindowRunningSumBenchmarks : BenchmarkBase
{
    // ── SUM(Total) OVER (PARTITION BY Status) — Running Sum ──

    private const string RunningSumSql =
        "SELECT OrderId, Total, SUM(Total) OVER (PARTITION BY Status) AS RunningSum FROM orders";

    [Benchmark(Baseline = true)]
    public async Task<List<OrderRunningSumDto>> Raw_RunningSum()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = RunningSumSql;
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess);
        var results = new List<OrderRunningSumDto>();
        while (await reader.ReadAsync())
        {
            results.Add(new OrderRunningSumDto
            {
                OrderId = reader.GetInt32(0),
                Total = reader.GetDecimal(1),
                RunningSum = reader.GetDecimal(2)
            });
        }
        return results;
    }

    [Benchmark]
    public async Task<List<OrderRunningSumDto>> Dapper_RunningSum()
    {
        return (await Connection.QueryAsync<OrderRunningSumDto>(RunningSumSql)).AsList();
    }

    [Benchmark]
    public async Task<List<OrderRunningSumDto>> EfCore_RunningSum()
    {
        return await EfContext.Database
            .SqlQueryRaw<OrderRunningSumDto>(RunningSumSql)
            .ToListAsync();
    }

    [Benchmark]
    public async Task<List<OrderRunningSumDto>> Quarry_RunningSum()
    {
        return await QuarryDb.Orders()
            .Select(o => new OrderRunningSumDto
            {
                OrderId = o.OrderId,
                Total = o.Total,
                RunningSum = Sql.Sum(o.Total, over => over.PartitionBy(o.Status))
            })
            .ExecuteFetchAllAsync();
    }

    [Benchmark]
    public async Task<List<OrderRunningSumDto>> SqlKata_RunningSum()
    {
        var query = new Query("orders")
            .SelectRaw("OrderId, Total, SUM(Total) OVER (PARTITION BY Status) AS RunningSum");
        var compiled = SqlKataCompiler.Compile(query);

        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = compiled.Sql;
        await using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<OrderRunningSumDto>();
        while (await reader.ReadAsync())
        {
            results.Add(new OrderRunningSumDto
            {
                OrderId = reader.GetInt32(0),
                Total = reader.GetDecimal(1),
                RunningSum = reader.GetDecimal(2)
            });
        }
        return results;
    }
}
