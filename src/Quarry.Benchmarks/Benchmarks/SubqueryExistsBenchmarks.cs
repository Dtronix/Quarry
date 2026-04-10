using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quarry.Benchmarks.Infrastructure;
using SqlKata;
using SqlKata.Compilers;

namespace Quarry.Benchmarks.Benchmarks;

public class SubqueryExistsBenchmarks : BenchmarkBase
{
    // ── EXISTS: users who have at least one order ──

    private const string ExistsSql =
        "SELECT UserId, UserName FROM users WHERE EXISTS (SELECT 1 FROM orders WHERE orders.UserId = users.UserId)";

    [Benchmark(Baseline = true)]
    public async Task<List<UserIdNameDto>> Raw_Exists()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = ExistsSql;
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
    public async Task<List<UserIdNameDto>> Dapper_Exists()
    {
        return (await Connection.QueryAsync<UserIdNameDto>(ExistsSql)).AsList();
    }

    [Benchmark]
    public async Task<List<UserIdNameDto>> EfCore_Exists()
    {
        return await EfContext.Users.AsNoTracking()
            .Where(u => u.Orders.Any())
            .Select(u => new UserIdNameDto { UserId = u.UserId, UserName = u.UserName })
            .ToListAsync();
    }

    [Benchmark]
    public async Task<List<UserIdNameDto>> Quarry_Exists()
    {
        return await QuarryDb.Users()
            .Where(u => u.Orders.Any())
            .Select(u => new UserIdNameDto
            {
                UserId = u.UserId,
                UserName = u.UserName
            })
            .ExecuteFetchAllAsync();
    }

    [Benchmark]
    public async Task<List<UserIdNameDto>> SqlKata_Exists()
    {
        var query = new Query("users")
            .Select("UserId", "UserName")
            .WhereExists(q => q.From("orders")
                .WhereColumns("orders.UserId", "=", "users.UserId")
                .SelectRaw("1"));
        var compiled = SqlKataCompiler.Compile(query);

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
