using System.Collections.Generic;
using System.Linq;

namespace Quarry.Internal;

/// <summary>
/// Runtime helper for materializing IEnumerable&lt;T&gt; collections into
/// IReadOnlyList&lt;T&gt; for parameter binding in generated interceptor code.
/// </summary>
public static class CollectionHelper
{
    /// <summary>
    /// Materializes an IEnumerable&lt;T&gt; into an IReadOnlyList&lt;T&gt; for parameter binding.
    /// If the source already implements IReadOnlyList&lt;T&gt;, returns it without allocation.
    /// </summary>
    public static IReadOnlyList<T> Materialize<T>(IEnumerable<T> source)
    {
        return source is IReadOnlyList<T> list ? list : source.ToList();
    }
}
