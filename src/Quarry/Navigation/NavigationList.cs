using System.Collections;

namespace Quarry;

/// <summary>
/// Represents a lazily-loaded collection of related entities.
/// Implements IReadOnlyList&lt;T&gt; and provides an IsLoaded property
/// to check if the collection has been populated via a join.
/// </summary>
/// <typeparam name="T">The type of entities in the collection.</typeparam>
public sealed class NavigationList<T> : IReadOnlyList<T>
{
    private readonly List<T>? _items;

    /// <summary>
    /// Gets whether this navigation collection has been loaded.
    /// Returns false when the related entities were not fetched via a join.
    /// </summary>
    public bool IsLoaded { get; }

    /// <summary>
    /// Gets the number of elements in the collection.
    /// Returns 0 if the collection has not been loaded.
    /// </summary>
    public int Count => _items?.Count ?? 0;

    /// <summary>
    /// Gets the element at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the element to get.</param>
    /// <exception cref="InvalidOperationException">Thrown if the collection has not been loaded.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the index is out of range.</exception>
    public T this[int index]
    {
        get
        {
            if (_items is null)
                throw new InvalidOperationException("Navigation collection has not been loaded. Use a join to load related entities.");
            return _items[index];
        }
    }

    /// <summary>
    /// Creates an unloaded navigation list.
    /// </summary>
    internal NavigationList()
    {
        _items = null;
        IsLoaded = false;
    }

    /// <summary>
    /// Creates a loaded navigation list with the specified items.
    /// </summary>
    /// <param name="items">The items to populate the collection with.</param>
    internal NavigationList(IEnumerable<T> items)
    {
        _items = items.ToList();
        IsLoaded = true;
    }

    /// <summary>
    /// Creates a loaded navigation list from an existing list.
    /// </summary>
    /// <param name="items">The list of items.</param>
    internal NavigationList(List<T> items)
    {
        _items = items;
        IsLoaded = true;
    }

    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// Returns an empty enumerator if the collection has not been loaded.
    /// </summary>
    public IEnumerator<T> GetEnumerator()
    {
        return (_items ?? Enumerable.Empty<T>()).GetEnumerator();
    }

    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// </summary>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Creates an unloaded navigation list.
    /// </summary>
    public static NavigationList<T> Unloaded() => new();

    /// <summary>
    /// Creates a loaded navigation list with the specified items.
    /// </summary>
    /// <param name="items">The items to populate the collection with.</param>
    public static NavigationList<T> Loaded(IEnumerable<T> items) => new(items);

    /// <summary>
    /// Creates a loaded navigation list from an existing list.
    /// </summary>
    /// <param name="items">The list of items.</param>
    public static NavigationList<T> Loaded(List<T> items) => new(items);
}
