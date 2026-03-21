using System;
using System.Collections.Generic;
using System.Reflection;

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

    public MigrationBuilder InsertData(string table, object row, string? schema = null)
    {
        return InsertData(table, new[] { row }, schema);
    }

    public MigrationBuilder InsertData(string table, object[] rows, string? schema = null)
    {
        if (rows.Length == 0)
            throw new ArgumentException("At least one row must be provided.", nameof(rows));

        var (columns, firstValues) = ExtractProperties(rows[0]);
        var allRows = new object?[rows.Length][];
        allRows[0] = firstValues;

        for (var i = 1; i < rows.Length; i++)
        {
            var (cols, vals) = ExtractProperties(rows[i]);
            if (cols.Length != columns.Length)
                throw new InvalidOperationException($"Row {i} has {cols.Length} columns but expected {columns.Length}.");
            allRows[i] = vals;
        }

        _operations.Add(new InsertDataOperation(table, schema, columns, allRows));
        return this;
    }

    public MigrationBuilder UpdateData(string table, object set, object where, string? schema = null)
    {
        var (setCols, setVals) = ExtractProperties(set);
        var (whereCols, whereVals) = ExtractProperties(where);
        _operations.Add(new UpdateDataOperation(table, schema, setCols, setVals, whereCols, whereVals));
        return this;
    }

    public MigrationBuilder DeleteData(string table, object where, string? schema = null)
    {
        var (whereCols, whereVals) = ExtractProperties(where);
        _operations.Add(new DeleteDataOperation(table, schema, whereCols, whereVals));
        return this;
    }

    public MigrationBuilder Sql(string sql)
    {
        _operations.Add(new RawSqlOperation(sql));
        return this;
    }

    private static (string[] Columns, object?[] Values) ExtractProperties(object obj)
    {
        var properties = obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var columns = new string[properties.Length];
        var values = new object?[properties.Length];
        for (var i = 0; i < properties.Length; i++)
        {
            columns[i] = properties[i].Name;
            values[i] = properties[i].GetValue(obj);
        }
        return (columns, values);
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

    public MigrationBuilder SuppressTransaction()
    {
        if (_operations.Count == 0)
            throw new InvalidOperationException("SuppressTransaction() must be called after an operation has been added to the builder.");
        _operations[^1].SuppressTransaction = true;
        return this;
    }

    /// <summary>
    /// Builds the SQL for all operations using the specified dialect.
    /// </summary>
    public string BuildSql(SqlDialect dialect)
    {
        return DdlRenderer.Render(_operations, dialect);
    }

    /// <summary>
    /// Builds idempotent SQL with IF NOT EXISTS / IF EXISTS guards.
    /// </summary>
    public string BuildIdempotentSql(SqlDialect dialect)
    {
        return DdlRenderer.Render(_operations, dialect, idempotent: true);
    }

    /// <summary>
    /// Partitions operations into transactional and non-transactional groups,
    /// returning their rendered SQL separately plus combined SQL for logging/checksums.
    /// For PostgreSQL, IsConcurrent operations are automatically treated as non-transactional.
    /// </summary>
    internal (string TransactionalSql, string NonTransactionalSql, string AllSql) BuildPartitionedSql(SqlDialect dialect, bool idempotent = false)
    {
        var transactional = new List<MigrationOperation>();
        var nonTransactional = new List<MigrationOperation>();

        foreach (var op in _operations)
        {
            // Auto-imply SuppressTransaction for PostgreSQL CONCURRENTLY
            var suppressed = op.SuppressTransaction ||
                             (op.IsConcurrent && dialect == SqlDialect.PostgreSQL);

            if (suppressed)
                nonTransactional.Add(op);
            else
                transactional.Add(op);
        }

        var txSql = transactional.Count > 0 ? DdlRenderer.Render(transactional, dialect, idempotent) : string.Empty;
        var nonTxSql = nonTransactional.Count > 0 ? DdlRenderer.Render(nonTransactional, dialect, idempotent) : string.Empty;

        // Derive combined SQL from partitioned results to avoid re-rendering
        string allSql;
        if (txSql.Length > 0 && nonTxSql.Length > 0)
            allSql = txSql + "\n" + nonTxSql;
        else if (txSql.Length > 0)
            allSql = txSql;
        else
            allSql = nonTxSql;

        return (txSql, nonTxSql, allSql);
    }

    internal bool HasNonTransactionalOperations(SqlDialect dialect)
    {
        foreach (var op in _operations)
        {
            if (op.SuppressTransaction || (op.IsConcurrent && dialect == SqlDialect.PostgreSQL))
                return true;
        }
        return false;
    }

    internal IReadOnlyList<MigrationOperation> GetOperations() => _operations;
}
