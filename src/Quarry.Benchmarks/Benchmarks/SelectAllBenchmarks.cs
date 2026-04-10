using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quarry.Benchmarks.Infrastructure;
using SqlKata;
using SqlKata.Compilers;

namespace Quarry.Benchmarks.Benchmarks;

public class SelectAllBenchmarks : BenchmarkBase
{
    [Benchmark(Baseline = true)]
    public async Task<List<RawUser>> Raw_SelectAll()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = "SELECT UserId, UserName, Email, IsActive, CreatedAt, LastLogin FROM users";
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess);
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
    public async Task<List<DapperUser>> Dapper_SelectAll()
    {
        return (await Connection.QueryAsync<DapperUser>(
            "SELECT UserId, UserName, Email, IsActive, CreatedAt, LastLogin FROM users")).AsList();
    }

    [Benchmark]
    public async Task<List<EfUser>> EfCore_SelectAll()
    {
        return await EfContext.Users.AsNoTracking().ToListAsync();
    }

    [Benchmark]
    public async Task<List<User>> Quarry_SelectAll()
    {
        return await QuarryDb.Users()
            .Select(u => u)
            .ExecuteFetchAllAsync();
    }

    [Benchmark]
    public async Task<List<SqlKataUser>> SqlKata_SelectAll()
    {
        var query = new Query("users")
            .Select("UserId", "UserName", "Email", "IsActive", "CreatedAt", "LastLogin");
        var compiled = SqlKataCompiler.Compile(query);

        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = compiled.Sql;
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
}
