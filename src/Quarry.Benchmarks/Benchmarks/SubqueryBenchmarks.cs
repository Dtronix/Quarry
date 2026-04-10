using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quarry.Benchmarks.Infrastructure;
using SqlKata;
using SqlKata.Compilers;

namespace Quarry.Benchmarks.Benchmarks;

public class SubqueryBenchmarks : BenchmarkBase
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

    // ── Filtered EXISTS: users with at least one order > 50 ──

    private const string FilteredExistsSql =
        "SELECT UserId, UserName FROM users WHERE EXISTS (SELECT 1 FROM orders WHERE orders.UserId = users.UserId AND Total > 50)";

    [Benchmark]
    public async Task<List<UserIdNameDto>> Raw_FilteredExists()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = FilteredExistsSql;
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
    public async Task<List<UserIdNameDto>> Dapper_FilteredExists()
    {
        return (await Connection.QueryAsync<UserIdNameDto>(FilteredExistsSql)).AsList();
    }

    [Benchmark]
    public async Task<List<UserIdNameDto>> EfCore_FilteredExists()
    {
        return await EfContext.Users.AsNoTracking()
            .Where(u => u.Orders.Any(o => o.Total > 50))
            .Select(u => new UserIdNameDto { UserId = u.UserId, UserName = u.UserName })
            .ToListAsync();
    }

    [Benchmark]
    public async Task<List<UserIdNameDto>> Quarry_FilteredExists()
    {
        return await QuarryDb.Users()
            .Where(u => u.Orders.Any(o => o.Total > 50))
            .Select(u => new UserIdNameDto
            {
                UserId = u.UserId,
                UserName = u.UserName
            })
            .ExecuteFetchAllAsync();
    }

    [Benchmark]
    public async Task<List<UserIdNameDto>> SqlKata_FilteredExists()
    {
        var query = new Query("users")
            .Select("UserId", "UserName")
            .WhereExists(q => q.From("orders")
                .WhereColumns("orders.UserId", "=", "users.UserId")
                .Where("Total", ">", 50)
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

    // ── Scalar COUNT subquery: users with more than 2 orders ──

    private const string CountSubquerySql =
        "SELECT UserId, UserName FROM users WHERE (SELECT COUNT(*) FROM orders WHERE orders.UserId = users.UserId) > 2";

    [Benchmark]
    public async Task<List<UserIdNameDto>> Raw_CountSubquery()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = CountSubquerySql;
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
    public async Task<List<UserIdNameDto>> Dapper_CountSubquery()
    {
        return (await Connection.QueryAsync<UserIdNameDto>(CountSubquerySql)).AsList();
    }

    [Benchmark]
    public async Task<List<UserIdNameDto>> EfCore_CountSubquery()
    {
        return await EfContext.Users.AsNoTracking()
            .Where(u => u.Orders.Count > 2)
            .Select(u => new UserIdNameDto { UserId = u.UserId, UserName = u.UserName })
            .ToListAsync();
    }

    [Benchmark]
    public async Task<List<UserIdNameDto>> Quarry_CountSubquery()
    {
        return await QuarryDb.Users()
            .Where(u => u.Orders.Count() > 2)
            .Select(u => new UserIdNameDto
            {
                UserId = u.UserId,
                UserName = u.UserName
            })
            .ExecuteFetchAllAsync();
    }

    [Benchmark]
    public async Task<List<UserIdNameDto>> SqlKata_CountSubquery()
    {
        var query = new Query("users")
            .Select("UserId", "UserName")
            .WhereRaw("(SELECT COUNT(*) FROM orders WHERE orders.UserId = users.UserId) > 2");
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

    // ── Aggregate SUM subquery: users whose total order value > 200 ──

    private const string SumSubquerySql =
        "SELECT UserId, UserName FROM users WHERE (SELECT SUM(Total) FROM orders WHERE orders.UserId = users.UserId) > 200";

    [Benchmark]
    public async Task<List<UserIdNameDto>> Raw_SumSubquery()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = SumSubquerySql;
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
    public async Task<List<UserIdNameDto>> Dapper_SumSubquery()
    {
        return (await Connection.QueryAsync<UserIdNameDto>(SumSubquerySql)).AsList();
    }

    [Benchmark]
    public async Task<List<UserIdNameDto>> EfCore_SumSubquery()
    {
        return await EfContext.Users.AsNoTracking()
            .Where(u => u.Orders.Sum(o => o.Total) > 200)
            .Select(u => new UserIdNameDto { UserId = u.UserId, UserName = u.UserName })
            .ToListAsync();
    }

    [Benchmark]
    public async Task<List<UserIdNameDto>> Quarry_SumSubquery()
    {
        return await QuarryDb.Users()
            .Where(u => u.Orders.Sum(o => o.Total) > 200)
            .Select(u => new UserIdNameDto
            {
                UserId = u.UserId,
                UserName = u.UserName
            })
            .ExecuteFetchAllAsync();
    }

    [Benchmark]
    public async Task<List<UserIdNameDto>> SqlKata_SumSubquery()
    {
        var query = new Query("users")
            .Select("UserId", "UserName")
            .WhereRaw("(SELECT SUM(Total) FROM orders WHERE orders.UserId = users.UserId) > 200");
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
