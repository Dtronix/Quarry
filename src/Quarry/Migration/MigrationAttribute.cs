using System;

namespace Quarry;

/// <summary>
/// Marks a class as a migration.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class MigrationAttribute : Attribute
{
    /// <summary>The migration version number.</summary>
    public int Version { get; set; }

    /// <summary>The migration name.</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// When set, indicates this migration is a squashed baseline that replaces
    /// all original migrations up to and including this version number.
    /// The runner will skip this baseline on databases that already have
    /// any migration in the squashed range applied.
    /// </summary>
    public int SquashedFrom { get; set; }
}
