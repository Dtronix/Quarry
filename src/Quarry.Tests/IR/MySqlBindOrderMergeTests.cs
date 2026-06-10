using System.Collections.Generic;
using NUnit.Framework;
using Quarry.Generators.IR;

namespace Quarry.Tests.IR;

/// <summary>
/// Unit tests for <see cref="PipelineOrchestrator.TryMergeTextOrder"/> — the cross-variant
/// merge that combines each mask variant's SQL-text slot sequence into the single
/// per-chain bind ranking (#303). Slots that co-occur in two variants must agree on
/// relative order; slots that never co-occur (mutually exclusive conditional branches)
/// get a deterministic placement and their relative order is immaterial.
/// </summary>
[TestFixture]
public class MySqlBindOrderMergeTests
{
    private static List<int>? Merge(params int[][] variants)
    {
        List<int>? master = null;
        foreach (var v in variants)
        {
            if (!PipelineOrchestrator.TryMergeTextOrder(ref master, new List<int>(v)))
                return null;
        }
        return master;
    }

    [Test]
    public void FirstVariant_InitializesMaster()
    {
        var master = Merge(new[] { 2, 0, 1 });
        Assert.That(master, Is.EqualTo(new[] { 2, 0, 1 }));
    }

    [Test]
    public void IdenticalVariants_NoChange()
    {
        var master = Merge(new[] { 1, 0 }, new[] { 1, 0 });
        Assert.That(master, Is.EqualTo(new[] { 1, 0 }));
    }

    [Test]
    public void SubsetVariant_ConsistentOrder_NoChange()
    {
        // Mask-off variant omits the conditional slot 1; surviving slots keep order.
        var master = Merge(new[] { 2, 0, 1 }, new[] { 2, 0 });
        Assert.That(master, Is.EqualTo(new[] { 2, 0, 1 }));
    }

    [Test]
    public void SupersetVariant_InsertsUnseenSlotAfterAnchor()
    {
        // Base variant lacks the conditional slot 1; the mask-on variant places it
        // between 0 and 3 — the merge must insert it right after its predecessor (0).
        var master = Merge(new[] { 2, 0, 3 }, new[] { 2, 0, 1, 3 });
        Assert.That(master, Is.EqualTo(new[] { 2, 0, 1, 3 }));
    }

    [Test]
    public void UnseenSlotAtSequenceStart_InsertsAtFront()
    {
        var master = Merge(new[] { 0, 2 }, new[] { 1, 0, 2 });
        Assert.That(master, Is.EqualTo(new[] { 1, 0, 2 }));
    }

    [Test]
    public void ContradictoryRelativeOrder_ReturnsFalse()
    {
        // Slots 0 and 1 co-occur in both variants with opposite relative order —
        // structurally impossible for real renderers; the merge must refuse.
        List<int>? master = null;
        Assert.That(PipelineOrchestrator.TryMergeTextOrder(ref master, new List<int> { 0, 1 }), Is.True);
        Assert.That(PipelineOrchestrator.TryMergeTextOrder(ref master, new List<int> { 1, 0 }), Is.False);
    }

    [Test]
    public void MutuallyExclusiveSlots_DeterministicPlacement()
    {
        // Slots 1 and 2 never co-occur (exclusive branch group). Their relative bind
        // order is immaterial (never co-bound), but the merge must be deterministic:
        // each unseen slot lands immediately after its predecessor in its variant.
        var master = Merge(new[] { 0, 1, 3 }, new[] { 0, 2, 3 });
        Assert.That(master, Is.EqualTo(new[] { 0, 2, 1, 3 }));
    }

    [Test]
    public void EmptyIncoming_NoChange()
    {
        var master = Merge(new[] { 1, 0 }, new int[0]);
        Assert.That(master, Is.EqualTo(new[] { 1, 0 }));
    }
}
