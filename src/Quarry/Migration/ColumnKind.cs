namespace Quarry.Migration;

/// <summary>
/// The kind of column definition.
/// </summary>
public enum ColumnKind
{
    /// <summary>A standard column (Col&lt;T&gt;).</summary>
    Standard,

    /// <summary>A primary key column (Key&lt;T&gt;).</summary>
    PrimaryKey,

    /// <summary>A foreign key column (Ref&lt;TEntity, TKey&gt;).</summary>
    ForeignKey
}
