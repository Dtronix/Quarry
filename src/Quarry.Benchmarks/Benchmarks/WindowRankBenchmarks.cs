using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quarry.Benchmarks.Infrastructure;
using SqlKata;
using SqlKata.Compilers;

namespace Quarry.Benchmarks.Benchmarks;

public class WindowRankBenchmarks : BenchmarkBase
{
    // ── RANK() OVER (PARTITION BY Status ORDER BY Total) ──

    private const string RankSql =
        "SELECT OrderId, RANK() OVER (PARTITION BY Status ORDER BY Total) AS Rank FROM orders";

    [Benchmark(Baseline = true)]
    public async Task<List<OrderRankDto>> Raw_Rank()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = RankSql;
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess);
        var results = new List<OrderRankDto>();
        while (await reader.ReadAsync())
        {
            results.Add(new OrderRankDto
            {
                OrderId = reader.GetInt32(0),
                Rank = reader.GetInt64(1)
            });
        }
        return results;
    }

    [Benchmark]
    public async Task<List<OrderRankDto>> Dapper_Rank()
    {
        return (await Connection.QueryAsync<OrderRankDto>(RankSql)).AsList();
    }

    [Benchmark]
    public async Task<List<OrderRankDto>> EfCore_Rank()
    {
        return await EfContext.Database
            .SqlQueryRaw<OrderRankDto>(RankSql)
            .ToListAsync();
    }

    [Benchmark]
    public async Task<List<OrderRankDto>> Quarry_Rank()
    {
        return await QuarryDb.Orders()
            .Select(o => new OrderRankDto
            {
                OrderId = o.OrderId,
                Rank = Sql.Rank(over => over.PartitionBy(o.Status).OrderBy(o.Total))
            })
            .ExecuteFetchAllAsync();
    }

    [Benchmark]
    public async Task<List<OrderRankDto>> SqlKata_Rank()
    {
        var query = new Query("orders")
            .SelectRaw("OrderId, RANK() OVER (PARTITION BY Status ORDER BY Total) AS Rank");
        var compiled = SqlKataCompiler.Compile(query);

        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = compiled.Sql;
        await using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<OrderRankDto>();
        while (await reader.ReadAsync())
        {
            results.Add(new OrderRankDto
            {
                OrderId = reader.GetInt32(0),
                Rank = reader.GetInt64(1)
            });
        }
        return results;
    }
}
