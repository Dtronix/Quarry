using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quarry.Benchmarks.Infrastructure;

namespace Quarry.Benchmarks.Benchmarks;

public class SelectBenchmarks : BenchmarkBase
{
    // --- Select All ---

    [Benchmark(Baseline = true)]
    public async Task<List<EfUser>> Raw_SelectAll()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = "SELECT UserId, UserName, Email, IsActive, CreatedAt, LastLogin FROM users";
        await using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<EfUser>();
        while (await reader.ReadAsync())
        {
            results.Add(new EfUser
            {
                UserId = reader.GetInt32(0),
                UserName = reader.GetString(1),
                Email = reader.IsDBNull(2) ? null : reader.GetString(2),
                IsActive = reader.GetBoolean(3),
                CreatedAt = reader.GetDateTime(4),
                LastLogin = reader.IsDBNull(5) ? null : reader.GetDateTime(5)
            });
        }
        return results;
    }

    [Benchmark]
    public async Task<List<EfUser>> Dapper_SelectAll()
    {
        return (await Connection.QueryAsync<EfUser>(
            "SELECT UserId, UserName, Email, IsActive, CreatedAt, LastLogin FROM users")).AsList();
    }

    [Benchmark]
    public async Task<List<EfUser>> EfCore_SelectAll()
    {
        return await EfContext.Users.AsNoTracking().ToListAsync();
    }

    [Benchmark]
    public async Task<List<EfUser>> Quarry_SelectAll()
    {
        return await QuarryDb.Users()
            .Select(u => new EfUser
            {
                UserId = u.UserId,
                UserName = u.UserName,
                Email = u.Email,
                IsActive = u.IsActive,
                CreatedAt = u.CreatedAt,
                LastLogin = u.LastLogin
            })
            .ExecuteFetchAllAsync();
    }

    // --- Select with Projection ---

    [Benchmark]
    public async Task<List<UserSummaryDto>> Raw_SelectProjection()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = "SELECT UserId, UserName, IsActive FROM users";
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
}
