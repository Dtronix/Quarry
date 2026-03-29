using System;
using System.Collections.Generic;

namespace Quarry.Generators.Models;

/// <summary>
/// Groups per-variable extractors for a single clause, maintaining per-clause scoping.
/// Different clauses may originate from different lambdas with different display classes,
/// so extractors are not deduplicated across clauses.
/// </summary>
internal sealed class ClauseExtractionPlan : IEquatable<ClauseExtractionPlan>
{
    public ClauseExtractionPlan(
        string clauseUniqueId,
        string delegateParamName,
        IReadOnlyList<CapturedVariableExtractor> extractors)
    {
        ClauseUniqueId = clauseUniqueId;
        DelegateParamName = delegateParamName;
        Extractors = extractors;
    }

    /// <summary>
    /// The UniqueId of the clause site this plan belongs to.
    /// Used to look up the extraction plan during emission.
    /// </summary>
    public string ClauseUniqueId { get; }

    /// <summary>
    /// The delegate parameter name in the interceptor method signature.
    /// "func" for Where/OrderBy/GroupBy/Having/Select/Join, "action" for UpdateSetAction.
    /// </summary>
    public string DelegateParamName { get; }

    /// <summary>
    /// The per-variable extractors for this clause, deduplicated by variable name.
    /// </summary>
    public IReadOnlyList<CapturedVariableExtractor> Extractors { get; }

    public bool Equals(ClauseExtractionPlan? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return ClauseUniqueId == other.ClauseUniqueId
            && DelegateParamName == other.DelegateParamName
            && EqualityHelpers.SequenceEqual(Extractors, other.Extractors);
    }

    public override bool Equals(object? obj) => Equals(obj as ClauseExtractionPlan);
    public override int GetHashCode() => HashCode.Combine(ClauseUniqueId, DelegateParamName, Extractors.Count);
}
