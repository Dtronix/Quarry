using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quarry.Benchmarks.Infrastructure;
using SqlKata;
using SqlKata.Compilers;

namespace Quarry.Benchmarks.Benchmarks;

/// <summary>
/// Runs a query 1000 times in a loop to measure sustained throughput and total
/// allocations across all frameworks.
/// </summary>
[MemoryDiagnoser]
public class ThroughputBenchmarks : BenchmarkBase
{
    private const int Iterations = 1000;

    [Benchmark(Baseline = true)]
    public async Task Raw_Throughput()
    {
        for (int i = 0; i < Iterations; i++)
        {
            var id = i % 100 + 1;
            await using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT UserId, UserName, Email, IsActive, CreatedAt, LastLogin FROM users WHERE UserId = @id";
            cmd.Parameters.AddWithValue("@id", id);
            await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess | CommandBehavior.SingleRow);
            if (await reader.ReadAsync())
            {
                _ = new RawUser
                {
                    UserId = reader.GetInt32(0),
                    UserName = reader.GetString(1),
                    Email = reader.IsDBNull(2) ? null : reader.GetString(2),
                    IsActive = reader.GetBoolean(3),
                    CreatedAt = reader.GetDateTime(4),
                    LastLogin = reader.IsDBNull(5) ? null : reader.GetDateTime(5)
                };
            }
        }
    }

    [Benchmark]
    public async Task Dapper_Throughput()
    {
        for (int i = 0; i < Iterations; i++)
        {
            var id = i % 100 + 1;
            _ = await Connection.QueryFirstOrDefaultAsync<DapperUser>(
                "SELECT UserId, UserName, Email, IsActive, CreatedAt, LastLogin FROM users WHERE UserId = @id",
                new { id });
        }
    }

    [Benchmark]
    public async Task EfCore_Throughput()
    {
        for (int i = 0; i < Iterations; i++)
        {
            var id = i % 100 + 1;
            _ = await EfContext.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserId == id);
        }
    }

    [Benchmark]
    public async Task Quarry_Throughput()
    {
        for (int i = 0; i < Iterations; i++)
        {
            var id = i % 100 + 1;
            _ = await QuarryDb.RawSqlAsync<User>(
                "SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"users\" WHERE \"UserId\" = @p0", id).ToListAsync();
        }
    }

    [Benchmark]
    public async Task SqlKata_Throughput()
    {
        for (int i = 0; i < Iterations; i++)
        {
            var id = i % 100 + 1;
            var query = new Query("users")
                .Select("UserId", "UserName", "Email", "IsActive", "CreatedAt", "LastLogin")
                .Where("UserId", id);
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
                _ = new SqlKataUser
                {
                    UserId = reader.GetInt32(0),
                    UserName = reader.GetString(1),
                    Email = reader.IsDBNull(2) ? null : reader.GetString(2),
                    IsActive = reader.GetBoolean(3),
                    CreatedAt = reader.GetDateTime(4),
                    LastLogin = reader.IsDBNull(5) ? null : reader.GetDateTime(5)
                };
            }
        }
    }
}
