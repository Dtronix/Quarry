using Quarry.Generators.Parsing;

namespace Quarry.Tests.Parsing;

/// <summary>
/// Unit tests for <see cref="ChainAnalyzer.ValidateMaskEnumeration"/> — the #307
/// defense-in-depth check that every structurally reachable mask has an enumerated
/// SQL variant. Exercised here with synthetic cascade shapes because the validator
/// must never fire through the public pipeline (per-arm enumeration is complete);
/// these tests prove it WOULD fire on a deliberately pruned mask list.
/// </summary>
[TestFixture]
public class MaskReachabilityValidatorTests
{
    private static (IReadOnlyList<int> ArmBitSets, bool ZeroAllowed) Cascade(
        bool zeroAllowed, params int[] armBitSets)
        => (armBitSets, zeroAllowed);

    // ── Passing shapes: per-arm enumeration output is accepted ──

    [Test]
    public void SingleIfNoElse_ZeroAndBit_Valid()
    {
        var cascades = new[] { Cascade(zeroAllowed: true, 0b1) };
        Assert.That(ChainAnalyzer.ValidateMaskEnumeration(cascades, 1, new[] { 0, 1 }), Is.True);
    }

    [Test]
    public void IfElse_OneBitPerArm_Valid()
    {
        var cascades = new[] { Cascade(zeroAllowed: false, 0b01, 0b10) };
        Assert.That(ChainAnalyzer.ValidateMaskEnumeration(cascades, 2, new[] { 1, 2 }), Is.True);
    }

    [Test]
    public void ThreeArmElseIf_PerArmMasks_Valid()
    {
        var cascades = new[] { Cascade(zeroAllowed: false, 0b001, 0b010, 0b100) };
        Assert.That(ChainAnalyzer.ValidateMaskEnumeration(cascades, 3, new[] { 1, 2, 4 }), Is.True);
    }

    [Test]
    public void MultiClauseArm_ArmBitsTogether_Valid()
    {
        // if-arm sets bits 0+1 together, else-arm sets bit 2 → reachable = {3, 4}.
        var cascades = new[] { Cascade(zeroAllowed: false, 0b011, 0b100) };
        Assert.That(ChainAnalyzer.ValidateMaskEnumeration(cascades, 3, new[] { 3, 4 }), Is.True);
    }

    [Test]
    public void TwoIndependentCascades_CrossProduct_Valid()
    {
        var cascades = new[]
        {
            Cascade(zeroAllowed: true, 0b01),
            Cascade(zeroAllowed: true, 0b10)
        };
        Assert.That(ChainAnalyzer.ValidateMaskEnumeration(cascades, 2, new[] { 0, 1, 2, 3 }), Is.True);
    }

    [Test]
    public void UnreachableExtraMasks_SupersetIsAccepted()
    {
        // Extra enumerated masks beyond the reachable set are harmless.
        var cascades = new[] { Cascade(zeroAllowed: false, 0b01, 0b10) };
        Assert.That(ChainAnalyzer.ValidateMaskEnumeration(cascades, 2, new[] { 0, 1, 2, 3 }), Is.True);
    }

    [Test]
    public void NoConditionalBits_ZeroMaskOnly_Valid()
    {
        Assert.That(ChainAnalyzer.ValidateMaskEnumeration(
            Array.Empty<(IReadOnlyList<int>, bool)>(), 0, new[] { 0 }), Is.True);
    }

    // ── Failing shapes: deliberately pruned enumerations are detected ──

    [Test]
    public void SingleIfNoElse_MissingZeroMask_Detected()
    {
        var cascades = new[] { Cascade(zeroAllowed: true, 0b1) };
        Assert.That(ChainAnalyzer.ValidateMaskEnumeration(cascades, 1, new[] { 1 }), Is.False);
    }

    [Test]
    public void ThreeArmElseIf_MissingArmMask_Detected()
    {
        var cascades = new[] { Cascade(zeroAllowed: false, 0b001, 0b010, 0b100) };
        Assert.That(ChainAnalyzer.ValidateMaskEnumeration(cascades, 3, new[] { 1, 2 }), Is.False);
    }

    [Test]
    public void ElseIfChain_OldConditionTextEnumeration_Detected()
    {
        // The exact #307 defect-2 repro: the old condition-text grouping enumerated
        // {2,3,4,5} for a 3-arm else-if whose reachable masks are {1,2,4} — runtime
        // arm 0 (mask 1) dispatched a null variant. The validator must flag it.
        var cascades = new[] { Cascade(zeroAllowed: false, 0b001, 0b010, 0b100) };
        Assert.That(ChainAnalyzer.ValidateMaskEnumeration(cascades, 3, new[] { 2, 3, 4, 5 }), Is.False);
    }

    [Test]
    public void MultiClauseArm_OldPerBitEnumeration_Detected()
    {
        // Defect-2 repro shape 2: two clauses in one if-arm ({3}) plus an else-arm
        // ({4}); the old enumeration produced per-bit masks {1,2,4} — mask 3 missing.
        var cascades = new[] { Cascade(zeroAllowed: false, 0b011, 0b100) };
        Assert.That(ChainAnalyzer.ValidateMaskEnumeration(cascades, 3, new[] { 1, 2, 4 }), Is.False);
    }

    [Test]
    public void CascadeWithUnrepresentedArm_MissingZeroMask_Detected()
    {
        // if/else where only the if-arm touches the chain: taking the else sets no
        // bits, so mask 0 is reachable even though the cascade has a final else.
        var cascades = new[] { Cascade(zeroAllowed: true, 0b1) };
        Assert.That(ChainAnalyzer.ValidateMaskEnumeration(cascades, 1, new[] { 1 }), Is.False);
    }

    [Test]
    public void NestedFinalElseCascade_MissingZeroMask_Detected()
    {
        // Review F3: a fully-represented if/else nested inside an outer conditional arm
        // can be skipped entirely — BuildCascadeShapes derives ZeroAllowed=true from the
        // cascade's relative depth, and an enumeration without mask 0 must be flagged.
        var cascades = new[] { Cascade(zeroAllowed: true, 0b01, 0b10) };
        Assert.That(ChainAnalyzer.ValidateMaskEnumeration(cascades, 2, new[] { 0, 1, 2 }), Is.True);
        Assert.That(ChainAnalyzer.ValidateMaskEnumeration(cascades, 2, new[] { 1, 2 }), Is.False);
    }

    [Test]
    public void TwoCascades_MissingCombination_Detected()
    {
        var cascades = new[]
        {
            Cascade(zeroAllowed: true, 0b01),
            Cascade(zeroAllowed: true, 0b10)
        };
        Assert.That(ChainAnalyzer.ValidateMaskEnumeration(cascades, 2, new[] { 0, 1, 2 }), Is.False);
    }
}
