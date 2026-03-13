namespace Quarry;

/// <summary>
/// Marks a class as a Quarry database context and specifies build-time configuration.
/// The source generator uses this attribute as the entry point for code generation.
/// </summary>
/// <remarks>
/// <para>
/// Apply this attribute to a partial class that inherits from <see cref="QuarryContext"/>.
/// The source generator will discover schemas via the context's partial <c>QueryBuilder&lt;T&gt;</c> properties.
/// </para>
/// <para>
/// The <see cref="Dialect"/> property is required and specifies the target database for SQL generation.
/// The <see cref="Schema"/> property is optional and specifies the database schema for qualified table names.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [QuarryContext(Dialect = SqlDialect.PostgreSQL, Schema = "public")]
/// public partial class AppDbContext : QuarryContext
/// {
///     public partial QueryBuilder&lt;User&gt; Users { get; }
///     public partial QueryBuilder&lt;Order&gt; Orders { get; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class QuarryContextAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the SQL dialect to use for code generation.
    /// This is required and determines the SQL syntax for all generated queries.
    /// </summary>
    public SqlDialect Dialect { get; set; }

    /// <summary>
    /// Gets or sets the database schema name for qualified table references.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When specified, table names will be qualified with this schema
    /// (e.g., "public.users" for PostgreSQL, "dbo.users" for SQL Server).
    /// </para>
    /// <para>
    /// If not specified, tables are referenced without schema qualification.
    /// </para>
    /// <para>
    /// This property allows the same schema classes to be reused across multiple
    /// contexts targeting different database schemas.
    /// </para>
    /// </remarks>
    /// <example>
    /// PostgreSQL: "public" generates "public"."tablename"
    /// SQL Server: "dbo" generates [dbo].[tablename]
    /// </example>
    public string? Schema { get; set; }
}
