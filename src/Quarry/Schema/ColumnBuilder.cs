namespace Quarry;

/// <summary>
/// Fluent builder for configuring column modifiers.
/// Implicitly converts to Col&lt;T&gt; or Key&lt;T&gt;.
/// </summary>
/// <typeparam name="T">The CLR type of the column value.</typeparam>
public readonly struct ColumnBuilder<T>
{
    // Note: The actual modifier values are extracted by the source generator
    // at compile time from the syntax tree. No runtime state is needed.

    /// <summary>
    /// Marks this column as an auto-increment identity column.
    /// </summary>
    public ColumnBuilder<T> Identity() => default;

    /// <summary>
    /// Marks this column as client-generated (required for GUID primary keys).
    /// </summary>
    public ColumnBuilder<T> ClientGenerated() => default;

    /// <summary>
    /// Marks this column as a computed/read-only column.
    /// </summary>
    public ColumnBuilder<T> Computed() => default;

    /// <summary>
    /// Marks this column as a computed column with the specified SQL expression.
    /// </summary>
    /// <param name="expression">The SQL expression for the computed column.</param>
    public ColumnBuilder<T> Computed(string expression) => default;

    /// <summary>
    /// Specifies the collation for this column.
    /// </summary>
    /// <param name="collation">The collation name (e.g., "Latin1_General_CI_AS", "en_US.utf8").</param>
    public ColumnBuilder<T> Collation(string collation) => default;

    /// <summary>
    /// Specifies the maximum length for string columns.
    /// </summary>
    /// <param name="maxLength">The maximum length.</param>
    public ColumnBuilder<T> Length(int maxLength) => default;

    /// <summary>
    /// Specifies the precision and scale for decimal columns.
    /// </summary>
    /// <param name="precision">The total number of digits.</param>
    /// <param name="scale">The number of digits after the decimal point.</param>
    public ColumnBuilder<T> Precision(int precision, int scale) => default;

    /// <summary>
    /// Specifies a default value for the column.
    /// </summary>
    /// <param name="value">The default value.</param>
    public ColumnBuilder<T> Default(T value) => default;

    /// <summary>
    /// Specifies a default value factory for the column.
    /// </summary>
    /// <param name="factory">A function that produces the default value.</param>
    public ColumnBuilder<T> Default(Func<T> factory) => default;

    /// <summary>
    /// Maps this property to a different column name in the database.
    /// </summary>
    /// <param name="columnName">The database column name.</param>
    public ColumnBuilder<T> MapTo(string columnName) => default;

    /// <summary>
    /// Applies a custom type mapping to this column.
    /// The mapping class must inherit from <see cref="TypeMapping{TCustom, TDb}"/>.
    /// </summary>
    /// <typeparam name="TMapping">The type mapping class.</typeparam>
    public ColumnBuilder<T> Mapped<TMapping>() => default;

    /// <summary>
    /// Marks this column as having a unique constraint.
    /// Syntactic sugar for single-column unique indexes.
    /// </summary>
    public ColumnBuilder<T> Unique() => default;

    /// <summary>
    /// Marks this column as containing sensitive data.
    /// Parameter values for sensitive columns are redacted in log output.
    /// </summary>
    public ColumnBuilder<T> Sensitive() => default;
}
