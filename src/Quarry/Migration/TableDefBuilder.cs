using System;
using System.Collections.Generic;

namespace Quarry.Migration;

/// <summary>
/// Fluent builder for constructing <see cref="TableDef"/> instances.
/// </summary>
public sealed class TableDefBuilder
{
    private string _tableName = "";
    private string? _schemaName;
    private NamingStyleKind _namingStyle;
    private readonly List<ColumnDef> _columns = new List<ColumnDef>();
    private readonly List<ForeignKeyDef> _foreignKeys = new List<ForeignKeyDef>();
    private readonly List<IndexDef> _indexes = new List<IndexDef>();
    private List<string>? _compositeKeyColumns;

    public TableDefBuilder Name(string tableName) { _tableName = tableName; return this; }
    public TableDefBuilder Schema(string? schemaName) { _schemaName = schemaName; return this; }
    public TableDefBuilder NamingStyle(NamingStyleKind style) { _namingStyle = style; return this; }

    public TableDefBuilder AddColumn(Action<ColumnDefBuilder> configure)
    {
        var builder = new ColumnDefBuilder();
        configure(builder);
        _columns.Add(builder.Build());
        return this;
    }

    public TableDefBuilder AddForeignKey(
        string constraintName, string columnName,
        string referencedTable, string referencedColumn,
        ForeignKeyAction onDelete = ForeignKeyAction.NoAction,
        ForeignKeyAction onUpdate = ForeignKeyAction.NoAction)
    {
        _foreignKeys.Add(new ForeignKeyDef(constraintName, columnName, referencedTable, referencedColumn, onDelete, onUpdate));
        return this;
    }

    public TableDefBuilder AddIndex(string name, IReadOnlyList<string> columns, bool isUnique = false, string? filter = null, string? method = null)
    {
        _indexes.Add(new IndexDef(name, columns, isUnique, filter, method));
        return this;
    }

    public TableDefBuilder CompositeKey(params string[] columns)
    {
        _compositeKeyColumns = new List<string>(columns);
        return this;
    }

    public TableDef Build()
    {
        return new TableDef(_tableName, _schemaName, _namingStyle, _columns, _foreignKeys, _indexes, _compositeKeyColumns);
    }
}
