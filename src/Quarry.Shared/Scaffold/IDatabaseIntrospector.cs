using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Quarry.Shared.Scaffold;

internal interface IDatabaseIntrospector : IDisposable
{
    Task<List<TableMetadata>> GetTablesAsync(string? schemaFilter);
    Task<List<ColumnMetadata>> GetColumnsAsync(string tableName, string? schema);
    Task<PrimaryKeyMetadata?> GetPrimaryKeyAsync(string tableName, string? schema);
    Task<List<ForeignKeyMetadata>> GetForeignKeysAsync(string tableName, string? schema);
    Task<List<IndexMetadata>> GetIndexesAsync(string tableName, string? schema);
}
