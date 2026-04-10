using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quarry.Benchmarks.Infrastructure;
using SqlKata;
using SqlKata.Compilers;

namespace Quarry.Benchmarks.Benchmarks;

public class CteSimpleBenchmarks : BenchmarkBase
{
    private const string SimpleCteFilterSql = """
        WITH cte AS (
            SELECT OrderId, UserId, Total, Status, OrderDate, Notes
            FROM orders WHERE Total > 50
        )
        SELECT OrderId, Total FROM cte
        """;

    [Benchmark(Baseline = true)]
    public async Task<List<OrderIdTotalDto>> Raw_SimpleCte()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = SimpleCteFilterSql;
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
    public async Task<List<OrderIdTotalDto>> Dapper_SimpleCte()
    {
        return (await Connection.QueryAsync<OrderIdTotalDto>(SimpleCteFilterSql)).AsList();
    }

    [Benchmark]
    public async Task<List<OrderIdTotalDto>> EfCore_SimpleCte()
    {
        return await EfContext.Database
            .SqlQueryRaw<OrderIdTotalDto>(SimpleCteFilterSql)
            .ToListAsync();
    }

    [Benchmark]
    public async Task<List<OrderIdTotalDto>> Quarry_SimpleCte()
    {
        return await QuarryDb
            .With<Order>(orders => orders.Where(o => o.Total > 50))
            .FromCte<Order>()
            .Select(o => new OrderIdTotalDto
            {
                OrderId = o.OrderId,
                Total = o.Total
            })
            .ExecuteFetchAllAsync();
    }

    [Benchmark]
    public async Task<List<OrderIdTotalDto>> SqlKata_SimpleCte()
    {
        // SqlKata has no native CTE support; use raw SQL.
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = SimpleCteFilterSql;
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
