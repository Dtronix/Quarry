using System.Text;

namespace Quarry.Generators.Utilities;

/// <summary>
/// Generates stable, human-readable, filesystem-safe tags from file paths.
/// Used for unique output filenames in per-file interceptor generation.
/// </summary>
internal static class FileHasher
{
    /// <summary>
    /// Converts a file path into a sanitized, human-readable tag suitable for
    /// use in filenames and C# identifiers. Strips the extension, normalizes
    /// separators to underscores, and removes invalid characters.
    /// Example: "src/Models/User.cs" → "src_Models_User"
    /// </summary>
    public static string ComputeFileTag(string filePath)
    {
        // Normalize slashes
        var normalized = filePath.Replace('\\', '/');

        // Strip file extension
        var dotIndex = normalized.LastIndexOf('.');
        if (dotIndex > normalized.LastIndexOf('/') + 1)
            normalized = normalized.Substring(0, dotIndex);

        // Strip leading drive letter (e.g. "C:/") or leading slash
        if (normalized.Length >= 2 && normalized[1] == ':')
            normalized = normalized.Substring(2);
        normalized = normalized.TrimStart('/');

        // Replace path separators and other non-identifier chars with underscores
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (c == '/')
                sb.Append('_');
            else if (char.IsLetterOrDigit(c) || c == '_')
                sb.Append(c);
            // Skip other characters (spaces, dots, etc.)
        }

        // Collapse consecutive underscores and trim
        var result = sb.ToString();
        while (result.Contains("__"))
            result = result.Replace("__", "_");
        result = result.Trim('_');

        return result.Length > 0 ? result : "unknown";
    }
}
