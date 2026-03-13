namespace Quarry;

/// <summary>
/// Marker type for composite primary key declarations in schema definitions.
/// Used as the return type for properties that define multi-column primary keys.
/// </summary>
/// <remarks>
/// <para>
/// Example usage in a schema class:
/// <code>
/// public Ref&lt;StudentSchema, int&gt; StudentId =&gt; ForeignKey&lt;StudentSchema, int&gt;();
/// public Ref&lt;CourseSchema, int&gt; CourseId =&gt; ForeignKey&lt;CourseSchema, int&gt;();
/// public CompositeKey PK =&gt; PrimaryKey(StudentId, CourseId);
/// </code>
/// </para>
/// </remarks>
public readonly struct CompositeKey
{
}
