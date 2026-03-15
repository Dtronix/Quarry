using System;
using System.Collections.Generic;

namespace Quarry.Generators.Models;

/// <summary>
/// The optimization tier determined for a query chain.
/// </summary>
internal enum OptimizationTier
{
    /// <summary>
    /// Tier 1: All clause combinations are enumerable (up to 4 conditional bits = 16 variants).
    /// The execution interceptor carries const string SQL for every possible path.
    /// Zero runtime string work.
    /// </summary>
    PrebuiltDispatch,

    /// <summary>
    /// Tier 2: Clauses are intercepted and pre-quoted, but too many combinations for a dispatch table.
    /// A lightweight concat assembles the final SQL at runtime.
    /// </summary>
    PrequotedFragments,

    /// <summary>
    /// Tier 3: Dynamic/opaque composition. No execution interceptor emitted.
    /// Current SqlBuilder path runs unchanged.
    /// </summary>
    RuntimeBuild
}

/// <summary>
/// The role a clause plays in a query chain.
/// Distinct from <see cref="ClauseKind"/> which is used for SQL clause translation.
/// ClauseRole tracks clause position in a chain and includes Select/Limit/Offset
/// which ClauseKind does not cover.
/// </summary>
internal enum ClauseRole
{
    Select,
    Where,
    OrderBy,
    ThenBy,
    GroupBy,
    Having,
    Join,
    Set,
    Limit,
    Offset,
    Distinct,
    DeleteWhere,
    UpdateWhere,
    UpdateSet,
    WithTimeout
}

/// <summary>
/// How a conditional clause relates to other clauses at the same branch point.
/// </summary>
internal enum BranchKind
{
    /// <summary>
    /// An if block with no else, or with an else that does not assign to the variable.
    /// The clause is either applied or not. Consumes 1 bit.
    /// </summary>
    Independent,

    /// <summary>
    /// An if/else where both branches assign to the variable.
    /// The clauses are alternatives. For if/else with one clause each, consumes 1 bit.
    /// </summary>
    MutuallyExclusive
}

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
