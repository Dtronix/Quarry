using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quarry.Benchmarks.Infrastructure;
using SqlKata;
using SqlKata.Compilers;

namespace Quarry.Benchmarks.Benchmarks;

public class CteMultiBenchmarks : BenchmarkBase
{
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

    [Benchmark(Baseline = true)]
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
