using System;
using System.Collections.Generic;

namespace Quarry.Generators.Models;

internal static class EqualityHelpers
{
    public static bool SequenceEqual<T>(IReadOnlyList<T>? a, IReadOnlyList<T>? b) where T : IEquatable<T>
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (!a[i].Equals(b[i])) return false;
        }
        return true;
    }

    public static int HashSequence<T>(IReadOnlyList<T>? list) where T : IEquatable<T>
    {
        if (list is null || list.Count == 0) return 0;
        var hash = new HashCode();
        foreach (var item in list)
            hash.Add(item);
        return hash.ToHashCode();
    }

    public static bool NullableSequenceEqual<T>(IReadOnlyList<T>? a, IReadOnlyList<T>? b) where T : IEquatable<T>
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return SequenceEqual(a, b);
    }

    public static bool DictionaryEqual<TKey, TValue>(
        Dictionary<TKey, TValue>? a, Dictionary<TKey, TValue>? b)
        where TKey : notnull, IEquatable<TKey>
        where TValue : IEquatable<TValue>
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a.Count != b.Count) return false;
        foreach (var kvp in a)
        {
            if (!b.TryGetValue(kvp.Key, out var otherValue) || !kvp.Value.Equals(otherValue))
                return false;
        }
        return true;
    }

    public static bool TupleListEqual(
        IReadOnlyList<(string TableName, string? SchemaName)>? a,
        IReadOnlyList<(string TableName, string? SchemaName)>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].TableName != b[i].TableName || a[i].SchemaName != b[i].SchemaName)
                return false;
        }
        return true;
    }
}
