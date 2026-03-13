using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Quarry.Shared.Scaffold;

internal sealed class SqliteIntrospector : IDatabaseIntrospector
{
    private readonly SqliteConnection _connection;

    private SqliteIntrospector(SqliteConnection connection)
    {
        _connection = connection;
    }

    public static async Task<SqliteIntrospector> CreateAsync(string connectionString)
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync();
            return new SqliteIntrospector(connection);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public async Task<List<TableMetadata>> GetTablesAsync(string? schemaFilter)
    {
        var tables = new List<TableMetadata>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(new TableMetadata(reader.GetString(0), null));
        }

        return tables;
    }

    public async Task<List<ColumnMetadata>> GetColumnsAsync(string tableName, string? schema)
    {
        var columns = new List<ColumnMetadata>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({QuoteIdentifier(tableName)})";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var cid = reader.GetInt32(0);
            var name = reader.GetString(1);
            var type = reader.IsDBNull(2) ? "TEXT" : reader.GetString(2);
            var notNull = reader.GetInt32(3) != 0;
            var defaultValue = reader.IsDBNull(4) ? null : reader.GetString(4);
            var pk = reader.GetInt32(5);

            // Normalize empty type to TEXT
            if (string.IsNullOrWhiteSpace(type))
                type = "TEXT";

            // SQLite INTEGER PRIMARY KEY is an alias for the rowid (auto-increment)
            var isIdentity = pk > 0 && type.Equals("INTEGER", StringComparison.OrdinalIgnoreCase);

            columns.Add(new ColumnMetadata(
                name: name,
                dataType: type.ToUpperInvariant(),
                isNullable: !notNull && pk == 0,
                isIdentity: isIdentity,
                defaultExpression: defaultValue,
                ordinalPosition: cid));
        }

        return columns;
    }

    public async Task<PrimaryKeyMetadata?> GetPrimaryKeyAsync(string tableName, string? schema)
    {
        var pkColumns = new List<(int PkOrder, string Name)>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({QuoteIdentifier(tableName)})";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var pk = reader.GetInt32(5);
            if (pk > 0)
            {
                pkColumns.Add((pk, reader.GetString(1)));
            }
        }

        if (pkColumns.Count == 0)
            return null;

        pkColumns.Sort((a, b) => a.PkOrder.CompareTo(b.PkOrder));
        return new PrimaryKeyMetadata(null, pkColumns.ConvertAll(p => p.Name));
    }

    public async Task<List<ForeignKeyMetadata>> GetForeignKeysAsync(string tableName, string? schema)
    {
        var fks = new List<ForeignKeyMetadata>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"PRAGMA foreign_key_list({QuoteIdentifier(tableName)})";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var id = reader.GetInt32(0);
            var seq = reader.GetInt32(1);
            var refTable = reader.GetString(2);
            var from = reader.GetString(3);
            var to = reader.GetString(4);
            var onUpdate = reader.GetString(5);
            var onDelete = reader.GetString(6);

            fks.Add(new ForeignKeyMetadata(
                constraintName: $"FK_{tableName}_{from}",
                columnName: from,
                referencedTable: refTable,
                referencedColumn: to,
                onDelete: NormalizeFkAction(onDelete),
                onUpdate: NormalizeFkAction(onUpdate)));
        }

        return fks;
    }

    public async Task<List<IndexMetadata>> GetIndexesAsync(string tableName, string? schema)
    {
        var indexes = new List<IndexMetadata>();
        using var listCmd = _connection.CreateCommand();
        listCmd.CommandText = $"PRAGMA index_list({QuoteIdentifier(tableName)})";

        var indexEntries = new List<(string Name, bool IsUnique, string Origin)>();
        using (var reader = await listCmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(1);
                var isUnique = reader.GetInt32(2) != 0;
                var origin = reader.GetString(3); // "c" = CREATE INDEX, "u" = UNIQUE, "pk" = PRIMARY KEY

                indexEntries.Add((name, isUnique, origin));
            }
        }

        foreach (var (name, isUnique, origin) in indexEntries)
        {
            var columns = new List<string>();
            using var infoCmd = _connection.CreateCommand();
            infoCmd.CommandText = $"PRAGMA index_info({QuoteIdentifier(name)})";

            using var infoReader = await infoCmd.ExecuteReaderAsync();
            while (await infoReader.ReadAsync())
            {
                var colName = infoReader.IsDBNull(2) ? null : infoReader.GetString(2);
                if (colName != null)
                    columns.Add(colName);
            }

            if (columns.Count > 0)
            {
                indexes.Add(new IndexMetadata(name, columns, isUnique, isPrimaryKey: origin == "pk"));
            }
        }

        return indexes;
    }

    private static string NormalizeFkAction(string action)
    {
        return action.ToUpperInvariant() switch
        {
            "CASCADE" => "CASCADE",
            "SET NULL" => "SET NULL",
            "SET DEFAULT" => "SET DEFAULT",
            "RESTRICT" => "RESTRICT",
            "NO ACTION" => "NO ACTION",
            _ => "NO ACTION"
        };
    }

    private static string QuoteIdentifier(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"") + "\"";
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
