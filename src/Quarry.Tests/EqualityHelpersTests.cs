using Quarry.Generators.Models;

namespace Quarry.Tests;

[TestFixture]
public class EqualityHelpersTests
{
    #region SequenceEqual

    [Test]
    public void SequenceEqual_BothNull_ReturnsFalse()
    {
        // ReferenceEquals(null, null) is true, so this returns true
        Assert.That(EqualityHelpers.SequenceEqual<int>(null, null), Is.True);
    }

    [Test]
    public void SequenceEqual_SameReference_ReturnsTrue()
    {
        var list = new List<int> { 1, 2, 3 };
        Assert.That(EqualityHelpers.SequenceEqual<int>(list, list), Is.True);
    }

    [Test]
    public void SequenceEqual_OneNull_ReturnsFalse()
    {
        var list = new List<int> { 1, 2, 3 };
        Assert.That(EqualityHelpers.SequenceEqual<int>(list, null), Is.False);
        Assert.That(EqualityHelpers.SequenceEqual<int>(null, list), Is.False);
    }

    [Test]
    public void SequenceEqual_DifferentLengths_ReturnsFalse()
    {
        var a = new List<int> { 1, 2, 3 };
        var b = new List<int> { 1, 2 };
        Assert.That(EqualityHelpers.SequenceEqual(a, b), Is.False);
    }

    [Test]
    public void SequenceEqual_SameElements_ReturnsTrue()
    {
        var a = new List<int> { 1, 2, 3 };
        var b = new List<int> { 1, 2, 3 };
        Assert.That(EqualityHelpers.SequenceEqual(a, b), Is.True);
    }

    [Test]
    public void SequenceEqual_DifferentElements_ReturnsFalse()
    {
        var a = new List<int> { 1, 2, 3 };
        var b = new List<int> { 1, 9, 3 };
        Assert.That(EqualityHelpers.SequenceEqual(a, b), Is.False);
    }

    [Test]
    public void SequenceEqual_EmptyLists_ReturnsTrue()
    {
        var a = new List<string>();
        var b = new List<string>();
        Assert.That(EqualityHelpers.SequenceEqual(a, b), Is.True);
    }

    [Test]
    public void SequenceEqual_StringElements_ReturnsTrue()
    {
        var a = new List<string> { "foo", "bar" };
        var b = new List<string> { "foo", "bar" };
        Assert.That(EqualityHelpers.SequenceEqual(a, b), Is.True);
    }

    #endregion

    #region NullableSequenceEqual

    [Test]
    public void NullableSequenceEqual_BothNull_ReturnsTrue()
    {
        Assert.That(EqualityHelpers.NullableSequenceEqual<int>(null, null), Is.True);
    }

    [Test]
    public void NullableSequenceEqual_OneNull_ReturnsFalse()
    {
        var list = new List<int> { 1 };
        Assert.That(EqualityHelpers.NullableSequenceEqual<int>(list, null), Is.False);
        Assert.That(EqualityHelpers.NullableSequenceEqual<int>(null, list), Is.False);
    }

    [Test]
    public void NullableSequenceEqual_SameElements_ReturnsTrue()
    {
        var a = new List<int> { 1, 2 };
        var b = new List<int> { 1, 2 };
        Assert.That(EqualityHelpers.NullableSequenceEqual(a, b), Is.True);
    }

    [Test]
    public void NullableSequenceEqual_DifferentElements_ReturnsFalse()
    {
        var a = new List<int> { 1 };
        var b = new List<int> { 2 };
        Assert.That(EqualityHelpers.NullableSequenceEqual(a, b), Is.False);
    }

    #endregion

    #region HashSequence

    [Test]
    public void HashSequence_Null_ReturnsZero()
    {
        Assert.That(EqualityHelpers.HashSequence<int>(null), Is.EqualTo(0));
    }

    [Test]
    public void HashSequence_Empty_ReturnsZero()
    {
        Assert.That(EqualityHelpers.HashSequence(new List<int>()), Is.EqualTo(0));
    }

    [Test]
    public void HashSequence_SameElements_ReturnsSameHash()
    {
        var a = new List<int> { 1, 2, 3 };
        var b = new List<int> { 1, 2, 3 };
        Assert.That(EqualityHelpers.HashSequence(a), Is.EqualTo(EqualityHelpers.HashSequence(b)));
    }

    [Test]
    public void HashSequence_DifferentElements_ReturnsDifferentHash()
    {
        var a = new List<int> { 1, 2, 3 };
        var b = new List<int> { 4, 5, 6 };
        Assert.That(EqualityHelpers.HashSequence(a), Is.Not.EqualTo(EqualityHelpers.HashSequence(b)));
    }

