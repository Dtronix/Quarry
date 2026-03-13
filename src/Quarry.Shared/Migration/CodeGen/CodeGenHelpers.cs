using System.Text;

namespace Quarry.Shared.Migration;

/// <summary>
/// Shared utilities for migration and snapshot code generation.
/// </summary>
#if QUARRY_GENERATOR
internal
#else
public
#endif
static class CodeGenHelpers
{
    /// <summary>
    /// Escapes a string value for use inside a C# string literal.
    /// </summary>
    public static string EscapeCSharpString(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t")
            .Replace("\0", "\\0");
    }

    /// <summary>
    /// Sanitizes a name into a valid C# identifier component.
    /// Strips non-alphanumeric/underscore characters and ensures
    /// the result does not start with a digit.
    /// </summary>
    public static string SanitizeCSharpName(string name)
    {
        var sb = new StringBuilder(name.Length);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsLetterOrDigit(c) || c == '_')
                sb.Append(c);
        }

        // Ensure result starts with a letter or underscore
        if (sb.Length > 0 && char.IsDigit(sb[0]))
            sb.Insert(0, '_');

        return sb.Length > 0 ? sb.ToString() : "_";
    }
}
