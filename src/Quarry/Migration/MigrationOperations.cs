using System.Collections.Generic;

namespace Quarry.Migration;

// --- Column and table definitions ---

internal record ColumnDefinition(
    string Name,
    string? SqlType,
    string? ClrType,
    int? MaxLength,
    int? Precision,
    int? Scale,
    bool IsNullable,
    string? DefaultValue,
    string? DefaultExpression,
    bool IsIdentity,
    string? Collation = null);

internal record TableDefinition(
    string Name,
    string? Schema,
    IReadOnlyList<ColumnDefinition> Columns,
    IReadOnlyList<TableConstraint> Constraints);

// --- Constraints ---

internal abstract record TableConstraint;

internal record PrimaryKeyConstraint(string Name, string[] Columns) : TableConstraint;

internal record ForeignKeyConstraint(
    string Name, string Column, string RefTable, string RefColumn,
    ForeignKeyAction? OnDelete,
    ForeignKeyAction? OnUpdate) : TableConstraint;

internal record IndexConstraint(
    string Name, string[] Columns, bool IsUnique, string? Filter) : TableConstraint;

// --- Operations ---

internal abstract class MigrationOperation
{
    public bool IsOnline { get; set; }
    public int? BatchSize { get; set; }
    public bool IsConcurrent { get; set; }
    public bool SuppressTransaction { get; set; }
}

internal sealed class CreateTableOperation(string Name, string? Schema, TableDefinition Table) : MigrationOperation
{
    public string Name { get; } = Name;
    public string? Schema { get; } = Schema;
    public TableDefinition Table { get; } = Table;
}

internal sealed class DropTableOperation(string Name, string? Schema) : MigrationOperation
{
    public string Name { get; } = Name;
    public string? Schema { get; } = Schema;
}

internal sealed class RenameTableOperation : MigrationOperation
{
    public string OldName { get; }
    public string NewName { get; }
    public string? Schema { get; }
    public string? OldSchema { get; }
    public string? NewSchema { get; }
    public bool IsSchemaTransfer => OldSchema != null || NewSchema != null;

    public RenameTableOperation(string oldName, string newName, string? schema)
    {
        OldName = oldName;
        NewName = newName;
        Schema = schema;
    }

    public RenameTableOperation(string oldName, string newName, string? oldSchema, string? newSchema)
    {
        OldName = oldName;
        NewName = newName;
        OldSchema = oldSchema;
        NewSchema = newSchema;
        Schema = oldSchema; // Fallback for compat
    }
}

internal sealed class AddColumnOperation(string Table, string? Schema, string Column, ColumnDefinition Definition) : MigrationOperation
{
    public string Table { get; } = Table;
    public string? Schema { get; } = Schema;
    public string Column { get; } = Column;
    public ColumnDefinition Definition { get; } = Definition;
}

internal sealed class DropColumnOperation(string Table, string? Schema, string Column) : MigrationOperation
{
    public string Table { get; } = Table;
    public string? Schema { get; } = Schema;
    public string Column { get; } = Column;
    public TableDefinition? SourceTable { get; set; }
}

internal sealed class RenameColumnOperation(string Table, string? Schema, string OldName, string NewName) : MigrationOperation
{
    public string Table { get; } = Table;
    public string? Schema { get; } = Schema;
    public string OldName { get; } = OldName;
    public string NewName { get; } = NewName;
}

internal sealed class AlterColumnOperation(string Table, string? Schema, string Column, ColumnDefinition Definition) : MigrationOperation
{
    public string Table { get; } = Table;
    public string? Schema { get; } = Schema;
    public string Column { get; } = Column;
    public ColumnDefinition Definition { get; } = Definition;
    public TableDefinition? SourceTable { get; set; }
}

