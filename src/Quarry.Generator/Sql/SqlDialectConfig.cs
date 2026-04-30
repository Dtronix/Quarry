using Microsoft.CodeAnalysis;

namespace Quarry.Generators.Sql;

/// <summary>
/// Per-context dialect configuration: dialect identity plus mode flags
/// that depend on consumer server settings (e.g. MySQL <c>sql_mode</c>).
/// Generator-internal carrier; runtime code consumes <see cref="SqlDialect"/>
/// directly.
/// </summary>
internal sealed record SqlDialectConfig(
    SqlDialect Dialect,
    bool MySqlBackslashEscapes = true)
{
    /// <summary>
    /// Parses a <c>QuarryContextAttribute</c> into both a <see cref="SqlDialectConfig"/>
    /// (dialect + mode flags) and a <c>Schema</c> string. Single pass over
    /// <see cref="AttributeData.NamedArguments"/> so callers don't need to iterate twice.
    /// </summary>
    public static (SqlDialectConfig Config, string? Schema) ParseAttribute(AttributeData attribute)
    {
        // Default Dialect=SQLite when the attribute omits a Dialect= named arg.
        // Preserves pre-refactor ContextParser behavior (the same silent default).
        // A consumer-facing QRY diagnostic for missing Dialect= would be a clearer
        // ergonomic improvement — tracked as a follow-up; out of scope for #273
        // which is the carrier refactor + LIKE-emit fix.
        var dialect = SqlDialect.SQLite;
        var mysqlBackslashEscapes = true;
        string? schema = null;

        foreach (var named in attribute.NamedArguments)
        {
            switch (named.Key)
            {
                case "Dialect":
                    if (named.Value.Value is int dialectValue)
                        dialect = (SqlDialect)dialectValue;
                    break;
                case "MySqlBackslashEscapes":
                    if (named.Value.Value is bool b)
                        mysqlBackslashEscapes = b;
                    break;
                case "Schema":
                    schema = named.Value.Value as string;
                    break;
            }
        }

        return (new SqlDialectConfig(dialect, mysqlBackslashEscapes), schema);
    }

    /// <summary>
    /// Parses just the dialect-config portion of a <c>QuarryContextAttribute</c>.
    /// Convenience for tests; production code that also needs Schema should use
    /// <see cref="ParseAttribute(AttributeData)"/>.
    /// </summary>
    public static SqlDialectConfig FromAttribute(AttributeData attribute)
        => ParseAttribute(attribute).Config;
}
