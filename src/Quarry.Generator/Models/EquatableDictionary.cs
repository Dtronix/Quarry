using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Quarry.Generators.Models;

/// <summary>
/// A readonly dictionary wrapper that provides structural (key-value-wise) equality.
/// Used for <see cref="PrebuiltChainInfo.SqlMap"/> and similar pipeline-visible dictionaries.
/// </summary>
internal readonly struct EquatableDictionary<TKey, TValue> : IEquatable<EquatableDictionary<TKey, TValue>>, IReadOnlyDictionary<TKey, TValue>
    where TKey : IEquatable<TKey>
    where TValue : IEquatable<TValue>
{
    private readonly ImmutableDictionary<TKey, TValue>? _dictionary;

    public EquatableDictionary(ImmutableDictionary<TKey, TValue> dictionary)
    {
        _dictionary = dictionary;
    }

    public EquatableDictionary(Dictionary<TKey, TValue> dictionary)
    {
        _dictionary = dictionary.ToImmutableDictionary();
    }

    public EquatableDictionary(IEnumerable<KeyValuePair<TKey, TValue>> pairs)
    {
        _dictionary = pairs.ToImmutableDictionary();
    }

    private ImmutableDictionary<TKey, TValue> Dictionary =>
        _dictionary ?? ImmutableDictionary<TKey, TValue>.Empty;

    public int Count => Dictionary.Count;

    public TValue this[TKey key] => Dictionary[key];

    public IEnumerable<TKey> Keys => Dictionary.Keys;
    public IEnumerable<TValue> Values => Dictionary.Values;

    public bool ContainsKey(TKey key) => Dictionary.ContainsKey(key);
    public bool TryGetValue(TKey key, out TValue value) => Dictionary.TryGetValue(key, out value!);

    public bool Equals(EquatableDictionary<TKey, TValue> other)
    {
        var a = Dictionary;
        var b = other.Dictionary;

        if (ReferenceEquals(a, b))
            return true;
        if (a.Count != b.Count)
            return false;

        foreach (var kvp in a)
        {
            if (!b.TryGetValue(kvp.Key, out var otherValue) || !kvp.Value.Equals(otherValue))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableDictionary<TKey, TValue> other && Equals(other);

    public override int GetHashCode()
    {
        var dict = Dictionary;
        if (dict.Count == 0)
            return 0;

        // Order-independent hash: XOR individual entry hashes
        int hash = 0;
        foreach (var kvp in dict)
            hash ^= HashCode.Combine(kvp.Key, kvp.Value);
        return hash;
    }

    public static bool operator ==(EquatableDictionary<TKey, TValue> left, EquatableDictionary<TKey, TValue> right) => left.Equals(right);
    public static bool operator !=(EquatableDictionary<TKey, TValue> left, EquatableDictionary<TKey, TValue> right) => !left.Equals(right);

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => Dictionary.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => Dictionary.GetEnumerator();

    public static readonly EquatableDictionary<TKey, TValue> Empty = new(ImmutableDictionary<TKey, TValue>.Empty);
}
