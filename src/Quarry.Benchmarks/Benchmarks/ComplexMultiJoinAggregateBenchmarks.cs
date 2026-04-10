using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quarry.Benchmarks.Infrastructure;
using SqlKata;
using SqlKata.Compilers;

namespace Quarry.Benchmarks.Benchmarks;

public class ComplexMultiJoinAggregateBenchmarks : BenchmarkBase
{
    [Benchmark(Baseline = true)]
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
            .ExecuteScalarAsync<int>();
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
