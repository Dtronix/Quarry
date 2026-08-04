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

    /// <summary>
    /// Tries to find an entity mapping by C# entity type name (e.g., "User" matches "UserSchema").
    /// Used by EF Core converter where detection provides the entity type, not the SQL table name.
    /// </summary>
    public bool TryGetEntityByTypeName(string typeName, out EntityMapping mapping)
    {
        foreach (var entity in _entities.Values)
        {
            if (string.Equals(entity.ClassName, typeName + "Schema", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(entity.ClassName, typeName, StringComparison.OrdinalIgnoreCase))
            {
                mapping = entity;
                return true;
            }
        }

        mapping = null!;
        return false;
    }

    public IEnumerable<EntityMapping> Entities => _entities.Values;
}

/// <summary>
/// A single column's mapping detail: the C# property it binds to and its CLR type.
/// </summary>
/// <remarks>
/// The CLR type is only needed to synthesize CTE DTO properties (#331); the rest of the
/// converter works from property names alone.
/// </remarks>
internal sealed class ColumnMapping
{
    /// <summary>C# property name (e.g., "UserId").</summary>
    public string PropertyName { get; }

    /// <summary>CLR type name as written in source (e.g., "int", "string?").</summary>
    public string ClrTypeName { get; }

    /// <summary>True for <c>Ref&lt;TEntity, TKey&gt;</c> columns, which surface as wrapper types.</summary>
    public bool IsForeignKey { get; }

    public ColumnMapping(string propertyName, string clrTypeName, bool isForeignKey)
    {
        PropertyName = propertyName;
        ClrTypeName = clrTypeName;
        IsForeignKey = isForeignKey;
    }
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
    /// The entity type name used in chain generic arguments (e.g., "User"), derived by
    /// stripping the <c>Schema</c> suffix from <see cref="ClassName"/>.
    /// </summary>
    public string EntityTypeName { get; }

    /// <summary>
    /// Maps SQL column names (case-insensitive) to C# property names.
    /// </summary>
    private readonly Dictionary<string, string> _columns;

    /// <summary>
    /// Per-column detail including CLR types. Null when the mapping was built without it,
    /// in which case CTE DTO synthesis is not possible and is refused rather than guessed.
    /// </summary>
    private readonly Dictionary<string, ColumnMapping>? _columnDetails;

    public EntityMapping(
        string tableName,
        string? schemaName,
        string className,
        string accessorName,
        Dictionary<string, string> columns,
        Dictionary<string, ColumnMapping>? columnDetails = null)
    {
        TableName = tableName;
        SchemaName = schemaName;
        ClassName = className;
        AccessorName = accessorName;
        EntityTypeName = className.EndsWith("Schema", StringComparison.Ordinal)
            ? className.Substring(0, className.Length - "Schema".Length)
            : className;
        _columns = columns;
        _columnDetails = columnDetails;
    }

    /// <summary>
    /// Tries to resolve a SQL column name to a C# property name (case-insensitive).
    /// </summary>
    public bool TryGetProperty(string columnName, out string propertyName)
        => _columns.TryGetValue(columnName, out propertyName!);

    /// <summary>
    /// Tries to resolve a SQL column name to its full mapping detail, including CLR type.
    /// Returns false when this mapping carries no type information.
    /// </summary>
    public bool TryGetColumn(string columnName, out ColumnMapping column)
    {
        if (_columnDetails != null && _columnDetails.TryGetValue(columnName, out column!))
            return true;

        column = null!;
        return false;
    }

    public IEnumerable<KeyValuePair<string, string>> Columns => _columns;
}
