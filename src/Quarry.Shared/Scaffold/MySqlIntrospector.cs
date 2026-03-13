using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using MySqlConnector;

namespace Quarry.Shared.Scaffold;

internal sealed class MySqlIntrospector : IDatabaseIntrospector
{
    private readonly MySqlConnection _connection;
    private readonly string _database;

    private MySqlIntrospector(MySqlConnection connection)
    {
        _connection = connection;
        _database = connection.Database;
    }

    public static async Task<MySqlIntrospector> CreateAsync(string connectionString)
    {
        var connection = new MySqlConnection(connectionString);
        try
        {
            await connection.OpenAsync();
            return new MySqlIntrospector(connection);
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
        cmd.CommandText = @"
            SELECT TABLE_NAME, TABLE_SCHEMA
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = @db
              AND TABLE_TYPE = 'BASE TABLE'
            ORDER BY TABLE_NAME";
        cmd.Parameters.AddWithValue("@db", _database);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(new TableMetadata(reader.GetString(0), reader.GetString(1)));
        }

        return tables;
    }

    public async Task<List<ColumnMetadata>> GetColumnsAsync(string tableName, string? schema)
    {
        var columns = new List<ColumnMetadata>();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT
                COLUMN_NAME,
                COLUMN_TYPE,
                IS_NULLABLE,
                CHARACTER_MAXIMUM_LENGTH,
                NUMERIC_PRECISION,
                NUMERIC_SCALE,
                COLUMN_DEFAULT,
                ORDINAL_POSITION,
                EXTRA,
                DATA_TYPE
            FROM information_schema.COLUMNS
            WHERE TABLE_NAME = @table AND TABLE_SCHEMA = @db
            ORDER BY ORDINAL_POSITION";
        cmd.Parameters.AddWithValue("@table", tableName);
        cmd.Parameters.AddWithValue("@db", _database);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var columnType = reader.GetString(1); // e.g., "int(11)", "varchar(255)", "tinyint(1)"
            var dataType = reader.GetString(9);   // e.g., "int", "varchar", "tinyint"
            var extra = reader.IsDBNull(8) ? "" : reader.GetString(8);
            var maxLen = reader.IsDBNull(3) ? (int?)null : Convert.ToInt32(reader.GetValue(3));
            var precision = reader.IsDBNull(4) ? (int?)null : Convert.ToInt32(reader.GetValue(4));
            var scale = reader.IsDBNull(5) ? (int?)null : Convert.ToInt32(reader.GetValue(5));

            columns.Add(new ColumnMetadata(
                name: reader.GetString(0),
                dataType: columnType.ToUpperInvariant(),
                isNullable: reader.GetString(2) == "YES",
                maxLength: maxLen,
                precision: precision,
                scale: scale,
                isIdentity: extra.Contains("auto_increment", StringComparison.OrdinalIgnoreCase),
                defaultExpression: reader.IsDBNull(6) ? null : reader.GetString(6),
                ordinalPosition: reader.GetInt32(7)));
        }

        return columns;
    }

    public async Task<PrimaryKeyMetadata?> GetPrimaryKeyAsync(string tableName, string? schema)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT kcu.COLUMN_NAME, tc.CONSTRAINT_NAME
            FROM information_schema.TABLE_CONSTRAINTS tc
            JOIN information_schema.KEY_COLUMN_USAGE kcu
                ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
                AND tc.TABLE_SCHEMA = kcu.TABLE_SCHEMA
            WHERE tc.TABLE_NAME = @table
              AND tc.TABLE_SCHEMA = @db
              AND tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
            ORDER BY kcu.ORDINAL_POSITION";
        cmd.Parameters.AddWithValue("@table", tableName);
        cmd.Parameters.AddWithValue("@db", _database);

        var columns = new List<string>();
        string? constraintName = null;

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
            constraintName ??= reader.GetString(1);
        }

        return columns.Count > 0 ? new PrimaryKeyMetadata(constraintName, columns) : null;
    }

    public async Task<List<ForeignKeyMetadata>> GetForeignKeysAsync(string tableName, string? schema)
    {
        var fks = new List<ForeignKeyMetadata>();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT
                rc.CONSTRAINT_NAME,
                kcu.COLUMN_NAME,
                kcu.REFERENCED_TABLE_NAME,
                kcu.REFERENCED_COLUMN_NAME,
                kcu.REFERENCED_TABLE_SCHEMA,
                rc.DELETE_RULE,
                rc.UPDATE_RULE
            FROM information_schema.REFERENTIAL_CONSTRAINTS rc
            JOIN information_schema.KEY_COLUMN_USAGE kcu
                ON rc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
                AND rc.CONSTRAINT_SCHEMA = kcu.TABLE_SCHEMA
            WHERE kcu.TABLE_NAME = @table
              AND kcu.TABLE_SCHEMA = @db
              AND kcu.REFERENCED_TABLE_NAME IS NOT NULL";
        cmd.Parameters.AddWithValue("@table", tableName);
        cmd.Parameters.AddWithValue("@db", _database);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            fks.Add(new ForeignKeyMetadata(
                constraintName: reader.GetString(0),
                columnName: reader.GetString(1),
                referencedTable: reader.GetString(2),
                referencedColumn: reader.GetString(3),
                referencedSchema: reader.IsDBNull(4) ? null : reader.GetString(4),
                onDelete: reader.GetString(5),
                onUpdate: reader.GetString(6)));
        }

        return fks;
    }

    public async Task<List<IndexMetadata>> GetIndexesAsync(string tableName, string? schema)
    {
        var indexes = new List<IndexMetadata>();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"SHOW INDEX FROM `{tableName.Replace("`", "``")}`";

        var indexMap = new Dictionary<string, (bool IsUnique, bool IsPrimary, List<(int Seq, string Col)> Columns)>();

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var keyName = reader.GetString(2);
            var seqInIndex = reader.GetInt32(3);
            var colName = reader.GetString(4);
            var nonUnique = reader.GetInt32(1);

            if (!indexMap.TryGetValue(keyName, out var entry))
            {
                entry = (nonUnique == 0, keyName == "PRIMARY", new List<(int, string)>());
                indexMap[keyName] = entry;
            }

            entry.Columns.Add((seqInIndex, colName));
        }

        foreach (var (name, (isUnique, isPrimary, cols)) in indexMap)
        {
            cols.Sort((a, b) => a.Seq.CompareTo(b.Seq));
            indexes.Add(new IndexMetadata(name, cols.ConvertAll(c => c.Col), isUnique, isPrimary));
        }

        return indexes;
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
