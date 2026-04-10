using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quarry.Benchmarks.Infrastructure;
using SqlKata;
using SqlKata.Compilers;

namespace Quarry.Benchmarks.Benchmarks;

public class FilterWhereByIdBenchmarks : BenchmarkBase
{
    // Static field forces the source generator to parameterize the value.
    // Compare Quarry_WhereById (constant → inlined into SQL) vs
    // Quarry_WhereById_Parameterized (field → @p0 parameter).
    private static int _whereByIdTarget;

    public override void GlobalSetup()
    {
        base.GlobalSetup();
        _whereByIdTarget = 42;
    }

    [Benchmark(Baseline = true)]
    public async Task<RawUser?> Raw_WhereById()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = "SELECT UserId, UserName, Email, IsActive, CreatedAt, LastLogin FROM users WHERE UserId = @id";
        cmd.Parameters.AddWithValue("@id", 42);
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess | CommandBehavior.SingleRow);
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
        // Constant 42 is inlined into SQL by the source generator (no DbParameter).
        return await QuarryDb.Users()
            .Where(u => u.UserId == 42)
            .Select(u => u)
            .ExecuteFetchFirstOrDefaultAsync();
    }

    [Benchmark]
    public async Task<User?> Quarry_WhereById_Parameterized()
    {
        // Field variable forces the source generator to emit a @p0 parameter,
        // matching what Raw/Dapper do. This is the apples-to-apples comparison.
        return await QuarryDb.Users()
            .Where(u => u.UserId == _whereByIdTarget)
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
