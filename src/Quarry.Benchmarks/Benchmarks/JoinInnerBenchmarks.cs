using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quarry.Benchmarks.Infrastructure;
using SqlKata;
using SqlKata.Compilers;

namespace Quarry.Benchmarks.Benchmarks;

public class JoinInnerBenchmarks : BenchmarkBase
{
    [Benchmark(Baseline = true)]
    public async Task<List<UserOrderDto>> Raw_InnerJoin()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = "SELECT u.UserName, o.Total FROM users u INNER JOIN orders o ON u.UserId = o.UserId";
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess);
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
    public async Task<List<UserOrderDto>> Dapper_InnerJoin()
    {
        return (await Connection.QueryAsync<UserOrderDto>(
            "SELECT u.UserName, o.Total FROM users u INNER JOIN orders o ON u.UserId = o.UserId")).AsList();
    }

    [Benchmark]
    public async Task<List<UserOrderDto>> EfCore_InnerJoin()
    {
        return await EfContext.Users.AsNoTracking()
            .Join(EfContext.Orders, u => u.UserId, o => o.UserId,
                (u, o) => new UserOrderDto { UserName = u.UserName, Total = o.Total })
            .ToListAsync();
    }

    [Benchmark]
    public async Task<List<UserOrderDto>> Quarry_InnerJoin()
    {
        return await QuarryDb.Users()
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => new UserOrderDto
            {
                UserName = u.UserName,
                Total = o.Total
            })
            .ExecuteFetchAllAsync();
    }

    [Benchmark]
    public async Task<List<UserOrderDto>> SqlKata_InnerJoin()
    {
        var query = new Query("users as u")
            .Select("u.UserName", "o.Total")
            .Join("orders as o", "u.UserId", "o.UserId");
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
}
