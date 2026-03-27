using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quarry.Benchmarks.Infrastructure;
using SqlKata;
using SqlKata.Compilers;

namespace Quarry.Benchmarks.Benchmarks;

public class UpdateBenchmarks : BenchmarkBase
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
        // Reset the row we modified
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = "UPDATE users SET UserName = 'User001' WHERE UserId = 1";
        cmd.ExecuteNonQuery();
    }

    // --- Single Row Update ---

    [Benchmark(Baseline = true)]
    public async Task<int> Raw_UpdateSingleRow()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = "UPDATE users SET UserName = @name WHERE UserId = @id";
        cmd.Parameters.AddWithValue("@name", "UpdatedUser");
        cmd.Parameters.AddWithValue("@id", 1);
        return await cmd.ExecuteNonQueryAsync();
    }

    [Benchmark]
    public async Task<int> Dapper_UpdateSingleRow()
    {
        return await Connection.ExecuteAsync(
            "UPDATE users SET UserName = @UserName WHERE UserId = @UserId",
            new { UserName = "UpdatedUser", UserId = 1 });
    }

    [Benchmark]
    public async Task<int> EfCore_UpdateSingleRow()
    {
        var user = await _iterationEfContext.Users.FindAsync(1);
        user!.UserName = "UpdatedUser";
        return await _iterationEfContext.SaveChangesAsync();
    }

    [Benchmark]
    public async Task<int> Quarry_UpdateSingleRow()
    {
        return await QuarryDb.Users()
            .Update()
            .Set(u => u.UserName = "UpdatedUser")
            .Where(u => u.UserId == 1)
            .ExecuteNonQueryAsync();
    }

    [Benchmark]
    public async Task<int> SqlKata_UpdateSingleRow()
    {
        var query = new Query("users").Where("UserId", 1).AsUpdate(new { UserName = "UpdatedUser" });
        var compiled = SqlKataCompiler.Compile(query);
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = compiled.Sql;
        foreach (var binding in compiled.Bindings)
            cmd.Parameters.AddWithValue($"@p{cmd.Parameters.Count}", binding);
        return await cmd.ExecuteNonQueryAsync();
    }
}
