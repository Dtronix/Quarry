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
    private readonly Dictionary<string, EntityInfo> _byAccessorName;

    public EntityRegistry(
        Dictionary<string, List<EntityRegistryEntry>> byEntityType,
        Dictionary<string, EntityInfo> byEntityName,
        Dictionary<string, EntityInfo> byAccessorName)
    {
        _byEntityType = byEntityType;
        _byEntityName = byEntityName;
        _byAccessorName = byAccessorName;
    }

    /// <summary>
    /// Entity name → EntityInfo lookup for subquery resolution in the semantic translation path.
    /// </summary>
    public IReadOnlyDictionary<string, EntityInfo> ByEntityName => _byEntityName;

    /// <summary>
    /// Builds an EntityRegistry from all discovered contexts.
    /// </summary>
    public static EntityRegistry Build(ImmutableArray<ContextInfo> contexts, CancellationToken ct)
    {
        var byEntityType = new Dictionary<string, List<EntityRegistryEntry>>(StringComparer.Ordinal);
        var byEntityName = new Dictionary<string, EntityInfo>(StringComparer.Ordinal);
        var byAccessorName = new Dictionary<string, EntityInfo>(StringComparer.Ordinal);

        foreach (var context in contexts)
        {
            ct.ThrowIfCancellationRequested();

            // Index accessor names from EntityMappings (e.g., "Packages" → Package entity)
            foreach (var mapping in context.EntityMappings)
            {
                if (!byAccessorName.ContainsKey(mapping.PropertyName))
                    byAccessorName[mapping.PropertyName] = mapping.Entity;
            }

            foreach (var entity in context.Entities)
            {
                var entry = new EntityRegistryEntry(entity, context);

                // Build name variants for multi-key indexing:
                // shortName, contextNamespace-qualified, schemaNamespace-qualified, global::
                var shortName = entity.EntityName;
                var schemaQualified = $"{entity.SchemaNamespace}.{shortName}";
                var contextQualified = string.IsNullOrEmpty(context.Namespace)
                    ? shortName
                    : $"{context.Namespace}.{shortName}";
                var globalName = $"global::{contextQualified}";

                // Index by schema-qualified name (always unique per entity)
                AddToIndex(byEntityType, schemaQualified, entry);

                // Index by context-qualified name (may differ from schema-qualified)
                if (contextQualified != schemaQualified)
                    AddToIndex(byEntityType, contextQualified, entry);

                // Index by global:: name
                AddToIndex(byEntityType, globalName, entry);

                // Index by short name (accumulates all entities with same short name)
                if (shortName != schemaQualified && shortName != contextQualified)
                    AddToIndex(byEntityType, shortName, entry);

                // Index by entity name for subquery resolution (first-writer-wins)
                if (!byEntityName.ContainsKey(shortName))
                    byEntityName[shortName] = entity;
            }
        }

        return new EntityRegistry(byEntityType, byEntityName, byAccessorName);
    }

    private static void AddToIndex(
        Dictionary<string, List<EntityRegistryEntry>> index,
        string key,
        EntityRegistryEntry entry)
    {
        if (!index.TryGetValue(key, out var list))
        {
            list = new List<EntityRegistryEntry>();
            index[key] = list;
        }
        // Avoid duplicate entries for the same context
        foreach (var existing in list)
        {
            if (existing.Context.ClassName == entry.Context.ClassName)
                return;
        }
        list.Add(entry);
    }

    /// <summary>
    /// Resolves entity metadata by entity type name, optionally scoped to a context.
    /// Uses fallback resolution: direct lookup → strip global:: → short name after last dot.
    /// </summary>
    public EntityRegistryEntry? Resolve(string entityTypeName, string? contextClassName = null)
    {
        var entries = GetEntries(entityTypeName);
        if (entries == null || entries.Count == 0)
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
    /// Resolves entity metadata with ambiguity detection.
    /// </summary>
    public EntityRegistryEntry? Resolve(string entityTypeName, string? contextClassName, out bool isAmbiguous)
    {
        isAmbiguous = false;
        var entries = GetEntries(entityTypeName);
        if (entries == null || entries.Count == 0)
            return null;

        if (entries.Count == 1)
            return entries[0];

        // Multiple contexts — use contextClassName to disambiguate
        if (contextClassName != null)
        {
            foreach (var entry in entries)
            {
                if (entry.Context.ClassName == contextClassName)
                    return entry;
            }
        }

        // Fallback: first-writer-wins — flag as ambiguous
        isAmbiguous = contextClassName == null;
        return entries[0];
    }

    /// <summary>
    /// Returns true if the registry contains any entries for the given type name.
    /// Uses fallback resolution (global:: stripping, short name).
    /// </summary>
    public bool Contains(string typeName)
    {
        var entries = GetEntries(typeName);
        return entries != null && entries.Count > 0;
    }

    /// <summary>
    /// Gets the count of distinct context entries for a type name (for ambiguity diagnostics).
    /// </summary>
    public int GetEntryCount(string typeName)
    {
        var entries = GetEntries(typeName);
        return entries?.Count ?? 0;
    }

    /// <summary>
    /// Gets the first entry for a type name (for QRY015 diagnostics).
    /// </summary>
    public EntityRegistryEntry? GetFirstEntry(string typeName)
    {
        var entries = GetEntries(typeName);
        return entries != null && entries.Count > 0 ? entries[0] : null;
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
    /// Gets entity info by context accessor method name (e.g., "Packages" → Package entity).
    /// </summary>
    public EntityInfo? GetByAccessorName(string accessorName)
    {
        _byAccessorName.TryGetValue(accessorName, out var entity);
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

    /// <summary>
    /// Looks up entries with fallback resolution: direct → strip global:: → short name.
    /// </summary>
    private List<EntityRegistryEntry>? GetEntries(string typeName)
    {
        if (_byEntityType.TryGetValue(typeName, out var list))
            return list;

        var name = typeName.StartsWith("global::") ? typeName.Substring(8) : typeName;
        if (name != typeName && _byEntityType.TryGetValue(name, out list))
            return list;

        var lastDot = name.LastIndexOf('.');
        var shortName = lastDot > 0 ? name.Substring(lastDot + 1) : name;
        if (shortName != name && _byEntityType.TryGetValue(shortName, out list))
            return list;

        return null;
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
