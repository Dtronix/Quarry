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
}
