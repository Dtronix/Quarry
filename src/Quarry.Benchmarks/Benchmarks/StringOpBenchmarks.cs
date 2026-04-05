using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quarry.Benchmarks.Infrastructure;
using SqlKata;
using SqlKata.Compilers;

namespace Quarry.Benchmarks.Benchmarks;

public class StringOpBenchmarks : BenchmarkBase
{
    // --- Contains (LIKE '%...%') ---

    [Benchmark(Baseline = true)]
    public async Task<List<UserSummaryDto>> Raw_Contains()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = "SELECT UserId, UserName, IsActive FROM users WHERE UserName LIKE '%User05%'";
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
    public async Task<List<UserSummaryDto>> Dapper_Contains()
    {
        return (await Connection.QueryAsync<UserSummaryDto>(
            "SELECT UserId, UserName, IsActive FROM users WHERE UserName LIKE '%User05%'")).AsList();
    }

    [Benchmark]
    public async Task<List<UserSummaryDto>> EfCore_Contains()
    {
        return await EfContext.Users.AsNoTracking()
            .Where(u => u.UserName.Contains("User05"))
            .Select(u => new UserSummaryDto
            {
                UserId = u.UserId,
                UserName = u.UserName,
                IsActive = u.IsActive
            })
            .ToListAsync();
    }

    [Benchmark]
    public async Task<List<UserSummaryDto>> Quarry_Contains()
    {
        return await QuarryDb.Users()
            .Where(u => u.UserName.Contains("User05"))
            .Select(u => new UserSummaryDto
            {
                UserId = u.UserId,
                UserName = u.UserName,
                IsActive = u.IsActive
            })
            .ExecuteFetchAllAsync();
    }

    [Benchmark]
    public async Task<List<UserSummaryDto>> SqlKata_Contains()
    {
        var query = new Query("users")
            .Select("UserId", "UserName", "IsActive")
            .WhereContains("UserName", "User05");
        var compiled = SqlKataCompiler.Compile(query);

        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = compiled.Sql;
        foreach (var binding in compiled.Bindings)
        {
            cmd.Parameters.AddWithValue($"@p{cmd.Parameters.Count}", binding);
        }
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

    // --- StartsWith (LIKE '...%') ---

    [Benchmark]
    public async Task<List<UserSummaryDto>> Raw_StartsWith()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = "SELECT UserId, UserName, IsActive FROM users WHERE UserName LIKE 'User0%'";
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
    public async Task<List<UserSummaryDto>> Dapper_StartsWith()
    {
        return (await Connection.QueryAsync<UserSummaryDto>(
            "SELECT UserId, UserName, IsActive FROM users WHERE UserName LIKE 'User0%'")).AsList();
    }

    [Benchmark]
    public async Task<List<UserSummaryDto>> EfCore_StartsWith()
    {
        return await EfContext.Users.AsNoTracking()
            .Where(u => u.UserName.StartsWith("User0"))
            .Select(u => new UserSummaryDto
            {
                UserId = u.UserId,
                UserName = u.UserName,
                IsActive = u.IsActive
            })
            .ToListAsync();
    }

    [Benchmark]
    public async Task<List<UserSummaryDto>> Quarry_StartsWith()
    {
        return await QuarryDb.Users()
            .Where(u => u.UserName.StartsWith("User0"))
            .Select(u => new UserSummaryDto
            {
                UserId = u.UserId,
                UserName = u.UserName,
                IsActive = u.IsActive
            })
            .ExecuteFetchAllAsync();
    }

    [Benchmark]
    public async Task<List<UserSummaryDto>> SqlKata_StartsWith()
    {
        var query = new Query("users")
            .Select("UserId", "UserName", "IsActive")
            .WhereStarts("UserName", "User0");
        var compiled = SqlKataCompiler.Compile(query);

        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = compiled.Sql;
        foreach (var binding in compiled.Bindings)
        {
            cmd.Parameters.AddWithValue($"@p{cmd.Parameters.Count}", binding);
        }
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
