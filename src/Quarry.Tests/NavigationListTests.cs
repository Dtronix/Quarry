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
    public void Unloaded_IndexerThrowsInvalidOperationException()
    {
        var list = NavigationList<TestEntity>.Unloaded();

        Assert.Throws<InvalidOperationException>(() => _ = list[0]);
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
