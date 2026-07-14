#if QUARRY_RUNTIME
namespace Quarry.Migration;
#else
namespace Quarry.Shared.Migration;
#endif

/// <summary>
/// The kind of column definition.
/// </summary>
#if QUARRY_GENERATOR
internal
#else
public
#endif
enum ColumnKind
{
    /// <summary>A standard column (Col&lt;T&gt;).</summary>
    Standard,

    /// <summary>A primary key column (Key&lt;T&gt;).</summary>
    PrimaryKey,

    /// <summary>A foreign key column (Ref&lt;TEntity, TKey&gt;).</summary>
    ForeignKey
}
