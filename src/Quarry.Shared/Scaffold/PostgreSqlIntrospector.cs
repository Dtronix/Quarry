using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using Npgsql;

namespace Quarry.Shared.Scaffold;

internal sealed class PostgreSqlIntrospector : IDatabaseIntrospector
{
    private readonly NpgsqlConnection _connection;

    private PostgreSqlIntrospector(NpgsqlConnection connection)
    {
        _connection = connection;
    }

    public static async Task<PostgreSqlIntrospector> CreateAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync();
            return new PostgreSqlIntrospector(connection);
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
        var schema = schemaFilter ?? "public";

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT table_name, table_schema
            FROM information_schema.tables
            WHERE table_schema = @schema
              AND table_type = 'BASE TABLE'
            ORDER BY table_name";
        cmd.Parameters.AddWithValue("schema", schema);

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
        schema ??= "public";

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT
                c.column_name,
                c.data_type,
                c.is_nullable,
                c.character_maximum_length,
                c.numeric_precision,
                c.numeric_scale,
                c.column_default,
                c.ordinal_position,
                c.udt_name,
                CASE WHEN c.is_identity = 'YES' THEN true
                     WHEN c.column_default LIKE 'nextval%' THEN true
                     ELSE false END AS is_identity
            FROM information_schema.columns c
            WHERE c.table_name = @table AND c.table_schema = @schema
            ORDER BY c.ordinal_position";
        cmd.Parameters.AddWithValue("table", tableName);
        cmd.Parameters.AddWithValue("schema", schema);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var dataType = reader.GetString(1);
            var udtName = reader.IsDBNull(8) ? null : reader.GetString(8);

            // Use UDT name for user-defined types and array types
            if (dataType == "USER-DEFINED" && udtName != null)
                dataType = udtName;
            if (dataType == "ARRAY" && udtName != null)
                dataType = udtName;

            var maxLen = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3);
            var precision = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4);
            var scale = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5);

            // For types like varchar, numeric — use precision/scale or maxLength
            var resolvedType = ResolvePostgresType(dataType, maxLen, precision, scale);

            columns.Add(new ColumnMetadata(
                name: reader.GetString(0),
                dataType: resolvedType,
                isNullable: reader.GetString(2) == "YES",
                maxLength: maxLen,
                precision: precision,
                scale: scale,
                isIdentity: reader.GetBoolean(9),
                defaultExpression: reader.IsDBNull(6) ? null : reader.GetString(6),
                ordinalPosition: reader.GetInt32(7)));
        }

        return columns;
    }

    public async Task<PrimaryKeyMetadata?> GetPrimaryKeyAsync(string tableName, string? schema)
    {
        schema ??= "public";

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT kcu.column_name, tc.constraint_name
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
                ON tc.constraint_name = kcu.constraint_name
                AND tc.table_schema = kcu.table_schema
            WHERE tc.table_name = @table
              AND tc.table_schema = @schema
              AND tc.constraint_type = 'PRIMARY KEY'
            ORDER BY kcu.ordinal_position";
        cmd.Parameters.AddWithValue("table", tableName);
        cmd.Parameters.AddWithValue("schema", schema);

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
        schema ??= "public";

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT
                c.conname AS constraint_name,
                a_src.attname AS column_name,
                ref_t.relname AS referenced_table,
                a_ref.attname AS referenced_column,
                ref_ns.nspname AS referenced_schema,
                CASE c.confdeltype
                    WHEN 'a' THEN 'NO ACTION'
                    WHEN 'r' THEN 'RESTRICT'
                    WHEN 'c' THEN 'CASCADE'
                    WHEN 'n' THEN 'SET NULL'
                    WHEN 'd' THEN 'SET DEFAULT'
                    ELSE 'NO ACTION'
                END AS delete_rule,
                CASE c.confupdtype
                    WHEN 'a' THEN 'NO ACTION'
                    WHEN 'r' THEN 'RESTRICT'
                    WHEN 'c' THEN 'CASCADE'
                    WHEN 'n' THEN 'SET NULL'
                    WHEN 'd' THEN 'SET DEFAULT'
                    ELSE 'NO ACTION'
                END AS update_rule
            FROM pg_constraint c
            JOIN pg_class t ON t.oid = c.conrelid
            JOIN pg_namespace n ON n.oid = t.relnamespace
            JOIN pg_class ref_t ON ref_t.oid = c.confrelid
            JOIN pg_namespace ref_ns ON ref_ns.oid = ref_t.relnamespace
            CROSS JOIN LATERAL unnest(c.conkey, c.confkey) WITH ORDINALITY AS k(src_attnum, ref_attnum, ord)
            JOIN pg_attribute a_src ON a_src.attrelid = t.oid AND a_src.attnum = k.src_attnum
            JOIN pg_attribute a_ref ON a_ref.attrelid = ref_t.oid AND a_ref.attnum = k.ref_attnum
            WHERE t.relname = @table
              AND n.nspname = @schema
              AND c.contype = 'f'
            ORDER BY c.conname, k.ord";
        cmd.Parameters.AddWithValue("table", tableName);
        cmd.Parameters.AddWithValue("schema", schema);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            fks.Add(new ForeignKeyMetadata(
                constraintName: reader.GetString(0),
                columnName: reader.GetString(1),
                referencedTable: reader.GetString(2),
                referencedColumn: reader.GetString(3),
                referencedSchema: reader.GetString(4),
                onDelete: reader.GetString(5),
                onUpdate: reader.GetString(6)));
        }

        return fks;
    }

    public async Task<List<IndexMetadata>> GetIndexesAsync(string tableName, string? schema)
    {
        var indexes = new List<IndexMetadata>();
        schema ??= "public";

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT
                i.relname AS index_name,
                array_agg(a.attname ORDER BY k.n) AS columns,
                ix.indisunique AS is_unique,
                ix.indisprimary AS is_primary
            FROM pg_index ix
            JOIN pg_class t ON t.oid = ix.indrelid
            JOIN pg_class i ON i.oid = ix.indexrelid
            JOIN pg_namespace n ON n.oid = t.relnamespace
            CROSS JOIN LATERAL unnest(ix.indkey) WITH ORDINALITY AS k(attnum, n)
            JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = k.attnum
            WHERE t.relname = @table
              AND n.nspname = @schema
            GROUP BY i.relname, ix.indisunique, ix.indisprimary
            ORDER BY i.relname";
        cmd.Parameters.AddWithValue("table", tableName);
        cmd.Parameters.AddWithValue("schema", schema);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            var colArray = (string[])reader.GetValue(1);
            var isUnique = reader.GetBoolean(2);
            var isPrimary = reader.GetBoolean(3);

            indexes.Add(new IndexMetadata(name, colArray, isUnique, isPrimary));
        }

        return indexes;
    }

    private static string ResolvePostgresType(string dataType, int? maxLen, int? precision, int? scale)
    {
        // Return the type as-is; the ReverseTypeMapper handles normalization
        return dataType;
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
