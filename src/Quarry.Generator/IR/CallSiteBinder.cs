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

        // If the entry's context doesn't match the call site's context, the registry returned a
        // foreign-context entry because the entity type name happened to match a globalName key
        // for some other context (e.g., when the discovery resolved Pg.Products() to the
        // Quarry.Tests.Samples.Product user-written partial — that name's globalName key belongs
        // to the Lite context, so registry.Resolve returns the Lite entry). Re-resolve by walking
        // the registry's contexts to find the matching one.
        if (entry != null && raw.ContextClassName != null && entry.Context.ClassName != raw.ContextClassName)
        {
            EntityRegistryEntry? rebound = null;
            foreach (var ctx in registry.AllContexts)
            {
                if (ctx.ClassName != raw.ContextClassName) continue;
                foreach (var e in ctx.Entities)
                {
                    if (e.EntityName == entry.Entity.EntityName)
                    {
                        rebound = new EntityRegistryEntry(e, ctx);
                        break;
                    }
                }
                if (rebound != null) break;
            }
            if (rebound != null)
                entry = rebound;
        }

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

            // Normalize entity type name only when the discovery resolved to a user-written class
            // in a DIFFERENT namespace than the context's. EntityCodeGenerator emits each entity
            // in the context's namespace, so the per-context generated class lives at
            // {contextNamespace}.{entityName}. When the user has a partial class declared in the
            // schema's namespace (e.g., to attach an [EntityReader]), discovery picks that up and
            // pins the interceptor signature to the wrong type, causing CS9144 at compile time.
            // We rewrite to the context-qualified form so the per-context type matches what
            // PgDb.Products() / MyDb.Products() / SsDb.Products() actually return.
            //
            // For unresolved (Error) types — the common case where no user-written partial exists —
            // discovery records only the simple name (e.g., "User"), which compiles correctly via
            // namespace lookup. Leave those alone to avoid changing existing carrier output formats.
            if (!string.IsNullOrEmpty(contextNamespace) && NeedsContextNamespaceNormalization(raw.EntityTypeName, contextNamespace))
            {
                var normalized = $"global::{contextNamespace}.{entry.Entity.EntityName}";
                if (raw.EntityTypeName != normalized)
                    raw = raw.WithEntityTypeName(normalized);
            }

            // Cross-entity set operations carry an OperandEntityTypeName captured at discovery
            // (e.g., Union(Pg.Products()...) on a Pg.Users() chain). Apply the same normalization
            // so the cross-entity argument type in the interceptor matches the per-context generated
            // operand entity rather than a foreign-namespace user-written class.
            if (!string.IsNullOrEmpty(contextNamespace)
                && raw.OperandEntityTypeName != null
                && NeedsContextNamespaceNormalization(raw.OperandEntityTypeName, contextNamespace))
            {
                var operandSimple = raw.OperandEntityTypeName;
                if (operandSimple.StartsWith("global::"))
                    operandSimple = operandSimple.Substring(8);
                var lastDot = operandSimple.LastIndexOf('.');
                if (lastDot >= 0)
                    operandSimple = operandSimple.Substring(lastDot + 1);
                var normalizedOperand = $"{contextNamespace}.{operandSimple}";
                if (raw.OperandEntityTypeName != normalizedOperand)
                    raw = raw.WithOperandEntityTypeName(normalizedOperand);
            }
        }
        else
        {
            // Entity not found — create a minimal bound site for non-analyzable reporting.
            // This path is also taken legitimately for CTE chains, where downstream sites
            // (FromCte<TDto>, Select<TDto>, Prepare) carry a DTO type as the entity type
            // and the DTO is not a schema entity. Resolve the dialect by looking up the
            // chain's context class instead of falling back to PostgreSQL — the PG fallback
            // silently corrupts dialect-specific identifier quoting in the rendered SQL
            // (e.g., a MySQL chain would emit "Name" instead of `Name`).
            entity = EntityRef.Empty(raw.EntityTypeName);
            contextClassName = raw.ContextClassName ?? "";
            contextNamespace = raw.ContextNamespace ?? "";
            dialect = SqlDialect.PostgreSQL; // placeholder
            if (!string.IsNullOrEmpty(raw.ContextClassName))
            {
                foreach (var ctx in registry.AllContexts)
                {
                    if (ctx.ClassName == raw.ContextClassName)
                    {
                        dialect = ctx.Dialect;
                        break;
                    }
                }
            }
            else if (registry.AllContexts.Length == 1)
            {
                // Single-context project: when raw.ContextClassName is missing (e.g.,
                // discovery couldn't walk back to the chain root), there's only one
                // possible answer — use it instead of the PostgreSQL placeholder.
                dialect = registry.AllContexts[0].Dialect;
            }
            tableName = "";
            schemaName = null;
        }

        // Build InsertInfo for insert sites (including Prepare on insert builders)
        InsertInfo? insertInfo = null;
        if ((raw.Kind is InterceptorKind.InsertExecuteNonQuery
            or InterceptorKind.InsertExecuteScalar
            or InterceptorKind.InsertToDiagnostics
            || (raw.Kind == InterceptorKind.Prepare && raw.BuilderKind == BuilderKind.Insert))
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

        // Build InsertInfo for batch insert sites (column names come from lambda selector)
        if ((raw.Kind is InterceptorKind.BatchInsertColumnSelector
            or InterceptorKind.BatchInsertExecuteNonQuery
            or InterceptorKind.BatchInsertExecuteScalar
            or InterceptorKind.BatchInsertToDiagnostics
            || (raw.Kind == InterceptorKind.Prepare && raw.BuilderKind == BuilderKind.ExecutableBatchInsert))
            && entry != null)
        {
            HashSet<string>? propNames = null;
            if (raw.BatchInsertColumnNames.HasValue)
            {
                propNames = new HashSet<string>(System.StringComparer.Ordinal);
                foreach (var name in raw.BatchInsertColumnNames.Value)
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
            or InterceptorKind.CrossJoin or InterceptorKind.FullOuterJoin
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

        // Pass through joined entity type names from discovery.
        // For explicit joins, post-join sites get JoinedEntityTypeNames from the builder type args.
        // For navigation joins, Roslyn can't resolve the post-join builder type, so these are null
        // and get synthesized by ChainAnalyzer during chain analysis, then propagated by
        // PipelineOrchestrator before file grouping.
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

        // RawSqlTypeInfo was resolved by DisplayClassEnricher using the supplemental compilation
        RawSqlTypeInfo? rawSqlTypeInfo = raw.RawSqlTypeInfo;

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
    /// Returns true if the discovery's entity type name has a fully-qualified namespace prefix
    /// that does NOT match the call site's context namespace. Used to identify the case where
    /// the source generator's discovery resolved to a user-written class in the schema namespace
    /// instead of the per-context generated class in the context namespace. Simple names (no
    /// namespace) and names already in the context namespace are left untouched.
    /// </summary>
    private static bool NeedsContextNamespaceNormalization(string entityTypeName, string contextNamespace)
    {
        var name = entityTypeName.StartsWith("global::") ? entityTypeName.Substring(8) : entityTypeName;
        var lastDot = name.LastIndexOf('.');
        if (lastDot < 0)
            return false; // Simple name — namespace lookup at compile time will pick the right one
        var ns = name.Substring(0, lastDot);
        return ns != contextNamespace;
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
