using System.Collections.Immutable;
using Quarry.Generators.Models;

namespace Quarry.Tests;

[TestFixture]
public class EquatableCollectionTests
{
    #region EquatableArray - Equality

    [Test]
    public void EquatableArray_SameElements_AreEqual()
    {
        var a = new EquatableArray<int>(ImmutableArray.Create(1, 2, 3));
        var b = new EquatableArray<int>(ImmutableArray.Create(1, 2, 3));
        Assert.That(a.Equals(b), Is.True);
        Assert.That(a == b, Is.True);
    }

    [Test]
    public void EquatableArray_DifferentElements_AreNotEqual()
    {
        var a = new EquatableArray<int>(ImmutableArray.Create(1, 2, 3));
        var b = new EquatableArray<int>(ImmutableArray.Create(1, 9, 3));
        Assert.That(a.Equals(b), Is.False);
        Assert.That(a != b, Is.True);
    }

    [Test]
    public void EquatableArray_DifferentLengths_AreNotEqual()
    {
        var a = new EquatableArray<int>(ImmutableArray.Create(1, 2));
        var b = new EquatableArray<int>(ImmutableArray.Create(1, 2, 3));
        Assert.That(a.Equals(b), Is.False);
    }

    [Test]
    public void EquatableArray_BothDefault_AreEqual()
    {
        var a = new EquatableArray<int>(default(ImmutableArray<int>));
        var b = new EquatableArray<int>(default(ImmutableArray<int>));
        Assert.That(a.Equals(b), Is.True);
    }

    [Test]
    public void EquatableArray_OneDefault_AreNotEqual()
    {
        var a = new EquatableArray<int>(default(ImmutableArray<int>));
        var b = new EquatableArray<int>(ImmutableArray.Create(1));
        Assert.That(a.Equals(b), Is.False);
        Assert.That(b.Equals(a), Is.False);
    }

    [Test]
    public void EquatableArray_BothEmpty_AreEqual()
    {
        var a = EquatableArray<int>.Empty;
        var b = new EquatableArray<int>(ImmutableArray<int>.Empty);
        Assert.That(a.Equals(b), Is.True);
    }

    [Test]
    public void EquatableArray_ObjectEquals_Works()
    {
        var a = new EquatableArray<int>(ImmutableArray.Create(1, 2));
        object b = new EquatableArray<int>(ImmutableArray.Create(1, 2));
        Assert.That(a.Equals(b), Is.True);
    }

    [Test]
    public void EquatableArray_ObjectEquals_WrongType_ReturnsFalse()
    {
        var a = new EquatableArray<int>(ImmutableArray.Create(1));
        Assert.That(a.Equals("not an array"), Is.False);
    }

    #endregion

    #region EquatableArray - HashCode

