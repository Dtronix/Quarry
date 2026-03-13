using System;

namespace Quarry.Shared.Migration;

/// <summary>
/// Captures a foreign key relationship definition.
/// </summary>
#if QUARRY_GENERATOR
internal
#else
public
#endif
sealed class ForeignKeyDef : IEquatable<ForeignKeyDef>
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
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + (ConstraintName?.GetHashCode() ?? 0);
            hash = hash * 31 + (ColumnName?.GetHashCode() ?? 0);
            hash = hash * 31 + (ReferencedTable?.GetHashCode() ?? 0);
            hash = hash * 31 + (ReferencedColumn?.GetHashCode() ?? 0);
            hash = hash * 31 + OnDelete.GetHashCode();
            hash = hash * 31 + OnUpdate.GetHashCode();
            return hash;
        }
    }
}
