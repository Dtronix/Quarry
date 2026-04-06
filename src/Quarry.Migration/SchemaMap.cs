using System;
using System.Collections.Generic;

namespace Quarry.Migration;

/// <summary>
/// Maps SQL table names to Quarry entity types and SQL column names to entity property names.
/// </summary>
internal sealed class SchemaMap
{
    private readonly Dictionary<string, EntityMapping> _entities;

    public SchemaMap(Dictionary<string, EntityMapping> entities)
    {
        _entities = entities;
    }

    /// <summary>
    /// Tries to find an entity mapping for the given SQL table name (case-insensitive).
    /// </summary>
    public bool TryGetEntity(string tableName, out EntityMapping mapping)
        => _entities.TryGetValue(tableName, out mapping!);

    public IEnumerable<EntityMapping> Entities => _entities.Values;
}

/// <summary>
/// Mapping from SQL table to a Quarry entity type.
/// </summary>
internal sealed class EntityMapping
{
    /// <summary>SQL table name (e.g., "users").</summary>
    public string TableName { get; }

    /// <summary>SQL schema name (e.g., "dbo"), or null if default.</summary>
    public string? SchemaName { get; }

    /// <summary>C# schema class name (e.g., "UserSchema").</summary>
    public string ClassName { get; }

    /// <summary>Chain API accessor method name (e.g., "Users").</summary>
    public string AccessorName { get; }

    /// <summary>
    /// Maps SQL column names (case-insensitive) to C# property names.
    /// </summary>
    private readonly Dictionary<string, string> _columns;

    public EntityMapping(
        string tableName,
        string? schemaName,
        string className,
        string accessorName,
        Dictionary<string, string> columns)
    {
        TableName = tableName;
        SchemaName = schemaName;
        ClassName = className;
        AccessorName = accessorName;
        _columns = columns;
    }

    /// <summary>
    /// Tries to resolve a SQL column name to a C# property name (case-insensitive).
    /// </summary>
    public bool TryGetProperty(string columnName, out string propertyName)
        => _columns.TryGetValue(columnName, out propertyName!);

    public IEnumerable<KeyValuePair<string, string>> Columns => _columns;
}
