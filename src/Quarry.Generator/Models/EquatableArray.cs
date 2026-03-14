using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Quarry.Generators.Models;

/// <summary>
/// A readonly wrapper around <see cref="ImmutableArray{T}"/> that provides
/// structural (element-wise) equality. Used throughout the incremental pipeline
/// so that Roslyn's caching can detect unchanged values.
/// </summary>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
    where T : IEquatable<T>
{
    private readonly ImmutableArray<T> _array;

    public EquatableArray(ImmutableArray<T> array)
    {
        _array = array;
    }

    public EquatableArray(IEnumerable<T> items)
    {
        _array = items.ToImmutableArray();
    }

    /// <summary>
    /// Gets the underlying <see cref="ImmutableArray{T}"/>.
    /// Returns <see cref="ImmutableArray{T}.Empty"/> if the backing array is default.
    /// </summary>
    public ImmutableArray<T> AsImmutableArray() => _array.IsDefault ? ImmutableArray<T>.Empty : _array;

    public int Count => _array.IsDefault ? 0 : _array.Length;

    public T this[int index] => _array[index];

    public bool IsDefault => _array.IsDefault;

    public bool IsEmpty => _array.IsDefault || _array.IsEmpty;

    public bool Equals(EquatableArray<T> other)
    {
        if (_array.IsDefault && other._array.IsDefault)
            return true;
        if (_array.IsDefault || other._array.IsDefault)
            return false;
        if (_array.Length != other._array.Length)
            return false;

        for (int i = 0; i < _array.Length; i++)
        {
            if (!_array[i].Equals(other._array[i]))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        if (_array.IsDefault || _array.Length == 0)
            return 0;

        var hash = new HashCode();
        foreach (var item in _array)
            hash.Add(item);
        return hash.ToHashCode();
    }

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);
    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);

    public ImmutableArray<T>.Enumerator GetEnumerator() =>
        _array.IsDefault ? ImmutableArray<T>.Empty.GetEnumerator() : _array.GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() =>
        ((IEnumerable<T>)(_array.IsDefault ? ImmutableArray<T>.Empty : _array)).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() =>
        ((IEnumerable)(_array.IsDefault ? ImmutableArray<T>.Empty : _array)).GetEnumerator();

    public static readonly EquatableArray<T> Empty = new(ImmutableArray<T>.Empty);

    public static implicit operator EquatableArray<T>(ImmutableArray<T> array) => new(array);
}
