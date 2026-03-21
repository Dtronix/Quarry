using System;
using System.Data.Common;
using System.Threading.Tasks;

namespace Quarry.Migration;

/// <summary>
/// Options for controlling migration execution.
/// </summary>
public sealed class MigrationOptions
{
    /// <summary>Target version (null = latest for upgrade, 0 for full downgrade).</summary>
    public int? TargetVersion { get; set; }

    /// <summary>Migration direction.</summary>
    public MigrationDirection Direction { get; set; } = MigrationDirection.Upgrade;

    /// <summary>Whether to run backup SQL before destructive steps.</summary>
    public bool RunBackups { get; set; }

    /// <summary>Print SQL without executing.</summary>
    public bool DryRun { get; set; }

    /// <summary>
    /// When true, throws on checksum mismatches between applied and current migration SQL.
    /// When false (default), logs a warning.
    /// </summary>
    public bool StrictChecksums { get; set; }

    /// <summary>
    /// When true, wraps DDL with IF NOT EXISTS / IF EXISTS guards for safe re-runs.
    /// </summary>
    public bool Idempotent { get; set; }

    /// <summary>
    /// When true, allows execution to proceed past migrations with 'running' status
    /// (from a previous crash). When false (default), throws an exception.
    /// </summary>
    public bool IgnoreIncomplete { get; set; }

    /// <summary>Optional logger for migration output.</summary>
    public Action<string>? Logger { get; set; }

    /// <summary>Timeout for each migration command. Null = ADO.NET default (30s).</summary>
    public TimeSpan? CommandTimeout { get; set; }

    /// <summary>Lock acquisition timeout. Emits SET LOCK_TIMEOUT / SET statement_timeout before DDL.</summary>
    public TimeSpan? LockTimeout { get; set; }

    /// <summary>Called before each migration is applied or rolled back. Parameters: version, name, connection.</summary>
    public Func<int, string, DbConnection, Task>? BeforeEach { get; set; }

    /// <summary>Called after each migration is successfully applied or rolled back. Parameters: version, name, elapsed time, connection.</summary>
    public Func<int, string, TimeSpan, DbConnection, Task>? AfterEach { get; set; }

    /// <summary>Called when a migration fails, before rollback. Parameters: version, name, exception, connection.</summary>
    public Func<int, string, Exception, DbConnection, Task>? OnError { get; set; }
}