    #endregion

    #region DictionaryEqual

    [Test]
    public void DictionaryEqual_BothNull_ReturnsTrue()
    {
        Assert.That(
            EqualityHelpers.DictionaryEqual<string, int>(null, null),
            Is.True);
    }

    [Test]
    public void DictionaryEqual_OneNull_ReturnsFalse()
    {
        var dict = new Dictionary<string, int> { ["a"] = 1 };
        Assert.That(EqualityHelpers.DictionaryEqual<string, int>(dict, null), Is.False);
        Assert.That(EqualityHelpers.DictionaryEqual<string, int>(null, dict), Is.False);
    }

    [Test]
    public void DictionaryEqual_SameReference_ReturnsTrue()
    {
        var dict = new Dictionary<string, int> { ["a"] = 1 };
        Assert.That(EqualityHelpers.DictionaryEqual(dict, dict), Is.True);
    }

    [Test]
    public void DictionaryEqual_SameContent_ReturnsTrue()
    {
        var a = new Dictionary<string, int> { ["x"] = 10, ["y"] = 20 };
        var b = new Dictionary<string, int> { ["x"] = 10, ["y"] = 20 };
        Assert.That(EqualityHelpers.DictionaryEqual(a, b), Is.True);
    }

    [Test]
    public void DictionaryEqual_DifferentValues_ReturnsFalse()
    {
        var a = new Dictionary<string, int> { ["x"] = 10 };
        var b = new Dictionary<string, int> { ["x"] = 99 };
        Assert.That(EqualityHelpers.DictionaryEqual(a, b), Is.False);
    }

    [Test]
    public void DictionaryEqual_DifferentKeys_ReturnsFalse()
    {
        var a = new Dictionary<string, int> { ["x"] = 10 };
        var b = new Dictionary<string, int> { ["y"] = 10 };
        Assert.That(EqualityHelpers.DictionaryEqual(a, b), Is.False);
    }

    [Test]
    public void DictionaryEqual_DifferentCount_ReturnsFalse()
    {
        var a = new Dictionary<string, int> { ["x"] = 10 };
        var b = new Dictionary<string, int> { ["x"] = 10, ["y"] = 20 };
        Assert.That(EqualityHelpers.DictionaryEqual(a, b), Is.False);
    }

    [Test]
    public void DictionaryEqual_EmptyDictionaries_ReturnsTrue()
    {
        var a = new Dictionary<string, int>();
        var b = new Dictionary<string, int>();
        Assert.That(EqualityHelpers.DictionaryEqual(a, b), Is.True);
    }

    #endregion

    #region TupleListEqual

    [Test]
    public void TupleListEqual_BothNull_ReturnsTrue()
    {
        Assert.That(EqualityHelpers.TupleListEqual(null, null), Is.True);
    }

    [Test]
    public void TupleListEqual_OneNull_ReturnsFalse()
    {
        var list = new List<(string, string?)> { ("Users", "dbo") };
        Assert.That(EqualityHelpers.TupleListEqual(list, null), Is.False);
        Assert.That(EqualityHelpers.TupleListEqual(null, list), Is.False);
    }

    [Test]
    public void TupleListEqual_SameContent_ReturnsTrue()
    {
        var a = new List<(string, string?)> { ("Users", "dbo"), ("Orders", null) };
        var b = new List<(string, string?)> { ("Users", "dbo"), ("Orders", null) };
        Assert.That(EqualityHelpers.TupleListEqual(a, b), Is.True);
    }

    [Test]
    public void TupleListEqual_DifferentTableName_ReturnsFalse()
    {
        var a = new List<(string, string?)> { ("Users", "dbo") };
        var b = new List<(string, string?)> { ("Orders", "dbo") };
        Assert.That(EqualityHelpers.TupleListEqual(a, b), Is.False);
    }

    [Test]
    public void TupleListEqual_DifferentSchemaName_ReturnsFalse()
    {
        var a = new List<(string, string?)> { ("Users", "dbo") };
        var b = new List<(string, string?)> { ("Users", "public") };
        Assert.That(EqualityHelpers.TupleListEqual(a, b), Is.False);
    }

    [Test]
    public void TupleListEqual_NullVsNonNullSchema_ReturnsFalse()
    {
        var a = new List<(string, string?)> { ("Users", null) };
        var b = new List<(string, string?)> { ("Users", "dbo") };
        Assert.That(EqualityHelpers.TupleListEqual(a, b), Is.False);
    }

    #endregion
}
