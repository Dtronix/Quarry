using System;
using System.Collections.Generic;

namespace Quarry.Migration;

/// <summary>
/// Builder for defining table columns, keys, and indexes within a CreateTable operation.
/// </summary>
public sealed class TableBuilder
{
    private readonly string _tableName;
    private readonly string? _schema;
    private readonly List<ColumnDefinition> _columns = new();
    private readonly List<TableConstraint> _constraints = new();

    internal TableBuilder(string tableName, string? schema)
    {
        _tableName = tableName;
        _schema = schema;
    }

    public TableBuilder Column(string name, Action<ColumnBuilder> configure)
    {
        var cb = new ColumnBuilder();
        configure(cb);
        _columns.Add(cb.Build());
        _columns[^1] = _columns[^1] with { Name = name };
        return this;
    }

    public TableBuilder PrimaryKey(string name, params string[] columns)
    {
        _constraints.Add(new PrimaryKeyConstraint(name, columns));
        return this;
    }

    public TableBuilder ForeignKey(string name, string column, string refTable, string refColumn,
        ForeignKeyAction? onDelete = null, ForeignKeyAction? onUpdate = null)
    {
        _constraints.Add(new ForeignKeyConstraint(name, column, refTable, refColumn, onDelete, onUpdate));
        return this;
    }

    public TableBuilder Index(string name, string[] columns, bool unique = false, string? filter = null)
    {
        _constraints.Add(new IndexConstraint(name, columns, unique, filter));
        return this;
    }

    internal TableDefinition Build() => new(_tableName, _schema, _columns, _constraints);
}
