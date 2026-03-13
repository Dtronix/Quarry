using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Threading.Tasks;
using Quarry.Logging;
using Quarry.Shared.Sql;

namespace Quarry.Migration;

/// <summary>
/// Executes migrations against a database connection.
/// </summary>
public static class MigrationRunner
{
    private const string HistoryTable = "__quarry_migrations";

    /// <summary>
    /// Runs migrations against the database.
    /// </summary>
    public static async Task RunAsync(
        DbConnection connection,
        SqlDialect dialect,
        (int Version, string Name, Action<MigrationBuilder> Upgrade, Action<MigrationBuilder> Downgrade, Action<MigrationBuilder> Backup)[] migrations,
        MigrationOptions? options = null)
    {
        options ??= new MigrationOptions();
        var log = options.Logger ?? (_ => { });

        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        MigrationLog.EnsureHistoryTable();
        await EnsureHistoryTableAsync(connection, dialect);
        var applied = await GetAppliedVersionsAsync(connection, dialect);
        MigrationLog.AppliedCount(applied.Count);

        if (options.Direction == MigrationDirection.Upgrade)
        {
            var target = options.TargetVersion ?? int.MaxValue;
            foreach (var m in migrations)
            {
                if (m.Version > target) break;
                if (applied.Contains(m.Version))
                {
                    MigrationLog.Skipped(m.Version);
                    continue;
                }

                MigrationLog.Applying(m.Version, m.Name);
                log($"Applying migration {m.Version}: {m.Name}...");
                await ApplyMigrationAsync(connection, dialect, m, options, log);
                log($"Migration {m.Version} applied.");
            }
        }
        else // Downgrade
        {
            var target = options.TargetVersion ?? 0;
            for (var i = migrations.Length - 1; i >= 0; i--)
            {
                var m = migrations[i];
                if (m.Version <= target) break;
                if (!applied.Contains(m.Version)) continue;

                MigrationLog.RollingBack(m.Version, m.Name);
                log($"Rolling back migration {m.Version}: {m.Name}...");
                await RollbackMigrationAsync(connection, dialect, m, options, log);
                log($"Migration {m.Version} rolled back.");
            }
        }
    }

    private static async Task ApplyMigrationAsync(
        DbConnection connection,
        SqlDialect dialect,
        (int Version, string Name, Action<MigrationBuilder> Upgrade, Action<MigrationBuilder> Downgrade, Action<MigrationBuilder> Backup) migration,
        MigrationOptions options,
        Action<string> log)
    {
        var builder = new MigrationBuilder();
        migration.Upgrade(builder);
        var sql = builder.BuildSql(dialect);
        MigrationLog.SqlGenerated(migration.Version, sql);

        if (options.DryRun)
        {
            MigrationLog.DryRun(migration.Version);
            log(sql);
            return;
        }

        var sw = Stopwatch.StartNew();
        using var tx = await connection.BeginTransactionAsync();
        try
        {
            if (options.RunBackups)
            {
                var backupBuilder = new MigrationBuilder();
                migration.Backup(backupBuilder);
                var backupSql = backupBuilder.BuildSql(dialect);
                if (!string.IsNullOrWhiteSpace(backupSql))
                {
                    MigrationLog.BackupSqlGenerated(migration.Version, backupSql);
                    using var backupCmd = connection.CreateCommand();
                    backupCmd.Transaction = tx;
                    backupCmd.CommandText = backupSql;
                    await backupCmd.ExecuteNonQueryAsync();
                }
            }

            using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();

            sw.Stop();
            MigrationLog.Applied(migration.Version, (int)sw.ElapsedMilliseconds);
            var checksum = ComputeChecksum(sql);
            await InsertHistoryRowAsync(connection, tx, dialect, migration.Version, migration.Name, checksum, (int)sw.ElapsedMilliseconds);

            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            MigrationLog.Failed(migration.Version, migration.Name, "upgrade", ex);
            await tx.RollbackAsync();
            throw new InvalidOperationException(
                $"Migration {migration.Version} ({migration.Name}) failed during upgrade. SQL: {sql}", ex);
        }
    }

