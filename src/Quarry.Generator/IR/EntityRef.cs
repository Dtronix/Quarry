using System;
using System.Collections.Generic;
using Quarry.Generators.Models;

namespace Quarry.Generators.IR;

/// <summary>
/// Lightweight reference to entity metadata for column resolution.
/// Avoids carrying the full EntityInfo (which includes Location and other heavy fields).
/// </summary>
internal sealed class EntityRef : IEquatable<EntityRef>
{
    public EntityRef(
        string entityName,
        string tableName,
        string? schemaName,
        string schemaNamespace,
        IReadOnlyList<ColumnInfo> columns,
        IReadOnlyList<NavigationInfo> navigations,
        string? customEntityReaderClass = null)
    {
        EntityName = entityName;
        TableName = tableName;
        SchemaName = schemaName;
        SchemaNamespace = schemaNamespace;
        Columns = columns;
        Navigations = navigations;
        CustomEntityReaderClass = customEntityReaderClass;
    }

    public string EntityName { get; }
    public string TableName { get; }
    public string? SchemaName { get; }
    public string SchemaNamespace { get; }
    public IReadOnlyList<ColumnInfo> Columns { get; }
    public IReadOnlyList<NavigationInfo> Navigations { get; }
    public string? CustomEntityReaderClass { get; }

    /// <summary>
    /// Creates an EntityRef from an EntityInfo.
    /// </summary>
    public static EntityRef FromEntityInfo(EntityInfo entity)
    {
        return new EntityRef(
            entity.EntityName,
            entity.TableName,
            schemaName: null, // EntityInfo doesn't carry schema name directly
            entity.SchemaNamespace,
            entity.Columns,
            entity.Navigations,
            entity.CustomEntityReaderClass);
    }

    public bool Equals(EntityRef? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return EntityName == other.EntityName
            && TableName == other.TableName
            && SchemaName == other.SchemaName
            && SchemaNamespace == other.SchemaNamespace
            && CustomEntityReaderClass == other.CustomEntityReaderClass
            && EqualityHelpers.SequenceEqual(Columns, other.Columns)
            && EqualityHelpers.SequenceEqual(Navigations, other.Navigations);
    }

    public override bool Equals(object? obj) => Equals(obj as EntityRef);

    public override int GetHashCode()
    {
        return HashCode.Combine(EntityName, TableName, SchemaNamespace, Columns.Count);
    }
}
