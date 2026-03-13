using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Quarry.Shared.Scaffold;

internal sealed class SqlServerIntrospector : IDatabaseIntrospector
{
    private readonly SqlConnection _connection;

    private SqlServerIntrospector(SqlConnection connection)
    {
        _connection = connection;
    }

    public static async Task<SqlServerIntrospector> CreateAsync(string connectionString)
    {
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync();
            return new SqlServerIntrospector(connection);
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
        var schema = schemaFilter ?? "dbo";

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT TABLE_NAME, TABLE_SCHEMA
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = @schema
              AND TABLE_TYPE = 'BASE TABLE'
            ORDER BY TABLE_NAME";
        cmd.Parameters.AddWithValue("@schema", schema);

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
        schema ??= "dbo";

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT
                c.COLUMN_NAME,
                c.DATA_TYPE,
                c.IS_NULLABLE,
                c.CHARACTER_MAXIMUM_LENGTH,
                c.NUMERIC_PRECISION,
                c.NUMERIC_SCALE,
                c.COLUMN_DEFAULT,
                c.ORDINAL_POSITION,
                COLUMNPROPERTY(OBJECT_ID(c.TABLE_SCHEMA + '.' + c.TABLE_NAME), c.COLUMN_NAME, 'IsIdentity') AS IsIdentity
            FROM INFORMATION_SCHEMA.COLUMNS c
            WHERE c.TABLE_NAME = @table AND c.TABLE_SCHEMA = @schema
            ORDER BY c.ORDINAL_POSITION";
        cmd.Parameters.AddWithValue("@table", tableName);
        cmd.Parameters.AddWithValue("@schema", schema);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var dataType = reader.GetString(1).ToUpperInvariant();
            var maxLen = reader.IsDBNull(3) ? (int?)null : Convert.ToInt32(reader.GetValue(3));
            var precision = reader.IsDBNull(4) ? (int?)null : Convert.ToInt32(reader.GetValue(4));
            var scale = reader.IsDBNull(5) ? (int?)null : Convert.ToInt32(reader.GetValue(5));

            // Reconstruct full type name
            if (maxLen.HasValue && (dataType == "NVARCHAR" || dataType == "VARCHAR" || dataType == "VARBINARY" || dataType == "NCHAR" || dataType == "CHAR" || dataType == "BINARY"))
            {
                dataType = maxLen == -1 ? $"{dataType}(MAX)" : $"{dataType}({maxLen})";
            }
            else if (precision.HasValue && (dataType == "DECIMAL" || dataType == "NUMERIC"))
            {
                dataType = $"{dataType}({precision},{scale ?? 0})";
            }

            columns.Add(new ColumnMetadata(
                name: reader.GetString(0),
                dataType: dataType,
                isNullable: reader.GetString(2) == "YES",
                maxLength: maxLen,
                precision: precision,
                scale: scale,
                isIdentity: reader.IsDBNull(8) ? false : Convert.ToInt32(reader.GetValue(8)) == 1,
                defaultExpression: reader.IsDBNull(6) ? null : reader.GetString(6),
                ordinalPosition: reader.GetInt32(7)));
        }

        return columns;
    }

    public async Task<PrimaryKeyMetadata?> GetPrimaryKeyAsync(string tableName, string? schema)
    {
        schema ??= "dbo";

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT kcu.COLUMN_NAME, tc.CONSTRAINT_NAME
            FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
            JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
                ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
                AND tc.TABLE_SCHEMA = kcu.TABLE_SCHEMA
            WHERE tc.TABLE_NAME = @table
              AND tc.TABLE_SCHEMA = @schema
              AND tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
            ORDER BY kcu.ORDINAL_POSITION";
        cmd.Parameters.AddWithValue("@table", tableName);
        cmd.Parameters.AddWithValue("@schema", schema);

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
        schema ??= "dbo";

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT
                fk.name AS constraint_name,
                COL_NAME(fkc.parent_object_id, fkc.parent_column_id) AS column_name,
                OBJECT_NAME(fkc.referenced_object_id) AS referenced_table,
                COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) AS referenced_column,
                SCHEMA_NAME(rt.schema_id) AS referenced_schema,
                fk.delete_referential_action_desc,
                fk.update_referential_action_desc
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
            JOIN sys.tables rt ON fkc.referenced_object_id = rt.object_id
            WHERE fk.parent_object_id = OBJECT_ID(@qualifiedName)
            ORDER BY fk.name, fkc.constraint_column_id";
        cmd.Parameters.AddWithValue("@qualifiedName", $"{schema}.{tableName}");

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            fks.Add(new ForeignKeyMetadata(
                constraintName: reader.GetString(0),
                columnName: reader.GetString(1),
                referencedTable: reader.GetString(2),
                referencedColumn: reader.GetString(3),
                referencedSchema: reader.GetString(4),
                onDelete: NormalizeSqlServerAction(reader.GetString(5)),
                onUpdate: NormalizeSqlServerAction(reader.GetString(6))));
        }

        return fks;
    }

    public async Task<List<IndexMetadata>> GetIndexesAsync(string tableName, string? schema)
    {
        var indexes = new List<IndexMetadata>();
        schema ??= "dbo";

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT
                i.name AS index_name,
                i.is_unique,
                i.is_primary_key,
                STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal) AS columns
            FROM sys.indexes i
            JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            WHERE i.object_id = OBJECT_ID(@qualifiedName)
              AND i.name IS NOT NULL
              AND ic.is_included_column = 0
            GROUP BY i.name, i.is_unique, i.is_primary_key
            ORDER BY i.name";
        cmd.Parameters.AddWithValue("@qualifiedName", $"{schema}.{tableName}");

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            var isUnique = reader.GetBoolean(1);
            var isPrimary = reader.GetBoolean(2);
            var colStr = reader.GetString(3);
            var columns = new List<string>(colStr.Split(','));

            indexes.Add(new IndexMetadata(name, columns, isUnique, isPrimary));
        }

        return indexes;
    }

    private static string NormalizeSqlServerAction(string action)
    {
        return action.ToUpperInvariant() switch
        {
            "CASCADE" => "CASCADE",
            "SET_NULL" => "SET NULL",
            "SET_DEFAULT" => "SET DEFAULT",
            "NO_ACTION" => "NO ACTION",
            _ => "NO ACTION"
        };
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
