namespace Quarry.Internal;

/// <summary>
/// Pre-computed parameter name strings for each SQL dialect, eliminating
/// runtime string concatenation for parameter naming in generated interceptor code.
/// Sized to cover SQL Server's 2100-parameter ceiling so batch inserts never fall
/// back to per-call concatenation (allocated once on first access).
/// </summary>
public static class ParameterNames
{
    /// <summary>
    /// Number of pre-computed entries per dialect. 2100 matches SQL Server's maximum
    /// parameter count, the largest a single batch insert can bind.
    /// </summary>
    private const int CacheSize = 2100;

    private static readonly string[] _atP = BuildArray("@p", 0, CacheSize);
    private static readonly string[] _dollar = BuildArray("$", 1, CacheSize);

    /// <summary>Returns "@p0"…"@p2099" for cached indices; falls back to "@p" + index beyond that.</summary>
    public static string AtP(int index) =>
        (uint)index < (uint)_atP.Length ? _atP[index] : "@p" + index;

    /// <summary>Returns "$1"…"$2100" for cached indices (1-based output); falls back to "$" + (index+1) beyond that.</summary>
    public static string Dollar(int index) =>
        (uint)index < (uint)_dollar.Length ? _dollar[index] : "$" + (index + 1);

    private static string[] BuildArray(string prefix, int startValue, int count)
    {
        var arr = new string[count];
        for (int i = 0; i < count; i++)
            arr[i] = prefix + (startValue + i);
        return arr;
    }
}
