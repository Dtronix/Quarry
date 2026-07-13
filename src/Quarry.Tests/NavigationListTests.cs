namespace Quarry.Tests;

/// <summary>
/// Tests for the NavigationList&lt;T&gt; class.
/// </summary>
public class NavigationListTests
{
    private class TestEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    [Test]
    public void Unloaded_IsLoadedIsFalse()
    {
        var list = NavigationList<TestEntity>.Unloaded();

        Assert.That(list.IsLoaded, Is.False);
    }

    [Test]
    public void Unloaded_CountIsZero()
    {
        var list = NavigationList<TestEntity>.Unloaded();

        Assert.That(list.Count, Is.EqualTo(0));
    }

    [Test]
    public void Unloaded_EnumeratorReturnsEmpty()
    {
        var list = NavigationList<TestEntity>.Unloaded();

        var items = list.ToList();

        Assert.That(items, Is.Empty);
    }

    [Test]
    public void Unloaded_GetEnumerator_ReturnsSharedInstance()
    {
        // #308 item 6d: the unloaded path returns a shared empty enumerator instead of
        // allocating one (via Enumerable.Empty<T>().GetEnumerator()) each time.
        var list = NavigationList<TestEntity>.Unloaded();

        var e1 = list.GetEnumerator();
        var e2 = list.GetEnumerator();

        Assert.That(e2, Is.SameAs(e1));
        Assert.That(e1.MoveNext(), Is.False);
    }

    [Test]
    public void Unloaded_IndexerThrowsInvalidOperationException()
    {
        var list = NavigationList<TestEntity>.Unloaded();

        Assert.Throws<InvalidOperationException>(() => _ = list[0]);
    }

    [Test]
    public void Unloaded_ReturnsSharedSingleton()
    {
        // #308 item 2: Unloaded() must return a cached singleton, not a fresh
        // allocation per call — generated entities initialize every Many<T>
        // navigation from it once per row.
        var a = NavigationList<TestEntity>.Unloaded();
        var b = NavigationList<TestEntity>.Unloaded();

        Assert.That(a, Is.SameAs(b));
        Assert.That(a.IsLoaded, Is.False);
        Assert.That(a.Count, Is.EqualTo(0));
    }

    [Test]
    public void Unloaded_IsDistinctPerTypeArgument()
    {
        // The singleton is per closed generic type; different type arguments
        // must not share an instance (and the types are unrelated anyway).
        var ints = NavigationList<int>.Unloaded();
        var strings = NavigationList<string>.Unloaded();

        Assert.That(ints, Is.SameAs(NavigationList<int>.Unloaded()));
        Assert.That(strings, Is.SameAs(NavigationList<string>.Unloaded()));
        Assert.That(ints.IsLoaded, Is.False);
        Assert.That(strings.IsLoaded, Is.False);
    }

    [Test]
    public void Loaded_IsLoadedIsTrue()
    {
        var items = new List<TestEntity>
        {
            new() { Id = 1, Name = "One" },
            new() { Id = 2, Name = "Two" }
        };
        var list = NavigationList<TestEntity>.Loaded(items);

        Assert.That(list.IsLoaded, Is.True);
    }

    [Test]
    public void Loaded_CountReflectsItems()
    {
        var items = new List<TestEntity>
        {
            new() { Id = 1, Name = "One" },
            new() { Id = 2, Name = "Two" },
            new() { Id = 3, Name = "Three" }
        };
        var list = NavigationList<TestEntity>.Loaded(items);

        Assert.That(list.Count, Is.EqualTo(3));
    }

    [Test]
    public void Loaded_IndexerReturnsCorrectItem()
    {
        var items = new List<TestEntity>
        {
            new() { Id = 1, Name = "One" },
            new() { Id = 2, Name = "Two" }
        };
        var list = NavigationList<TestEntity>.Loaded(items);

        Assert.That(list[0].Name, Is.EqualTo("One"));
        Assert.That(list[1].Name, Is.EqualTo("Two"));
    }

    [Test]
    public void Loaded_IndexerOutOfRangeThrowsArgumentOutOfRangeException()
    {
        var items = new List<TestEntity> { new() { Id = 1, Name = "One" } };
        var list = NavigationList<TestEntity>.Loaded(items);

        Assert.Throws<ArgumentOutOfRangeException>(() => _ = list[5]);
    }

    [Test]
    public void Loaded_EnumeratorReturnsAllItems()
    {
        var items = new List<TestEntity>
        {
            new() { Id = 1, Name = "One" },
            new() { Id = 2, Name = "Two" }
        };
        var list = NavigationList<TestEntity>.Loaded(items);

        var enumerated = list.ToList();

        Assert.That(enumerated, Has.Count.EqualTo(2));
        Assert.That(enumerated[0].Name, Is.EqualTo("One"));
        Assert.That(enumerated[1].Name, Is.EqualTo("Two"));
    }

    [Test]
    public void Loaded_FromEnumerable_Works()
    {
        IEnumerable<TestEntity> items = new[]
        {
            new TestEntity { Id = 1, Name = "One" }
        };
        var list = NavigationList<TestEntity>.Loaded(items);

        Assert.That(list.IsLoaded, Is.True);
        Assert.That(list.Count, Is.EqualTo(1));
    }

    [Test]
    public void Loaded_EmptyList_IsStillLoaded()
    {
        var list = NavigationList<TestEntity>.Loaded(new List<TestEntity>());

        Assert.That(list.IsLoaded, Is.True);
        Assert.That(list.Count, Is.EqualTo(0));
    }

    [Test]
    public void ImplementsIReadOnlyList()
    {
        var list = NavigationList<TestEntity>.Loaded(new List<TestEntity>());

        Assert.That(list, Is.InstanceOf<IReadOnlyList<TestEntity>>());
    }

    [Test]
    public void ForeachWorks()
    {
        var items = new List<TestEntity>
        {
            new() { Id = 1, Name = "One" },
            new() { Id = 2, Name = "Two" }
        };
        var list = NavigationList<TestEntity>.Loaded(items);

        var names = new List<string>();
        foreach (var item in list)
        {
            names.Add(item.Name);
        }

        Assert.That(names, Is.EqualTo(new[] { "One", "Two" }));
    }
}
