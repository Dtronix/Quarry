using System;
using Quarry.Generators.Sql;
using Quarry;
using Microsoft.CodeAnalysis;

namespace Quarry.Generators.Models;

/// <summary>
/// Represents a discovered Quarry context with its configuration and entity mappings.
/// </summary>
internal sealed class ContextInfo : IEquatable<ContextInfo>
{
    /// <summary>
    /// The name of the context class.
    /// </summary>
    public string ClassName { get; }

    /// <summary>
    /// The namespace of the context class.
    /// </summary>
    public string Namespace { get; }

    /// <summary>
    /// The SQL dialect configured for this context.
    /// </summary>
    public SqlDialect Dialect { get; }

    /// <summary>
    /// The database schema name (e.g., "public", "dbo").
    /// Null if not specified.
    /// </summary>
    public string? Schema { get; }

    /// <summary>
    /// The entities discovered via partial QueryBuilder properties.
    /// </summary>
    public IReadOnlyList<EntityInfo> Entities { get; }

    /// <summary>
    /// The entity mappings with property names from the context class.
    /// </summary>
    public IReadOnlyList<EntityMapping> EntityMappings { get; }

    /// <summary>
    /// The location for diagnostic reporting.
    /// </summary>
    public Location Location { get; }

    public ContextInfo(
        string className,
        string @namespace,
        SqlDialect dialect,
        string? schema,
        IReadOnlyList<EntityInfo> entities,
        IReadOnlyList<EntityMapping> entityMappings,
        Location location)
    {
        ClassName = className;
        Namespace = @namespace;
        Dialect = dialect;
        Schema = schema;
        Entities = entities;
        EntityMappings = entityMappings;
        Location = location;
    }

    public bool Equals(ContextInfo? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return ClassName == other.ClassName
            && Namespace == other.Namespace
            && Dialect == other.Dialect
            && Schema == other.Schema
            && EqualityHelpers.SequenceEqual(Entities, other.Entities)
            && EqualityHelpers.SequenceEqual(EntityMappings, other.EntityMappings);
    }

    public override bool Equals(object? obj) => Equals(obj as ContextInfo);

    public override int GetHashCode()
    {
        return HashCode.Combine(ClassName, Namespace, Dialect, Schema, Entities.Count);
    }
}
