namespace Quarry;

/// <summary>
/// A prepared query that allows multiple terminal operations to be called
/// on the same compiled query chain without rebuilding it.
/// </summary>
/// <remarks>
/// <para>
/// <c>PreparedQuery</c> sits in the terminal position — it can only be called
/// where a terminal would be valid, after all clauses and projections are finalized.
/// The source generator intercepts and replaces all method bodies at compile time.
/// </para>
/// <para>
/// The default method implementations throw <see cref="NotSupportedException"/> —
/// they exist only so the compiler resolves the call sites. The generator replaces
/// them entirely with optimized code.
/// </para>
/// </remarks>
/// <typeparam name="TResult">
/// The row type for select queries, <c>int</c> for delete/update, or <c>TKey</c> for insert scalar returns.
/// </typeparam>
public sealed class PreparedQuery<TResult>
{
    /// <summary>
    /// Returns a <see cref="QueryDiagnostics"/> containing the generated SQL,
    /// bound parameters, and optimization metadata for this query chain.
    /// </summary>
    public QueryDiagnostics ToDiagnostics()
        => throw new NotSupportedException("PreparedQuery methods must be intercepted by the Quarry source generator.");

    /// <summary>
    /// Executes the query and returns all results as a list.
    /// </summary>
    public Task<List<TResult>> ExecuteFetchAllAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException("PreparedQuery methods must be intercepted by the Quarry source generator.");

    /// <summary>
    /// Executes the query and returns the first result.
    /// </summary>
    public Task<TResult> ExecuteFetchFirstAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException("PreparedQuery methods must be intercepted by the Quarry source generator.");

    /// <summary>
    /// Executes the query and returns the first result, or default if no results.
    /// </summary>
    public Task<TResult?> ExecuteFetchFirstOrDefaultAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException("PreparedQuery methods must be intercepted by the Quarry source generator.");

    /// <summary>
    /// Executes the query and returns exactly one result.
    /// </summary>
    public Task<TResult> ExecuteFetchSingleAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException("PreparedQuery methods must be intercepted by the Quarry source generator.");

    /// <summary>
    /// Executes the query and returns the scalar result.
    /// </summary>
    public Task<TScalar> ExecuteScalarAsync<TScalar>(CancellationToken cancellationToken = default)
        => throw new NotSupportedException("PreparedQuery methods must be intercepted by the Quarry source generator.");

    /// <summary>
    /// Executes the query and returns results as an async enumerable for streaming.
    /// </summary>
    public IAsyncEnumerable<TResult> ToAsyncEnumerable(CancellationToken cancellationToken = default)
        => throw new NotSupportedException("PreparedQuery methods must be intercepted by the Quarry source generator.");

    /// <summary>
    /// Executes the modification query and returns the number of rows affected.
    /// </summary>
    public Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException("PreparedQuery methods must be intercepted by the Quarry source generator.");
}
