using System;
using System.Collections.Generic;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;

namespace Quarry.Generators.IR;

/// <summary>
/// A query plan with materialized SQL strings and codegen metadata.
/// Produced by Stage 5 (SQL assembly) from QueryPlan.
/// Replaces PrebuiltChainInfo.
/// </summary>
internal sealed class AssembledPlan : IEquatable<AssembledPlan>
{
    public AssembledPlan(
        QueryPlan plan,
        Dictionary<ulong, AssembledSqlVariant> sqlVariants,
        string? readerDelegateCode,
        int maxParameterCount,
        TranslatedCallSite executionSite,
        IReadOnlyList<TranslatedCallSite> clauseSites,
        string entityTypeName,
        string? resultTypeName,
        SqlDialect dialect,
        string? entitySchemaNamespace = null,
        bool isTraced = false)
    {
        Plan = plan;
        SqlVariants = sqlVariants;
        ReaderDelegateCode = readerDelegateCode;
        MaxParameterCount = maxParameterCount;
        ExecutionSite = executionSite;
        ClauseSites = clauseSites;
        EntityTypeName = entityTypeName;
        ResultTypeName = resultTypeName;
        Dialect = dialect;
        EntitySchemaNamespace = entitySchemaNamespace;
        IsTraced = isTraced;
    }

    public QueryPlan Plan { get; }
    public Dictionary<ulong, AssembledSqlVariant> SqlVariants { get; }
    public string? ReaderDelegateCode { get; }
    public int MaxParameterCount { get; }
    public TranslatedCallSite ExecutionSite { get; }
    public IReadOnlyList<TranslatedCallSite> ClauseSites { get; }
    public string EntityTypeName { get; }
    public string? ResultTypeName { get; }
    public SqlDialect Dialect { get; }
    public string? EntitySchemaNamespace { get; }

    /// <summary>Whether this chain has a .Trace() call and should emit trace comments.</summary>
    public bool IsTraced { get; }

    public bool Equals(AssembledPlan? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Plan.Equals(other.Plan)
            && EntityTypeName == other.EntityTypeName
            && ResultTypeName == other.ResultTypeName
            && Dialect == other.Dialect
            && MaxParameterCount == other.MaxParameterCount
            && ReaderDelegateCode == other.ReaderDelegateCode
            && EntitySchemaNamespace == other.EntitySchemaNamespace
            && EqualityHelpers.DictionaryEqual(SqlVariants, other.SqlVariants);
    }

    public override bool Equals(object? obj) => Equals(obj as AssembledPlan);

    public override int GetHashCode()
    {
        return HashCode.Combine(Plan.GetHashCode(), EntityTypeName, Dialect, MaxParameterCount);
    }
}

/// <summary>
/// A single SQL variant for a specific bitmask value.
/// </summary>
internal sealed class AssembledSqlVariant : IEquatable<AssembledSqlVariant>
{
    public AssembledSqlVariant(string sql, int parameterCount)
    {
        Sql = sql;
        ParameterCount = parameterCount;
    }

    public string Sql { get; }
    public int ParameterCount { get; }

    public bool Equals(AssembledSqlVariant? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Sql == other.Sql && ParameterCount == other.ParameterCount;
    }

    public override bool Equals(object? obj) => Equals(obj as AssembledSqlVariant);
    public override int GetHashCode() => HashCode.Combine(Sql, ParameterCount);
}
