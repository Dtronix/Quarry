using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Quarry.Benchmarks.Context;
using Quarry.Benchmarks.Infrastructure;
using SqlKata;

namespace Quarry.Benchmarks.Benchmarks;

/// <summary>
/// Variable-column UPDATE — the SET column list is decided at runtime by caller
/// flags rather than at the call site. Patch's headline use case: the only
/// <c>Set</c> overload that can express it. Every other library has to build
/// SQL or expression trees dynamically.
/// </summary>
/// <remarks>
/// Two benchmark categories — <c>OneColumn</c> and <c>AllColumns</c> — each with
/// their own <c>Raw_*</c> baseline. Comparing <c>Quarry_AllColumns</c> against the
/// <c>OneColumn</c> baseline would be meaningless (different SQL shapes); the
/// category split gives one ratio column per scenario.
/// </remarks>
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class PatchUpdateBenchmarks : BenchmarkBase
{
    private const string OneColumnCategory = "OneColumn";
    private const string AllColumnsCategory = "AllColumns";


    private EfBenchContext _iterationEfContext = null!;

    // Static to work around source generator bug: UnsafeAccessor emits StaticField
    // for all class-level fields. See UpdateBenchmarks for the original reference.
    private static int _targetId;

    // Flag fields drive the conditional setters. Set to true in GlobalSetup so
    // every iteration takes the same branch (matches ConditionalBranchBenchmarks),
    // but field reads can't be constant-folded — what we're measuring is the cost
    // of code shaped like a runtime-conditional patch, not actual flag variation.
    private static bool _setName, _setEmail, _setActive, _setLastLogin;

    private const string NewName = "Updated";
    private const string NewEmail = "updated@example.com";

    public override void GlobalSetup()
    {
        base.GlobalSetup();
        _targetId = 1;
        _setName = true;
        _setEmail = true;
        _setActive = true;
        _setLastLogin = true;
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
        // Reset row 1 back to its seed values (DatabaseSetup.cs).
        // AllColumns benchmarks mutate four columns, so a full reset is required.
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = "UPDATE users SET UserName = 'User001', Email = 'user001@example.com', IsActive = 1, LastLogin = NULL WHERE UserId = 1";
        cmd.ExecuteNonQuery();
    }

    // --- One column potentially set (only _setName checked) ---

    [Benchmark(Baseline = true), BenchmarkCategory(OneColumnCategory)]
    public async Task<int> Raw_OneColumn()
    {
        if (!_setName) return 0;
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = "UPDATE users SET UserName = @name WHERE UserId = @id";
        cmd.Parameters.AddWithValue("@name", NewName);
        cmd.Parameters.AddWithValue("@id", _targetId);
        return await cmd.ExecuteNonQueryAsync();
    }

    [Benchmark, BenchmarkCategory(OneColumnCategory)]
    public async Task<int> Dapper_OneColumn()
    {
        if (!_setName) return 0;
        return await Connection.ExecuteAsync(
            "UPDATE users SET UserName = @UserName WHERE UserId = @UserId",
            new { UserName = NewName, UserId = _targetId });
    }

    [Benchmark, BenchmarkCategory(OneColumnCategory)]
    public async Task<int> EfCore_OneColumn()
    {
        // EF has no clean runtime-variable SetProperty story without hand-building
        // an Expression<Func<SetPropertyCalls<T>, SetPropertyCalls<T>>> tree, which
        // is not how EF developers write patches in practice. Load-mutate-save is
        // the real EF idiom — costs 2 round-trips (SELECT + UPDATE) per call.
        var user = await _iterationEfContext.Users.FirstAsync(u => u.UserId == _targetId);
        if (_setName) user.UserName = NewName;
        return await _iterationEfContext.SaveChangesAsync();
    }

    [Benchmark, BenchmarkCategory(OneColumnCategory)]
    public async Task<int> Quarry_OneColumn()
    {
        return await QuarryDb.Users()
            .Update()
            .Set((ref User.Patch p) =>
            {
                if (_setName) p.UserName = NewName;
            })
            .Where(u => u.UserId == _targetId)
            .ExecuteNonQueryAsync();
    }

    [Benchmark, BenchmarkCategory(OneColumnCategory)]
    public async Task<int> SqlKata_OneColumn()
    {
        if (!_setName) return 0;
        var query = new Query("users")
            .Where("UserId", _targetId)
            .AsUpdate(new { UserName = NewName });
        var compiled = SqlKataCompiler.Compile(query);
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = compiled.Sql;
        foreach (var binding in compiled.Bindings)
            cmd.Parameters.AddWithValue($"@p{cmd.Parameters.Count}", binding);
        return await cmd.ExecuteNonQueryAsync();
    }

    // --- All four columns potentially set ---

    [Benchmark(Baseline = true), BenchmarkCategory(AllColumnsCategory)]
    public async Task<int> Raw_AllColumns()
    {
        var sb = new StringBuilder("UPDATE users SET ");
        await using var cmd = Connection.CreateCommand();
        bool first = true;
        if (_setName)
        {
            sb.Append("UserName = @name");
            cmd.Parameters.AddWithValue("@name", NewName);
            first = false;
        }
        if (_setEmail)
        {
            if (!first) sb.Append(", ");
            sb.Append("Email = @email");
            cmd.Parameters.AddWithValue("@email", NewEmail);
            first = false;
        }
        if (_setActive)
        {
            if (!first) sb.Append(", ");
            sb.Append("IsActive = @active");
            cmd.Parameters.AddWithValue("@active", 1);
            first = false;
        }
        if (_setLastLogin)
        {
            if (!first) sb.Append(", ");
            sb.Append("LastLogin = @last");
            cmd.Parameters.AddWithValue("@last", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
        }
        sb.Append(" WHERE UserId = @id");
        cmd.Parameters.AddWithValue("@id", _targetId);
        cmd.CommandText = sb.ToString();
        return await cmd.ExecuteNonQueryAsync();
    }

    [Benchmark, BenchmarkCategory(AllColumnsCategory)]
    public async Task<int> Dapper_AllColumns()
    {
        var sb = new StringBuilder("UPDATE users SET ");
        var p = new DynamicParameters();
        bool first = true;
        if (_setName)
        {
            sb.Append("UserName = @UserName");
            p.Add("UserName", NewName);
            first = false;
        }
        if (_setEmail)
        {
            if (!first) sb.Append(", ");
            sb.Append("Email = @Email");
            p.Add("Email", NewEmail);
            first = false;
        }
        if (_setActive)
        {
            if (!first) sb.Append(", ");
            sb.Append("IsActive = @IsActive");
            p.Add("IsActive", true);
            first = false;
        }
        if (_setLastLogin)
        {
            if (!first) sb.Append(", ");
            sb.Append("LastLogin = @LastLogin");
            p.Add("LastLogin", DateTime.UtcNow);
        }
        sb.Append(" WHERE UserId = @UserId");
        p.Add("UserId", _targetId);
        return await Connection.ExecuteAsync(sb.ToString(), p);
    }

    [Benchmark, BenchmarkCategory(AllColumnsCategory)]
    public async Task<int> EfCore_AllColumns()
    {
        // Same idiom as EfCore_OneColumn — load-mutate-save, 2 round-trips.
        var user = await _iterationEfContext.Users.FirstAsync(u => u.UserId == _targetId);
        if (_setName) user.UserName = NewName;
        if (_setEmail) user.Email = NewEmail;
        if (_setActive) user.IsActive = true;
        if (_setLastLogin) user.LastLogin = DateTime.UtcNow;
        return await _iterationEfContext.SaveChangesAsync();
    }

    [Benchmark, BenchmarkCategory(AllColumnsCategory)]
    public async Task<int> Quarry_AllColumns()
    {
        return await QuarryDb.Users()
            .Update()
            .Set((ref User.Patch p) =>
            {
                if (_setName) p.UserName = NewName;
                if (_setEmail) p.Email = NewEmail;
                if (_setActive) p.IsActive = true;
                if (_setLastLogin) p.LastLogin = DateTime.UtcNow;
            })
            .Where(u => u.UserId == _targetId)
            .ExecuteNonQueryAsync();
    }

    [Benchmark, BenchmarkCategory(AllColumnsCategory)]
    public async Task<int> SqlKata_AllColumns()
    {
        var values = new Dictionary<string, object>();
        if (_setName) values["UserName"] = NewName;
        if (_setEmail) values["Email"] = NewEmail;
        if (_setActive) values["IsActive"] = 1;
        if (_setLastLogin) values["LastLogin"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var query = new Query("users").Where("UserId", _targetId).AsUpdate(values);
        var compiled = SqlKataCompiler.Compile(query);
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = compiled.Sql;
        foreach (var binding in compiled.Bindings)
            cmd.Parameters.AddWithValue($"@p{cmd.Parameters.Count}", binding);
        return await cmd.ExecuteNonQueryAsync();
    }
}
