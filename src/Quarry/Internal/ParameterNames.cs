namespace Quarry.Internal;

/// <summary>
/// Pre-computed parameter name strings for each SQL dialect, eliminating
/// runtime string concatenation for parameter naming in generated interceptor code.
/// 256 entries per dialect (~12KB total, allocated once on first access).
/// </summary>
public static class ParameterNames
{
    private static readonly string[] _atP = BuildArray("@p", 0, 256);
    private static readonly string[] _dollar = BuildArray("$", 1, 256);

    /// <summary>Returns "@p0"…"@p255" for indices 0–255; falls back to "@p" + index beyond that.</summary>
    public static string AtP(int index) =>
        (uint)index < (uint)_atP.Length ? _atP[index] : "@p" + index;

    /// <summary>Returns "$1"…"$256" for indices 0–255 (1-based output); falls back to "$" + (index+1) beyond that.</summary>
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
