using System;

namespace Quarry;

/// <summary>
/// Marks a class as a migration schema snapshot.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class MigrationSnapshotAttribute : Attribute
{
    /// <summary>The snapshot version number.</summary>
    public int Version { get; set; }

    /// <summary>The snapshot name.</summary>
    public string Name { get; set; } = "";

    /// <summary>The ISO 8601 timestamp when the snapshot was created.</summary>
    public string Timestamp { get; set; } = "";

    /// <summary>The parent snapshot version (0 if this is the first snapshot).</summary>
    public int ParentVersion { get; set; }

    /// <summary>Lightweight hash of the schema at snapshot time (table count, column count, names).</summary>
    public string SchemaHash { get; set; } = "";
}
