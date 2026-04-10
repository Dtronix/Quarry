using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quarry.Benchmarks.Context;
using Quarry.Benchmarks.Infrastructure;
using SqlKata;
using SqlKata.Compilers;

namespace Quarry.Benchmarks.Benchmarks;

public class InsertBatchBenchmarks : BenchmarkBase
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
    public async Task<int> Raw_BatchInsert10()
    {
        await using var cmd = Connection.CreateCommand();
        var sb = new System.Text.StringBuilder("INSERT INTO users (UserName, Email, IsActive, CreatedAt) VALUES ");
        for (int i = 0; i < 10; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append($"(@name{i}, @email{i}, @active{i}, @created{i})");
            cmd.Parameters.AddWithValue($"@name{i}", $"BatchUser{i}");
            cmd.Parameters.AddWithValue($"@email{i}", $"batch{i}@example.com");
            cmd.Parameters.AddWithValue($"@active{i}", 1);
            cmd.Parameters.AddWithValue($"@created{i}", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
        }
        cmd.CommandText = sb.ToString();
        return await cmd.ExecuteNonQueryAsync();
    }

    [Benchmark]
    public async Task<int> Dapper_BatchInsert10()
    {
        var sb = new System.Text.StringBuilder("INSERT INTO users (UserName, Email, IsActive, CreatedAt) VALUES ");
        var parameters = new DynamicParameters();
        for (int i = 0; i < 10; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append($"(@name{i}, @email{i}, @active{i}, @created{i})");
            parameters.Add($"name{i}", $"BatchUser{i}");
            parameters.Add($"email{i}", $"batch{i}@example.com");
            parameters.Add($"active{i}", true);
            parameters.Add($"created{i}", DateTime.UtcNow);
        }
        return await Connection.ExecuteAsync(sb.ToString(), parameters);
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

    [Benchmark]
    public async Task<int> SqlKata_BatchInsert10()
    {
        var columns = new[] { "UserName", "Email", "IsActive", "CreatedAt" };
        var rows = Enumerable.Range(0, 10).Select(i => new object[]
        {
            $"BatchUser{i}",
            $"batch{i}@example.com",
            1,
            DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
        });
        var query = new Query("users").AsInsert(columns, rows);
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
