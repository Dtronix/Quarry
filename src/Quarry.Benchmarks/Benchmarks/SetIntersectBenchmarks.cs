using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quarry.Benchmarks.Infrastructure;
using SqlKata;
using SqlKata.Compilers;

namespace Quarry.Benchmarks.Benchmarks;

public class SetIntersectBenchmarks : BenchmarkBase
{
    // ── INTERSECT: active users ∩ users with non-null email ──

    private const string IntersectSql = """
        SELECT UserId, UserName FROM users WHERE IsActive = 1
        INTERSECT
        SELECT UserId, UserName FROM users WHERE Email IS NOT NULL
        """;

    [Benchmark(Baseline = true)]
    public async Task<List<UserIdNameDto>> Raw_Intersect()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = IntersectSql;
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
    public async Task<List<UserIdNameDto>> Dapper_Intersect()
    {
        return (await Connection.QueryAsync<UserIdNameDto>(IntersectSql)).AsList();
    }

    [Benchmark]
    public async Task<List<UserIdNameDto>> EfCore_Intersect()
    {
        return await EfContext.Users.AsNoTracking()
            .Where(u => u.IsActive)
            .Select(u => new UserIdNameDto { UserId = u.UserId, UserName = u.UserName })
            .Intersect(
                EfContext.Users.AsNoTracking()
                    .Where(u => u.Email != null)
                    .Select(u => new UserIdNameDto { UserId = u.UserId, UserName = u.UserName }))
            .ToListAsync();
    }

    [Benchmark]
    public async Task<List<UserIdNameDto>> Quarry_Intersect()
    {
        return await QuarryDb.Users()
            .Where(u => u.IsActive)
            .Select(u => new UserIdNameDto
            {
                UserId = u.UserId,
                UserName = u.UserName
            })
            .Intersect(
                QuarryDb.Users()
                    .Where(u => u.Email != null)
                    .Select(u => new UserIdNameDto
                    {
                        UserId = u.UserId,
                        UserName = u.UserName
                    }))
            .ExecuteFetchAllAsync();
    }

    [Benchmark]
    public async Task<List<UserIdNameDto>> SqlKata_Intersect()
    {
        var q1 = new Query("users").Select("UserId", "UserName").Where("IsActive", true);
        var q2 = new Query("users").Select("UserId", "UserName").WhereNotNull("Email");
        q1.Intersect(q2);
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
