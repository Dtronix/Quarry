using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Quarry.Shared.Scaffold;

internal sealed class SqlServerIntrospector : DatabaseIntrospectorBase
{
    private SqlServerIntrospector(SqlConnection connection) : base(connection) { }

    public static Task<SqlServerIntrospector> CreateAsync(string connectionString) =>
        CreateCoreAsync(new SqlConnection(connectionString), c => new SqlServerIntrospector((SqlConnection)c));

    public override Task<List<TableMetadata>> GetTablesAsync(string? schemaFilter)
    {
        var schema = schemaFilter ?? "dbo";
        return ExecuteListAsync(@"
            SELECT TABLE_NAME, TABLE_SCHEMA
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = @schema
              AND TABLE_TYPE = 'BASE TABLE'
            ORDER BY TABLE_NAME",
            r => new TableMetadata(r.GetString(0), r.GetString(1)),
            cmd => AddParameter(cmd, "@schema", schema));
    }

    public override Task<List<ColumnMetadata>> GetColumnsAsync(string tableName, string? schema)
    {
        schema ??= "dbo";
        return ExecuteListAsync(@"
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
            ORDER BY c.ORDINAL_POSITION",
            r =>
            {
                var dataType = r.GetString(1).ToUpperInvariant();
                var maxLen = r.IsDBNull(3) ? (int?)null : Convert.ToInt32(r.GetValue(3));
                var precision = r.IsDBNull(4) ? (int?)null : Convert.ToInt32(r.GetValue(4));
                var scale = r.IsDBNull(5) ? (int?)null : Convert.ToInt32(r.GetValue(5));

                if (maxLen.HasValue && (dataType == "NVARCHAR" || dataType == "VARCHAR" || dataType == "VARBINARY" || dataType == "NCHAR" || dataType == "CHAR" || dataType == "BINARY"))
                    dataType = maxLen == -1 ? $"{dataType}(MAX)" : $"{dataType}({maxLen})";
                else if (precision.HasValue && (dataType == "DECIMAL" || dataType == "NUMERIC"))
                    dataType = $"{dataType}({precision},{scale ?? 0})";

                return new ColumnMetadata(
                    name: r.GetString(0),
                    dataType: dataType,
                    isNullable: r.GetString(2) == "YES",
                    maxLength: maxLen,
                    precision: precision,
                    scale: scale,
                    isIdentity: r.IsDBNull(8) ? false : Convert.ToInt32(r.GetValue(8)) == 1,
                    defaultExpression: r.IsDBNull(6) ? null : r.GetString(6),
                    ordinalPosition: r.GetInt32(7));
            },
            cmd =>
            {
                AddParameter(cmd, "@table", tableName);
                AddParameter(cmd, "@schema", schema);
            });
    }

    public override async Task<PrimaryKeyMetadata?> GetPrimaryKeyAsync(string tableName, string? schema)
    {
        schema ??= "dbo";
        var rows = await ExecuteListAsync(@"
            SELECT kcu.COLUMN_NAME, tc.CONSTRAINT_NAME
            FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
            JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
                ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
                AND tc.TABLE_SCHEMA = kcu.TABLE_SCHEMA
            WHERE tc.TABLE_NAME = @table
              AND tc.TABLE_SCHEMA = @schema
              AND tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
            ORDER BY kcu.ORDINAL_POSITION",
            r => (Column: r.GetString(0), Constraint: r.GetString(1)),
            cmd =>
            {
                AddParameter(cmd, "@table", tableName);
                AddParameter(cmd, "@schema", schema);
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
        schema ??= "dbo";
        return ExecuteListAsync(@"
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
            ORDER BY fk.name, fkc.constraint_column_id",
            r => new ForeignKeyMetadata(
                constraintName: r.GetString(0),
                columnName: r.GetString(1),
                referencedTable: r.GetString(2),
                referencedColumn: r.GetString(3),
                referencedSchema: r.GetString(4),
                onDelete: NormalizeSqlServerAction(r.GetString(5)),
                onUpdate: NormalizeSqlServerAction(r.GetString(6))),
            cmd => AddParameter(cmd, "@qualifiedName", $"{schema}.{tableName}"));
    }

    public override Task<List<IndexMetadata>> GetIndexesAsync(string tableName, string? schema)
    {
        schema ??= "dbo";
        return ExecuteListAsync(@"
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
            ORDER BY i.name",
            r => new IndexMetadata(
                r.GetString(0),
                new List<string>(r.GetString(3).Split(',')),
                r.GetBoolean(1),
                r.GetBoolean(2)),
            cmd => AddParameter(cmd, "@qualifiedName", $"{schema}.{tableName}"));
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
}
