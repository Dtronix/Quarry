using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quarry.Benchmarks.Infrastructure;
using SqlKata;
using SqlKata.Compilers;

namespace Quarry.Benchmarks.Benchmarks;

public class CteProjectionBenchmarks : BenchmarkBase
{
    private const string CteProjectionSql = """
        WITH cte AS (
            SELECT OrderId, Total, Status
            FROM orders WHERE Total > 50
        )
        SELECT OrderId, Total FROM cte
        """;

    [Benchmark(Baseline = true)]
    public async Task<List<OrderIdTotalDto>> Raw_CteProjection()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = CteProjectionSql;
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess);
        var results = new List<OrderIdTotalDto>();
        while (await reader.ReadAsync())
        {
            results.Add(new OrderIdTotalDto
            {
                OrderId = reader.GetInt32(0),
                Total = reader.GetDecimal(1)
            });
        }
        return results;
    }

    [Benchmark]
    public async Task<List<OrderIdTotalDto>> Dapper_CteProjection()
    {
        return (await Connection.QueryAsync<OrderIdTotalDto>(CteProjectionSql)).AsList();
    }

    [Benchmark]
    public async Task<List<OrderIdTotalDto>> EfCore_CteProjection_RawFallback()
    {
        return await EfContext.Database
            .SqlQueryRaw<OrderIdTotalDto>(CteProjectionSql)
            .ToListAsync();
    }

    [Benchmark]
    public async Task<List<OrderIdTotalDto>> Quarry_CteProjection()
    {
        return await QuarryDb
            .With<Order, OrderSummaryDto>(orders => orders
                .Where(o => o.Total > 50)
                .Select(o => new OrderSummaryDto
                {
                    OrderId = o.OrderId,
                    Total = o.Total,
                    Status = o.Status
                }))
            .FromCte<OrderSummaryDto>()
            .Select(d => new OrderIdTotalDto
            {
                OrderId = d.OrderId,
                Total = d.Total
            })
            .ExecuteFetchAllAsync();
    }

    [Benchmark]
    public async Task<List<OrderIdTotalDto>> SqlKata_CteProjection_RawFallback()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = CteProjectionSql;
        await using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<OrderIdTotalDto>();
        while (await reader.ReadAsync())
        {
            results.Add(new OrderIdTotalDto
            {
                OrderId = reader.GetInt32(0),
                Total = reader.GetDecimal(1)
            });
        }
        return results;
    }
}
