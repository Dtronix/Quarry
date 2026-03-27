using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quarry.Benchmarks.Infrastructure;
using SqlKata;
using SqlKata.Compilers;

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
        return await QuarryDb.Users()
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

    [Benchmark]
    public async Task<List<UserOrderDto>> SqlKata_JoinFilterPaginate()
    {
        var query = new Query("users as u")
            .Select("u.UserName", "o.Total")
            .Join("orders as o", "u.UserId", "o.UserId")
            .Where("u.IsActive", true)
            .Limit(10)
            .Offset(5);
        var compiled = SqlKataCompiler.Compile(query);

        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = compiled.Sql;
        foreach (var binding in compiled.Bindings)
        {
            cmd.Parameters.AddWithValue($"@p{cmd.Parameters.Count}", binding);
        }
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
        return await QuarryDb.Users()
            .Where(u => u.IsActive)
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Join<OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id)
            .Select((u, o, oi) => Sql.Count())
            .ExecuteFetchFirstAsync();
    }

    [Benchmark]
    public async Task<int> SqlKata_MultiJoinAggregate()
    {
        var query = new Query("users as u")
            .Join("orders as o", "u.UserId", "o.UserId")
            .Join("order_items as oi", "o.OrderId", "oi.OrderId")
            .Where("u.IsActive", true)
            .AsCount();
        var compiled = SqlKataCompiler.Compile(query);

        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = compiled.Sql;
        foreach (var binding in compiled.Bindings)
        {
            cmd.Parameters.AddWithValue($"@p{cmd.Parameters.Count}", binding);
        }
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }
}
