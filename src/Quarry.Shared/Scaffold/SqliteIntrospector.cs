using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Quarry.Shared.Scaffold;

internal sealed class SqliteIntrospector : DatabaseIntrospectorBase
{
    private SqliteIntrospector(SqliteConnection connection) : base(connection) { }

    public static Task<SqliteIntrospector> CreateAsync(string connectionString) =>
        CreateCoreAsync(new SqliteConnection(connectionString), c => new SqliteIntrospector((SqliteConnection)c));

    public override Task<List<TableMetadata>> GetTablesAsync(string? schemaFilter)
    {
        return ExecuteListAsync(
            "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name",
            r => new TableMetadata(r.GetString(0), null));
    }

    public override Task<List<ColumnMetadata>> GetColumnsAsync(string tableName, string? schema)
    {
        return ExecuteListAsync(
            $"PRAGMA table_info({QuoteIdentifier(tableName)})",
            r =>
            {
                var cid = r.GetInt32(0);
                var name = r.GetString(1);
                var type = r.IsDBNull(2) ? "TEXT" : r.GetString(2);
                var notNull = r.GetInt32(3) != 0;
                var defaultValue = r.IsDBNull(4) ? null : r.GetString(4);
                var pk = r.GetInt32(5);

                if (string.IsNullOrWhiteSpace(type))
                    type = "TEXT";

                var isIdentity = pk > 0 && type.Equals("INTEGER", StringComparison.OrdinalIgnoreCase);

                return new ColumnMetadata(
                    name: name,
                    dataType: type.ToUpperInvariant(),
                    isNullable: !notNull && pk == 0,
                    isIdentity: isIdentity,
                    defaultExpression: defaultValue,
                    ordinalPosition: cid);
            });
    }

    public override async Task<PrimaryKeyMetadata?> GetPrimaryKeyAsync(string tableName, string? schema)
    {
        var allColumns = await ExecuteListAsync(
            $"PRAGMA table_info({QuoteIdentifier(tableName)})",
            r => (Pk: r.GetInt32(5), Name: r.GetString(1)));

        var pkColumns = new List<(int PkOrder, string Name)>();
        foreach (var (pk, name) in allColumns)
        {
            if (pk > 0)
                pkColumns.Add((pk, name));
        }

        if (pkColumns.Count == 0)
            return null;

        pkColumns.Sort((a, b) => a.PkOrder.CompareTo(b.PkOrder));
        return new PrimaryKeyMetadata(null, pkColumns.ConvertAll(p => p.Name));
    }

    public override Task<List<ForeignKeyMetadata>> GetForeignKeysAsync(string tableName, string? schema)
    {
        return ExecuteListAsync(
            $"PRAGMA foreign_key_list({QuoteIdentifier(tableName)})",
            r => new ForeignKeyMetadata(
                constraintName: $"FK_{tableName}_{r.GetString(3)}",
                columnName: r.GetString(3),
                referencedTable: r.GetString(2),
                referencedColumn: r.GetString(4),
                onDelete: NormalizeFkAction(r.GetString(6)),
                onUpdate: NormalizeFkAction(r.GetString(5))));
    }

    public override async Task<List<IndexMetadata>> GetIndexesAsync(string tableName, string? schema)
    {
        var indexEntries = await ExecuteListAsync(
            $"PRAGMA index_list({QuoteIdentifier(tableName)})",
            r => (Name: r.GetString(1), IsUnique: r.GetInt32(2) != 0, Origin: r.GetString(3)));

        var indexes = new List<IndexMetadata>();
        foreach (var (name, isUnique, origin) in indexEntries)
        {
            var columns = await ExecuteListAsync(
                $"PRAGMA index_info({QuoteIdentifier(name)})",
                r => r.IsDBNull(2) ? null : r.GetString(2));

            var nonNullColumns = columns.FindAll(c => c != null)!;
            if (nonNullColumns.Count > 0)
            {
                indexes.Add(new IndexMetadata(name, nonNullColumns!, isUnique, isPrimaryKey: origin == "pk"));
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
}
