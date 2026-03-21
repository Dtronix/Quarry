using System;
using System.Collections.Generic;

namespace Quarry.Shared.Migration;

/// <summary>
/// Aggregates a complete table definition for snapshot comparison.
/// </summary>
#if QUARRY_GENERATOR
internal
#else
public
#endif
sealed class TableDef : IEquatable<TableDef>
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
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + (TableName?.GetHashCode() ?? 0);
            hash = hash * 31 + (SchemaName?.GetHashCode() ?? 0);
            hash = hash * 31 + NamingStyle.GetHashCode();
            hash = hash * 31 + Columns.Count;
            hash = hash * 31 + ForeignKeys.Count;
            hash = hash * 31 + Indexes.Count;
            hash = hash * 31 + (CompositeKeyColumns?.Count ?? 0);
            hash = hash * 31 + (CharacterSet?.GetHashCode() ?? 0);
            return hash;
        }
    }
}
