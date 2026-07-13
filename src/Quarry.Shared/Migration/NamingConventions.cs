using System.Text;

namespace Quarry.Shared.Migration;

/// <summary>
/// Utilities for converting property names to column names based on naming conventions.
/// </summary>
#if QUARRY_GENERATOR
internal
#else
public
#endif
static class NamingConventions
{
    /// <summary>
    /// Reduces a name to a case- and separator-insensitive canonical form: lowercased
    /// with underscores, hyphens, and spaces removed. Two names with the same canonical
    /// form differ only by naming convention (e.g. <c>user_name</c>, <c>userName</c>,
    /// <c>UserName</c>, <c>username</c> all canonicalize to <c>username</c>). Used to
    /// detect convention-only renames deterministically, without heuristic scoring.
    /// </summary>
    public static string Canonicalize(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name ?? string.Empty;

        var sb = new StringBuilder(name.Length);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c == '_' || c == '-' || c == ' ')
                continue;
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    public static string ToColumnName(string propertyName, NamingStyleKind style)
    {
        switch (style)
        {
            case NamingStyleKind.SnakeCase:
                return ToSnakeCase(propertyName);
            case NamingStyleKind.CamelCase:
                return ToCamelCase(propertyName);
            case NamingStyleKind.LowerCase:
                return propertyName.ToLowerInvariant();
            default:
                return propertyName;
        }
    }

    public static string ToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var sb = new StringBuilder();
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                    sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    public static string ToCamelCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        if (input.Length == 1)
            return input.ToLowerInvariant();

        return char.ToLowerInvariant(input[0]) + input.Substring(1);
    }
}
