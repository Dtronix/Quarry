using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quarry.Benchmarks.Infrastructure;
using SqlKata;
using SqlKata.Compilers;

namespace Quarry.Benchmarks.Benchmarks;

public class SetUnionAllBenchmarks : BenchmarkBase
{
    // ── UNION ALL: active users + user with ID 1 ──

    private const string UnionAllSql = """
        SELECT UserId, UserName FROM users WHERE IsActive = 1
        UNION ALL
        SELECT UserId, UserName FROM users WHERE UserId = 1
        """;

    [Benchmark(Baseline = true)]
    public async Task<List<UserIdNameDto>> Raw_UnionAll()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = UnionAllSql;
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
    public async Task<List<UserIdNameDto>> Dapper_UnionAll()
    {
        return (await Connection.QueryAsync<UserIdNameDto>(UnionAllSql)).AsList();
    }

    [Benchmark]
    public async Task<List<UserIdNameDto>> EfCore_UnionAll()
    {
        return await EfContext.Users.AsNoTracking()
            .Where(u => u.IsActive)
            .Select(u => new UserIdNameDto { UserId = u.UserId, UserName = u.UserName })
            .AsQueryable()
            .Concat(
                EfContext.Users.AsNoTracking()
                    .Where(u => u.UserId == 1)
                    .Select(u => new UserIdNameDto { UserId = u.UserId, UserName = u.UserName }))
            .ToListAsync();
    }

    [Benchmark]
    public async Task<List<UserIdNameDto>> Quarry_UnionAll()
    {
        return await QuarryDb.Users()
            .Where(u => u.IsActive)
            .Select(u => new UserIdNameDto
            {
                UserId = u.UserId,
                UserName = u.UserName
            })
            .UnionAll(
                QuarryDb.Users()
                    .Where(u => u.UserId == 1)
                    .Select(u => new UserIdNameDto
                    {
                        UserId = u.UserId,
                        UserName = u.UserName
                    }))
            .ExecuteFetchAllAsync();
    }

    [Benchmark]
    public async Task<List<UserIdNameDto>> SqlKata_UnionAll()
    {
        var q1 = new Query("users").Select("UserId", "UserName").Where("IsActive", true);
        var q2 = new Query("users").Select("UserId", "UserName").Where("UserId", 1);
        q1.UnionAll(q2);
        var compiled = SqlKataCompiler.Compile(q1);

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
