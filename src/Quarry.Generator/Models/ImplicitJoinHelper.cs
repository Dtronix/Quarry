using System.Collections.Generic;
using Quarry.Generators.IR;
using Quarry.Generators.Sql;
using Quarry.Shared.Migration;

namespace Quarry.Generators.Models;

/// <summary>
/// Shared helper for creating or reusing implicit join entries from One&lt;T&gt; navigation hops.
/// Used by both SqlExprBinder (clause-level) and ChainAnalyzer/BuildProjection (projection-level).
/// </summary>
internal static class ImplicitJoinHelper
{
    /// <summary>
    /// Creates an ImplicitJoinInfo for a One&lt;T&gt; navigation hop, or returns an
    /// existing one if a matching join already exists (deduplication by source alias + FK + target table).
    /// </summary>
    public static ImplicitJoinInfo? CreateOrReuse(
        SingleNavigationInfo nav,
        EntityRef sourceEntity,
        string sourceAlias,
        EntityRef targetEntity,
        SqlDialect dialect,
        List<ImplicitJoinInfo> existingJoins,
        ref int aliasCounter)
    {
        // Resolve FK column name on source entity
        string? fkColumnName = null;
        foreach (var col in sourceEntity.Columns)
        {
            if (col.PropertyName == nav.ForeignKeyPropertyName)
            {
                fkColumnName = col.ColumnName;
                break;
            }
        }
        if (fkColumnName == null) return null;

        // Resolve PK column name on target entity
        string? pkColumnName = null;
        foreach (var col in targetEntity.Columns)
        {
            if (col.Kind == ColumnKind.PrimaryKey)
            {
                pkColumnName = col.ColumnName;
                break;
            }
        }
        if (pkColumnName == null) return null;

        // Dedup: check if a join with matching (sourceAlias, fkColumn, targetTable) exists
        foreach (var existing in existingJoins)
        {
            if (existing.SourceAlias == sourceAlias &&
                existing.FkColumnName == fkColumnName &&
                existing.TargetTableName == targetEntity.TableName)
                return existing;
        }

        // Allocate new alias
        var joinAlias = $"j{aliasCounter++}";
        var joinKind = nav.IsNullableFk ? JoinClauseKind.Left : JoinClauseKind.Inner;

        var info = new ImplicitJoinInfo(
            sourceAlias: sourceAlias,
            fkColumnName: fkColumnName,
            fkColumnQuoted: SqlExprBinder.QuoteIdentifier(fkColumnName, dialect),
            targetTableName: targetEntity.TableName,
            targetTableQuoted: SqlExprBinder.QuoteIdentifier(targetEntity.TableName, dialect),
            targetSchemaQuoted: null,
            targetAlias: joinAlias,
            targetPkColumnQuoted: SqlExprBinder.QuoteIdentifier(pkColumnName, dialect),
            joinKind: joinKind,
            targetPkColumnName: pkColumnName);

        existingJoins.Add(info);
        return info;
    }
}
