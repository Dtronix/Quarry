using System;
using System.Collections.Generic;

namespace Quarry.Migration;

/// <summary>
/// Defines a database index.
/// </summary>
public sealed class IndexDef : IEquatable<IndexDef>
{
    public string Name { get; }
    public IReadOnlyList<string> Columns { get; }
    public bool IsUnique { get; }
    public string? Filter { get; }
    public string? Method { get; }
    public bool[]? DescendingColumns { get; }

    public IndexDef(
        string name,
        IReadOnlyList<string> columns,
        bool isUnique = false,
        string? filter = null,
        string? method = null,
        bool[]? descendingColumns = null)
    {
        Name = name;
        Columns = columns;
        IsUnique = isUnique;
        Filter = filter;
        Method = method;
        DescendingColumns = descendingColumns;
    }

    public bool Equals(IndexDef? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (Name != other.Name || IsUnique != other.IsUnique
            || Filter != other.Filter || Method != other.Method
            || Columns.Count != other.Columns.Count)
            return false;
        for (var i = 0; i < Columns.Count; i++)
        {
            if (Columns[i] != other.Columns[i]) return false;
        }
        var descCount = DescendingColumns?.Length ?? 0;
        var otherDescCount = other.DescendingColumns?.Length ?? 0;
        if (descCount != otherDescCount) return false;
        for (var i = 0; i < descCount; i++)
        {
            if (DescendingColumns![i] != other.DescendingColumns![i]) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as IndexDef);

    public override int GetHashCode()
    {
        var hash = HashCode.Combine(Name, IsUnique, Filter, Method, Columns.Count);
        if (DescendingColumns != null)
        {
            for (var i = 0; i < DescendingColumns.Length; i++)
                hash = HashCode.Combine(hash, DescendingColumns[i]);
        }
        return hash;
    }
}
