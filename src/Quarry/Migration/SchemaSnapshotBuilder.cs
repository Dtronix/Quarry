using System;
using System.Collections.Generic;

namespace Quarry.Migration;

/// <summary>
/// Fluent builder for constructing <see cref="SchemaSnapshot"/> instances.
/// </summary>
public sealed class SchemaSnapshotBuilder
{
    private int _version;
    private string _name = "";
    private DateTimeOffset _timestamp;
    private int? _parentVersion;
    private readonly List<TableDef> _tables = new List<TableDef>();

    public SchemaSnapshotBuilder SetVersion(int version) { _version = version; return this; }
    public SchemaSnapshotBuilder SetName(string name) { _name = name; return this; }
    public SchemaSnapshotBuilder SetTimestamp(DateTimeOffset timestamp) { _timestamp = timestamp; return this; }
    public SchemaSnapshotBuilder SetParentVersion(int parentVersion) { _parentVersion = parentVersion; return this; }

    public SchemaSnapshotBuilder AddTable(Action<TableDefBuilder> configure)
    {
        var builder = new TableDefBuilder();
        configure(builder);
        _tables.Add(builder.Build());
        return this;
    }

    public SchemaSnapshot Build()
    {
        return new SchemaSnapshot(_version, _name, _timestamp, _parentVersion, _tables);
    }
}
