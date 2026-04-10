using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SqlKata;
using SqlKata.Compilers;

namespace Quarry.Benchmarks.Benchmarks;

public class AggregateCountBenchmarks : BenchmarkBase
{
    [Benchmark(Baseline = true)]
    public async Task<int> Raw_Count()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM users";
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    [Benchmark]
    public async Task<int> Dapper_Count()
    {
        return await Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM users");
    }

    [Benchmark]
    public async Task<int> EfCore_Count()
    {
        return await EfContext.Users.AsNoTracking().CountAsync();
    }

    [Benchmark]
    public async Task<int> Quarry_Count()
    {
        return await QuarryDb.Users()
            .Select(u => Sql.Count())
            .ExecuteScalarAsync<int>();
    }

    [Benchmark]
    public async Task<int> SqlKata_Count()
    {
        var query = new Query("users").AsCount();
        var compiled = SqlKataCompiler.Compile(query);

        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = compiled.Sql;
        foreach (var binding in compiled.Bindings)
        {
            cmd.Parameters.AddWithValue($"@p{cmd.Parameters.Count}", binding);
        }
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }
}
