using Quarry.Shared.Migration;

namespace Quarry.Generators.Parsing;

/// <summary>
/// Utilities for converting property names to column names based on naming conventions.
/// Delegates to the shared implementation.
/// </summary>
internal static class NamingConventions
{
    public static string ToColumnName(string propertyName, NamingStyleKind style)
    {
        return Quarry.Shared.Migration.NamingConventions.ToColumnName(propertyName, style);
    }
}
