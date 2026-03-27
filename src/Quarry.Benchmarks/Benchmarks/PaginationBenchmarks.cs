using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quarry.Benchmarks.Infrastructure;
using SqlKata;
using SqlKata.Compilers;

namespace Quarry.Benchmarks.Benchmarks;

public class PaginationBenchmarks : BenchmarkBase
{
    // --- Limit/Offset ---

    [Benchmark(Baseline = true)]
    public async Task<List<EfUser>> Raw_LimitOffset()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = "SELECT UserId, UserName, Email, IsActive, CreatedAt, LastLogin FROM users LIMIT 10 OFFSET 20";
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
    public async Task<List<EfUser>> Dapper_LimitOffset()
    {
        return (await Connection.QueryAsync<EfUser>(
            "SELECT UserId, UserName, Email, IsActive, CreatedAt, LastLogin FROM users LIMIT 10 OFFSET 20")).AsList();
    }

    [Benchmark]
    public async Task<List<EfUser>> EfCore_LimitOffset()
    {
        return await EfContext.Users.AsNoTracking()
            .Skip(20)
            .Take(10)
            .ToListAsync();
    }

    [Benchmark]
    public async Task<List<User>> Quarry_LimitOffset()
    {
        return await QuarryDb.Users()
            .Select(u => u)
            .Limit(10)
            .Offset(20)
            .ExecuteFetchAllAsync();
    }

    [Benchmark]
    public async Task<List<EfUser>> SqlKata_LimitOffset()
    {
        var query = new Query("users")
            .Select("UserId", "UserName", "Email", "IsActive", "CreatedAt", "LastLogin")
            .Limit(10)
            .Offset(20);
        var compiled = SqlKataCompiler.Compile(query);

        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = compiled.Sql;
        foreach (var binding in compiled.Bindings)
        {
            cmd.Parameters.AddWithValue($"@p{cmd.Parameters.Count}", binding);
        }
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

    // --- First Page ---

    [Benchmark]
    public async Task<List<EfUser>> Raw_FirstPage()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = "SELECT UserId, UserName, Email, IsActive, CreatedAt, LastLogin FROM users LIMIT 10";
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
    public async Task<List<EfUser>> Dapper_FirstPage()
    {
        return (await Connection.QueryAsync<EfUser>(
            "SELECT UserId, UserName, Email, IsActive, CreatedAt, LastLogin FROM users LIMIT 10")).AsList();
    }

    [Benchmark]
    public async Task<List<EfUser>> EfCore_FirstPage()
    {
        return await EfContext.Users.AsNoTracking()
            .Take(10)
            .ToListAsync();
    }

    [Benchmark]
    public async Task<List<User>> Quarry_FirstPage()
    {
        return await QuarryDb.Users()
            .Select(u => u)
            .Limit(10)
            .ExecuteFetchAllAsync();
    }

    [Benchmark]
    public async Task<List<EfUser>> SqlKata_FirstPage()
    {
        var query = new Query("users")
            .Select("UserId", "UserName", "Email", "IsActive", "CreatedAt", "LastLogin")
            .Limit(10);
        var compiled = SqlKataCompiler.Compile(query);

        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = compiled.Sql;
        foreach (var binding in compiled.Bindings)
        {
            cmd.Parameters.AddWithValue($"@p{cmd.Parameters.Count}", binding);
        }
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
}