    private static async Task RollbackMigrationAsync(
        DbConnection connection,
        SqlDialect dialect,
        (int Version, string Name, Action<MigrationBuilder> Upgrade, Action<MigrationBuilder> Downgrade, Action<MigrationBuilder> Backup) migration,
        MigrationOptions options,
        Action<string> log)
    {
        var builder = new MigrationBuilder();
        migration.Downgrade(builder);
        var sql = builder.BuildSql(dialect);
        MigrationLog.SqlGenerated(migration.Version, sql);

        if (options.DryRun)
        {
            MigrationLog.DryRun(migration.Version);
            log(sql);
            return;
        }

        var sw = Stopwatch.StartNew();
        using var tx = await connection.BeginTransactionAsync();
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();

            await DeleteHistoryRowAsync(connection, tx, dialect, migration.Version);

            sw.Stop();
            MigrationLog.RolledBack(migration.Version);
            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            MigrationLog.Failed(migration.Version, migration.Name, "rollback", ex);
            await tx.RollbackAsync();
            throw new InvalidOperationException(
                $"Migration {migration.Version} ({migration.Name}) failed during rollback. SQL: {sql}", ex);
        }
    }

    private static async Task EnsureHistoryTableAsync(DbConnection connection, SqlDialect dialect)
    {
        var sql = dialect == SqlDialect.SQLite
            ? $@"CREATE TABLE IF NOT EXISTS {HistoryTable} (
                version INTEGER NOT NULL PRIMARY KEY,
                name TEXT NOT NULL,
                applied_at TEXT NOT NULL,
                checksum TEXT NOT NULL,
                execution_time_ms INTEGER NOT NULL,
                applied_by TEXT NOT NULL
            );"
            : $@"IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '{HistoryTable}')
            CREATE TABLE {HistoryTable} (
                version INT NOT NULL PRIMARY KEY,
                name VARCHAR(256) NOT NULL,
                applied_at DATETIME NOT NULL,
                checksum VARCHAR(64) NOT NULL,
                execution_time_ms INT NOT NULL,
                applied_by VARCHAR(256) NOT NULL
            );";

        // For PostgreSQL/MySQL, adjust the IF NOT EXISTS syntax
        if (dialect == SqlDialect.PostgreSQL || dialect == SqlDialect.MySQL)
        {
            sql = $@"CREATE TABLE IF NOT EXISTS {HistoryTable} (
                version INT NOT NULL PRIMARY KEY,
                name VARCHAR(256) NOT NULL,
                applied_at TIMESTAMP NOT NULL,
                checksum VARCHAR(64) NOT NULL,
                execution_time_ms INT NOT NULL,
                applied_by VARCHAR(256) NOT NULL
            );";
        }

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<HashSet<int>> GetAppliedVersionsAsync(DbConnection connection, SqlDialect dialect)
    {
        var versions = new HashSet<int>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT version FROM {HistoryTable} ORDER BY version;";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            versions.Add(reader.GetInt32(0));
        }
        return versions;
    }

    private static async Task InsertHistoryRowAsync(
        DbConnection connection, DbTransaction tx, SqlDialect dialect,
        int version, string name, string checksum, int executionTimeMs)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $@"INSERT INTO {HistoryTable} (version, name, applied_at, checksum, execution_time_ms, applied_by)
            VALUES ({SqlFormatting.FormatParameter(dialect, 0)}, {SqlFormatting.FormatParameter(dialect, 1)}, {SqlFormatting.FormatParameter(dialect, 2)}, {SqlFormatting.FormatParameter(dialect, 3)}, {SqlFormatting.FormatParameter(dialect, 4)}, {SqlFormatting.FormatParameter(dialect, 5)});";

        AddParameter(cmd, dialect, 0, version);
        AddParameter(cmd, dialect, 1, name);
        AddParameter(cmd, dialect, 2, DateTime.UtcNow.ToString("o"));
        AddParameter(cmd, dialect, 3, checksum);
        AddParameter(cmd, dialect, 4, executionTimeMs);
        AddParameter(cmd, dialect, 5, $"{Environment.MachineName}/{Environment.UserName}");

        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task DeleteHistoryRowAsync(
        DbConnection connection, DbTransaction tx, SqlDialect dialect, int version)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"DELETE FROM {HistoryTable} WHERE version = {SqlFormatting.FormatParameter(dialect, 0)};";
        AddParameter(cmd, dialect, 0, version);
        await cmd.ExecuteNonQueryAsync();
    }

    private static void AddParameter(DbCommand cmd, SqlDialect dialect, int index, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = SqlFormatting.GetParameterName(dialect, index);
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    private static string ComputeChecksum(string sql)
    {
        // FNV-1a 64-bit hash for a lightweight checksum
        const ulong fnvOffset = 14695981039346656037UL;
        const ulong fnvPrime = 1099511628211UL;
        var hash = fnvOffset;
        for (var i = 0; i < sql.Length; i++)
        {
            hash ^= sql[i];
            hash *= fnvPrime;
        }
        return hash.ToString("x16");
    }
}
