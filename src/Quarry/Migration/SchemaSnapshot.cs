using System;
using System.Collections.Generic;

namespace Quarry.Migration;

/// <summary>
/// A complete point-in-time capture of all tables in the schema.
/// </summary>
public sealed class SchemaSnapshot
{
    public int Version { get; }
    public string Name { get; }
    public DateTimeOffset Timestamp { get; }
    public int? ParentVersion { get; }
    public IReadOnlyList<TableDef> Tables { get; }

    public SchemaSnapshot(
        int version,
        string name,
        DateTimeOffset timestamp,
        int? parentVersion,
        IReadOnlyList<TableDef> tables)
    {
        Version = version;
        Name = name;
        Timestamp = timestamp;
        ParentVersion = parentVersion;
        Tables = tables;
    }

    public TableDef? GetTable(string name)
    {
        for (var i = 0; i < Tables.Count; i++)
        {
            if (string.Equals(Tables[i].TableName, name, StringComparison.OrdinalIgnoreCase))
                return Tables[i];
        }
        return null;
    }
}
