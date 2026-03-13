using System;

namespace Quarry.Shared.Migration;

/// <summary>
/// Computes Levenshtein edit distance between two strings.
/// </summary>
#if QUARRY_GENERATOR
internal
#else
public
#endif
static class LevenshteinDistance
{
    /// <summary>
    /// Returns a similarity score between 0.0 (completely different) and 1.0 (identical).
    /// </summary>
    public static double Similarity(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.Ordinal))
            return 1.0;

        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return 0.0;

        var distance = Compute(a, b);
        var maxLen = Math.Max(a.Length, b.Length);
        return 1.0 - ((double)distance / maxLen);
    }

    /// <summary>
    /// Computes the Levenshtein edit distance between two strings.
    /// </summary>
    public static int Compute(string a, string b)
    {
        if (string.IsNullOrEmpty(a)) return b?.Length ?? 0;
        if (string.IsNullOrEmpty(b)) return a.Length;

        var lenA = a.Length;
        var lenB = b.Length;

        // Use two-row optimization to reduce memory
        var prev = new int[lenB + 1];
        var curr = new int[lenB + 1];

        for (var j = 0; j <= lenB; j++)
            prev[j] = j;

        for (var i = 1; i <= lenA; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= lenB; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }
            var tmp = prev;
            prev = curr;
            curr = tmp;
        }

        return prev[lenB];
    }
}
