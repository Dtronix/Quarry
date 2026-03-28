using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using Npgsql;

namespace Quarry.Shared.Scaffold;

internal sealed class PostgreSqlIntrospector : DatabaseIntrospectorBase
{
    private PostgreSqlIntrospector(NpgsqlConnection connection) : base(connection) { }

    public static Task<PostgreSqlIntrospector> CreateAsync(string connectionString) =>
        CreateCoreAsync(new NpgsqlConnection(connectionString), c => new PostgreSqlIntrospector((NpgsqlConnection)c));

    public override Task<List<TableMetadata>> GetTablesAsync(string? schemaFilter)
    {
        var schema = schemaFilter ?? "public";
        return ExecuteListAsync(@"
            SELECT table_name, table_schema
            FROM information_schema.tables
            WHERE table_schema = @schema
              AND table_type = 'BASE TABLE'
            ORDER BY table_name",
            r => new TableMetadata(r.GetString(0), r.GetString(1)),
            cmd => AddParameter(cmd, "schema", schema));
    }

    public override Task<List<ColumnMetadata>> GetColumnsAsync(string tableName, string? schema)
    {
        schema ??= "public";
        return ExecuteListAsync(@"
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
            ORDER BY c.ordinal_position",
            r =>
            {
                var dataType = r.GetString(1);
                var udtName = r.IsDBNull(8) ? null : r.GetString(8);

                if (dataType == "USER-DEFINED" && udtName != null)
                    dataType = udtName;
                if (dataType == "ARRAY" && udtName != null)
                    dataType = udtName;

                var maxLen = r.IsDBNull(3) ? (int?)null : r.GetInt32(3);
                var precision = r.IsDBNull(4) ? (int?)null : r.GetInt32(4);
                var scale = r.IsDBNull(5) ? (int?)null : r.GetInt32(5);

                return new ColumnMetadata(
                    name: r.GetString(0),
                    dataType: dataType,
                    isNullable: r.GetString(2) == "YES",
                    maxLength: maxLen,
                    precision: precision,
                    scale: scale,
                    isIdentity: r.GetBoolean(9),
                    defaultExpression: r.IsDBNull(6) ? null : r.GetString(6),
                    ordinalPosition: r.GetInt32(7));
            },
            cmd =>
            {
                AddParameter(cmd, "table", tableName);
                AddParameter(cmd, "schema", schema);
            });
    }

    public override async Task<PrimaryKeyMetadata?> GetPrimaryKeyAsync(string tableName, string? schema)
    {
        schema ??= "public";

        var rows = await ExecuteListAsync(@"
            SELECT kcu.column_name, tc.constraint_name
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
                ON tc.constraint_name = kcu.constraint_name
                AND tc.table_schema = kcu.table_schema
            WHERE tc.table_name = @table
              AND tc.table_schema = @schema
              AND tc.constraint_type = 'PRIMARY KEY'
            ORDER BY kcu.ordinal_position",
            r => (Column: r.GetString(0), Constraint: r.GetString(1)),
            cmd =>
            {
                AddParameter(cmd, "table", tableName);
                AddParameter(cmd, "schema", schema);
            });

        if (rows.Count == 0) return null;

        string? constraintName = null;
        var columns = new List<string>();
        foreach (var (col, constraint) in rows)
        {
            columns.Add(col);
            constraintName ??= constraint;
        }

        return new PrimaryKeyMetadata(constraintName, columns);
    }

    public override Task<List<ForeignKeyMetadata>> GetForeignKeysAsync(string tableName, string? schema)
    {
        schema ??= "public";
        return ExecuteListAsync(@"
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
            ORDER BY c.conname, k.ord",
            r => new ForeignKeyMetadata(
                constraintName: r.GetString(0),
                columnName: r.GetString(1),
                referencedTable: r.GetString(2),
                referencedColumn: r.GetString(3),
                referencedSchema: r.GetString(4),
                onDelete: r.GetString(5),
                onUpdate: r.GetString(6)),
            cmd =>
            {
                AddParameter(cmd, "table", tableName);
                AddParameter(cmd, "schema", schema);
            });
    }

    public override Task<List<IndexMetadata>> GetIndexesAsync(string tableName, string? schema)
    {
        schema ??= "public";
        return ExecuteListAsync(@"
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
            ORDER BY i.relname",
            r =>
            {
                var colArray = (string[])r.GetValue(1);
                return new IndexMetadata(r.GetString(0), colArray, r.GetBoolean(2), r.GetBoolean(3));
            },
            cmd =>
            {
                AddParameter(cmd, "table", tableName);
                AddParameter(cmd, "schema", schema);
            });
    }
}
