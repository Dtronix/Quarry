using Microsoft.CodeAnalysis;

namespace Quarry.Generators.Sql;

/// <summary>
/// Per-context dialect configuration: dialect identity plus mode flags
/// that depend on consumer server settings (e.g. MySQL <c>sql_mode</c>).
/// Generator-internal carrier; runtime code consumes <see cref="SqlDialect"/>
/// directly.
/// </summary>
internal sealed record SqlDialectConfig(SqlDialect Dialect)
{
    public static SqlDialectConfig FromAttribute(AttributeData attribute)
    {
        var dialect = SqlDialect.SQLite;

        foreach (var named in attribute.NamedArguments)
        {
            switch (named.Key)
            {
                case "Dialect":
                    if (named.Value.Value is int dialectValue)
                        dialect = (SqlDialect)dialectValue;
                    break;
            }
        }

        return new SqlDialectConfig(dialect);
    }
}
