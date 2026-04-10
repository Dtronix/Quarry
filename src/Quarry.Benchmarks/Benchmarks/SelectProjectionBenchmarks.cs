using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quarry.Benchmarks.Infrastructure;
using SqlKata;
using SqlKata.Compilers;

namespace Quarry.Benchmarks.Benchmarks;

public class SelectProjectionBenchmarks : BenchmarkBase
{
    [Benchmark(Baseline = true)]
    public async Task<List<UserSummaryDto>> Raw_SelectProjection()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = "SELECT UserId, UserName, IsActive FROM users";
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess);
        var results = new List<UserSummaryDto>();
        while (await reader.ReadAsync())
        {
            results.Add(new UserSummaryDto
            {
                UserId = reader.GetInt32(0),
                UserName = reader.GetString(1),
                IsActive = reader.GetBoolean(2)
            });
        }
        return results;
    }

    [Benchmark]
    public async Task<List<UserSummaryDto>> Dapper_SelectProjection()
    {
        return (await Connection.QueryAsync<UserSummaryDto>(
            "SELECT UserId, UserName, IsActive FROM users")).AsList();
    }

    [Benchmark]
    public async Task<List<UserSummaryDto>> EfCore_SelectProjection()
    {
        return await EfContext.Users.AsNoTracking()
            .Select(u => new UserSummaryDto
            {
                UserId = u.UserId,
                UserName = u.UserName,
                IsActive = u.IsActive
            })
            .ToListAsync();
    }

    [Benchmark]
    public async Task<List<UserSummaryDto>> Quarry_SelectProjection()
    {
        return await QuarryDb.Users()
            .Select(u => new UserSummaryDto
            {
                UserId = u.UserId,
                UserName = u.UserName,
                IsActive = u.IsActive
            })
            .ExecuteFetchAllAsync();
    }

    [Benchmark]
    public async Task<List<UserSummaryDto>> SqlKata_SelectProjection()
    {
        var query = new Query("users")
            .Select("UserId", "UserName", "IsActive");
        var compiled = SqlKataCompiler.Compile(query);

        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = compiled.Sql;
        await using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<UserSummaryDto>();
        while (await reader.ReadAsync())
        {
            results.Add(new UserSummaryDto
            {
                UserId = reader.GetInt32(0),
                UserName = reader.GetString(1),
                IsActive = reader.GetBoolean(2)
            });
        }
        return results;
    }
}
