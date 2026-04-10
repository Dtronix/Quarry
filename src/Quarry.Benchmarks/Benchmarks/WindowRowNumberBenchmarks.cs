using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quarry.Benchmarks.Infrastructure;
using SqlKata;
using SqlKata.Compilers;

namespace Quarry.Benchmarks.Benchmarks;

public class WindowRowNumberBenchmarks : BenchmarkBase
{
    // ── ROW_NUMBER() OVER (PARTITION BY Status ORDER BY Total) ──

    private const string RowNumberSql =
        "SELECT OrderId, ROW_NUMBER() OVER (PARTITION BY Status ORDER BY Total) AS RowNum FROM orders";

    [Benchmark(Baseline = true)]
    public async Task<List<OrderRowNumberDto>> Raw_RowNumber()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = RowNumberSql;
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess);
        var results = new List<OrderRowNumberDto>();
        while (await reader.ReadAsync())
        {
            results.Add(new OrderRowNumberDto
            {
                OrderId = reader.GetInt32(0),
                RowNum = reader.GetInt64(1)
            });
        }
        return results;
    }

    [Benchmark]
    public async Task<List<OrderRowNumberDto>> Dapper_RowNumber()
    {
        return (await Connection.QueryAsync<OrderRowNumberDto>(RowNumberSql)).AsList();
    }

    [Benchmark]
    public async Task<List<OrderRowNumberDto>> EfCore_RowNumber()
    {
        return await EfContext.Database
            .SqlQueryRaw<OrderRowNumberDto>(RowNumberSql)
            .ToListAsync();
    }

    [Benchmark]
    public async Task<List<OrderRowNumberDto>> Quarry_RowNumber()
    {
        return await QuarryDb.Orders()
            .Select(o => new OrderRowNumberDto
            {
                OrderId = o.OrderId,
                RowNum = Sql.RowNumber(over => over.PartitionBy(o.Status).OrderBy(o.Total))
            })
            .ExecuteFetchAllAsync();
    }

    [Benchmark]
    public async Task<List<OrderRowNumberDto>> SqlKata_RowNumber()
    {
        var query = new Query("orders")
            .SelectRaw("OrderId, ROW_NUMBER() OVER (PARTITION BY Status ORDER BY Total) AS RowNum");
        var compiled = SqlKataCompiler.Compile(query);

        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = compiled.Sql;
        await using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<OrderRowNumberDto>();
        while (await reader.ReadAsync())
        {
            results.Add(new OrderRowNumberDto
            {
                OrderId = reader.GetInt32(0),
                RowNum = reader.GetInt64(1)
            });
        }
        return results;
    }
}
