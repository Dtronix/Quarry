using System;
using Microsoft.CodeAnalysis;
using Quarry.Shared.Migration;

namespace Quarry.Generators.Models;

/// <summary>
/// Represents a discovered entity type and its schema metadata.
/// </summary>
internal sealed class EntityInfo : IEquatable<EntityInfo>
{
    /// <summary>
    /// The entity class name (e.g., "User").
    /// </summary>
    public string EntityName { get; }

    /// <summary>
    /// The schema class name (e.g., "UserSchema").
    /// </summary>
    public string SchemaClassName { get; }

    /// <summary>
    /// The namespace of the schema class.
    /// </summary>
    public string SchemaNamespace { get; }

    /// <summary>
    /// The database table name from the schema's Table property.
    /// </summary>
    public string TableName { get; }

    /// <summary>
    /// The naming style for column name mapping.
    /// </summary>
    public NamingStyleKind NamingStyle { get; }

    /// <summary>
    /// The columns defined in the schema.
    /// </summary>
    public IReadOnlyList<ColumnInfo> Columns { get; }

    /// <summary>
    /// The navigation properties (Many&lt;T&gt;) defined in the schema.
    /// </summary>
    public IReadOnlyList<NavigationInfo> Navigations { get; }

    /// <summary>
    /// The singular navigation properties (One&lt;T&gt;) defined in the schema.
    /// </summary>
    public IReadOnlyList<SingleNavigationInfo> SingleNavigations { get; }

    /// <summary>
    /// The skip-navigation properties (HasManyThrough) defined in the schema.
    /// </summary>
    public IReadOnlyList<ThroughNavigationInfo> ThroughNavigations { get; }

    /// <summary>
    /// The indexes defined in the schema.
    /// </summary>
    public IReadOnlyList<IndexInfo> Indexes { get; }

    /// <summary>
    /// The property names forming a composite primary key, or null if no composite key is defined.
    /// Column order matches argument order in the PrimaryKey() call.
    /// </summary>
    public IReadOnlyList<string>? CompositeKeyColumns { get; }

    /// <summary>
    /// The location for diagnostic reporting.
    /// </summary>
    public Location Location { get; }

    /// <summary>
    /// The fully qualified class name of a custom EntityReader&lt;T&gt; for this entity, if specified
    /// via [EntityReader] attribute on the schema class. Null when the default generated reader is used
    /// or when the attribute specifies an invalid type.
    /// </summary>
    public string? CustomEntityReaderClass { get; }

    /// <summary>
    /// When non-null, the [EntityReader] attribute was present but the specified type was invalid
    /// (doesn't inherit EntityReader&lt;T&gt; or T doesn't match). Holds the invalid type FQN for diagnostics.
    /// </summary>
    public string? InvalidEntityReaderClass { get; }

    public EntityInfo(
        string entityName,
        string schemaClassName,
        string schemaNamespace,
        string tableName,
        NamingStyleKind namingStyle,
        IReadOnlyList<ColumnInfo> columns,
        IReadOnlyList<NavigationInfo> navigations,
        IReadOnlyList<IndexInfo> indexes,
        Location location,
        string? customEntityReaderClass = null,
        string? invalidEntityReaderClass = null,
        IReadOnlyList<string>? compositeKeyColumns = null,
        IReadOnlyList<SingleNavigationInfo>? singleNavigations = null,
        IReadOnlyList<ThroughNavigationInfo>? throughNavigations = null)
    {
        EntityName = entityName;
        SchemaClassName = schemaClassName;
        SchemaNamespace = schemaNamespace;
        TableName = tableName;
        NamingStyle = namingStyle;
        Columns = columns;
        Navigations = navigations;
        SingleNavigations = singleNavigations ?? Array.Empty<SingleNavigationInfo>();
        ThroughNavigations = throughNavigations ?? Array.Empty<ThroughNavigationInfo>();
        Indexes = indexes;
        Location = location;
        CustomEntityReaderClass = customEntityReaderClass;
        InvalidEntityReaderClass = invalidEntityReaderClass;
        CompositeKeyColumns = compositeKeyColumns;
    }

    public bool Equals(EntityInfo? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return EntityName == other.EntityName
            && SchemaClassName == other.SchemaClassName
            && SchemaNamespace == other.SchemaNamespace
            && TableName == other.TableName
            && NamingStyle == other.NamingStyle
            && EqualityHelpers.SequenceEqual(Columns, other.Columns)
            && EqualityHelpers.SequenceEqual(Navigations, other.Navigations)
            && EqualityHelpers.SequenceEqual(SingleNavigations, other.SingleNavigations)
            && EqualityHelpers.SequenceEqual(ThroughNavigations, other.ThroughNavigations)
            && EqualityHelpers.SequenceEqual(Indexes, other.Indexes)
            && EqualityHelpers.NullableSequenceEqual(CompositeKeyColumns, other.CompositeKeyColumns)
            && CustomEntityReaderClass == other.CustomEntityReaderClass
            && InvalidEntityReaderClass == other.InvalidEntityReaderClass;
    }

    public override bool Equals(object? obj) => Equals(obj as EntityInfo);

    public override int GetHashCode()
    {
        return HashCode.Combine(EntityName, SchemaClassName, TableName, NamingStyle, Columns.Count);
    }
}
