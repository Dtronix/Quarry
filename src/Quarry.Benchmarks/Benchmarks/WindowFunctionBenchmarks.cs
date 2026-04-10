using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quarry.Benchmarks.Infrastructure;
using SqlKata;
using SqlKata.Compilers;

namespace Quarry.Benchmarks.Benchmarks;

public class WindowFunctionBenchmarks : BenchmarkBase
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

    // ── SUM(Total) OVER (PARTITION BY Status) — Running Sum ──

    private const string RunningSumSql =
        "SELECT OrderId, Total, SUM(Total) OVER (PARTITION BY Status) AS RunningSum FROM orders";

    [Benchmark]
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

    // ── RANK() OVER (PARTITION BY Status ORDER BY Total) ──

    private const string RankSql =
        "SELECT OrderId, RANK() OVER (PARTITION BY Status ORDER BY Total) AS Rank FROM orders";

    [Benchmark]
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

    // ── LAG(Total, 1) OVER (ORDER BY OrderDate) ──

    private const string LagSql =
        "SELECT OrderId, Total, LAG(Total, 1) OVER (ORDER BY OrderDate) AS PrevTotal FROM orders";

    [Benchmark]
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
    public async Task<List<OrderLagDto>> EfCore_Lag()
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
    public async Task<List<OrderLagDto>> SqlKata_Lag()
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
