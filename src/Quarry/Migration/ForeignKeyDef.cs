using System;

namespace Quarry.Migration;

/// <summary>
/// Captures a foreign key relationship definition.
/// </summary>
public sealed class ForeignKeyDef : IEquatable<ForeignKeyDef>
{
    public string ConstraintName { get; }
    public string ColumnName { get; }
    public string ReferencedTable { get; }
    public string ReferencedColumn { get; }
    public ForeignKeyAction OnDelete { get; }
    public ForeignKeyAction OnUpdate { get; }

    public ForeignKeyDef(
        string constraintName,
        string columnName,
        string referencedTable,
        string referencedColumn,
        ForeignKeyAction onDelete = ForeignKeyAction.NoAction,
        ForeignKeyAction onUpdate = ForeignKeyAction.NoAction)
    {
        ConstraintName = constraintName;
        ColumnName = columnName;
        ReferencedTable = referencedTable;
        ReferencedColumn = referencedColumn;
        OnDelete = onDelete;
        OnUpdate = onUpdate;
    }

    public bool Equals(ForeignKeyDef? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return ConstraintName == other.ConstraintName
            && ColumnName == other.ColumnName
            && ReferencedTable == other.ReferencedTable
            && ReferencedColumn == other.ReferencedColumn
            && OnDelete == other.OnDelete
            && OnUpdate == other.OnUpdate;
    }

    public override bool Equals(object? obj) => Equals(obj as ForeignKeyDef);

    public override int GetHashCode()
    {
        return HashCode.Combine(ConstraintName, ColumnName, ReferencedTable, ReferencedColumn, OnDelete, OnUpdate);
    }
}
