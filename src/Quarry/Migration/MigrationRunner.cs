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
    public static Task RunAsync(
        DbConnection connection,
        SqlDialect dialect,
        (int Version, string Name, Action<MigrationBuilder> Upgrade, Action<MigrationBuilder> Downgrade, Action<MigrationBuilder> Backup)[] migrations,
        MigrationOptions? options = null)
    {
        // Wrap legacy tuple format into extended format with SquashedFrom = 0
        var extended = new (int Version, string Name, Action<MigrationBuilder> Upgrade, Action<MigrationBuilder> Downgrade, Action<MigrationBuilder> Backup, int SquashedFrom)[migrations.Length];
        for (var i = 0; i < migrations.Length; i++)
        {
            var m = migrations[i];
            extended[i] = (m.Version, m.Name, m.Upgrade, m.Downgrade, m.Backup, 0);
        }
        return RunAsync(connection, dialect, extended, options);
    }

    /// <summary>
    /// Runs migrations against the database, with squash support.
    /// </summary>
    public static async Task RunAsync(
        DbConnection connection,
        SqlDialect dialect,
        (int Version, string Name, Action<MigrationBuilder> Upgrade, Action<MigrationBuilder> Downgrade, Action<MigrationBuilder> Backup, int SquashedFrom)[] migrations,
        MigrationOptions? options = null)
    {
        options ??= new MigrationOptions();
        var log = options.Logger ?? (_ => { });

        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        MigrationLog.EnsureHistoryTable();
        await EnsureHistoryTableAsync(connection, dialect, options);

        // Check for incomplete migrations from previous crashes
        var incomplete = await GetIncompleteMigrationsAsync(connection, dialect);
        if (incomplete.Count > 0)
        {
            foreach (var version in incomplete)
                MigrationLog.IncompleteDetected(version);

            if (!options.IgnoreIncomplete)
            {
                throw new InvalidOperationException(
                    $"Incomplete migration(s) detected with 'running' status: {string.Join(", ", incomplete)}. " +
                    "This indicates a previous crash mid-migration. Inspect the database state and set " +
                    "MigrationOptions.IgnoreIncomplete = true to proceed.");
            }
        }

        var appliedMap = await GetAppliedVersionsWithChecksumsAsync(connection, dialect, options);

        // When ignoring incomplete migrations, add them to appliedMap so they get skipped
        if (options.IgnoreIncomplete)
        {
            foreach (var version in incomplete)
            {
                if (!appliedMap.ContainsKey(version))
                    appliedMap[version] = "";
            }
        }
        MigrationLog.AppliedCount(appliedMap.Count);

        // Validate checksums for applied migrations (skip squash baselines that aren't applied)
        ValidateChecksumsExtended(migrations, appliedMap, dialect, options, log);

        if (options.LockTimeout.HasValue && dialect == SqlDialect.SQLite)
            MigrationLog.LockTimeoutSkippedSQLite();

        if (options.Direction == MigrationDirection.Upgrade)
        {
            var target = options.TargetVersion ?? int.MaxValue;
            foreach (var m in migrations)
            {
                if (m.Version > target) break;
                if (appliedMap.ContainsKey(m.Version))
                {
                    MigrationLog.Skipped(m.Version);
                    continue;
                }

                // Squash-aware skip: if this is a squash baseline, and the DB already has
                // any version in the squashed range applied, skip the baseline
                if (m.SquashedFrom > 0)
                {
                    var hasSquashedVersions = false;
                    foreach (var appliedVersion in appliedMap.Keys)
                    {
                        if (appliedVersion >= m.Version && appliedVersion <= m.SquashedFrom)
                        {
                            hasSquashedVersions = true;
                            break;
                        }
                    }

                    if (hasSquashedVersions)
                    {
                        MigrationLog.Skipped(m.Version);
                        log($"Skipping squash baseline {m.Version} — database already has migrations from the squashed range.");
                        continue;
                    }
                }

                MigrationLog.Applying(m.Version, m.Name);
                log($"Applying migration {m.Version}: {m.Name}...");
                await ApplyMigrationExtendedAsync(connection, dialect, m, options, log);
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
                if (!appliedMap.ContainsKey(m.Version)) continue;

                MigrationLog.RollingBack(m.Version, m.Name);
                log($"Rolling back migration {m.Version}: {m.Name}...");
                await RollbackMigrationExtendedAsync(connection, dialect, m, options, log);
                log($"Migration {m.Version} rolled back.");
            }
        }
    }

    private static void ValidateChecksumsExtended(
        (int Version, string Name, Action<MigrationBuilder> Upgrade, Action<MigrationBuilder> Downgrade, Action<MigrationBuilder> Backup, int SquashedFrom)[] migrations,
        Dictionary<int, string> appliedMap,
        SqlDialect dialect,
        MigrationOptions options,
        Action<string> log)
    {
        var legacy = new (int, string, Action<MigrationBuilder>, Action<MigrationBuilder>, Action<MigrationBuilder>)[migrations.Length];
        for (var i = 0; i < migrations.Length; i++)
        {
            var m = migrations[i];
            legacy[i] = (m.Version, m.Name, m.Upgrade, m.Downgrade, m.Backup);
        }
        ValidateChecksums(legacy, appliedMap, dialect, options, log);
    }

    private static Task ApplyMigrationExtendedAsync(
        DbConnection connection,
        SqlDialect dialect,
        (int Version, string Name, Action<MigrationBuilder> Upgrade, Action<MigrationBuilder> Downgrade, Action<MigrationBuilder> Backup, int SquashedFrom) migration,
        MigrationOptions options,
        Action<string> log)
    {
        return ApplyMigrationAsync(connection, dialect,
            (migration.Version, migration.Name, migration.Upgrade, migration.Downgrade, migration.Backup),
            options, log);
    }

    private static Task RollbackMigrationExtendedAsync(
        DbConnection connection,
        SqlDialect dialect,
        (int Version, string Name, Action<MigrationBuilder> Upgrade, Action<MigrationBuilder> Downgrade, Action<MigrationBuilder> Backup, int SquashedFrom) migration,
        MigrationOptions options,
        Action<string> log)
    {
        return RollbackMigrationAsync(connection, dialect,
            (migration.Version, migration.Name, migration.Upgrade, migration.Downgrade, migration.Backup),
            options, log);
    }

    private static void ValidateChecksums(
        (int Version, string Name, Action<MigrationBuilder> Upgrade, Action<MigrationBuilder> Downgrade, Action<MigrationBuilder> Backup)[] migrations,
        Dictionary<int, string> appliedMap,
        SqlDialect dialect,
        MigrationOptions options,
        Action<string> log)
    {
        foreach (var m in migrations)
        {
            if (!appliedMap.TryGetValue(m.Version, out var storedChecksum))
                continue;

            // Skip entries with empty checksums (e.g., incomplete migrations added via IgnoreIncomplete)
            if (string.IsNullOrEmpty(storedChecksum))
                continue;

            var builder = new MigrationBuilder();
            m.Upgrade(builder);
            var currentChecksum = ComputeChecksum(builder.BuildSql(dialect));

            if (currentChecksum == storedChecksum)
                continue;

            MigrationLog.ChecksumMismatch(m.Version, storedChecksum, currentChecksum);

            if (options.StrictChecksums)
            {
                throw new InvalidOperationException(
                    $"Migration {m.Version} ({m.Name}) checksum mismatch. " +
                    $"Stored: {storedChecksum}, Current: {currentChecksum}. " +
                    "The migration code has been modified after it was applied.");
            }

            log($"WARNING: Migration {m.Version} ({m.Name}) checksum mismatch — stored: {storedChecksum}, current: {currentChecksum}");
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

        var hasNonTx = builder.HasNonTransactionalOperations(dialect);
        var (txSql, nonTxSql, allSql) = builder.BuildPartitionedSql(dialect, options.Idempotent);
        MigrationLog.SqlGenerated(migration.Version, allSql);

        if (hasNonTx)
        {
            MigrationLog.NonTransactionalWarning(migration.Version);
            MigrationLog.NonTransactionalSqlGenerated(migration.Version, nonTxSql);
        }

        await WarnLargeTablesAsync(connection, dialect, builder.GetOperations(), options, log);

        if (options.DryRun)
        {
            MigrationLog.DryRun(migration.Version);
            log(allSql);
            return;
        }

        if (options.BeforeEach != null)
            await options.BeforeEach(migration.Version, migration.Name, connection);

        // Insert 'running' status before executing DDL
        // Checksum always computed from non-idempotent SQL for stability across mode changes
        var checksum = ComputeChecksum(builder.BuildSql(dialect));
        await InsertHistoryRowAsync(connection, null, dialect, migration.Version, migration.Name, checksum, 0, "running", options);
        MigrationLog.StatusUpdated(migration.Version, "running");

        var sw = Stopwatch.StartNew();

        // Phase 1: Transactional operations (including backups and history row)
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
                    ApplyCommandTimeout(backupCmd, options);
                    await backupCmd.ExecuteNonQueryAsync();
                }
            }

            await EmitLockTimeoutAsync(connection, tx, dialect, options);

            if (!string.IsNullOrEmpty(txSql))
            {
                using var cmd = connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = txSql;
                ApplyCommandTimeout(cmd, options);
                await cmd.ExecuteNonQueryAsync();
            }

            // Update status to 'applied'
            await UpdateHistoryStatusAsync(connection, tx, dialect, migration.Version, "applied", (int)sw.ElapsedMilliseconds, options);
            MigrationLog.StatusUpdated(migration.Version, "applied");

            await tx.CommitAsync();

            if (options.AfterEach != null)
                await options.AfterEach(migration.Version, migration.Name, sw.Elapsed, connection);
        }
        catch (Exception ex)
        {
            MigrationLog.Failed(migration.Version, migration.Name, "upgrade", ex);

            if (options.OnError != null)
            {
                try { await options.OnError(migration.Version, migration.Name, ex, connection); }
                catch (Exception hookEx)
                {
                    MigrationLog.Failed(migration.Version, migration.Name, "OnError hook", hookEx);
                }
            }

            await tx.RollbackAsync();
            // Clean up the 'running' row since the transaction failed
            try { await DeleteHistoryRowAsync(connection, null, dialect, migration.Version, options); }
            catch (Exception cleanupEx)
            {
                MigrationLog.Failed(migration.Version, migration.Name, "cleanup", cleanupEx);
            }
            throw new InvalidOperationException(
                $"Migration {migration.Version} ({migration.Name}) failed during upgrade (transactional phase). SQL: {txSql}", ex);
        }

        // Phase 2: Non-transactional operations (executed outside any transaction)
        if (!string.IsNullOrEmpty(nonTxSql))
        {
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = nonTxSql;
                ApplyCommandTimeout(cmd, options);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                MigrationLog.Failed(migration.Version, migration.Name, "upgrade (non-transactional)", ex);
                throw new InvalidOperationException(
                    $"Migration {migration.Version} ({migration.Name}) failed during upgrade (non-transactional phase). " +
                    $"Transactional operations have already been committed and cannot be rolled back. SQL: {nonTxSql}", ex);
            }
        }

        sw.Stop();
        MigrationLog.Applied(migration.Version, (int)sw.ElapsedMilliseconds);
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

        var hasNonTx = builder.HasNonTransactionalOperations(dialect);
        var (txSql, nonTxSql, allSql) = builder.BuildPartitionedSql(dialect, options.Idempotent);
        MigrationLog.SqlGenerated(migration.Version, allSql);

        if (hasNonTx)
            MigrationLog.NonTransactionalWarning(migration.Version);

        if (options.DryRun)
        {
            MigrationLog.DryRun(migration.Version);
            log(allSql);
            return;
        }

        if (options.BeforeEach != null)
            await options.BeforeEach(migration.Version, migration.Name, connection);

        var sw = Stopwatch.StartNew();

        // Phase 1: Non-transactional operations first during rollback
        // (reverse of apply order — suppressed ops were applied last, so drop them first)
        if (!string.IsNullOrEmpty(nonTxSql))
        {
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = nonTxSql;
                ApplyCommandTimeout(cmd, options);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                MigrationLog.Failed(migration.Version, migration.Name, "rollback (non-transactional)", ex);
                throw new InvalidOperationException(
                    $"Migration {migration.Version} ({migration.Name}) failed during rollback (non-transactional phase). SQL: {nonTxSql}", ex);
            }
        }

        // Phase 2: Transactional operations + history row removal
        using var tx = await connection.BeginTransactionAsync();
        try
        {
            await EmitLockTimeoutAsync(connection, tx, dialect, options);

            if (!string.IsNullOrEmpty(txSql))
            {
                using var cmd = connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = txSql;
                ApplyCommandTimeout(cmd, options);
                await cmd.ExecuteNonQueryAsync();
            }

            await DeleteHistoryRowAsync(connection, tx, dialect, migration.Version, options);

            sw.Stop();
            MigrationLog.RolledBack(migration.Version);
            await tx.CommitAsync();

            if (options.AfterEach != null)
                await options.AfterEach(migration.Version, migration.Name, sw.Elapsed, connection);
        }
        catch (Exception ex)
        {
            MigrationLog.Failed(migration.Version, migration.Name, "rollback", ex);

            if (options.OnError != null)
            {
                try { await options.OnError(migration.Version, migration.Name, ex, connection); }
                catch (Exception hookEx)
                {
                    MigrationLog.Failed(migration.Version, migration.Name, "OnError hook", hookEx);
                }
            }

            await tx.RollbackAsync();
            throw new InvalidOperationException(
                $"Migration {migration.Version} ({migration.Name}) failed during rollback. SQL: {txSql}", ex);
        }
    }

    private static async Task EnsureHistoryTableAsync(DbConnection connection, SqlDialect dialect, MigrationOptions options)
    {
        var sql = dialect switch
        {
            SqlDialect.SQLite => $@"CREATE TABLE IF NOT EXISTS {HistoryTable} (
                version INTEGER NOT NULL PRIMARY KEY,
                name TEXT NOT NULL,
                applied_at TEXT NOT NULL,
                checksum TEXT NOT NULL,
                execution_time_ms INTEGER NOT NULL,
                applied_by TEXT NOT NULL,
                started_at TEXT,
                status TEXT NOT NULL DEFAULT 'applied',
                squash_from INTEGER
            );",
            SqlDialect.PostgreSQL or SqlDialect.MySQL => $@"CREATE TABLE IF NOT EXISTS {HistoryTable} (
                version INT NOT NULL PRIMARY KEY,
                name VARCHAR(256) NOT NULL,
                applied_at TIMESTAMP NOT NULL,
                checksum VARCHAR(64) NOT NULL,
                execution_time_ms INT NOT NULL,
                applied_by VARCHAR(256) NOT NULL,
                started_at TIMESTAMP,
                status VARCHAR(20) NOT NULL DEFAULT 'applied',
                squash_from INT
            );",
            _ => $@"IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '{HistoryTable}')
            CREATE TABLE {HistoryTable} (
                version INT NOT NULL PRIMARY KEY,
                name VARCHAR(256) NOT NULL,
                applied_at DATETIME NOT NULL,
                checksum VARCHAR(64) NOT NULL,
                execution_time_ms INT NOT NULL,
                applied_by VARCHAR(256) NOT NULL,
                started_at DATETIME,
                status VARCHAR(20) NOT NULL DEFAULT 'applied',
                squash_from INT
            );"
        };

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        ApplyCommandTimeout(cmd, options);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<List<int>> GetIncompleteMigrationsAsync(DbConnection connection, SqlDialect dialect)
    {
        var versions = new List<int>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT version FROM {HistoryTable} WHERE status = 'running' ORDER BY version;";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            versions.Add(reader.GetInt32(0));
        }
        return versions;
    }

    private static async Task<Dictionary<int, string>> GetAppliedVersionsWithChecksumsAsync(DbConnection connection, SqlDialect dialect, MigrationOptions options)
    {
        var map = new Dictionary<int, string>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT version, checksum FROM {HistoryTable} WHERE status = 'applied' ORDER BY version;";
        ApplyCommandTimeout(cmd, options);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            map[reader.GetInt32(0)] = reader.IsDBNull(1) ? "" : reader.GetString(1);
        }
        return map;
    }

    private static async Task InsertHistoryRowAsync(
        DbConnection connection, DbTransaction? tx, SqlDialect dialect,
        int version, string name, string checksum, int executionTimeMs, string status, MigrationOptions options)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $@"INSERT INTO {HistoryTable} (version, name, applied_at, checksum, execution_time_ms, applied_by, started_at, status)
            VALUES ({SqlFormatting.FormatParameter(dialect, 0)}, {SqlFormatting.FormatParameter(dialect, 1)}, {SqlFormatting.FormatParameter(dialect, 2)}, {SqlFormatting.FormatParameter(dialect, 3)}, {SqlFormatting.FormatParameter(dialect, 4)}, {SqlFormatting.FormatParameter(dialect, 5)}, {SqlFormatting.FormatParameter(dialect, 6)}, {SqlFormatting.FormatParameter(dialect, 7)});";

        // Pass DateTime (not a string). On SQLite the TEXT `applied_at` /
        // `started_at` columns accept either; on PostgreSQL the columns are
        // TIMESTAMP and require the provider to bind a real DateTime value
        // so Npgsql can send the correct wire type. SqlServer / MySQL
        // behave like PostgreSQL here (DATETIME / TIMESTAMP).
        var now = DateTime.UtcNow;
        AddParameter(cmd, dialect, 0, version);
        AddParameter(cmd, dialect, 1, name);
        AddParameter(cmd, dialect, 2, now);
        AddParameter(cmd, dialect, 3, checksum);
        AddParameter(cmd, dialect, 4, executionTimeMs);
        AddParameter(cmd, dialect, 5, $"{Environment.MachineName}/{Environment.UserName}");
        AddParameter(cmd, dialect, 6, now);
        AddParameter(cmd, dialect, 7, status);

        ApplyCommandTimeout(cmd, options);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task UpdateHistoryStatusAsync(
        DbConnection connection, DbTransaction? tx, SqlDialect dialect,
        int version, string status, int executionTimeMs, MigrationOptions options)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $@"UPDATE {HistoryTable}
            SET status = {SqlFormatting.FormatParameter(dialect, 0)},
                execution_time_ms = {SqlFormatting.FormatParameter(dialect, 1)}
            WHERE version = {SqlFormatting.FormatParameter(dialect, 2)};";

        AddParameter(cmd, dialect, 0, status);
        AddParameter(cmd, dialect, 1, executionTimeMs);
        AddParameter(cmd, dialect, 2, version);

        ApplyCommandTimeout(cmd, options);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task DeleteHistoryRowAsync(
        DbConnection connection, DbTransaction? tx, SqlDialect dialect, int version, MigrationOptions options)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"DELETE FROM {HistoryTable} WHERE version = {SqlFormatting.FormatParameter(dialect, 0)};";
        AddParameter(cmd, dialect, 0, version);
        ApplyCommandTimeout(cmd, options);
        await cmd.ExecuteNonQueryAsync();
    }

    private static void AddParameter(DbCommand cmd, SqlDialect dialect, int index, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = SqlFormatting.GetParameterName(dialect, index);
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    private static void ApplyCommandTimeout(DbCommand cmd, MigrationOptions options)
    {
        if (options.CommandTimeout.HasValue)
            cmd.CommandTimeout = (int)options.CommandTimeout.Value.TotalSeconds;
    }

    private static async Task EmitLockTimeoutAsync(
        DbConnection connection, DbTransaction tx, SqlDialect dialect, MigrationOptions options)
    {
        var sql = GetLockTimeoutSql(dialect, options);
        if (sql == null)
            return;

        MigrationLog.LockTimeoutEmitted(sql);
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        ApplyCommandTimeout(cmd, options);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Returns the dialect-specific SET command for lock timeout, or null if not applicable.
    /// </summary>
    internal static string? GetLockTimeoutSql(SqlDialect dialect, MigrationOptions options)
    {
        if (!options.LockTimeout.HasValue || dialect == SqlDialect.SQLite)
            return null;

        var timeout = options.LockTimeout.Value;
        return dialect switch
        {
            SqlDialect.SqlServer => $"SET LOCK_TIMEOUT {(int)timeout.TotalMilliseconds};",
            SqlDialect.PostgreSQL => $"SET statement_timeout = '{(int)timeout.TotalMilliseconds}ms';",
            SqlDialect.MySQL => $"SET innodb_lock_wait_timeout = {(int)timeout.TotalSeconds};",
            _ => throw new NotSupportedException($"LockTimeout is not supported for dialect {dialect}.")
        };
    }

    /// <summary>
    /// Extracts unique table names from operations that perform DDL on existing tables.
    /// CreateTable and RawSql are excluded since they don't touch existing data.
    /// </summary>
    internal static HashSet<string> GetAffectedTableNames(IReadOnlyList<MigrationOperation> operations)
    {
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var op in operations)
        {
            var name = op switch
            {
                DropTableOperation o => o.Name,
                RenameTableOperation o => o.OldName,
                AddColumnOperation o => o.Table,
                DropColumnOperation o => o.Table,
                RenameColumnOperation o => o.Table,
                AlterColumnOperation o => o.Table,
                AddForeignKeyOperation o => o.Table,
                DropForeignKeyOperation o => o.Table,
                AddIndexOperation o => o.Table,
                DropIndexOperation o => o.Table,
                _ => null
            };
            if (name != null)
                tables.Add(name);
        }
        return tables;
    }

    /// <summary>
    /// Returns the dialect-specific catalog query for estimated row count.
    /// The query must accept a single parameter (table name) at index 0.
    /// Returns null for SQLite (no catalog statistics).
    /// </summary>
    internal static string? GetEstimatedRowCountSql(SqlDialect dialect)
    {
        return dialect switch
        {
            SqlDialect.SqlServer =>
                "SELECT SUM(p.rows) FROM sys.partitions p " +
                "JOIN sys.tables t ON p.object_id = t.object_id " +
                $"WHERE t.name = {SqlFormatting.FormatParameter(SqlDialect.SqlServer, 0)} AND p.index_id IN (0, 1);",
            SqlDialect.PostgreSQL =>
                $"SELECT reltuples::bigint FROM pg_class WHERE relname = {SqlFormatting.FormatParameter(SqlDialect.PostgreSQL, 0)};",
            SqlDialect.MySQL =>
                $"SELECT table_rows FROM information_schema.tables WHERE table_name = {SqlFormatting.FormatParameter(SqlDialect.MySQL, 0)} AND table_schema = DATABASE();",
            _ => null
        };
    }

    private static async Task WarnLargeTablesAsync(
        DbConnection connection,
        SqlDialect dialect,
        IReadOnlyList<MigrationOperation> operations,
        MigrationOptions options,
        Action<string> log)
    {
        if (!options.WarnOnLargeTable)
            return;

        if (dialect == SqlDialect.SQLite)
        {
            MigrationLog.LargeTableSkippedSQLite();
            return;
        }

        var tables = GetAffectedTableNames(operations);
        if (tables.Count == 0)
            return;

        var querySql = GetEstimatedRowCountSql(dialect)!;

        foreach (var table in tables)
        {
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = querySql;
                AddParameter(cmd, dialect, 0, table);
                ApplyCommandTimeout(cmd, options);

                var result = await cmd.ExecuteScalarAsync();
                if (result is null or DBNull)
                    continue;

                var estimatedRows = Convert.ToInt64(result);
                if (estimatedRows > options.LargeTableThreshold)
                {
                    MigrationLog.LargeTableWarning(table, estimatedRows);
                    log($"WARNING: Table '{table}' has ~{estimatedRows} rows. ALTER TABLE may take a long time and acquire locks.");
                }
            }
            catch
            {
                // Catalog query failed — swallow and continue (best-effort warning)
            }
        }
    }

    internal static string ComputeChecksum(string sql)
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
