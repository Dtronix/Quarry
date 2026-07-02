using System.Collections.Generic;
using NUnit.Framework;
using Quarry.Generators.IR;

namespace Quarry.Tests.IR;

/// <summary>
/// Unit tests for <see cref="PipelineOrchestrator.TryMergeTextOrders"/> — the cross-variant
/// merge that combines each mask variant's SQL-text slot sequence into the single
/// per-chain bind ranking (#303). Slots that co-occur in two variants must agree on
/// relative order; slots that never co-occur (mutually exclusive conditional branches)
/// get the GlobalIndex (smallest-slot-first) tiebreak. The merge is a topological sort,
/// so variant enumeration order must never affect the result — the review pass-2 High
/// finding was an anchor-insertion merge that reported false contradictions when the
/// mask enumerator fed singleton variants before the combined one.
/// </summary>
[TestFixture]
public class MySqlBindOrderMergeTests
{
    private static List<int>? Merge(int totalSlots, params int[][] variants)
    {
        var sequences = new List<int[]>(variants);
        return PipelineOrchestrator.TryMergeTextOrders(sequences, totalSlots, out var master)
            ? master
            : null;
    }

    [Test]
    public void SingleVariant_IsTheRanking()
    {
        var master = Merge(3, new[] { 2, 0, 1 });
        Assert.That(master, Is.EqualTo(new[] { 2, 0, 1 }));
    }

    [Test]
    public void IdenticalVariants_NoChange()
    {
        var master = Merge(2, new[] { 1, 0 }, new[] { 1, 0 });
        Assert.That(master, Is.EqualTo(new[] { 1, 0 }));
    }

    [Test]
    public void SubsetVariant_ConsistentOrder_NoChange()
    {
        // Mask-off variant omits the conditional slot 1; surviving slots keep order.
        var master = Merge(3, new[] { 2, 0, 1 }, new[] { 2, 0 });
        Assert.That(master, Is.EqualTo(new[] { 2, 0, 1 }));
    }

    [Test]
    public void SupersetVariant_PlacesConditionalSlotByItsConstraints()
    {
        // Base variant lacks the conditional slot 1; the mask-on variant places it
        // between 0 and 3 — the merged ranking must respect that.
        var master = Merge(4, new[] { 2, 0, 3 }, new[] { 2, 0, 1, 3 });
        Assert.That(master, Is.EqualTo(new[] { 2, 0, 1, 3 }));
    }

    [Test]
    public void UnseenSlotAtSequenceStart_RanksFirst()
    {
        var master = Merge(3, new[] { 0, 2 }, new[] { 1, 0, 2 });
        Assert.That(master, Is.EqualTo(new[] { 1, 0, 2 }));
    }

    [Test]
    public void ContradictoryRelativeOrder_ReturnsFalse()
    {
        // Slots 0 and 1 co-occur in both variants with opposite relative order —
        // structurally impossible for real renderers; the merge must refuse.
        var sequences = new List<int[]> { new[] { 0, 1 }, new[] { 1, 0 } };
        Assert.That(PipelineOrchestrator.TryMergeTextOrders(sequences, 2, out _), Is.False);
    }

    [Test]
    public void IndependentConditionals_SingletonsThenCombined_NoFalseContradiction()
    {
        // Two independently conditional parameterized clauses, masks in ascending
        // order exactly as the mask enumerator produces them: [], [0], [1], [0,1].
        // The pass-2 High finding: the anchor-insertion merge seeded [0], guessed
        // [1,0] for the second singleton, then falsely rejected [0,1].
        var master = Merge(2, new[] { 0 }, new[] { 1 }, new[] { 0, 1 });
        Assert.That(master, Is.EqualTo(new[] { 0, 1 }));
    }

    [Test]
    public void IndependentConditionals_CombinedVariantReversed_HoistedOrderWins()
    {
        // Same singleton-then-combined family, but the combined variant carries a
        // genuinely reordered text order (the #303 wrap shape): the ranking must be
        // the hoisted order, not identity and not a contradiction.
        var master = Merge(2, new[] { 0 }, new[] { 1 }, new[] { 1, 0 });
        Assert.That(master, Is.EqualTo(new[] { 1, 0 }));
    }

    [Test]
    public void IndependentConditionals_WithUnconditionalAnchor()
    {
        // Unconditional slot 0 present in every variant; conditional slots 1 and 2
        // co-occur only in the full mask: [0], [0,1], [0,2], [0,1,2].
        var master = Merge(3, new[] { 0 }, new[] { 0, 1 }, new[] { 0, 2 }, new[] { 0, 1, 2 });
        Assert.That(master, Is.EqualTo(new[] { 0, 1, 2 }));
    }

    [Test]
    public void MutuallyExclusiveSlots_GlobalIndexTiebreak()
    {
        // Slots 1 and 2 never co-occur (exclusive branch group). Their relative bind
        // order is immaterial (never co-bound), but the merge must be deterministic:
        // unconstrained slots rank smallest-first (GlobalIndex tiebreak).
        var master = Merge(4, new[] { 0, 1, 3 }, new[] { 0, 2, 3 });
        Assert.That(master, Is.EqualTo(new[] { 0, 1, 2, 3 }));
    }

    [Test]
    public void VariantOrder_DoesNotAffectResult()
    {
        // Topological merge is order-independent; feed the same variant set in
        // opposite enumeration orders and require identical rankings.
        var ascending = Merge(3, new[] { 2 }, new[] { 0, 2 }, new[] { 1, 0, 2 });
        var descending = Merge(3, new[] { 1, 0, 2 }, new[] { 0, 2 }, new[] { 2 });
        Assert.That(ascending, Is.EqualTo(new[] { 1, 0, 2 }));
        Assert.That(descending, Is.EqualTo(ascending));
    }

    [Test]
    public void EmptyAndAbsentSlots_MergeOverSeenSlotsOnly()
    {
        // Empty sequences contribute nothing; slots never seen in any variant are
        // excluded from the ranking (the caller's coverage check reports those).
        var master = Merge(4, new int[0], new[] { 2, 1 });
        Assert.That(master, Is.EqualTo(new[] { 2, 1 }));
    }
}
