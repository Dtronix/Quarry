using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quarry.Benchmarks.Infrastructure;
using SqlKata;
using SqlKata.Compilers;

namespace Quarry.Benchmarks.Benchmarks;

public class CteBenchmarks : BenchmarkBase
{
    // ── Simple CTE: filter orders with Total > 50, select from CTE ──

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

    // ── CTE with projection: CTE narrows columns, outer query selects from it ──

    private const string CteProjectionSql = """
        WITH cte AS (
            SELECT OrderId, Total, Status
            FROM orders WHERE Total > 50
        )
        SELECT OrderId, Total FROM cte
        """;

    [Benchmark]
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
    public async Task<List<OrderIdTotalDto>> EfCore_CteProjection()
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
    public async Task<List<OrderIdTotalDto>> SqlKata_CteProjection()
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

    // ── Multi-CTE: two CTEs (high-value orders + active users), query from orders CTE ──

    private const string MultiCteSql = """
        WITH high_orders AS (
            SELECT OrderId, UserId, Total, Status, OrderDate, Notes
            FROM orders WHERE Total > 50
        ),
        active_users AS (
            SELECT UserId, UserName, Email, IsActive, CreatedAt, LastLogin
            FROM users WHERE IsActive = 1
        )
        SELECT OrderId, Total FROM high_orders
        """;

    [Benchmark]
    public async Task<List<OrderIdTotalDto>> Raw_MultiCte()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = MultiCteSql;
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
    public async Task<List<OrderIdTotalDto>> Dapper_MultiCte()
    {
        return (await Connection.QueryAsync<OrderIdTotalDto>(MultiCteSql)).AsList();
    }

    [Benchmark]
    public async Task<List<OrderIdTotalDto>> EfCore_MultiCte()
    {
        return await EfContext.Database
            .SqlQueryRaw<OrderIdTotalDto>(MultiCteSql)
            .ToListAsync();
    }

    [Benchmark]
    public async Task<List<OrderIdTotalDto>> Quarry_MultiCte()
    {
        return await QuarryDb
            .With<Order>(orders => orders.Where(o => o.Total > 50))
            .With<User>(users => users.Where(u => u.IsActive == true))
            .FromCte<Order>()
            .Select(o => new OrderIdTotalDto
            {
                OrderId = o.OrderId,
                Total = o.Total
            })
            .ExecuteFetchAllAsync();
    }

    [Benchmark]
    public async Task<List<OrderIdTotalDto>> SqlKata_MultiCte()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = MultiCteSql;
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
