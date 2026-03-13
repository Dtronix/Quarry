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
