using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;
using Quarry.Benchmarks.Infrastructure;

namespace Quarry.Benchmarks.Benchmarks;

/// <summary>
/// Measures the per-row overhead of IAsyncEnumerable vs direct buffered loop.
/// Both methods execute identical SQL and read identical columns — the only
/// difference is whether rows flow through an async state machine (yield return)
/// or are added directly to a list in a while loop.
/// </summary>
public class EnumerableOverheadBenchmarks : BenchmarkBase
{
    private const string Sql = "SELECT UserId, UserName, Email, IsActive, CreatedAt, LastLogin FROM users";

    private static RawUser ReadRow(SqliteDataReader reader) => new()
    {
        UserId = reader.GetInt32(0),
        UserName = reader.GetString(1),
        Email = reader.IsDBNull(2) ? null : reader.GetString(2),
        IsActive = reader.GetBoolean(3),
        CreatedAt = reader.GetDateTime(4),
        LastLogin = reader.IsDBNull(5) ? null : reader.GetDateTime(5)
    };

    // --- Baseline: direct buffered loop (current ExecuteFetchAllAsync path) ---

    [Benchmark(Baseline = true)]
    public async Task<List<RawUser>> Buffered_Direct()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = Sql;
        await using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<RawUser>();
        while (await reader.ReadAsync())
        {
            results.Add(ReadRow(reader));
        }
        return results;
    }

    // --- IAsyncEnumerable consumed via await foreach + ToList ---

    [Benchmark]
    public async Task<List<RawUser>> AsyncEnumerable_AwaitForeach()
    {
        var results = new List<RawUser>();
        await foreach (var item in StreamRows())
        {
            results.Add(item);
        }
        return results;
    }

    // --- IAsyncEnumerable consumed via ToListAsync (System.Linq.Async) ---

    [Benchmark]
    public async Task<List<RawUser>> AsyncEnumerable_ToListAsync()
    {
        return await StreamRows().ToListAsync();
    }

    private async IAsyncEnumerable<RawUser> StreamRows()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = Sql;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            yield return ReadRow(reader);
        }
    }
}
