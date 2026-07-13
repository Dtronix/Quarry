using Quarry.Internal;

namespace Quarry.Tests;

/// <summary>
/// Tests for <see cref="ParameterNames"/> — the pre-computed parameter-name cache
/// (#308 item 6b widened the cache to 2100 to cover SQL Server's parameter ceiling).
/// The returned value must be identical whether it comes from the cache or the concat
/// fallback; these tests verify correctness across the cache boundary.
/// </summary>
[TestFixture]
public class ParameterNamesTests
{
    [TestCase(0, "@p0")]
    [TestCase(1, "@p1")]
    [TestCase(255, "@p255")]
    [TestCase(256, "@p256")]
    [TestCase(2099, "@p2099")]   // last cached entry
    [TestCase(2100, "@p2100")]   // first concat fallback
    [TestCase(5000, "@p5000")]   // large concat fallback
    public void AtP_ReturnsCorrectName(int index, string expected)
    {
        Assert.That(ParameterNames.AtP(index), Is.EqualTo(expected));
    }

    [TestCase(0, "$1")]
    [TestCase(1, "$2")]
    [TestCase(255, "$256")]
    [TestCase(256, "$257")]
    [TestCase(2099, "$2100")]    // last cached entry (1-based output)
    [TestCase(2100, "$2101")]    // first concat fallback
    [TestCase(5000, "$5001")]    // large concat fallback
    public void Dollar_ReturnsCorrectName_OneBased(int index, string expected)
    {
        Assert.That(ParameterNames.Dollar(index), Is.EqualTo(expected));
    }

    [Test]
    public void AtP_CachedEntriesAreInterned_SameReferenceAcrossCalls()
    {
        // Within the cache range, repeated calls return the same string instance
        // (no per-call allocation).
        var atpFirst = ParameterNames.AtP(2099);
        var atpSecond = ParameterNames.AtP(2099);
        var dollarFirst = ParameterNames.Dollar(2099);
        var dollarSecond = ParameterNames.Dollar(2099);
        Assert.That(atpSecond, Is.SameAs(atpFirst));
        Assert.That(dollarSecond, Is.SameAs(dollarFirst));
    }
}
