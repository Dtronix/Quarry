using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quarry.Benchmarks.Context;
using Quarry.Benchmarks.Infrastructure;

namespace Quarry.Benchmarks.Benchmarks;

public class InsertBenchmarks : BenchmarkBase
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

    // --- Single Insert ---

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

    // --- Batch Insert (10 rows) ---

    [Benchmark]
    public async Task<int> Raw_BatchInsert10()
    {
        var total = 0;
        for (int i = 0; i < 10; i++)
        {
            await using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO users (UserName, Email, IsActive, CreatedAt) VALUES (@name, @email, @active, @created)";
            cmd.Parameters.AddWithValue("@name", $"BatchUser{i}");
            cmd.Parameters.AddWithValue("@email", $"batch{i}@example.com");
            cmd.Parameters.AddWithValue("@active", 1);
            cmd.Parameters.AddWithValue("@created", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
            total += await cmd.ExecuteNonQueryAsync();
        }
        return total;
    }

    [Benchmark]
    public async Task<int> Dapper_BatchInsert10()
    {
        var users = Enumerable.Range(0, 10).Select(i => new
        {
            UserName = $"BatchUser{i}",
            Email = $"batch{i}@example.com",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        return await Connection.ExecuteAsync(
            "INSERT INTO users (UserName, Email, IsActive, CreatedAt) VALUES (@UserName, @Email, @IsActive, @CreatedAt)",
            users);
    }

    [Benchmark]
    public async Task<int> EfCore_BatchInsert10()
    {
        for (int i = 0; i < 10; i++)
        {
            _iterationEfContext.Users.Add(new EfUser
            {
                UserName = $"BatchUser{i}",
                Email = $"batch{i}@example.com",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }
        return await _iterationEfContext.SaveChangesAsync();
    }

    [Benchmark]
    public async Task<int> Quarry_BatchInsert10()
    {
        var users = Enumerable.Range(0, 10).Select(i => new User
        {
            UserName = $"BatchUser{i}",
            Email = $"batch{i}@example.com",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        return await QuarryDb.Users().InsertBatch(u => (u.UserName, u.Email, u.IsActive, u.CreatedAt)).Values(users).ExecuteNonQueryAsync();
    }
}
