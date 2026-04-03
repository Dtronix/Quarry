using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quarry.Benchmarks.Infrastructure;
using SqlKata;
using SqlKata.Compilers;

namespace Quarry.Benchmarks.Benchmarks;

public class DeleteBenchmarks : BenchmarkBase
{
    private EfBenchContext _iterationEfContext = null!;

    // Use a field so the source generator cannot inline the value into the SQL string.
    // This forces Quarry to parameterize the query, matching what Raw/Dapper/SqlKata do.
    // Note: Quarry CAN inline compile-time constants (e.g. `.Where(u => u.UserId == 999)`)
    // which eliminates parameter allocation entirely — a unique strength of source generation.
    // Static to work around source generator bug: UnsafeAccessor emits StaticField
    // for all class-level fields. See handoff-bug.md for details.
    private static int _targetId;

    public override void GlobalSetup()
    {
        base.GlobalSetup();
        _targetId = 999;
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _iterationEfContext = CreateEfContext();
        // Ensure the target row exists before each iteration
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO users (UserId, UserName, Email, IsActive, CreatedAt)
            VALUES (999, 'DeleteMe', 'delete@example.com', 1, datetime('now'))
            """;
        cmd.ExecuteNonQuery();
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        _iterationEfContext?.Dispose();
    }

    // --- Single Row Delete ---

    [Benchmark(Baseline = true)]
    public async Task<int> Raw_DeleteSingleRow()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM users WHERE UserId = @id";
        cmd.Parameters.AddWithValue("@id", _targetId);
        return await cmd.ExecuteNonQueryAsync();
    }

    [Benchmark]
    public async Task<int> Dapper_DeleteSingleRow()
    {
        return await Connection.ExecuteAsync(
            "DELETE FROM users WHERE UserId = @UserId",
            new { UserId = _targetId });
    }

    [Benchmark]
    public async Task<int> EfCore_DeleteSingleRow()
    {
        return await _iterationEfContext.Users
            .Where(u => u.UserId == _targetId)
            .ExecuteDeleteAsync();
    }

    [Benchmark]
    public async Task<int> Quarry_DeleteSingleRow()
    {
        // Uses field _targetId → parameterized WHERE (@p0).
        // This is the apples-to-apples comparison against Raw's parameterized @id.
        return await QuarryDb.Users()
            .Delete()
            .Where(u => u.UserId == _targetId)
            .ExecuteNonQueryAsync();
    }

    [Benchmark]
    public async Task<int> Quarry_DeleteSingleRow_Inlined()
    {
        // Constant 999 is inlined into SQL by the source generator (no DbParameter).
        // Demonstrates Quarry's compile-time constant elimination.
        return await QuarryDb.Users()
            .Delete()
            .Where(u => u.UserId == 999)
            .ExecuteNonQueryAsync();
    }

    [Benchmark]
    public async Task<int> SqlKata_DeleteSingleRow()
    {
        var query = new Query("users").Where("UserId", _targetId).AsDelete();
        var compiled = SqlKataCompiler.Compile(query);
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = compiled.Sql;
        foreach (var binding in compiled.Bindings)
            cmd.Parameters.AddWithValue($"@p{cmd.Parameters.Count}", binding);
        return await cmd.ExecuteNonQueryAsync();
    }
}