    [Test]
    public void EquatableArray_SameElements_SameHashCode()
    {
        var a = new EquatableArray<int>(ImmutableArray.Create(1, 2, 3));
        var b = new EquatableArray<int>(ImmutableArray.Create(1, 2, 3));
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    [Test]
    public void EquatableArray_Default_HashCodeIsZero()
    {
        var a = new EquatableArray<int>(default(ImmutableArray<int>));
        Assert.That(a.GetHashCode(), Is.EqualTo(0));
    }

    [Test]
    public void EquatableArray_Empty_HashCodeIsZero()
    {
        var a = EquatableArray<int>.Empty;
        Assert.That(a.GetHashCode(), Is.EqualTo(0));
    }

    #endregion

    #region EquatableArray - Collection behavior

    [Test]
    public void EquatableArray_Count_ReturnsCorrectValue()
    {
        var arr = new EquatableArray<int>(ImmutableArray.Create(1, 2, 3));
        Assert.That(arr.Count, Is.EqualTo(3));
    }

    [Test]
    public void EquatableArray_DefaultCount_ReturnsZero()
    {
        var arr = new EquatableArray<int>(default(ImmutableArray<int>));
        Assert.That(arr.Count, Is.EqualTo(0));
    }

    [Test]
    public void EquatableArray_Indexer_ReturnsCorrectElement()
    {
        var arr = new EquatableArray<string>(ImmutableArray.Create("a", "b", "c"));
        Assert.That(arr[1], Is.EqualTo("b"));
    }

    [Test]
    public void EquatableArray_IsDefault_ReturnsTrueForDefault()
    {
        var arr = new EquatableArray<int>(default(ImmutableArray<int>));
        Assert.That(arr.IsDefault, Is.True);
    }

    [Test]
    public void EquatableArray_IsEmpty_ReturnsTrueForEmpty()
    {
        Assert.That(EquatableArray<int>.Empty.IsEmpty, Is.True);
    }

    [Test]
    public void EquatableArray_IsEmpty_ReturnsTrueForDefault()
    {
        var arr = new EquatableArray<int>(default(ImmutableArray<int>));
        Assert.That(arr.IsEmpty, Is.True);
    }

    [Test]
    public void EquatableArray_ImplicitConversion_Works()
    {
        EquatableArray<int> arr = ImmutableArray.Create(1, 2);
        Assert.That(arr.Count, Is.EqualTo(2));
    }

    [Test]
    public void EquatableArray_FromEnumerable_Works()
    {
        var arr = new EquatableArray<int>(new[] { 10, 20, 30 });
        Assert.That(arr.Count, Is.EqualTo(3));
        Assert.That(arr[0], Is.EqualTo(10));
    }

    [Test]
    public void EquatableArray_AsImmutableArray_ReturnsEmptyForDefault()
    {
        var arr = new EquatableArray<int>(default(ImmutableArray<int>));
        var immutable = arr.AsImmutableArray();
        Assert.That(immutable.IsDefault, Is.False);
        Assert.That(immutable.IsEmpty, Is.True);
    }

    [Test]
    public void EquatableArray_Enumeration_Works()
    {
        var arr = new EquatableArray<int>(ImmutableArray.Create(1, 2, 3));
        var list = new List<int>();
        foreach (var item in arr)
            list.Add(item);
        Assert.That(list, Is.EqualTo(new[] { 1, 2, 3 }));
    }

    #endregion

    #region EquatableDictionary - Equality

    [Test]
    public void EquatableDictionary_SameContent_AreEqual()
    {
        var a = new EquatableDictionary<string, int>(
            ImmutableDictionary.CreateRange(new[] {
                new KeyValuePair<string, int>("x", 1),
                new KeyValuePair<string, int>("y", 2)
            }));
        var b = new EquatableDictionary<string, int>(
            ImmutableDictionary.CreateRange(new[] {
                new KeyValuePair<string, int>("x", 1),
                new KeyValuePair<string, int>("y", 2)
            }));
        Assert.That(a.Equals(b), Is.True);
        Assert.That(a == b, Is.True);
    }

    [Test]
    public void EquatableDictionary_DifferentValues_AreNotEqual()
    {
        var a = new EquatableDictionary<string, int>(
            new Dictionary<string, int> { ["x"] = 1 });
        var b = new EquatableDictionary<string, int>(
            new Dictionary<string, int> { ["x"] = 99 });
        Assert.That(a.Equals(b), Is.False);
        Assert.That(a != b, Is.True);
    }

    [Test]
    public void EquatableDictionary_DifferentKeys_AreNotEqual()
    {
        var a = new EquatableDictionary<string, int>(
            new Dictionary<string, int> { ["x"] = 1 });
        var b = new EquatableDictionary<string, int>(
            new Dictionary<string, int> { ["y"] = 1 });
        Assert.That(a.Equals(b), Is.False);
    }

    [Test]
    public void EquatableDictionary_DifferentCount_AreNotEqual()
    {
        var a = new EquatableDictionary<string, int>(
            new Dictionary<string, int> { ["x"] = 1 });
        var b = new EquatableDictionary<string, int>(
            new Dictionary<string, int> { ["x"] = 1, ["y"] = 2 });
        Assert.That(a.Equals(b), Is.False);
    }

    [Test]
    public void EquatableDictionary_BothEmpty_AreEqual()
    {
        var a = EquatableDictionary<string, int>.Empty;
        var b = new EquatableDictionary<string, int>(
            ImmutableDictionary<string, int>.Empty);
        Assert.That(a.Equals(b), Is.True);
    }

    [Test]
    public void EquatableDictionary_BothDefault_AreEqual()
    {
        var a = default(EquatableDictionary<string, int>);
        var b = default(EquatableDictionary<string, int>);
        Assert.That(a.Equals(b), Is.True);
    }

    [Test]
    public void EquatableDictionary_ObjectEquals_Works()
    {
        var a = new EquatableDictionary<string, int>(
            new Dictionary<string, int> { ["x"] = 1 });
        object b = new EquatableDictionary<string, int>(
            new Dictionary<string, int> { ["x"] = 1 });
        Assert.That(a.Equals(b), Is.True);
    }

    #endregion

    #region EquatableDictionary - HashCode

    [Test]
    public void EquatableDictionary_SameContent_SameHashCode()
    {
        var a = new EquatableDictionary<string, int>(
            new Dictionary<string, int> { ["x"] = 1, ["y"] = 2 });
        var b = new EquatableDictionary<string, int>(
            new Dictionary<string, int> { ["x"] = 1, ["y"] = 2 });
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    [Test]
    public void EquatableDictionary_Empty_HashCodeIsZero()
    {
        var a = EquatableDictionary<string, int>.Empty;
        Assert.That(a.GetHashCode(), Is.EqualTo(0));
    }

    [Test]
    public void EquatableDictionary_HashIsOrderIndependent()
    {
        // XOR-based hash should be order-independent
        var a = new EquatableDictionary<string, int>(
            ImmutableDictionary.CreateRange(new[] {
                new KeyValuePair<string, int>("a", 1),
                new KeyValuePair<string, int>("b", 2)
            }));
        var b = new EquatableDictionary<string, int>(
            ImmutableDictionary.CreateRange(new[] {
                new KeyValuePair<string, int>("b", 2),
                new KeyValuePair<string, int>("a", 1)
            }));
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    #endregion

    #region EquatableDictionary - Collection behavior

    [Test]
    public void EquatableDictionary_Count_ReturnsCorrectValue()
    {
        var dict = new EquatableDictionary<string, int>(
            new Dictionary<string, int> { ["x"] = 1, ["y"] = 2 });
        Assert.That(dict.Count, Is.EqualTo(2));
    }

    [Test]
    public void EquatableDictionary_Indexer_ReturnsCorrectValue()
    {
        var dict = new EquatableDictionary<string, int>(
            new Dictionary<string, int> { ["key"] = 42 });
        Assert.That(dict["key"], Is.EqualTo(42));
    }

    [Test]
    public void EquatableDictionary_ContainsKey_Works()
    {
        var dict = new EquatableDictionary<string, int>(
            new Dictionary<string, int> { ["key"] = 1 });
        Assert.That(dict.ContainsKey("key"), Is.True);
        Assert.That(dict.ContainsKey("missing"), Is.False);
    }

    [Test]
    public void EquatableDictionary_TryGetValue_Works()
    {
        var dict = new EquatableDictionary<string, int>(
            new Dictionary<string, int> { ["key"] = 42 });

        Assert.That(dict.TryGetValue("key", out var value), Is.True);
        Assert.That(value, Is.EqualTo(42));
        Assert.That(dict.TryGetValue("missing", out _), Is.False);
    }

    [Test]
    public void EquatableDictionary_FromEnumerable_Works()
    {
        var pairs = new[] {
            new KeyValuePair<string, int>("a", 1),
            new KeyValuePair<string, int>("b", 2)
        };
        var dict = new EquatableDictionary<string, int>(pairs);
        Assert.That(dict.Count, Is.EqualTo(2));
        Assert.That(dict["a"], Is.EqualTo(1));
    }

    [Test]
    public void EquatableDictionary_Keys_ReturnsAllKeys()
    {
        var dict = new EquatableDictionary<string, int>(
            new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 });
        Assert.That(dict.Keys, Is.EquivalentTo(new[] { "a", "b" }));
    }

    [Test]
    public void EquatableDictionary_Values_ReturnsAllValues()
    {
        var dict = new EquatableDictionary<string, int>(
            new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 });
        Assert.That(dict.Values, Is.EquivalentTo(new[] { 1, 2 }));
    }

    #endregion
}
