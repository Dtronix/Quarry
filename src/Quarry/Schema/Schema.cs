using System.Linq.Expressions;

namespace Quarry;

/// <summary>
/// Base class for defining database table schemas.
/// Inherit from this class and define properties using Col&lt;T&gt;, Key&lt;T&gt;, Ref&lt;TEntity, TKey&gt;, and Many&lt;T&gt;.
/// </summary>
/// <remarks>
/// <para>
/// Schema classes must define a static Table property to specify the table name.
/// Optionally, a static SchemaName property can specify the database schema.
/// </para>
/// <para>
/// Example:
/// <code>
/// public class UserSchema : Schema
/// {
///     public static string Table =&gt; "users";
///     public static string SchemaName =&gt; "public";
///
///     public Key&lt;int&gt; UserId =&gt; Identity();
///     public Col&lt;string&gt; UserName =&gt; Length(100);
/// }
/// </code>
/// </para>
/// </remarks>
public abstract class Schema
{
    /// <summary>
    /// Gets the naming style for mapping property names to column names.
    /// Override this property to change the naming convention for all columns in this schema.
    /// Default is <see cref="NamingStyle.Exact"/>.
    /// </summary>
    protected virtual NamingStyle NamingStyle => NamingStyle.Exact;

    #region Column Modifiers

    /// <summary>
    /// Creates a column builder configured as an auto-increment identity column.
    /// </summary>
    /// <typeparam name="T">The column type.</typeparam>
    protected static ColumnBuilder<T> Identity<T>() => default;

    /// <summary>
    /// Creates a column builder configured as an auto-increment identity column.
    /// Type is inferred from the property return type.
    /// </summary>
    protected static ColumnBuilder<int> Identity() => default;

    /// <summary>
    /// Creates a column builder configured for client-generated values (required for GUID keys).
    /// </summary>
    /// <typeparam name="T">The column type.</typeparam>
    protected static ColumnBuilder<T> ClientGenerated<T>() => default;

    /// <summary>
    /// Creates a column builder configured for client-generated GUID values.
    /// </summary>
    protected static ColumnBuilder<Guid> ClientGenerated() => default;

    /// <summary>
    /// Creates a column builder with the specified maximum string length.
    /// </summary>
    /// <param name="maxLength">The maximum length.</param>
    protected static ColumnBuilder<string> Length(int maxLength) => default;

    /// <summary>
    /// Creates a column builder with the specified precision and scale for decimal values.
    /// </summary>
    /// <param name="precision">The total number of digits.</param>
    /// <param name="scale">The number of digits after the decimal point.</param>
    protected static ColumnBuilder<decimal> Precision(int precision, int scale) => default;

    /// <summary>
    /// Creates a column builder with a default value.
    /// </summary>
    /// <typeparam name="T">The column type.</typeparam>
    /// <param name="value">The default value.</param>
    protected static ColumnBuilder<T> Default<T>(T value) => default;

    /// <summary>
    /// Creates a column builder with a default value factory.
    /// </summary>
    /// <typeparam name="T">The column type.</typeparam>
    /// <param name="factory">A function that produces the default value.</param>
    protected static ColumnBuilder<T> Default<T>(Func<T> factory) => default;

    /// <summary>
    /// Creates a column builder configured as a computed (read-only) column.
    /// </summary>
    /// <typeparam name="T">The column type.</typeparam>
    protected static ColumnBuilder<T> Computed<T>() => default;

    /// <summary>
    /// Creates a column builder with a mapped column name.
    /// </summary>
    /// <typeparam name="T">The column type.</typeparam>
    /// <param name="columnName">The database column name.</param>
    protected static ColumnBuilder<T> MapTo<T>(string columnName) => default;

    /// <summary>
    /// Creates a column builder with a custom type mapping.
    /// The mapping class must inherit from <see cref="TypeMapping{TCustom, TDb}"/>.
    /// </summary>
    /// <typeparam name="T">The custom CLR type stored in the column.</typeparam>
    /// <typeparam name="TMapping">The type mapping class that converts between T and the database type.</typeparam>
    protected static ColumnBuilder<T> Mapped<T, TMapping>() => new ColumnBuilder<T>().Mapped<TMapping>();

    #endregion

    #region Index Modifiers

    /// <summary>
    /// Creates an index builder for the specified columns.
    /// </summary>
    /// <param name="columns">The columns to include in the index.</param>
    protected static IndexBuilder Index(params IColumnMarker[] columns) => default;

    /// <summary>
    /// Creates an index builder for the specified columns with sort directions.
    /// </summary>
    /// <param name="columns">The columns with sort directions to include in the index.</param>
    protected static IndexBuilder Index(params IndexedColumn[] columns) => default;

    #endregion

    #region Composite Primary Key

    /// <summary>
    /// Creates a composite primary key from the specified columns.
    /// </summary>
    /// <param name="columns">The columns that form the composite primary key.</param>
    protected static CompositeKey PrimaryKey(params IColumnMarker[] columns) => default;

    #endregion

    #region Foreign Key Modifiers

    /// <summary>
    /// Creates a reference builder configured as a foreign key.
    /// </summary>
    /// <typeparam name="TEntity">The type of the referenced entity.</typeparam>
    /// <typeparam name="TKey">The type of the foreign key.</typeparam>
    protected static RefBuilder<TEntity, TKey> ForeignKey<TEntity, TKey>() where TEntity : Schema
        => default;

    /// <summary>
    /// Creates a reference builder configured as a foreign key.
    /// Types are inferred from the property return type.
    /// </summary>
    protected static RefBuilder<TEntity, TKey> ForeignKey<TEntity, TKey>(Ref<TEntity, TKey> _) where TEntity : Schema
        => default;

    #endregion

    #region Relationship Modifiers

    /// <summary>
    /// Defines a one-to-many relationship.
    /// </summary>
    /// <typeparam name="T">The type of the related entity.</typeparam>
    /// <param name="foreignKeySelector">Expression selecting the foreign key property on the related entity.</param>
    protected static RelationshipBuilder<T> HasMany<T>(Expression<Func<T, object?>> foreignKeySelector) where T : Schema
        => default;

    /// <summary>
    /// Defines a singular (N:1) navigation to a target entity.
    /// The foreign key is specified by name for disambiguation when multiple Ref columns
    /// reference the same entity type.
    /// </summary>
    /// <typeparam name="T">The target schema type.</typeparam>
    /// <param name="foreignKeyPropertyName">The name of the Ref property (use nameof()).</param>
    protected static OneBuilder<T> HasOne<T>(string foreignKeyPropertyName) where T : Schema
        => default;

    /// <summary>
    /// Defines a many-to-many skip-navigation through a junction table.
    /// The result is a <see cref="Many{T}"/> navigation that transparently traverses the junction entity.
    /// </summary>
    /// <typeparam name="TTarget">The target schema type.</typeparam>
    /// <typeparam name="TJunction">The junction schema type.</typeparam>
    /// <param name="junctionNavigation">Expression selecting the Many&lt;TJunction&gt; navigation to the junction table.</param>
    /// <param name="targetNavigation">Expression selecting the One&lt;TTarget&gt; navigation on the junction table.</param>
    protected static RelationshipBuilder<TTarget> HasManyThrough<TTarget, TJunction, TSelf>(
        Expression<Func<TSelf, object?>> junctionNavigation,
        Expression<Func<TJunction, object?>> targetNavigation)
        where TTarget : Schema
        where TJunction : Schema
        where TSelf : Schema
        => default;

    #endregion
}
