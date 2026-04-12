using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quarry.Benchmarks.Infrastructure;
using SqlKata;
using SqlKata.Compilers;

namespace Quarry.Benchmarks.Benchmarks;

public class SubquerySumBenchmarks : BenchmarkBase
{
    // ── Aggregate SUM subquery: users whose total order value > 200 ──

    private const string SumSubquerySql =
        "SELECT UserId, UserName FROM users WHERE (SELECT SUM(Total) FROM orders WHERE orders.UserId = users.UserId) > 200";

    [Benchmark(Baseline = true)]
    public async Task<List<UserIdNameDto>> Raw_SumSubquery()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = SumSubquerySql;
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess);
        var results = new List<UserIdNameDto>();
        while (await reader.ReadAsync())
        {
            results.Add(new UserIdNameDto
            {
                UserId = reader.GetInt32(0),
                UserName = reader.GetString(1)
            });
        }
        return results;
    }

    [Benchmark]
    public async Task<List<UserIdNameDto>> Dapper_SumSubquery()
    {
        return (await Connection.QueryAsync<UserIdNameDto>(SumSubquerySql)).AsList();
    }

    [Benchmark]
    public async Task<List<UserIdNameDto>> EfCore_SumSubquery()
    {
        return await EfContext.Users.AsNoTracking()
            .Where(u => u.Orders.Sum(o => o.Total) > 200)
            .Select(u => new UserIdNameDto { UserId = u.UserId, UserName = u.UserName })
            .ToListAsync();
    }

    [Benchmark]
    public async Task<List<UserIdNameDto>> Quarry_SumSubquery()
    {
        return await QuarryDb.Users()
            .Where(u => u.Orders.Sum(o => o.Total) > 200)
            .Select(u => new UserIdNameDto
            {
                UserId = u.UserId,
                UserName = u.UserName
            })
            .ExecuteFetchAllAsync();
    }

    [Benchmark]
    public async Task<List<UserIdNameDto>> SqlKata_SumSubquery_RawFallback()
    {
        var query = new Query("users")
            .Select("UserId", "UserName")
            .WhereRaw("(SELECT SUM(Total) FROM orders WHERE orders.UserId = users.UserId) > 200");
        var compiled = SqlKataCompiler.Compile(query);

        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = compiled.Sql;
        foreach (var binding in compiled.Bindings)
        {
            cmd.Parameters.AddWithValue($"@p{cmd.Parameters.Count}", binding);
        }
        await using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<UserIdNameDto>();
        while (await reader.ReadAsync())
        {
            results.Add(new UserIdNameDto
            {
                UserId = reader.GetInt32(0),
                UserName = reader.GetString(1)
            });
        }
        return results;
    }
}
