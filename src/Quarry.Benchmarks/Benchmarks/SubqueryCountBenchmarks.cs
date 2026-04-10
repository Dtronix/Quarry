using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quarry.Benchmarks.Infrastructure;
using SqlKata;
using SqlKata.Compilers;

namespace Quarry.Benchmarks.Benchmarks;

public class SubqueryCountBenchmarks : BenchmarkBase
{
    // ── Scalar COUNT subquery: users with more than 2 orders ──

    private const string CountSubquerySql =
        "SELECT UserId, UserName FROM users WHERE (SELECT COUNT(*) FROM orders WHERE orders.UserId = users.UserId) > 2";

    [Benchmark(Baseline = true)]
    public async Task<List<UserIdNameDto>> Raw_CountSubquery()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = CountSubquerySql;
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
    public async Task<List<UserIdNameDto>> Dapper_CountSubquery()
    {
        return (await Connection.QueryAsync<UserIdNameDto>(CountSubquerySql)).AsList();
    }

    [Benchmark]
    public async Task<List<UserIdNameDto>> EfCore_CountSubquery()
    {
        return await EfContext.Users.AsNoTracking()
            .Where(u => u.Orders.Count > 2)
            .Select(u => new UserIdNameDto { UserId = u.UserId, UserName = u.UserName })
            .ToListAsync();
    }

    [Benchmark]
    public async Task<List<UserIdNameDto>> Quarry_CountSubquery()
    {
        return await QuarryDb.Users()
            .Where(u => u.Orders.Count() > 2)
            .Select(u => new UserIdNameDto
            {
                UserId = u.UserId,
                UserName = u.UserName
            })
            .ExecuteFetchAllAsync();
    }

    [Benchmark]
    public async Task<List<UserIdNameDto>> SqlKata_CountSubquery()
    {
        var query = new Query("users")
            .Select("UserId", "UserName")
            .WhereRaw("(SELECT COUNT(*) FROM orders WHERE orders.UserId = users.UserId) > 2");
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
