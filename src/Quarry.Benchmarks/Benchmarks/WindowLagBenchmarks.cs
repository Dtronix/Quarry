using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quarry.Benchmarks.Infrastructure;
using SqlKata;
using SqlKata.Compilers;

namespace Quarry.Benchmarks.Benchmarks;

public class WindowLagBenchmarks : BenchmarkBase
{
    // ── LAG(Total, 1) OVER (ORDER BY OrderDate) ──

    private const string LagSql =
        "SELECT OrderId, Total, LAG(Total, 1) OVER (ORDER BY OrderDate) AS PrevTotal FROM orders";

    [Benchmark(Baseline = true)]
    public async Task<List<OrderLagDto>> Raw_Lag()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = LagSql;
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess);
        var results = new List<OrderLagDto>();
        while (await reader.ReadAsync())
        {
            results.Add(new OrderLagDto
            {
                OrderId = reader.GetInt32(0),
                Total = reader.GetDecimal(1),
                PrevTotal = reader.IsDBNull(2) ? null : reader.GetDecimal(2)
            });
        }
        return results;
    }

    [Benchmark]
    public async Task<List<DapperOrderLagDto>> Dapper_Lag()
    {
        return (await Connection.QueryAsync<DapperOrderLagDto>(LagSql)).AsList();
    }

    [Benchmark]
    public async Task<List<OrderLagDto>> EfCore_Lag_RawFallback()
    {
        return await EfContext.Database
            .SqlQueryRaw<OrderLagDto>(LagSql)
            .ToListAsync();
    }

    [Benchmark]
    public async Task<List<OrderLagDto>> Quarry_Lag()
    {
        return await QuarryDb.Orders()
            .Select(o => new OrderLagDto
            {
                OrderId = o.OrderId,
                Total = o.Total,
                PrevTotal = Sql.Lag(o.Total, 1, over => over.OrderBy(o.OrderDate))
            })
            .ExecuteFetchAllAsync();
    }

    [Benchmark]
    public async Task<List<OrderLagDto>> SqlKata_Lag_RawFallback()
    {
        var query = new Query("orders")
            .SelectRaw("OrderId, Total, LAG(Total, 1) OVER (ORDER BY OrderDate) AS PrevTotal");
        var compiled = SqlKataCompiler.Compile(query);

        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = compiled.Sql;
        await using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<OrderLagDto>();
        while (await reader.ReadAsync())
        {
            results.Add(new OrderLagDto
            {
                OrderId = reader.GetInt32(0),
                Total = reader.GetDecimal(1),
                PrevTotal = reader.IsDBNull(2) ? null : reader.GetDecimal(2)
            });
        }
        return results;
    }
}
