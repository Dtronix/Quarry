namespace Quarry.Internal;

/// <summary>
/// Cache entry for SQL strings built with expanded collection parameters.
/// One entry cached per (carrier, mask) pair. Immutable after construction.
/// Thread-safe: reference reads/writes are atomic in .NET — benign race on store.
/// </summary>
public sealed class CollectionSqlCache
{
    public readonly int Hash;
    public readonly string Sql;
    public readonly int ColShift;
    public readonly string[][] ColParts;

    public CollectionSqlCache(int hash, string sql, int colShift, string[][] colParts)
    {
        Hash = hash;
        Sql = sql;
        ColShift = colShift;
        ColParts = colParts;
    }
}
