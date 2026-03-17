using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quarry.Benchmarks.Infrastructure;

namespace Quarry.Benchmarks.Benchmarks;

public class FilterBenchmarks : BenchmarkBase
{
    // --- Where Active ---

    [Benchmark(Baseline = true)]
    public async Task<List<EfUser>> Raw_WhereActive()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = "SELECT UserId, UserName, Email, IsActive, CreatedAt, LastLogin FROM users WHERE IsActive = 1";
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
    public async Task<List<EfUser>> Dapper_WhereActive()
    {
        return (await Connection.QueryAsync<EfUser>(
            "SELECT UserId, UserName, Email, IsActive, CreatedAt, LastLogin FROM users WHERE IsActive = 1")).AsList();
    }

    [Benchmark]
    public async Task<List<EfUser>> EfCore_WhereActive()
    {
        return await EfContext.Users.AsNoTracking()
            .Where(u => u.IsActive)
            .ToListAsync();
    }

    [Benchmark]
    public async Task<List<EfUser>> Quarry_WhereActive()
    {
        return await QuarryDb.Users()
            .Where(u => u.IsActive)
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

    // --- Where Compound ---

    [Benchmark]
    public async Task<List<UserSummaryDto>> Raw_WhereCompound()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = "SELECT UserId, UserName, IsActive FROM users WHERE IsActive = 1 AND Email IS NOT NULL";
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
    public async Task<List<UserSummaryDto>> Dapper_WhereCompound()
    {
        return (await Connection.QueryAsync<UserSummaryDto>(
            "SELECT UserId, UserName, IsActive FROM users WHERE IsActive = 1 AND Email IS NOT NULL")).AsList();
    }

    [Benchmark]
    public async Task<List<UserSummaryDto>> EfCore_WhereCompound()
    {
        return await EfContext.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Email != null)
            .Select(u => new UserSummaryDto
            {
                UserId = u.UserId,
                UserName = u.UserName,
                IsActive = u.IsActive
            })
            .ToListAsync();
    }

    [Benchmark]
    public async Task<List<UserSummaryDto>> Quarry_WhereCompound()
    {
        return await QuarryDb.Users()
            .Where(u => u.IsActive)
            .Where(u => u.Email != null)
            .Select(u => new UserSummaryDto
            {
                UserId = u.UserId,
                UserName = u.UserName,
                IsActive = u.IsActive
            })
            .ExecuteFetchAllAsync();
    }

    // --- Where By ID ---

    [Benchmark]
    public async Task<EfUser?> Raw_WhereById()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = "SELECT UserId, UserName, Email, IsActive, CreatedAt, LastLogin FROM users WHERE UserId = @id";
        cmd.Parameters.AddWithValue("@id", 42);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new EfUser
            {
                UserId = reader.GetInt32(0),
                UserName = reader.GetString(1),
                Email = reader.IsDBNull(2) ? null : reader.GetString(2),
                IsActive = reader.GetBoolean(3),
                CreatedAt = reader.GetDateTime(4),
                LastLogin = reader.IsDBNull(5) ? null : reader.GetDateTime(5)
            };
        }
        return null;
    }

    [Benchmark]
    public async Task<EfUser?> Dapper_WhereById()
    {
        return await Connection.QueryFirstOrDefaultAsync<EfUser>(
            "SELECT UserId, UserName, Email, IsActive, CreatedAt, LastLogin FROM users WHERE UserId = @id",
            new { id = 42 });
    }

    [Benchmark]
    public async Task<EfUser?> EfCore_WhereById()
    {
        return await EfContext.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == 42);
    }

    [Benchmark]
    public async Task<EfUser?> Quarry_WhereById()
    {
        return await QuarryDb.Users()
            .Where(u => u.UserId == 42)
            .Select(u => new EfUser
            {
                UserId = u.UserId,
                UserName = u.UserName,
                Email = u.Email,
                IsActive = u.IsActive,
                CreatedAt = u.CreatedAt,
                LastLogin = u.LastLogin
            })
            .ExecuteFetchFirstOrDefaultAsync();
    }
}
