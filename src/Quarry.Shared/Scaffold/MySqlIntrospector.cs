using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using MySqlConnector;

namespace Quarry.Shared.Scaffold;

internal sealed class MySqlIntrospector : DatabaseIntrospectorBase
{
    private readonly string _database;

    private MySqlIntrospector(MySqlConnection connection) : base(connection)
    {
        _database = connection.Database;
    }

    public static Task<MySqlIntrospector> CreateAsync(string connectionString) =>
        CreateCoreAsync(new MySqlConnection(connectionString), c => new MySqlIntrospector((MySqlConnection)c));

    public override Task<List<TableMetadata>> GetTablesAsync(string? schemaFilter)
    {
        return ExecuteListAsync(@"
            SELECT TABLE_NAME, TABLE_SCHEMA
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = @db
              AND TABLE_TYPE = 'BASE TABLE'
            ORDER BY TABLE_NAME",
            r => new TableMetadata(r.GetString(0), r.GetString(1)),
            cmd => AddParameter(cmd, "@db", _database));
    }

    public override Task<List<ColumnMetadata>> GetColumnsAsync(string tableName, string? schema)
    {
        return ExecuteListAsync(@"
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
            ORDER BY ORDINAL_POSITION",
            r =>
            {
                var columnType = r.GetString(1);
                var extra = r.IsDBNull(8) ? "" : r.GetString(8);
                var maxLen = r.IsDBNull(3) ? (int?)null : Convert.ToInt32(r.GetValue(3));
                var precision = r.IsDBNull(4) ? (int?)null : Convert.ToInt32(r.GetValue(4));
                var scale = r.IsDBNull(5) ? (int?)null : Convert.ToInt32(r.GetValue(5));

                return new ColumnMetadata(
                    name: r.GetString(0),
                    dataType: columnType.ToUpperInvariant(),
                    isNullable: r.GetString(2) == "YES",
                    maxLength: maxLen,
                    precision: precision,
                    scale: scale,
                    isIdentity: extra.Contains("auto_increment", StringComparison.OrdinalIgnoreCase),
                    defaultExpression: r.IsDBNull(6) ? null : r.GetString(6),
                    ordinalPosition: r.GetInt32(7));
            },
            cmd =>
            {
                AddParameter(cmd, "@table", tableName);
                AddParameter(cmd, "@db", _database);
            });
    }

    public override async Task<PrimaryKeyMetadata?> GetPrimaryKeyAsync(string tableName, string? schema)
    {
        var rows = await ExecuteListAsync(@"
            SELECT kcu.COLUMN_NAME, tc.CONSTRAINT_NAME
            FROM information_schema.TABLE_CONSTRAINTS tc
            JOIN information_schema.KEY_COLUMN_USAGE kcu
                ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
                AND tc.TABLE_SCHEMA = kcu.TABLE_SCHEMA
            WHERE tc.TABLE_NAME = @table
              AND tc.TABLE_SCHEMA = @db
              AND tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
            ORDER BY kcu.ORDINAL_POSITION",
            r => (Column: r.GetString(0), Constraint: r.GetString(1)),
            cmd =>
            {
                AddParameter(cmd, "@table", tableName);
                AddParameter(cmd, "@db", _database);
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
        return ExecuteListAsync(@"
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
              AND kcu.REFERENCED_TABLE_NAME IS NOT NULL",
            r => new ForeignKeyMetadata(
                constraintName: r.GetString(0),
                columnName: r.GetString(1),
                referencedTable: r.GetString(2),
                referencedColumn: r.GetString(3),
                referencedSchema: r.IsDBNull(4) ? null : r.GetString(4),
                onDelete: r.GetString(5),
                onUpdate: r.GetString(6)),
            cmd =>
            {
                AddParameter(cmd, "@table", tableName);
                AddParameter(cmd, "@db", _database);
            });
    }

    public override async Task<List<IndexMetadata>> GetIndexesAsync(string tableName, string? schema)
    {
        var rawRows = await ExecuteListAsync(
            $"SHOW INDEX FROM `{tableName.Replace("`", "``")}`",
            r => (KeyName: r.GetString(2), Seq: r.GetInt32(3), ColName: r.GetString(4), NonUnique: r.GetInt32(1)));

        var indexMap = new Dictionary<string, (bool IsUnique, bool IsPrimary, List<(int Seq, string Col)> Columns)>();
        foreach (var (keyName, seq, colName, nonUnique) in rawRows)
        {
            if (!indexMap.TryGetValue(keyName, out var entry))
            {
                entry = (nonUnique == 0, keyName == "PRIMARY", new List<(int, string)>());
                indexMap[keyName] = entry;
            }
            entry.Columns.Add((seq, colName));
        }

        var indexes = new List<IndexMetadata>();
        foreach (var (name, (isUnique, isPrimary, cols)) in indexMap)
        {
            cols.Sort((a, b) => a.Seq.CompareTo(b.Seq));
            indexes.Add(new IndexMetadata(name, cols.ConvertAll(c => c.Col), isUnique, isPrimary));
        }

        return indexes;
    }
}
