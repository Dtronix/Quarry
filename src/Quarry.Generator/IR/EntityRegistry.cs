using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;

namespace Quarry.Generators.IR;

/// <summary>
/// Compiled entity metadata from all contexts. Used for binding call sites
/// to entity metadata without requiring semantic model access.
/// Changes only when schema classes change (not when query code changes).
/// </summary>
internal sealed class EntityRegistry : IEquatable<EntityRegistry>
{
    private readonly Dictionary<string, List<EntityRegistryEntry>> _byEntityType;
    private readonly Dictionary<string, EntityInfo> _byEntityName;

    public EntityRegistry(
        Dictionary<string, List<EntityRegistryEntry>> byEntityType,
        Dictionary<string, EntityInfo> byEntityName)
    {
        _byEntityType = byEntityType;
        _byEntityName = byEntityName;
    }

    /// <summary>
    /// Builds an EntityRegistry from all discovered contexts.
    /// </summary>
    public static EntityRegistry Build(ImmutableArray<ContextInfo> contexts, CancellationToken ct)
    {
        var byEntityType = new Dictionary<string, List<EntityRegistryEntry>>(StringComparer.Ordinal);
        var byEntityName = new Dictionary<string, EntityInfo>(StringComparer.Ordinal);

        foreach (var context in contexts)
        {
            ct.ThrowIfCancellationRequested();

            foreach (var entity in context.Entities)
            {
                var entry = new EntityRegistryEntry(entity, context);

                // Index by fully qualified entity type name
                var fullTypeName = $"{entity.SchemaNamespace}.{entity.EntityName}";
                if (!byEntityType.TryGetValue(fullTypeName, out var entries))
                {
                    entries = new List<EntityRegistryEntry>();
                    byEntityType[fullTypeName] = entries;
                }
                entries.Add(entry);

                // Also index by short name
                if (!byEntityType.ContainsKey(entity.EntityName))
                {
                    byEntityType[entity.EntityName] = entries;
                }

                // Index by entity name for subquery resolution
                byEntityName[entity.EntityName] = entity;
            }
        }

        return new EntityRegistry(byEntityType, byEntityName);
    }

    /// <summary>
    /// Resolves entity metadata by entity type name, optionally scoped to a context.
    /// </summary>
    public EntityRegistryEntry? Resolve(string entityTypeName, string? contextClassName = null)
    {
        if (!_byEntityType.TryGetValue(entityTypeName, out var entries) || entries.Count == 0)
            return null;

        if (contextClassName != null)
        {
            foreach (var entry in entries)
            {
                if (entry.Context.ClassName == contextClassName)
                    return entry;
            }
        }

        return entries[0];
    }

    /// <summary>
    /// Gets entity info by entity name (for subquery resolution).
    /// </summary>
    public EntityInfo? GetByName(string entityName)
    {
        _byEntityName.TryGetValue(entityName, out var entity);
        return entity;
    }

    /// <summary>
    /// Gets all entity entries as a flat dictionary (for backward compatibility with existing code).
    /// </summary>
    public Dictionary<string, EntityInfo> ToEntityLookup()
    {
        var result = new Dictionary<string, EntityInfo>(StringComparer.Ordinal);
        foreach (var kvp in _byEntityName)
        {
            result[kvp.Key] = kvp.Value;
        }
        return result;
    }

    public bool Equals(EntityRegistry? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (_byEntityName.Count != other._byEntityName.Count) return false;
        foreach (var kvp in _byEntityName)
        {
            if (!other._byEntityName.TryGetValue(kvp.Key, out var otherEntity))
                return false;
            if (!kvp.Value.Equals(otherEntity))
                return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as EntityRegistry);

    public override int GetHashCode()
    {
        return HashCode.Combine(_byEntityName.Count);
    }
}

/// <summary>
/// An entity registered in one context.
/// </summary>
internal sealed class EntityRegistryEntry : IEquatable<EntityRegistryEntry>
{
    public EntityRegistryEntry(EntityInfo entity, ContextInfo context)
    {
        Entity = entity;
        Context = context;
    }

    public EntityInfo Entity { get; }
    public ContextInfo Context { get; }

    public bool Equals(EntityRegistryEntry? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Entity.Equals(other.Entity) && Context.Equals(other.Context);
    }

    public override bool Equals(object? obj) => Equals(obj as EntityRegistryEntry);
    public override int GetHashCode() => HashCode.Combine(Entity.EntityName, Context.ClassName);
}