internal sealed class AddForeignKeyOperation(
    string Name, string Table, string? Schema, string Column,
    string RefTable, string RefColumn,
    ForeignKeyAction? OnDelete,
    ForeignKeyAction? OnUpdate) : MigrationOperation
{
    public string Name { get; } = Name;
    public string Table { get; } = Table;
    public string? Schema { get; } = Schema;
    public string Column { get; } = Column;
    public string RefTable { get; } = RefTable;
    public string RefColumn { get; } = RefColumn;
    public ForeignKeyAction? OnDelete { get; } = OnDelete;
    public ForeignKeyAction? OnUpdate { get; } = OnUpdate;
}

internal sealed class DropForeignKeyOperation(string Name, string Table, string? Schema) : MigrationOperation
{
    public string Name { get; } = Name;
    public string Table { get; } = Table;
    public string? Schema { get; } = Schema;
}

internal sealed class AddIndexOperation(
    string Name, string Table, string? Schema, string[] Columns,
    bool IsUnique, string? Filter, bool[]? DescendingColumns = null) : MigrationOperation
{
    public string Name { get; } = Name;
    public string Table { get; } = Table;
    public string? Schema { get; } = Schema;
    public string[] Columns { get; } = Columns;
    public bool IsUnique { get; } = IsUnique;
    public string? Filter { get; } = Filter;
    public bool[]? DescendingColumns { get; } = DescendingColumns;
}

internal sealed class DropIndexOperation(string Name, string Table, string? Schema) : MigrationOperation
{
    public string Name { get; } = Name;
    public string Table { get; } = Table;
    public string? Schema { get; } = Schema;
}

internal sealed class InsertDataOperation(string Table, string? Schema, string[] Columns, object?[][] Rows) : MigrationOperation
{
    public string Table { get; } = Table;
    public string? Schema { get; } = Schema;
    public string[] Columns { get; } = Columns;
    public object?[][] Rows { get; } = Rows;
}

internal sealed class UpdateDataOperation(string Table, string? Schema, string[] SetColumns, object?[] SetValues, string[] WhereColumns, object?[] WhereValues) : MigrationOperation
{
    public string Table { get; } = Table;
    public string? Schema { get; } = Schema;
    public string[] SetColumns { get; } = SetColumns;
    public object?[] SetValues { get; } = SetValues;
    public string[] WhereColumns { get; } = WhereColumns;
    public object?[] WhereValues { get; } = WhereValues;
}

internal sealed class DeleteDataOperation(string Table, string? Schema, string[] WhereColumns, object?[] WhereValues) : MigrationOperation
{
    public string Table { get; } = Table;
    public string? Schema { get; } = Schema;
    public string[] WhereColumns { get; } = WhereColumns;
    public object?[] WhereValues { get; } = WhereValues;
}

internal sealed class RawSqlOperation(string Sql) : MigrationOperation
{
    public string Sql { get; } = Sql;
}

internal sealed class CreateViewOperation(string Name, string? Schema, string Sql) : MigrationOperation
{
    public string Name { get; } = Name;
    public string? Schema { get; } = Schema;
    public string Sql { get; } = Sql;
}

internal sealed class DropViewOperation(string Name, string? Schema) : MigrationOperation
{
    public string Name { get; } = Name;
    public string? Schema { get; } = Schema;
}

internal sealed class AlterViewOperation(string Name, string? Schema, string Sql) : MigrationOperation
{
    public string Name { get; } = Name;
    public string? Schema { get; } = Schema;
    public string Sql { get; } = Sql;
}

internal sealed class CreateProcedureOperation(string Name, string? Schema, string Sql) : MigrationOperation
{
    public string Name { get; } = Name;
    public string? Schema { get; } = Schema;
    public string Sql { get; } = Sql;
}

internal sealed class DropProcedureOperation(string Name, string? Schema) : MigrationOperation
{
    public string Name { get; } = Name;
    public string? Schema { get; } = Schema;
}

internal sealed class AlterProcedureOperation(string Name, string? Schema, string Sql) : MigrationOperation
{
    public string Name { get; } = Name;
    public string? Schema { get; } = Schema;
    public string Sql { get; } = Sql;
}
