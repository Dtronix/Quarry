using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quarry.Benchmarks.Context;
using Quarry.Benchmarks.Infrastructure;
using SqlKata;
using SqlKata.Compilers;

namespace Quarry.Benchmarks.Benchmarks;

/// <summary>
/// Measures first-query latency. Each iteration creates a fresh context and runs
/// one query, showcasing Quarry's zero-startup-cost advantage vs EF Core's model
/// compilation overhead.
/// </summary>
[MemoryDiagnoser]
public class ColdStartBenchmarks
{
    private SqliteConnection _connection = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _connection = new SqliteConnection("Data Source=BenchDb;Mode=Memory;Cache=Shared");
        _connection.Open();
        DatabaseSetup.CreateAndSeed(_connection);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _connection?.Close();
        _connection?.Dispose();
    }

    // Each benchmark creates a NEW context and runs ONE query.
    // This measures setup + first-query cost.

    [Benchmark(Baseline = true)]
    public async Task<List<EfUser>> Raw_ColdStart()
    {
        await using var cmd = _connection.CreateCommand();
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
    public async Task<List<EfUser>> Dapper_ColdStart()
    {
        return (await _connection.QueryAsync<EfUser>(
            "SELECT UserId, UserName, Email, IsActive, CreatedAt, LastLogin FROM users WHERE IsActive = 1")).AsList();
    }

    [Benchmark]
    public async Task<List<EfUser>> EfCore_ColdStart()
    {
        // New context each time forces model compilation
        await using var ctx = new EfBenchContext(_connection);
        return await ctx.Users.AsNoTracking()
            .Where(u => u.IsActive)
            .ToListAsync();
    }

    [Benchmark]
    public async Task<List<User>> Quarry_ColdStart()
    {
        // New BenchDb each time — no model compilation needed
        using var db = new BenchDb(_connection);
        return await db.Users()
            .Where(u => u.IsActive)
            .Select(u => u)
            .ExecuteFetchAllAsync();
    }

    [Benchmark]
    public async Task<List<EfUser>> SqlKata_ColdStart()
    {
        // New compiler each time
        var compiler = new SqliteCompiler();
        var query = new Query("users")
            .Select("UserId", "UserName", "Email", "IsActive", "CreatedAt", "LastLogin")
            .Where("IsActive", true);
        var compiled = compiler.Compile(query);

        await using var cmd = _connection.CreateCommand();
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
