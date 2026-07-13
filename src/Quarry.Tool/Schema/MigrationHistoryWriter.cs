using System;
using System.Data.Common;
using System.Threading.Tasks;
using Quarry.Shared.Sql;

namespace Quarry.Tool.Schema;

/// <summary>
/// Writes rows to the <c>__quarry_migrations</c> history table from the CLI. Shared by
/// <c>migrate squash</c>, <c>migrate baseline</c>, and <c>migrate adopt</c> so a migration
/// can be recorded as already-applied without executing its DDL. The SQL mirrors the
/// runtime's history-table shape; the tool must ensure the table exists because — unlike
/// the runtime — it does not create it as a side effect of applying migrations.
/// </summary>
internal static class MigrationHistoryWriter
{
    private const string HistoryTable = "__quarry_migrations";

    /// <summary>
    /// Creates the migration-history table if it does not already exist. Mirrors the
    /// runtime's <c>EnsureHistoryTableAsync</c> DDL so the two agree on the schema.
    /// </summary>
    public static async Task EnsureHistoryTableAsync(DbConnection connection, SqlDialect dialect, DbTransaction? tx = null)
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
        if (tx != null) cmd.Transaction = tx;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Inserts a <c>status = 'applied'</c> history row for <paramref name="version"/> so
    /// <c>MigrateAsync</c> skips that migration instead of executing its DDL. When
    /// <paramref name="squashFrom"/> is set, the row is written as a squash baseline.
    /// </summary>
    public static async Task MarkAppliedAsync(
        DbConnection connection,
        SqlDialect dialect,
        int version,
        string name,
        string checksum,
        int? squashFrom = null,
        DbTransaction? tx = null)
    {
        using var cmd = connection.CreateCommand();
        if (tx != null) cmd.Transaction = tx;

        var appliedAt = DateTime.UtcNow.ToString("o");
        var appliedBy = $"{Environment.MachineName}/{Environment.UserName}";

        if (squashFrom.HasValue)
        {
            cmd.CommandText = $@"INSERT INTO {HistoryTable} (version, name, applied_at, checksum, execution_time_ms, applied_by, status, squash_from)
                VALUES ({P(dialect, 0)}, {P(dialect, 1)}, {P(dialect, 2)}, {P(dialect, 3)}, {P(dialect, 4)}, {P(dialect, 5)}, {P(dialect, 6)}, {P(dialect, 7)});";
            AddParameter(cmd, dialect, 0, version);
            AddParameter(cmd, dialect, 1, name);
            AddParameter(cmd, dialect, 2, appliedAt);
            AddParameter(cmd, dialect, 3, checksum);
            AddParameter(cmd, dialect, 4, 0);
            AddParameter(cmd, dialect, 5, appliedBy);
            AddParameter(cmd, dialect, 6, "applied");
            AddParameter(cmd, dialect, 7, squashFrom.Value);
        }
        else
        {
            cmd.CommandText = $@"INSERT INTO {HistoryTable} (version, name, applied_at, checksum, execution_time_ms, applied_by, status)
                VALUES ({P(dialect, 0)}, {P(dialect, 1)}, {P(dialect, 2)}, {P(dialect, 3)}, {P(dialect, 4)}, {P(dialect, 5)}, {P(dialect, 6)});";
            AddParameter(cmd, dialect, 0, version);
            AddParameter(cmd, dialect, 1, name);
            AddParameter(cmd, dialect, 2, appliedAt);
            AddParameter(cmd, dialect, 3, checksum);
            AddParameter(cmd, dialect, 4, 0);
            AddParameter(cmd, dialect, 5, appliedBy);
            AddParameter(cmd, dialect, 6, "applied");
        }

        await cmd.ExecuteNonQueryAsync();
    }

    private static string P(SqlDialect dialect, int index) => SqlFormatting.FormatParameter(dialect, index);

    private static void AddParameter(DbCommand cmd, SqlDialect dialect, int index, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = SqlFormatting.GetParameterName(dialect, index);
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
