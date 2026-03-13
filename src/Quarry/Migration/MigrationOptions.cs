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
}
