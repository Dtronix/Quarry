using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quarry;
using Quarry.Benchmarks.Infrastructure;
using SqlKata;
using SqlKata.Compilers;

namespace Quarry.Benchmarks.Benchmarks;

/// <summary>
/// Measures dynamic query building with conditional WHERE/ORDER BY/LIMIT clauses.
/// This is Quarry's unique strength — bitmask dispatch vs runtime string building.
/// </summary>
public class ConditionalBranchBenchmarks : BenchmarkBase
{
    private bool _filterActive;
    private bool _sortByName;
    private bool _limitResults;

    public override void GlobalSetup()
    {
        base.GlobalSetup();
        _filterActive = true;
        _sortByName = true;
        _limitResults = true;
    }

    [Benchmark(Baseline = true)]
    public async Task<List<RawUser>> Raw_ConditionalQuery()
    {
        var sql = "SELECT UserId, UserName, Email, IsActive, CreatedAt, LastLogin FROM users";
        if (_filterActive)
            sql += " WHERE IsActive = 1";
        if (_sortByName)
            sql += " ORDER BY UserName";
        if (_limitResults)
            sql += " LIMIT 25";

        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = sql;
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
    public async Task<List<DapperUser>> Dapper_ConditionalQuery()
    {
        var sql = "SELECT UserId, UserName, Email, IsActive, CreatedAt, LastLogin FROM users";
        if (_filterActive)
            sql += " WHERE IsActive = 1";
        if (_sortByName)
            sql += " ORDER BY UserName";
        if (_limitResults)
            sql += " LIMIT 25";

        return (await Connection.QueryAsync<DapperUser>(sql)).AsList();
    }

    [Benchmark]
    public async Task<List<EfUser>> EfCore_ConditionalQuery()
    {
        IQueryable<EfUser> query = EfContext.Users.AsNoTracking();

        if (_filterActive)
            query = query.Where(u => u.IsActive);
        if (_sortByName)
            query = query.OrderBy(u => u.UserName);
        if (_limitResults)
            query = query.Take(25);

        return await query.ToListAsync();
    }

    [Benchmark]
    public async Task<List<User>> Quarry_ConditionalQuery()
    {
        IQueryBuilder<User, User> query = QuarryDb.Users()
            .Select(u => u);

        if (_filterActive)
            query = query.Where(u => u.IsActive);
        if (_sortByName)
            query = query.OrderBy(u => u.UserName);
        if (_limitResults)
            query = query.Limit(25);

        return await query.ExecuteFetchAllAsync();
    }

    [Benchmark]
    public async Task<List<SqlKataUser>> SqlKata_ConditionalQuery()
    {
        var query = new Query("users")
            .Select("UserId", "UserName", "Email", "IsActive", "CreatedAt", "LastLogin");

        if (_filterActive)
            query = query.Where("IsActive", true);
        if (_sortByName)
            query = query.OrderBy("UserName");
        if (_limitResults)
            query = query.Limit(25);

        var compiled = SqlKataCompiler.Compile(query);

        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = compiled.Sql;
        foreach (var binding in compiled.Bindings)
        {
            cmd.Parameters.AddWithValue($"@p{cmd.Parameters.Count}", binding);
        }
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
