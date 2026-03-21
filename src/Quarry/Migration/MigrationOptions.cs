using System;

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

    /// <summary>Optional logger for migration output.</summary>
    public Action<string>? Logger { get; set; }

    /// <summary>Timeout for each migration command. Null = ADO.NET default (30s).</summary>
    public TimeSpan? CommandTimeout { get; set; }

    /// <summary>Lock acquisition timeout. Emits SET LOCK_TIMEOUT / SET statement_timeout before DDL.</summary>
    public TimeSpan? LockTimeout { get; set; }
}
