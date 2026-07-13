using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Quarry.Migration;
using Quarry.Shared.Sql;
using Quarry.Tool.Schema;

namespace Quarry.Tests.Migration;

/// <summary>
/// Tests for <see cref="MigrationHistoryWriter"/> (step 4): ensuring the history table
/// exists and marking a migration applied without executing its DDL.
/// </summary>
[TestFixture]
public class MigrationHistoryWriterTests
{
    private string _dbPath = null!;
    private string _connectionString = null!;

    [SetUp]
    public void SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"quarry_hist_{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_dbPath}";
    }

    [TearDown]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    [Test]
    public async Task EnsureHistoryTableAsync_CreatesTableOnFreshDb_AndIsIdempotent()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await MigrationHistoryWriter.EnsureHistoryTableAsync(conn, SqlDialect.SQLite);
        await MigrationHistoryWriter.EnsureHistoryTableAsync(conn, SqlDialect.SQLite); // idempotent

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='__quarry_migrations';";
        var result = await cmd.ExecuteScalarAsync();
        Assert.That(result, Is.EqualTo("__quarry_migrations"));
    }

    [Test]
    public async Task MarkAppliedAsync_InsertsAppliedRow()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await MigrationHistoryWriter.EnsureHistoryTableAsync(conn, SqlDialect.SQLite);

        await MigrationHistoryWriter.MarkAppliedAsync(conn, SqlDialect.SQLite, 3, "InitialCreate", "deadbeefcafef00d");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT version, name, checksum, status FROM __quarry_migrations WHERE version = 3;";
        using var reader = await cmd.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(3));
        Assert.That(reader.GetString(1), Is.EqualTo("InitialCreate"));
        Assert.That(reader.GetString(2), Is.EqualTo("deadbeefcafef00d"));
        Assert.That(reader.GetString(3), Is.EqualTo("applied"));
    }

    [Test]
    public async Task MarkAppliedAsync_Checksum_MatchesRuntimeComputeChecksum()
    {
        // A baseline row's checksum must equal what the runtime recomputes so strict
        // checksum validation passes. ComputeChecksum is the runtime's FNV-1a hash.
        const string sql = "CREATE TABLE users (id INTEGER PRIMARY KEY);";
        var checksum = MigrationRunner.ComputeChecksum(sql);

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await MigrationHistoryWriter.EnsureHistoryTableAsync(conn, SqlDialect.SQLite);
        await MigrationHistoryWriter.MarkAppliedAsync(conn, SqlDialect.SQLite, 1, "InitialCreate", checksum);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT checksum FROM __quarry_migrations WHERE version = 1;";
        var stored = (string?)await cmd.ExecuteScalarAsync();
        Assert.That(stored, Is.EqualTo(checksum));
        Assert.That(stored, Has.Length.EqualTo(16)); // FNV-1a rendered as 16 hex chars
    }

    [Test]
    public async Task MarkAppliedAsync_SquashFrom_WritesSquashBaselineRow()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await MigrationHistoryWriter.EnsureHistoryTableAsync(conn, SqlDialect.SQLite);

        await MigrationHistoryWriter.MarkAppliedAsync(conn, SqlDialect.SQLite, 1, "Baseline", "squashed", squashFrom: 5);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT squash_from, status FROM __quarry_migrations WHERE version = 1;";
        using var reader = await cmd.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(5));
        Assert.That(reader.GetString(1), Is.EqualTo("applied"));
    }
}
