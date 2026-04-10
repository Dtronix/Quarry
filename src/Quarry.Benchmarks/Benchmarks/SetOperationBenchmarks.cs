using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quarry.Benchmarks.Infrastructure;
using SqlKata;
using SqlKata.Compilers;

namespace Quarry.Benchmarks.Benchmarks;

public class SetOperationBenchmarks : BenchmarkBase
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

    // ── INTERSECT: active users ∩ users with non-null email ──

    private const string IntersectSql = """
        SELECT UserId, UserName FROM users WHERE IsActive = 1
        INTERSECT
        SELECT UserId, UserName FROM users WHERE Email IS NOT NULL
        """;

    [Benchmark]
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

    // ── EXCEPT: active users − users with ID ≤ 10 ──

    private const string ExceptSql = """
        SELECT UserId, UserName FROM users WHERE IsActive = 1
        EXCEPT
        SELECT UserId, UserName FROM users WHERE UserId <= 10
        """;

    [Benchmark]
    public async Task<List<UserIdNameDto>> Raw_Except()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = ExceptSql;
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
    public async Task<List<UserIdNameDto>> Dapper_Except()
    {
        return (await Connection.QueryAsync<UserIdNameDto>(ExceptSql)).AsList();
    }

    [Benchmark]
    public async Task<List<UserIdNameDto>> EfCore_Except()
    {
        return await EfContext.Users.AsNoTracking()
            .Where(u => u.IsActive)
            .Select(u => new UserIdNameDto { UserId = u.UserId, UserName = u.UserName })
            .Except(
                EfContext.Users.AsNoTracking()
                    .Where(u => u.UserId <= 10)
                    .Select(u => new UserIdNameDto { UserId = u.UserId, UserName = u.UserName }))
            .ToListAsync();
    }

    [Benchmark]
    public async Task<List<UserIdNameDto>> Quarry_Except()
    {
        return await QuarryDb.Users()
            .Where(u => u.IsActive)
            .Select(u => new UserIdNameDto
            {
                UserId = u.UserId,
                UserName = u.UserName
            })
            .Except(
                QuarryDb.Users()
                    .Where(u => u.UserId <= 10)
                    .Select(u => new UserIdNameDto
                    {
                        UserId = u.UserId,
                        UserName = u.UserName
                    }))
            .ExecuteFetchAllAsync();
    }

    [Benchmark]
    public async Task<List<UserIdNameDto>> SqlKata_Except()
    {
        var q1 = new Query("users").Select("UserId", "UserName").Where("IsActive", true);
        var q2 = new Query("users").Select("UserId", "UserName").Where("UserId", "<=", 10);
        q1.Except(q2);
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
