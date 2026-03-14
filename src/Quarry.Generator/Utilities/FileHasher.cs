using System.Security.Cryptography;
using System.Text;

namespace Quarry.Generators.Utilities;

/// <summary>
/// Generates stable, short, filesystem-safe hashes from file paths.
/// Used for unique output filenames in per-file interceptor generation.
/// </summary>
internal static class FileHasher
{
    /// <summary>
    /// Computes a stable 8-character hex hash from a file path.
    /// Normalizes path (lowercase, forward slashes) before hashing.
    /// </summary>
    public static string ComputeStableHash(string filePath)
    {
        var normalized = filePath.Replace('\\', '/').ToLowerInvariant();
        var bytes = Encoding.UTF8.GetBytes(normalized);

#if NET6_0_OR_GREATER
        var hash = SHA256.HashData(bytes);
#else
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(bytes);
#endif

        // Take first 4 bytes → 8 hex chars. Collision probability is negligible at project scale.
        return $"{hash[0]:x2}{hash[1]:x2}{hash[2]:x2}{hash[3]:x2}";
    }
}
