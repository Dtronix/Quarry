using System;
using Quarry.Generators.IR;

namespace Quarry.Generators.Models;

/// <summary>
/// Describes an implicit JOIN inferred from a One&lt;T&gt; navigation access.
/// </summary>
internal sealed class ImplicitJoinInfo : IEquatable<ImplicitJoinInfo>
{
    /// <summary>The alias of the source table (e.g., "t0" or "sq0").</summary>
    public string SourceAlias { get; }

    /// <summary>The FK column name on the source table (e.g., "user_id").</summary>
    public string FkColumnName { get; }

    /// <summary>The quoted FK column on the source table (e.g., "\"user_id\"").</summary>
    public string FkColumnQuoted { get; }

    /// <summary>The target table name (e.g., "users").</summary>
    public string TargetTableName { get; }

    /// <summary>The quoted target table name (e.g., "\"users\"").</summary>
    public string TargetTableQuoted { get; }

    /// <summary>The target schema name (nullable).</summary>
    public string? TargetSchemaQuoted { get; }

    /// <summary>The alias assigned to this join (e.g., "j0").</summary>
    public string TargetAlias { get; }

    /// <summary>The PK column name on the target table, quoted (e.g., "\"user_id\"").</summary>
    public string TargetPkColumnQuoted { get; }

    /// <summary>The unquoted PK column name on the target table (e.g., "user_id").</summary>
    public string TargetPkColumnName { get; }

    /// <summary>INNER or LEFT, based on FK nullability.</summary>
    public JoinClauseKind JoinKind { get; }

    public ImplicitJoinInfo(
        string sourceAlias,
        string fkColumnName,
        string fkColumnQuoted,
        string targetTableName,
        string targetTableQuoted,
        string? targetSchemaQuoted,
        string targetAlias,
        string targetPkColumnQuoted,
        JoinClauseKind joinKind,
        string? targetPkColumnName = null)
    {
        SourceAlias = sourceAlias;
        FkColumnName = fkColumnName;
        FkColumnQuoted = fkColumnQuoted;
        TargetTableName = targetTableName;
        TargetTableQuoted = targetTableQuoted;
        TargetSchemaQuoted = targetSchemaQuoted;
        TargetAlias = targetAlias;
        TargetPkColumnQuoted = targetPkColumnQuoted;
        TargetPkColumnName = targetPkColumnName ?? fkColumnName;
        JoinKind = joinKind;
    }

    public bool Equals(ImplicitJoinInfo? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return SourceAlias == other.SourceAlias
            && FkColumnName == other.FkColumnName
            && TargetAlias == other.TargetAlias
            && JoinKind == other.JoinKind;
    }

    public override bool Equals(object? obj) => Equals(obj as ImplicitJoinInfo);

    public override int GetHashCode()
    {
        return HashCode.Combine(SourceAlias, FkColumnName, TargetAlias, JoinKind);
    }
}
