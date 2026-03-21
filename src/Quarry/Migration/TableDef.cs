using System;
using System.Collections.Generic;

namespace Quarry.Migration;

/// <summary>
/// Aggregates a complete table definition for snapshot comparison.
/// </summary>
public sealed class TableDef : IEquatable<TableDef>
{
    public string TableName { get; }
    public string? SchemaName { get; }
    public NamingStyleKind NamingStyle { get; }
    public IReadOnlyList<ColumnDef> Columns { get; }
    public IReadOnlyList<ForeignKeyDef> ForeignKeys { get; }
    public IReadOnlyList<IndexDef> Indexes { get; }

    /// <summary>
    /// Column names forming a composite primary key, or null if the table uses a single-column PK.
    /// </summary>
    public IReadOnlyList<string>? CompositeKeyColumns { get; }

    /// <summary>
    /// Table-level character set (primarily for MySQL). Null means server default.
    /// </summary>
    public string? CharacterSet { get; }

    public TableDef(
        string tableName,
        string? schemaName,
        NamingStyleKind namingStyle,
        IReadOnlyList<ColumnDef> columns,
        IReadOnlyList<ForeignKeyDef> foreignKeys,
        IReadOnlyList<IndexDef> indexes,
        IReadOnlyList<string>? compositeKeyColumns = null,
        string? characterSet = null)
    {
        TableName = tableName;
        SchemaName = schemaName;
        NamingStyle = namingStyle;
        Columns = columns;
        ForeignKeys = foreignKeys;
        Indexes = indexes;
        CompositeKeyColumns = compositeKeyColumns;
        CharacterSet = characterSet;
    }

    public bool Equals(TableDef? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (TableName != other.TableName || SchemaName != other.SchemaName
            || NamingStyle != other.NamingStyle
            || CharacterSet != other.CharacterSet
            || Columns.Count != other.Columns.Count
            || ForeignKeys.Count != other.ForeignKeys.Count
            || Indexes.Count != other.Indexes.Count)
            return false;

        // Compare composite key columns
        var ckCount = CompositeKeyColumns?.Count ?? 0;
        var otherCkCount = other.CompositeKeyColumns?.Count ?? 0;
        if (ckCount != otherCkCount) return false;
        for (var i = 0; i < ckCount; i++)
        {
            if (CompositeKeyColumns![i] != other.CompositeKeyColumns![i]) return false;
        }

        for (var i = 0; i < Columns.Count; i++)
        {
            if (!Columns[i].Equals(other.Columns[i])) return false;
        }
        for (var i = 0; i < ForeignKeys.Count; i++)
        {
            if (!ForeignKeys[i].Equals(other.ForeignKeys[i])) return false;
        }
        for (var i = 0; i < Indexes.Count; i++)
        {
            if (!Indexes[i].Equals(other.Indexes[i])) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as TableDef);

    public override int GetHashCode()
    {
        var h = HashCode.Combine(TableName, SchemaName, NamingStyle, Columns.Count, ForeignKeys.Count, Indexes.Count);
        return HashCode.Combine(h, CompositeKeyColumns?.Count ?? 0, CharacterSet);
    }
}
