using System.Data.Common;

namespace Quarry;

/// <summary>
/// Optional interface for <see cref="TypeMapping{TCustom,TDb}"/> subclasses that need
/// dialect-specific behavior such as custom SQL type names or ADO.NET parameter configuration.
/// </summary>
/// <remarks>
/// <para>
/// Implement this interface on your TypeMapping to provide per-dialect customization.
/// The compile-time interceptor and runtime fallback paths both check for this interface.
/// </para>
/// <para>
/// Example — PostgreSQL jsonb support:
/// <code>
/// public class JsonDocMapping : TypeMapping&lt;JsonDoc, string&gt;, IDialectAwareTypeMapping
/// {
///     public override string ToDb(JsonDoc value) =&gt; JsonSerializer.Serialize(value);
///     public override JsonDoc FromDb(string value) =&gt; JsonSerializer.Deserialize&lt;JsonDoc&gt;(value)!;
///
///     public string? GetSqlTypeName(SqlDialect dialect) =&gt; dialect switch
///     {
///         SqlDialect.PostgreSQL =&gt; "jsonb",
///         _ =&gt; "TEXT"
///     };
///
///     public void ConfigureParameter(SqlDialect dialect, DbParameter parameter)
///     {
///         // Set NpgsqlDbType.Jsonb for PostgreSQL native support
///     }
/// }
/// </code>
/// </para>
/// </remarks>
public interface IDialectAwareTypeMapping
{
    /// <summary>
    /// Returns the dialect-specific SQL type name for DDL generation and CAST expressions,
    /// or <c>null</c> to use the default CLR-to-SQL type mapping from the dialect.
    /// </summary>
    /// <param name="dialect">The target SQL dialect.</param>
    /// <returns>The SQL type name (e.g., "jsonb", "TEXT", "NVARCHAR(MAX)"), or <c>null</c> for default.</returns>
    string? GetSqlTypeName(SqlDialect dialect);

    /// <summary>
    /// Configures dialect-specific properties on a <see cref="DbParameter"/> after the value has been set.
    /// Use this to set provider-specific parameter types (e.g., NpgsqlDbType).
    /// </summary>
    /// <param name="dialect">The target SQL dialect.</param>
    /// <param name="parameter">The ADO.NET parameter to configure.</param>
    void ConfigureParameter(SqlDialect dialect, DbParameter parameter);
}
