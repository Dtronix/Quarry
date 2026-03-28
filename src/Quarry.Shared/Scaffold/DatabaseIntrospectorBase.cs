using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;

namespace Quarry.Shared.Scaffold;

/// <summary>
/// Base class for database introspectors providing shared connection management
/// and query execution helpers.
/// </summary>
internal abstract class DatabaseIntrospectorBase : IDatabaseIntrospector
{
    private readonly DbConnection _connection;

    protected DatabaseIntrospectorBase(DbConnection connection)
    {
        _connection = connection;
    }

    protected DbConnection Connection => _connection;

    /// <summary>
    /// Opens a connection and wraps it in the concrete introspector, disposing on failure.
    /// </summary>
    protected static async Task<T> CreateCoreAsync<T>(DbConnection connection, Func<DbConnection, T> factory)
    {
        try
        {
            await connection.OpenAsync();
            return factory(connection);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Executes a query and maps each row to a result, collecting into a list.
    /// </summary>
    protected async Task<List<T>> ExecuteListAsync<T>(
        string sql,
        Func<DbDataReader, T> mapRow,
        Action<DbCommand>? configureCommand = null)
    {
        var results = new List<T>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        configureCommand?.Invoke(cmd);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(mapRow(reader));
        }

        return results;
    }

    /// <summary>
    /// Adds a parameter to a command using the generic DbCommand API.
    /// </summary>
    protected static void AddParameter(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    public abstract Task<List<TableMetadata>> GetTablesAsync(string? schemaFilter);
    public abstract Task<List<ColumnMetadata>> GetColumnsAsync(string tableName, string? schema);
    public abstract Task<PrimaryKeyMetadata?> GetPrimaryKeyAsync(string tableName, string? schema);
    public abstract Task<List<ForeignKeyMetadata>> GetForeignKeysAsync(string tableName, string? schema);
    public abstract Task<List<IndexMetadata>> GetIndexesAsync(string tableName, string? schema);

    public void Dispose()
    {
        _connection.Dispose();
    }
}
