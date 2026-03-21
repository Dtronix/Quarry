using System;
using System.Collections.Generic;

namespace Quarry.Generators.Models;

/// <summary>
/// Result of analyzing a query chain's control flow from declaration to execution.
/// Produced by <see cref="Quarry.Generators.Parsing.ChainAnalyzer"/>.
/// </summary>
internal sealed class ChainAnalysisResult : IEquatable<ChainAnalysisResult>
{
    public ChainAnalysisResult(
        OptimizationTier tier,
        IReadOnlyList<ChainedClauseSite> clauses,
        UsageSiteInfo executionSite,
        IReadOnlyList<ConditionalClause> conditionalClauses,
        IReadOnlyList<ulong> possibleMasks,
        string? notAnalyzableReason = null,
        IReadOnlyList<string>? unmatchedMethodNames = null,
        string? forkedVariableName = null)
    {
        Tier = tier;
        Clauses = clauses;
        ExecutionSite = executionSite;
        ConditionalClauses = conditionalClauses;
        PossibleMasks = possibleMasks;
        NotAnalyzableReason = notAnalyzableReason;
        UnmatchedMethodNames = unmatchedMethodNames;
        ForkedVariableName = forkedVariableName;
    }

    /// <summary>
    /// Gets the tier determined for this execution site.
    /// </summary>
    public OptimizationTier Tier { get; }

    /// <summary>
    /// Gets all clause sites in the chain, ordered by execution flow.
    /// </summary>
    public IReadOnlyList<ChainedClauseSite> Clauses { get; }

    /// <summary>
    /// Gets the execution site (terminal method call).
    /// </summary>
    public UsageSiteInfo ExecutionSite { get; }

    /// <summary>
    /// Gets conditional clause sites with assigned bit indices (tier 1 and tier 2).
    /// </summary>
    public IReadOnlyList<ConditionalClause> ConditionalClauses { get; }

    /// <summary>
    /// Gets all possible ClauseMask values (tier 1 only; empty for tier 2/3).
    /// </summary>
    public IReadOnlyList<ulong> PossibleMasks { get; }

    /// <summary>
    /// Gets the reason the chain could not be fully analyzed (tier 3 only).
    /// </summary>
    public string? NotAnalyzableReason { get; }

    /// <summary>
    /// Gets method names of fluent chain invocations that were not matched to any discovered usage site.
    /// These are builder methods like Limit, Offset, Distinct, AddWhereClause that are not interceptable.
    /// Non-null when the chain contains such methods — execution interceptors should be skipped.
    /// </summary>
    public IReadOnlyList<string>? UnmatchedMethodNames { get; }

    /// <summary>
    /// Gets the variable name when a forked chain is detected (QRY033).
    /// Non-null only when the chain was rejected due to a fork.
    /// </summary>
    public string? ForkedVariableName { get; }

    public bool Equals(ChainAnalysisResult? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Tier == other.Tier
            && NotAnalyzableReason == other.NotAnalyzableReason
            && ForkedVariableName == other.ForkedVariableName
            && EqualityHelpers.SequenceEqual(Clauses, other.Clauses)
            && ExecutionSite.Equals(other.ExecutionSite)
            && EqualityHelpers.SequenceEqual(ConditionalClauses, other.ConditionalClauses)
            && EqualityHelpers.SequenceEqual(PossibleMasks, other.PossibleMasks)
            && EqualityHelpers.NullableSequenceEqual(UnmatchedMethodNames, other.UnmatchedMethodNames);
    }

    public override bool Equals(object? obj) => Equals(obj as ChainAnalysisResult);

    public override int GetHashCode()
    {
        return HashCode.Combine(Tier, Clauses.Count, ConditionalClauses.Count, PossibleMasks.Count);
    }
}

/// <summary>
/// A clause site within an analyzed query chain.
/// </summary>
internal sealed class ChainedClauseSite : IEquatable<ChainedClauseSite>
{
    public ChainedClauseSite(
        UsageSiteInfo site,
        bool isConditional,
        int? bitIndex,
        ClauseRole role)
    {
        Site = site;
        IsConditional = isConditional;
        BitIndex = bitIndex;
        Role = role;
    }

    /// <summary>
    /// Gets the usage site info for this clause.
    /// </summary>
    public UsageSiteInfo Site { get; }

    /// <summary>
    /// Gets whether this clause is conditional (inside an if block).
    /// </summary>
    public bool IsConditional { get; }

    /// <summary>
    /// Gets the bit index for this conditional clause, or null if unconditional.
    /// </summary>
    public int? BitIndex { get; }

    /// <summary>
    /// Gets the role this clause plays in the query.
    /// </summary>
    public ClauseRole Role { get; }

    public bool Equals(ChainedClauseSite? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return IsConditional == other.IsConditional
            && BitIndex == other.BitIndex
            && Role == other.Role
            && Site.Equals(other.Site);
    }

    public override bool Equals(object? obj) => Equals(obj as ChainedClauseSite);

    public override int GetHashCode() => HashCode.Combine(IsConditional, BitIndex, Role);
}

/// <summary>
/// A conditional clause with its assigned bit index and branch classification.
/// </summary>
internal sealed class ConditionalClause : IEquatable<ConditionalClause>
{
    public ConditionalClause(
        int bitIndex,
        UsageSiteInfo site,
        BranchKind branchKind)
    {
        BitIndex = bitIndex;
        Site = site;
        BranchKind = branchKind;
    }

    /// <summary>
    /// Gets the bit index assigned to this conditional clause.
    /// </summary>
    public int BitIndex { get; }

    /// <summary>
    /// Gets the usage site for this conditional clause.
    /// </summary>
    public UsageSiteInfo Site { get; }

    /// <summary>
    /// Gets the branch classification for this conditional clause.
    /// </summary>
    public BranchKind BranchKind { get; }

    public bool Equals(ConditionalClause? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return BitIndex == other.BitIndex
            && BranchKind == other.BranchKind
            && Site.Equals(other.Site);
    }

    public override bool Equals(object? obj) => Equals(obj as ConditionalClause);

    public override int GetHashCode() => HashCode.Combine(BitIndex, BranchKind);
}
