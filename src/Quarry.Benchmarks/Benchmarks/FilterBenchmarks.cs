using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quarry.Benchmarks.Infrastructure;
using SqlKata;
using SqlKata.Compilers;

namespace Quarry.Benchmarks.Benchmarks;

public class FilterBenchmarks : BenchmarkBase
{
    // --- Where Active ---

    [Benchmark(Baseline = true)]
    public async Task<List<RawUser>> Raw_WhereActive()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = "SELECT UserId, UserName, Email, IsActive, CreatedAt, LastLogin FROM users WHERE IsActive = 1";
        await using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<RawUser>();
        while (await reader.ReadAsync())
        {
            results.Add(new RawUser
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
    public async Task<List<DapperUser>> Dapper_WhereActive()
    {
        return (await Connection.QueryAsync<DapperUser>(
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
    public async Task<List<User>> Quarry_WhereActive()
    {
        return await QuarryDb.Users()
            .Where(u => u.IsActive)
            .Select(u => u)
            .ExecuteFetchAllAsync();
    }

    [Benchmark]
    public async Task<List<SqlKataUser>> SqlKata_WhereActive()
    {
        var query = new Query("users")
            .Select("UserId", "UserName", "Email", "IsActive", "CreatedAt", "LastLogin")
            .Where("IsActive", true);
        var compiled = SqlKataCompiler.Compile(query);

        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = compiled.Sql;
        foreach (var binding in compiled.Bindings)
        {
            cmd.Parameters.AddWithValue($"@p{cmd.Parameters.Count}", binding);
        }
        await using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<SqlKataUser>();
        while (await reader.ReadAsync())
        {
            results.Add(new SqlKataUser
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
            .Where(u => u.IsActive && u.Email != null)
            .Select(u => new UserSummaryDto
            {
                UserId = u.UserId,
                UserName = u.UserName,
                IsActive = u.IsActive
            })
            .ExecuteFetchAllAsync();
    }

    [Benchmark]
    public async Task<List<UserSummaryDto>> SqlKata_WhereCompound()
    {
        var query = new Query("users")
            .Select("UserId", "UserName", "IsActive")
            .Where("IsActive", true)
            .WhereNotNull("Email");
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

    // --- Where By ID ---

    [Benchmark]
    public async Task<RawUser?> Raw_WhereById()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = "SELECT UserId, UserName, Email, IsActive, CreatedAt, LastLogin FROM users WHERE UserId = @id";
        cmd.Parameters.AddWithValue("@id", 42);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new RawUser
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
    public async Task<DapperUser?> Dapper_WhereById()
    {
        return await Connection.QueryFirstOrDefaultAsync<DapperUser>(
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
    public async Task<User?> Quarry_WhereById()
    {
        return await QuarryDb.Users()
            .Where(u => u.UserId == 42)
            .Select(u => u)
            .ExecuteFetchFirstOrDefaultAsync();
    }

    [Benchmark]
    public async Task<SqlKataUser?> SqlKata_WhereById()
    {
        var query = new Query("users")
            .Select("UserId", "UserName", "Email", "IsActive", "CreatedAt", "LastLogin")
            .Where("UserId", 42);
        var compiled = SqlKataCompiler.Compile(query);

        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = compiled.Sql;
        foreach (var binding in compiled.Bindings)
        {
            cmd.Parameters.AddWithValue($"@p{cmd.Parameters.Count}", binding);
        }
        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new SqlKataUser
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
}
