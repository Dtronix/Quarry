using System;
using System.Collections.Generic;
using System.Linq;
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
        Dictionary<int, AssembledSqlVariant> sqlVariants,
        string? readerDelegateCode,
        int maxParameterCount,
        TranslatedCallSite executionSite,
        IReadOnlyList<TranslatedCallSite> clauseSites,
        string entityTypeName,
        string? resultTypeName,
        SqlDialect dialect,
        string? entitySchemaNamespace = null,
        bool isTraced = false,
        ProjectionInfo? projectionInfo = null,
        IReadOnlyList<(string TableName, string? SchemaName)>? joinedTableInfos = null,
        IReadOnlyList<string>? traceLines = null,
        string? batchInsertReturningSuffix = null,
        int batchInsertColumnsPerRow = 0,
        IReadOnlyList<TranslatedCallSite>? preparedTerminals = null,
        TranslatedCallSite? prepareSite = null,
        Models.InsertInfo? insertInfo = null,
        bool isOperandChain = false)
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
        ProjectionInfo = projectionInfo;
        JoinedTableInfos = joinedTableInfos;
        TraceLines = traceLines;
        BatchInsertReturningSuffix = batchInsertReturningSuffix;
        BatchInsertColumnsPerRow = batchInsertColumnsPerRow;
        PreparedTerminals = preparedTerminals;
        PrepareSite = prepareSite;
        InsertInfo = insertInfo;
        IsOperandChain = isOperandChain;
    }

    private IReadOnlyList<ChainClauseEntry>? _clauseEntries;

    public QueryPlan Plan { get; }
    public Dictionary<int, AssembledSqlVariant> SqlVariants { get; }
    public string? ReaderDelegateCode { get; set; }
    public int MaxParameterCount { get; }
    public TranslatedCallSite ExecutionSite { get; }
    public IReadOnlyList<TranslatedCallSite> ClauseSites { get; }
    public string EntityTypeName { get; }
    public string? ResultTypeName { get; }
    public SqlDialect Dialect { get; }
    public string? EntitySchemaNamespace { get; }

    /// <summary>Whether this chain has a .Trace() call and should emit trace comments.</summary>
    public bool IsTraced { get; }

    /// <summary>Enriched projection info for reader delegate generation.</summary>
    public ProjectionInfo? ProjectionInfo { get; set; }

    /// <summary>Joined table infos for multi-entity join chains.</summary>
    public IReadOnlyList<(string TableName, string? SchemaName)>? JoinedTableInfos { get; set; }

    /// <summary>Trace comment lines for this chain (non-null only when traced with QUARRY_TRACE).</summary>
    public IReadOnlyList<string>? TraceLines { get; set; }

    /// <summary>For batch inserts: the RETURNING/OUTPUT suffix to append after row expansion.</summary>
    public string? BatchInsertReturningSuffix { get; }

    /// <summary>For batch inserts: the number of columns per row (for parameter index calculation).</summary>
    public int BatchInsertColumnsPerRow { get; }

    /// <summary>
    /// Terminal sites called on a PreparedQuery variable. Non-null only for multi-terminal chains (N>1).
    /// </summary>
    public IReadOnlyList<TranslatedCallSite>? PreparedTerminals { get; }

    /// <summary>
    /// The .Prepare() call site. Non-null only for multi-terminal chains.
    /// </summary>
    public TranslatedCallSite? PrepareSite { get; }

    /// <summary>
    /// Resolved insert column metadata. Non-null for Insert and BatchInsert chains.
    /// Stored at top level because InsertInfo may originate from a clause site or
    /// Prepare site rather than the execution terminal (e.g., Prepare chains).
    /// </summary>
    public Models.InsertInfo? InsertInfo { get; }

    /// <summary>
    /// True when this chain is consumed as a set operation operand (no standalone terminal).
    /// The carrier is generated for clause interceptors only — no execution terminal is emitted.
    /// </summary>
    public bool IsOperandChain { get; }

    // Convenience accessors that mirror PrebuiltChainInfo property names
    public QueryKind QueryKind => Plan.Kind;
    public string TableName => ExecutionSite.TableName;
    public string? SchemaName => ExecutionSite.SchemaName;
    public bool IsJoinChain => Plan.Joins.Count > 0;
    public IReadOnlyList<string>? JoinedEntityTypeNames => ExecutionSite.JoinedEntityTypeNames;
    public OptimizationTier Tier => Plan.Tier;
    public IReadOnlyList<string>? UnmatchedMethodNames => Plan.UnmatchedMethodNames;
    public string? ForkedVariableName => Plan.ForkedVariableName;
    public IReadOnlyList<QueryParameter> ChainParameters => Plan.Parameters;
    public IReadOnlyList<ConditionalTerm> ConditionalTerms => Plan.ConditionalTerms;
    public IReadOnlyList<int> PossibleMasks => Plan.PossibleMasks;
    public string? NotAnalyzableReason => Plan.NotAnalyzableReason;

    /// <summary>
    /// Builds clause entries with conditional bit indices, matching the old ChainedClauseSite pattern.
    /// </summary>
    public IReadOnlyList<ChainClauseEntry> GetClauseEntries()
    {
        if (_clauseEntries != null) return _clauseEntries;

        var entries = new List<ChainClauseEntry>();
        int condIdx = 0;
        foreach (var cs in ClauseSites)
        {
            var role = Parsing.ChainAnalyzer.MapInterceptorKindToClauseRole(cs.Kind);
            if (role == null) continue;

            int? bitIndex = null;
            if (cs.Bound.Raw.NestingContext != null && condIdx < Plan.ConditionalTerms.Count)
            {
                bitIndex = Plan.ConditionalTerms[condIdx].BitIndex;
                condIdx++;
            }

            // A clause is only conditional if it has a matching ConditionalTerm (bitIndex assigned).
            // Clause sites may have NestingContext from being inside nested control flow without
            // being genuinely conditional (relative depth <= baseline).
            entries.Add(new ChainClauseEntry(cs, bitIndex.HasValue, bitIndex, role.Value));
        }
        _clauseEntries = entries;
        return entries;
    }

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
            && IsOperandChain == other.IsOperandChain
            && IsTraced == other.IsTraced
            && BatchInsertReturningSuffix == other.BatchInsertReturningSuffix
            && BatchInsertColumnsPerRow == other.BatchInsertColumnsPerRow
            && ExecutionSite.Equals(other.ExecutionSite)
            && EqualityHelpers.SequenceEqual(ClauseSites, other.ClauseSites)
            && EqualityHelpers.NullableSequenceEqual(PreparedTerminals, other.PreparedTerminals)
            && Equals(PrepareSite, other.PrepareSite)
            && Equals(InsertInfo, other.InsertInfo)
            && EqualityHelpers.DictionaryEqual(SqlVariants, other.SqlVariants);
    }

    public override bool Equals(object? obj) => Equals(obj as AssembledPlan);

    public override int GetHashCode()
    {
        return HashCode.Combine(Plan.GetHashCode(), EntityTypeName, Dialect, MaxParameterCount, IsTraced, BatchInsertColumnsPerRow);
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

/// <summary>
/// A clause site within a chain with its conditional bit index and role.
/// Replaces ChainedClauseSite for the new pipeline.
/// </summary>
internal sealed class ChainClauseEntry
{
    public ChainClauseEntry(TranslatedCallSite site, bool isConditional, int? bitIndex, ClauseRole role)
    {
        Site = site;
        IsConditional = isConditional;
        BitIndex = bitIndex;
        Role = role;
    }

    public TranslatedCallSite Site { get; }
    public bool IsConditional { get; }
    public int? BitIndex { get; }
    public ClauseRole Role { get; }
}
