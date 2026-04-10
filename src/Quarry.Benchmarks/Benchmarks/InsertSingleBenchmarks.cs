using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quarry.Benchmarks.Context;
using Quarry.Benchmarks.Infrastructure;
using SqlKata;
using SqlKata.Compilers;

namespace Quarry.Benchmarks.Benchmarks;

public class InsertSingleBenchmarks : BenchmarkBase
{
    private EfBenchContext _iterationEfContext = null!;

    [IterationSetup]
    public void IterationSetup()
    {
        _iterationEfContext = CreateEfContext();
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        _iterationEfContext?.Dispose();
        // Remove any scratch rows inserted during this iteration
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM users WHERE UserId > 100";
        cmd.ExecuteNonQuery();
    }

    [Benchmark(Baseline = true)]
    public async Task<int> Raw_SingleInsert()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = "INSERT INTO users (UserName, Email, IsActive, CreatedAt) VALUES (@name, @email, @active, @created)";
        cmd.Parameters.AddWithValue("@name", "BenchUser");
        cmd.Parameters.AddWithValue("@email", "bench@example.com");
        cmd.Parameters.AddWithValue("@active", 1);
        cmd.Parameters.AddWithValue("@created", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
        return await cmd.ExecuteNonQueryAsync();
    }

    [Benchmark]
    public async Task<int> Dapper_SingleInsert()
    {
        return await Connection.ExecuteAsync(
            "INSERT INTO users (UserName, Email, IsActive, CreatedAt) VALUES (@UserName, @Email, @IsActive, @CreatedAt)",
            new { UserName = "BenchUser", Email = "bench@example.com", IsActive = true, CreatedAt = DateTime.UtcNow });
    }

    [Benchmark]
    public async Task<int> EfCore_SingleInsert()
    {
        _iterationEfContext.Users.Add(new EfUser
        {
            UserName = "BenchUser",
            Email = "bench@example.com",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        return await _iterationEfContext.SaveChangesAsync();
    }

    [Benchmark]
    public async Task<int> Quarry_SingleInsert()
    {
        return await QuarryDb.Users().Insert(new User
        {
            UserName = "BenchUser",
            Email = "bench@example.com",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        }).ExecuteNonQueryAsync();
    }

    [Benchmark]
    public async Task<int> SqlKata_SingleInsert()
    {
        var query = new Query("users").AsInsert(new Dictionary<string, object>
        {
            ["UserName"] = "BenchUser",
            ["Email"] = "bench@example.com",
            ["IsActive"] = 1,
            ["CreatedAt"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
        });
        var compiled = SqlKataCompiler.Compile(query);

        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = compiled.Sql;
        foreach (var binding in compiled.Bindings)
        {
            cmd.Parameters.AddWithValue($"@p{cmd.Parameters.Count}", binding);
        }
        return await cmd.ExecuteNonQueryAsync();
    }
}
