namespace Quarry.Tests;

/// <summary>
/// Helpers for asserting against query results in PG-execute mirror tests where
/// SQLite's incidental insertion-order return shape would otherwise force the
/// test to also assert positionally on the PG side. PostgreSQL does not
/// guarantee row order without an explicit <c>ORDER BY</c>, so a passing
/// <c>pgResults[0]</c>/<c>pgResults[1]</c> assertion today can flake tomorrow
/// after a planner change (statistics refresh, parallel scan, hash join chosen
/// for a CTE). Sort the materialised list in C# with a stable key so the test
/// is provider-independent.
/// </summary>
/// <remarks>
/// <para>
/// Use <see cref="SortedByAsync{T, TKey}(Task{List{T}}, Func{T, TKey})"/> instead
/// of inlining <c>(await q.ExecuteFetchAllAsync()).OrderBy(...).ToList()</c> so
/// the rationale lives in one place.
/// </para>
/// <para>
/// The helper is an extension on <see cref="Task{TResult}"/> of <see cref="List{T}"/>
/// (not on <see cref="PreparedQuery{TResult}"/>) so the Quarry chain analyzer
/// (QRY036) still sees <c>.ExecuteFetchAllAsync()</c> as the literal terminal at
/// the end of the chain. Wrapping the chain in an extension on PreparedQuery
/// would hide the terminal and trip the "no terminals on prepared query" rule.
/// </para>
/// <para>
/// SQLite assertions stay positional (we accept the asymmetry — Lite's
/// incidental insertion-order is the reference shape and PG's sorted shape
/// mirrors it); only the PG side runs through this helper.
/// </para>
/// </remarks>
internal static class PgRowOrderExtensions
{
    /// <summary>
    /// Awaits the fetch task and returns the result list sorted by
    /// <paramref name="keySelector"/>. Equivalent to
    /// <c>(await fetchTask).OrderBy(keySelector).ToList()</c>.
    /// </summary>
    public static async Task<List<T>> SortedByAsync<T, TKey>(
        this Task<List<T>> fetchTask,
        Func<T, TKey> keySelector)
    {
        var results = await fetchTask;
        return results.OrderBy(keySelector).ToList();
    }
}
