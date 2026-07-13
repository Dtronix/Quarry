using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Quarry.Shared.Scaffold;

namespace Quarry.Tool.Schema;

/// <summary>
/// Full introspection metadata for a single table (columns, primary key, foreign
/// keys, indexes). Shared by scaffolding and the migrate add/baseline/adopt commands.
/// </summary>
internal sealed class TableIntrospectionData
{
    public TableMetadata Table { get; }
    public List<ColumnMetadata> Columns { get; }
    public PrimaryKeyMetadata? PrimaryKey { get; }
    public List<ForeignKeyMetadata> ForeignKeys { get; }
    public List<IndexMetadata> Indexes { get; }

    public TableIntrospectionData(
        TableMetadata table,
        List<ColumnMetadata> columns,
        PrimaryKeyMetadata? primaryKey,
        List<ForeignKeyMetadata> foreignKeys,
        List<IndexMetadata> indexes)
    {
        Table = table;
        Columns = columns;
        PrimaryKey = primaryKey;
        ForeignKeys = foreignKeys;
        Indexes = indexes;
    }
}

/// <summary>
/// Shared database-introspection helpers: connection-string building, introspector
/// creation, and reading full per-table metadata from a live database. Used by the
/// <c>scaffold</c> command and by the <c>migrate add --from-database</c>,
/// <c>migrate baseline</c>, and <c>migrate adopt</c> commands.
/// </summary>
internal static class DatabaseSchemaReader
{
    /// <summary>
    /// Creates a dialect-specific database introspector over an open connection.
    /// </summary>
    public static async Task<IDatabaseIntrospector> CreateIntrospectorAsync(string dialect, string connectionString)
    {
        return dialect.ToLowerInvariant() switch
        {
            "sqlite" => await SqliteIntrospector.CreateAsync(connectionString),
            "postgresql" or "postgres" or "pg" => await PostgreSqlIntrospector.CreateAsync(connectionString),
            "sqlserver" or "mssql" => await SqlServerIntrospector.CreateAsync(connectionString),
            "mysql" => await MySqlIntrospector.CreateAsync(connectionString),
            _ => throw new InvalidOperationException($"Unknown dialect: {dialect}")
        };
    }

    /// <summary>
    /// Builds a connection string from discrete parts for the given dialect.
    /// </summary>
    public static string BuildConnectionString(string dialect, string? server, string? port, string? user, string? password, string database)
    {
        return dialect.ToLowerInvariant() switch
        {
            "sqlite" => BuildSqliteConnectionString(database),
            "postgresql" or "postgres" or "pg" => BuildNpgsqlConnectionString(server, port, user, password, database),
            "sqlserver" or "mssql" => BuildSqlServerConnectionString(server, port, user, password, database),
            "mysql" => BuildMySqlConnectionString(server, port, user, password, database),
            _ => throw new InvalidOperationException($"Cannot build connection string for dialect: {dialect}")
        };
    }

    /// <summary>
    /// Introspects all tables (after applying the optional table filter) and returns
    /// their full metadata. Pure data gathering — no console output — so it can be
    /// reused by any command.
    /// </summary>
    public static async Task<IReadOnlyList<TableIntrospectionData>> ReadTablesAsync(
        string dialect, string connectionString, string? schemaFilter, string? tables)
    {
        using var introspector = await CreateIntrospectorAsync(dialect, connectionString);

        var allTables = await introspector.GetTablesAsync(schemaFilter);
        allTables = TableFilter.Apply(allTables, tables);

        var result = new List<TableIntrospectionData>(allTables.Count);
        foreach (var table in allTables)
        {
            var columns = await introspector.GetColumnsAsync(table.Name, table.Schema);
            var pk = await introspector.GetPrimaryKeyAsync(table.Name, table.Schema);
            var fks = await introspector.GetForeignKeysAsync(table.Name, table.Schema);
            var indexes = await introspector.GetIndexesAsync(table.Name, table.Schema);

            result.Add(new TableIntrospectionData(table, columns, pk, fks, indexes));
        }

        return result;
    }

    private static string BuildSqliteConnectionString(string database)
    {
        var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = database };
        return builder.ConnectionString;
    }

    private static string BuildNpgsqlConnectionString(string? server, string? port, string? user, string? password, string database)
    {
        var builder = new Npgsql.NpgsqlConnectionStringBuilder
        {
            Host = server ?? "localhost",
            Port = int.TryParse(port, out var p) ? p : 5432,
            Database = database
        };
        if (user != null) builder.Username = user;
        if (password != null) builder.Password = password;
        return builder.ConnectionString;
    }

    private static string BuildSqlServerConnectionString(string? server, string? port, string? user, string? password, string database)
    {
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
        {
            DataSource = (server ?? "localhost") + (port != null ? $",{port}" : ""),
            InitialCatalog = database,
            TrustServerCertificate = true
        };
        if (user != null)
        {
            builder.UserID = user;
            if (password != null) builder.Password = password;
        }
        else
        {
            builder.IntegratedSecurity = true;
        }
        return builder.ConnectionString;
    }

    private static string BuildMySqlConnectionString(string? server, string? port, string? user, string? password, string database)
    {
        var builder = new MySqlConnector.MySqlConnectionStringBuilder
        {
            Server = server ?? "localhost",
            Port = uint.TryParse(port, out var p) ? p : 3306,
            Database = database
        };
        if (user != null) builder.UserID = user;
        if (password != null) builder.Password = password;
        return builder.ConnectionString;
    }
}
