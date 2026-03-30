using Quarry.Internal;

namespace Quarry.Tests;


[TestFixture]
internal class CollectionHelperTests
{
    [Test]
    public void Materialize_List_ReturnsSameInstance()
    {
        var list = new List<int> { 1, 2, 3 };
        var result = CollectionHelper.Materialize(list);
        Assert.That(result, Is.SameAs(list));
    }

    [Test]
    public void Materialize_Array_ReturnsSameInstance()
    {
        var array = new[] { 1, 2, 3 };
        var result = CollectionHelper.Materialize(array);
        Assert.That(result, Is.SameAs(array));
    }

    [Test]
    public void Materialize_LazyEnumerable_ReturnsNewList()
    {
        var source = new[] { new { Id = 1 }, new { Id = 2 }, new { Id = 3 } };
        IEnumerable<int> lazy = source.Select(x => x.Id);

        var result = CollectionHelper.Materialize(lazy);

        Assert.That(result, Is.Not.SameAs(lazy));
        Assert.That(result, Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(result.Count, Is.EqualTo(3));
    }

    [Test]
    public void Materialize_EmptyEnumerable_ReturnsEmptyList()
    {
        IEnumerable<int> empty = Enumerable.Empty<int>();
        var result = CollectionHelper.Materialize(empty);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(0));
    }
}
