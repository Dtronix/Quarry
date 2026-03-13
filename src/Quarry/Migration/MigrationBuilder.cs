using System;
using System.Collections.Generic;

namespace Quarry.Migration;

/// <summary>
/// Fluent API for building migration operations that generate DDL SQL.
/// </summary>
public sealed class MigrationBuilder
{
    private readonly List<MigrationOperation> _operations = new();

    public MigrationBuilder CreateTable(string name, string? schema, Action<TableBuilder> configure)
    {
        var tb = new TableBuilder(name, schema);
        configure(tb);
        _operations.Add(new CreateTableOperation(name, schema, tb.Build()));
        return this;
    }

    public MigrationBuilder DropTable(string name, string? schema = null)
    {
        _operations.Add(new DropTableOperation(name, schema));
        return this;
    }

    public MigrationBuilder RenameTable(string oldName, string newName, string? schema = null)
    {
        _operations.Add(new RenameTableOperation(oldName, newName, schema));
        return this;
    }

    public MigrationBuilder AddColumn(string table, string column, Action<ColumnBuilder> configure)
    {
        var cb = new ColumnBuilder();
        configure(cb);
        _operations.Add(new AddColumnOperation(table, null, column, cb.Build()));
        return this;
    }

    public MigrationBuilder DropColumn(string table, string column)
    {
        _operations.Add(new DropColumnOperation(table, null, column));
        return this;
    }

    public MigrationBuilder RenameColumn(string table, string oldName, string newName)
    {
        _operations.Add(new RenameColumnOperation(table, null, oldName, newName));
        return this;
    }

    public MigrationBuilder AlterColumn(string table, string column, Action<ColumnBuilder> configure)
    {
        var cb = new ColumnBuilder();
        configure(cb);
        _operations.Add(new AlterColumnOperation(table, null, column, cb.Build()));
        return this;
    }

    public MigrationBuilder AddForeignKey(string name, string table, string column,
        string refTable, string refColumn,
        ForeignKeyAction? onDelete = null,
        ForeignKeyAction? onUpdate = null)
    {
        _operations.Add(new AddForeignKeyOperation(name, table, null, column, refTable, refColumn, onDelete, onUpdate));
        return this;
    }

    public MigrationBuilder DropForeignKey(string name, string table)
    {
        _operations.Add(new DropForeignKeyOperation(name, table, null));
        return this;
    }

    public MigrationBuilder AddIndex(string name, string table, string[] columns, bool unique = false, string? filter = null)
    {
        _operations.Add(new AddIndexOperation(name, table, null, columns, unique, filter));
        return this;
    }

    public MigrationBuilder DropIndex(string name, string table)
    {
        _operations.Add(new DropIndexOperation(name, table, null));
        return this;
    }

    public MigrationBuilder Sql(string sql)
    {
        _operations.Add(new RawSqlOperation(sql));
        return this;
    }

    public MigrationBuilder Online()
    {
        if (_operations.Count == 0)
            throw new InvalidOperationException("Online() must be called after an operation has been added to the builder.");
        _operations[^1].IsOnline = true;
        return this;
    }

    public MigrationBuilder Batched(int batchSize)
    {
        if (_operations.Count == 0)
            throw new InvalidOperationException("Batched() must be called after an operation has been added to the builder.");
        _operations[^1].BatchSize = batchSize;
        return this;
    }

    public MigrationBuilder WithSourceTable(Action<TableBuilder> configure)
    {
        if (_operations.Count == 0)
            throw new InvalidOperationException("WithSourceTable() must be called after an operation has been added to the builder.");

        var lastOp = _operations[^1];
        if (lastOp is not DropColumnOperation and not AlterColumnOperation)
            throw new InvalidOperationException($"WithSourceTable() is only valid after DropColumn or AlterColumn, but the last operation is {lastOp.GetType().Name}.");

        var tableName = lastOp switch
        {
            DropColumnOperation dc => dc.Table,
            AlterColumnOperation ac => ac.Table,
            _ => null
        };

        var tb = new TableBuilder(tableName!, null);
        configure(tb);
        var tableDef = tb.Build();
        switch (lastOp)
        {
            case DropColumnOperation dc:
                dc.SourceTable = tableDef;
                break;
            case AlterColumnOperation ac:
                ac.SourceTable = tableDef;
                break;
        }

        return this;
    }

    public MigrationBuilder ConcurrentIndex()
    {
        if (_operations.Count == 0)
            throw new InvalidOperationException("ConcurrentIndex() must be called after an operation has been added to the builder.");
        _operations[^1].IsConcurrent = true;
        return this;
    }

    /// <summary>
    /// Builds the SQL for all operations using the specified dialect.
    /// </summary>
    public string BuildSql(SqlDialect dialect)
    {
        return DdlRenderer.Render(_operations, dialect);
    }

    internal IReadOnlyList<MigrationOperation> GetOperations() => _operations;
}
