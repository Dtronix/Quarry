using Logsmith;

namespace Quarry.Logging;

[LogCategory("Quarry.Migration")]
internal static partial class MigrationLog
{
    [LogMessage(LogLevel.Information, "Applying migration {version}: {name}")]
    internal static partial void Applying(int version, string name);

    [LogMessage(LogLevel.Information, "Migration {version} applied in {elapsedMs}ms")]
    internal static partial void Applied(int version, int elapsedMs);

    [LogMessage(LogLevel.Information, "Rolling back migration {version}: {name}")]
    internal static partial void RollingBack(int version, string name);

    [LogMessage(LogLevel.Information, "Migration {version} rolled back")]
    internal static partial void RolledBack(int version);

    [LogMessage(LogLevel.Debug, "Migration {version} SQL:\n{sql}", AlwaysEmit = true)]
    internal static partial void SqlGenerated(int version, string sql);

    [LogMessage(LogLevel.Debug, "Migration {version} backup SQL:\n{sql}", AlwaysEmit = true)]
    internal static partial void BackupSqlGenerated(int version, string sql);

    [LogMessage(LogLevel.Information, "Dry run — migration {version} not applied")]
    internal static partial void DryRun(int version);

    [LogMessage(LogLevel.Debug, "Ensuring migration history table exists")]
    internal static partial void EnsureHistoryTable();

    [LogMessage(LogLevel.Debug, "Found {count} previously applied migration(s)")]
    internal static partial void AppliedCount(int count);

    [LogMessage(LogLevel.Information, "Migration {version} skipped (already applied)")]
    internal static partial void Skipped(int version);

    [LogMessage(LogLevel.Error, "Migration {version} ({name}) failed during {direction}")]
    internal static partial void Failed(int version, string name, string direction, Exception ex);
}
