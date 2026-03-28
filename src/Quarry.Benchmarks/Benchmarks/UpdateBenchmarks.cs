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

    // Use a field so the source generator cannot inline the value into the SQL string.
    // This forces Quarry to parameterize the query, matching what Raw/Dapper/SqlKata do.
    // Note: Quarry CAN inline compile-time constants (e.g. `.Where(u => u.UserId == 1)`)
    // which eliminates parameter allocation entirely — a unique strength of source generation.
    // Static to work around source generator bug: UnsafeAccessor emits StaticField
    // for all class-level fields. See handoff-bug.md for details.
    private static int _targetId;

    public override void GlobalSetup()
    {
        base.GlobalSetup();
        _targetId = 1;
    }

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
        cmd.Parameters.AddWithValue("@id", _targetId);
        return await cmd.ExecuteNonQueryAsync();
    }

    [Benchmark]
    public async Task<int> Dapper_UpdateSingleRow()
    {
        return await Connection.ExecuteAsync(
            "UPDATE users SET UserName = @UserName WHERE UserId = @UserId",
            new { UserName = "UpdatedUser", UserId = _targetId });
    }

    [Benchmark]
    public async Task<int> EfCore_UpdateSingleRow()
    {
        return await _iterationEfContext.Users
            .Where(u => u.UserId == _targetId)
            .ExecuteUpdateAsync(u => u
                .SetProperty(x => x.UserName, "UpdatedUser"));
    }

    [Benchmark]
    public async Task<int> Quarry_UpdateSingleRow()
    {
        return await QuarryDb.Users()
            .Update()
            .Set(u => u.UserName = "UpdatedUser")
            .Where(u => u.UserId == _targetId)
            .ExecuteNonQueryAsync();
    }

    [Benchmark]
    public async Task<int> SqlKata_UpdateSingleRow()
    {
        var query = new Query("users").Where("UserId", _targetId).AsUpdate(new { UserName = "UpdatedUser" });
        var compiled = SqlKataCompiler.Compile(query);
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = compiled.Sql;
        foreach (var binding in compiled.Bindings)
            cmd.Parameters.AddWithValue($"@p{cmd.Parameters.Count}", binding);
        return await cmd.ExecuteNonQueryAsync();
    }
}
