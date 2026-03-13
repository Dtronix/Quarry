using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quarry.Benchmarks.Infrastructure;

namespace Quarry.Benchmarks.Benchmarks;

public class ComplexQueryBenchmarks : BenchmarkBase
{
    // --- Join + Filter + Paginate ---

    [Benchmark(Baseline = true)]
    public async Task<List<UserOrderDto>> Raw_JoinFilterPaginate()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = """
            SELECT u.UserName, o.Total
            FROM users u
            INNER JOIN orders o ON u.UserId = o.UserId
            WHERE u.IsActive = 1
            LIMIT 10 OFFSET 5
            """;
        await using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<UserOrderDto>();
        while (await reader.ReadAsync())
        {
            results.Add(new UserOrderDto
            {
                UserName = reader.GetString(0),
                Total = reader.GetDecimal(1)
            });
        }
        return results;
    }

    [Benchmark]
    public async Task<List<UserOrderDto>> Dapper_JoinFilterPaginate()
    {
        return (await Connection.QueryAsync<UserOrderDto>("""
            SELECT u.UserName, o.Total
            FROM users u
            INNER JOIN orders o ON u.UserId = o.UserId
            WHERE u.IsActive = 1
            LIMIT 10 OFFSET 5
            """)).AsList();
    }

    [Benchmark]
    public async Task<List<UserOrderDto>> EfCore_JoinFilterPaginate()
    {
        return await EfContext.Users.AsNoTracking()
            .Where(u => u.IsActive)
            .Join(EfContext.Orders, u => u.UserId, o => o.UserId,
                (u, o) => new UserOrderDto { UserName = u.UserName, Total = o.Total })
            .Skip(5)
            .Take(10)
            .ToListAsync();
    }

    [Benchmark]
    public async Task<List<UserOrderDto>> Quarry_JoinFilterPaginate()
    {
        return await QuarryDb.Users
            .Where(u => u.IsActive)
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => new UserOrderDto
            {
                UserName = u.UserName,
                Total = o.Total
            })
            .Limit(10)
            .Offset(5)
            .ExecuteFetchAllAsync();
    }

    // --- Multi-Join + Aggregate ---

    [Benchmark]
    public async Task<int> Raw_MultiJoinAggregate()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*)
            FROM users u
            INNER JOIN orders o ON u.UserId = o.UserId
            INNER JOIN order_items oi ON o.OrderId = oi.OrderId
            WHERE u.IsActive = 1
            """;
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    [Benchmark]
    public async Task<int> Dapper_MultiJoinAggregate()
    {
        return await Connection.ExecuteScalarAsync<int>("""
            SELECT COUNT(*)
            FROM users u
            INNER JOIN orders o ON u.UserId = o.UserId
            INNER JOIN order_items oi ON o.OrderId = oi.OrderId
            WHERE u.IsActive = 1
            """);
    }

    [Benchmark]
    public async Task<int> EfCore_MultiJoinAggregate()
    {
        return await EfContext.Users.AsNoTracking()
            .Where(u => u.IsActive)
            .Join(EfContext.Orders, u => u.UserId, o => o.UserId, (u, o) => new { u, o })
            .Join(EfContext.OrderItems, uo => uo.o.OrderId, oi => oi.OrderId, (uo, oi) => uo)
            .CountAsync();
    }

    [Benchmark]
    public async Task<int> Quarry_MultiJoinAggregate()
    {
        var results = await QuarryDb.Users
            .Where(u => u.IsActive)
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Join<OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id)
            .Select((u, o, oi) => Sql.Count())
            .ExecuteFetchAllAsync();
        return results[0];
    }
}
