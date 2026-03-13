namespace Quarry.Shared.Scaffold;

internal sealed class ForeignKeyMetadata
{
    public string? ConstraintName { get; }
    public string ColumnName { get; }
    public string ReferencedTable { get; }
    public string ReferencedColumn { get; }
    public string? ReferencedSchema { get; }
    public string OnDelete { get; }
    public string OnUpdate { get; }

    public ForeignKeyMetadata(
        string? constraintName,
        string columnName,
        string referencedTable,
        string referencedColumn,
        string? referencedSchema = null,
        string onDelete = "NO ACTION",
        string onUpdate = "NO ACTION")
    {
        ConstraintName = constraintName;
        ColumnName = columnName;
        ReferencedTable = referencedTable;
        ReferencedColumn = referencedColumn;
        ReferencedSchema = referencedSchema;
        OnDelete = onDelete;
        OnUpdate = onUpdate;
    }
}
