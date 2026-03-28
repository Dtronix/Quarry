namespace UnsafeAccessorGenTest;

/// <summary>
/// Minimal stub mimicking Quarry's IQueryBuilder.Where signature.
/// Uses Func&lt;T, bool&gt; (not Expression) — the whole point of this test.
/// </summary>
public class QueryLike<T>
{
    public QueryLike<T> Where(Func<T, bool> predicate)
    {
        // In real Quarry, this is intercepted and the Func is either discarded
        // (non-capturing) or its Target is inspected (capturing).
        // This default implementation captures the delegate for verification.
        LastFunc = predicate;
        return this;
    }

    /// <summary>Stores the last Func passed to Where, for test verification.</summary>
    public Delegate? LastFunc { get; private set; }
}

public static class QueryFactory
{
    public static QueryLike<string> Create() => new();
}
