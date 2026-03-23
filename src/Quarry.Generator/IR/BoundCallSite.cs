using System;
using System.Collections.Generic;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;

namespace Quarry.Generators.IR;

/// <summary>
/// Call site with entity and context binding resolved.
/// Produced by Stage 2 (binding) from RawCallSite + EntityRegistry.
/// </summary>
internal sealed class BoundCallSite : IEquatable<BoundCallSite>
{
    public BoundCallSite(
        RawCallSite raw,
        string contextClassName,
        string contextNamespace,
        SqlDialect dialect,
        string tableName,
        string? schemaName,
        EntityRef entity,
        EntityRef? joinedEntity = null,
        IReadOnlyList<string>? joinedEntityTypeNames = null,
        IReadOnlyList<EntityRef>? joinedEntities = null,
        InsertInfo? insertInfo = null,
        InsertInfo? updateInfo = null,
        RawSqlTypeInfo? rawSqlTypeInfo = null)
    {
        Raw = raw;
        ContextClassName = contextClassName;
        ContextNamespace = contextNamespace;
        Dialect = dialect;
        TableName = tableName;
        SchemaName = schemaName;
        Entity = entity;
        JoinedEntity = joinedEntity;
        JoinedEntityTypeNames = joinedEntityTypeNames;
        JoinedEntities = joinedEntities;
        InsertInfo = insertInfo;
        UpdateInfo = updateInfo;
        RawSqlTypeInfo = rawSqlTypeInfo;
    }

    /// <summary>Underlying raw discovery result (composition, not copying).</summary>
    public RawCallSite Raw { get; }

    // Resolved bindings
    public string ContextClassName { get; }
    public string ContextNamespace { get; }
    public SqlDialect Dialect { get; }
    public string TableName { get; }
    public string? SchemaName { get; }

    /// <summary>Entity metadata reference for column resolution.</summary>
    public EntityRef Entity { get; }

    /// <summary>Resolved joined entity metadata (for Join sites).</summary>
    public EntityRef? JoinedEntity { get; }
    public IReadOnlyList<string>? JoinedEntityTypeNames { get; }

    /// <summary>Resolved EntityRef for all entities in a multi-entity join (for enrichment).</summary>
    public IReadOnlyList<EntityRef>? JoinedEntities { get; }

    /// <summary>Insert column metadata.</summary>
    public InsertInfo? InsertInfo { get; }

    /// <summary>Update POCO column metadata.</summary>
    public InsertInfo? UpdateInfo { get; }

    /// <summary>RawSql type metadata.</summary>
    public RawSqlTypeInfo? RawSqlTypeInfo { get; }

    public bool Equals(BoundCallSite? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Raw.Equals(other.Raw)
            && ContextClassName == other.ContextClassName
            && ContextNamespace == other.ContextNamespace
            && Dialect == other.Dialect
            && TableName == other.TableName
            && SchemaName == other.SchemaName
            && Entity.Equals(other.Entity)
            && Equals(JoinedEntity, other.JoinedEntity)
            && EqualityHelpers.NullableSequenceEqual(JoinedEntityTypeNames, other.JoinedEntityTypeNames)
            && EqualityHelpers.NullableSequenceEqual(JoinedEntities, other.JoinedEntities)
            && Equals(InsertInfo, other.InsertInfo)
            && Equals(UpdateInfo, other.UpdateInfo)
            && Equals(RawSqlTypeInfo, other.RawSqlTypeInfo);
    }

    public override bool Equals(object? obj) => Equals(obj as BoundCallSite);

    public override int GetHashCode()
    {
        return HashCode.Combine(Raw.GetHashCode(), ContextClassName, Dialect, TableName);
    }
}
