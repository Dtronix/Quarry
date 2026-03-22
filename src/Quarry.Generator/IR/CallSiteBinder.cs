using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;

namespace Quarry.Generators.IR;

/// <summary>
/// Binds a raw call site against the entity registry to produce bound call sites.
/// Handles entity resolution, context binding, InsertInfo/UpdateInfo construction,
/// join entity resolution, navigation join entity type resolution, and RawSql type enrichment.
/// </summary>
internal static class CallSiteBinder
{
    /// <summary>
    /// Binds a raw call site against the entity registry to produce bound call sites.
    /// Returns one element for most sites; may return multiple for navigation joins
    /// that discover additional chain members.
    /// </summary>
    public static ImmutableArray<BoundCallSite> Bind(
        RawCallSite raw,
        EntityRegistry registry,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Resolve entity from registry with ambiguity detection
        var entry = registry.Resolve(raw.EntityTypeName, raw.ContextClassName, out var isAmbiguous);

        // Build entity ref and context metadata
        EntityRef entity;
        string contextClassName;
        string contextNamespace;
        SqlDialect dialect;
        string tableName;
        string? schemaName;

        if (entry != null)
        {
            entity = EntityRef.FromEntityInfo(entry.Entity);
            contextClassName = raw.ContextClassName ?? entry.Context.ClassName;
            contextNamespace = raw.ContextNamespace ?? entry.Context.Namespace ?? "";
            dialect = entry.Context.Dialect;
            tableName = entry.Entity.TableName;
            schemaName = entry.Context.Schema;
        }
        else
        {
            // Entity not found — create a minimal bound site for non-analyzable reporting
            entity = EntityRef.Empty(raw.EntityTypeName);
            contextClassName = raw.ContextClassName ?? "";
            contextNamespace = raw.ContextNamespace ?? "";
            dialect = SqlDialect.PostgreSQL; // placeholder
            tableName = "";
            schemaName = null;
        }

        // Build InsertInfo for insert sites
        InsertInfo? insertInfo = null;
        if (raw.Kind is InterceptorKind.InsertExecuteNonQuery
            or InterceptorKind.InsertExecuteScalar
            or InterceptorKind.InsertToDiagnostics
            && entry != null)
        {
            // Convert ImmutableArray<string> back to HashSet for InsertInfo.FromEntityInfo
            HashSet<string>? propNames = null;
            if (raw.InitializedPropertyNames.HasValue)
            {
                propNames = new HashSet<string>(System.StringComparer.Ordinal);
                foreach (var name in raw.InitializedPropertyNames.Value)
                    propNames.Add(name);
            }
            insertInfo = InsertInfo.FromEntityInfo(entry.Entity, dialect, propNames);
        }

        // Build UpdateInfo for UpdateSetPoco
        InsertInfo? updateInfo = null;
        if (raw.Kind == InterceptorKind.UpdateSetPoco && entry != null)
        {
            HashSet<string>? propNames = null;
            if (raw.InitializedPropertyNames.HasValue)
            {
                propNames = new HashSet<string>(System.StringComparer.Ordinal);
                foreach (var name in raw.InitializedPropertyNames.Value)
                    propNames.Add(name);
            }
            updateInfo = InsertInfo.FromEntityInfo(entry.Entity, dialect, propNames);
        }

        // Resolve joined entity for join sites
        EntityRef? joinedEntity = null;
        IReadOnlyList<string>? joinedEntityTypeNames = null;
        string? resolvedJoinedEntityTypeName = raw.JoinedEntityTypeName;

        if (raw.Kind is InterceptorKind.Join or InterceptorKind.LeftJoin or InterceptorKind.RightJoin
            && raw.JoinedEntityTypeName != null)
        {
            // For navigation joins with unresolved types, resolve from navigation metadata
            if (raw.IsNavigationJoin && !registry.Contains(raw.JoinedEntityTypeName) && entry != null)
            {
                resolvedJoinedEntityTypeName = ResolveNavigationJoinEntityType(
                    raw, entry.Entity, registry);
            }

            var joinedTypeName = resolvedJoinedEntityTypeName ?? raw.JoinedEntityTypeName;
            var joinedEntry = registry.Resolve(joinedTypeName);
            if (joinedEntry != null)
            {
                joinedEntity = EntityRef.FromEntityInfo(joinedEntry.Entity);
            }
        }

        // Pass through joined entity type names from discovery
        IReadOnlyList<EntityRef>? joinedEntities = null;
        if (raw.JoinedEntityTypeNames != null)
        {
            joinedEntityTypeNames = raw.JoinedEntityTypeNames;
            // Resolve EntityRef for all joined entities (for projection enrichment)
            var resolved = new List<EntityRef>(raw.JoinedEntityTypeNames.Count);
            foreach (var name in raw.JoinedEntityTypeNames)
            {
                var je = registry.Resolve(name);
                resolved.Add(je != null ? EntityRef.FromEntityInfo(je.Entity) : EntityRef.Empty(name));
            }
            joinedEntities = resolved;
        }

        // Pass through RawSql type info from discovery (enrichment happens in the adapter path)
        RawSqlTypeInfo? rawSqlTypeInfo = raw.RawSqlTypeInfo;

        // Trace logging: record binding results
        TraceCapture.Log(raw.UniqueId, $"[Trace] Binding ({raw.MethodName}):");
        TraceCapture.Log(raw.UniqueId, $"  entity={raw.EntityTypeName}, found={entry != null}, ambiguous={isAmbiguous}");
        if (entry != null)
            TraceCapture.Log(raw.UniqueId, $"  table={tableName}, dialect={dialect}");
        if (joinedEntity != null)
            TraceCapture.Log(raw.UniqueId, $"  joinedEntity={resolvedJoinedEntityTypeName}");
        if (joinedEntities != null)
            TraceCapture.Log(raw.UniqueId, $"  joinedEntities=[{string.Join(", ", raw.JoinedEntityTypeNames!)}]");

        var bound = new BoundCallSite(
            raw: raw,
            contextClassName: contextClassName,
            contextNamespace: contextNamespace,
            dialect: dialect,
            tableName: tableName,
            schemaName: schemaName,
            entity: entity,
            joinedEntity: joinedEntity,
            joinedEntityTypeNames: joinedEntityTypeNames,
            joinedEntities: joinedEntities,
            insertInfo: insertInfo,
            updateInfo: updateInfo,
            rawSqlTypeInfo: rawSqlTypeInfo);

        return ImmutableArray.Create(bound);
    }

    /// <summary>
    /// Resolves a navigation join's entity type from entity navigation metadata.
    /// Extracts the navigation property name from the SqlExpr tree (ColumnRefExpr).
    /// </summary>
    private static string? ResolveNavigationJoinEntityType(
        RawCallSite raw,
        EntityInfo leftEntity,
        EntityRegistry registry)
    {
        // The expression for a navigation join like u => u.Orders is a ColumnRefExpr
        var navPropertyName = ExtractNavigationPropertyName(raw.Expression);
        if (navPropertyName == null)
            return null;

        // Find matching navigation in entity metadata
        foreach (var nav in leftEntity.Navigations)
        {
            if (nav.PropertyName == navPropertyName && registry.Contains(nav.RelatedEntityName))
                return nav.RelatedEntityName;
        }

        return null;
    }

    /// <summary>
    /// Extracts the navigation property name from a SqlExpr that represents
    /// a simple member access (e.g., u => u.Orders produces ColumnRefExpr("Orders")).
    /// </summary>
    private static string? ExtractNavigationPropertyName(SqlExpr? expr)
    {
        if (expr is ColumnRefExpr columnRef)
            return columnRef.PropertyName;

        return null;
    }
}
