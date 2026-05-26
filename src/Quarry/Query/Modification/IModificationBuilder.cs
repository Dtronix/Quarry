namespace Quarry;

/// <summary>
/// Interface for constructing DELETE operations (before WHERE/All).
/// </summary>
public interface IDeleteBuilder<T> where T : class
{
    IExecutableDeleteBuilder<T> Where(Func<T, bool> predicate)
        => throw new InvalidOperationException("Carrier method IDeleteBuilder.Where is not intercepted in this optimized chain. This indicates a code generation bug.");
    IExecutableDeleteBuilder<T> All()
        => throw new InvalidOperationException("Carrier method IDeleteBuilder.All is not intercepted in this optimized chain. This indicates a code generation bug.");
    IDeleteBuilder<T> WithTimeout(TimeSpan timeout)
        => throw new InvalidOperationException("Carrier method IDeleteBuilder.WithTimeout is not intercepted in this optimized chain. This indicates a code generation bug.");

    QueryDiagnostics ToDiagnostics()
        => throw new InvalidOperationException("Carrier method IDeleteBuilder.ToDiagnostics is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Prepares this query chain for multiple terminal operations.
    /// </summary>
    PreparedQuery<int> Prepare()
        => throw new InvalidOperationException("Carrier method IDeleteBuilder.Prepare is not intercepted in this optimized chain. This indicates a code generation bug.");
}

/// <summary>
/// Interface for constructing DELETE operations that can be executed (after WHERE/All).
/// </summary>
public interface IExecutableDeleteBuilder<T> where T : class
{
    IExecutableDeleteBuilder<T> Where(Func<T, bool> predicate)
        => throw new InvalidOperationException("Carrier method IExecutableDeleteBuilder.Where is not intercepted in this optimized chain. This indicates a code generation bug.");
    IExecutableDeleteBuilder<T> WithTimeout(TimeSpan timeout)
        => throw new InvalidOperationException("Carrier method IExecutableDeleteBuilder.WithTimeout is not intercepted in this optimized chain. This indicates a code generation bug.");
    Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IExecutableDeleteBuilder.ExecuteNonQueryAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    QueryDiagnostics ToDiagnostics()
        => throw new InvalidOperationException("Carrier method IExecutableDeleteBuilder.ToDiagnostics is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Prepares this query chain for multiple terminal operations.
    /// </summary>
    PreparedQuery<int> Prepare()
        => throw new InvalidOperationException("Carrier method IExecutableDeleteBuilder.Prepare is not intercepted in this optimized chain. This indicates a code generation bug.");
}

/// <summary>
/// Interface for constructing UPDATE operations (before WHERE/All).
/// </summary>
/// <remarks>
/// DO NOT add additional generic <c>Set</c> overloads as default interface members
/// here. Adding generic <c>Set</c> DIMs has previously broken existing
/// <c>Set(T entity)</c> interceptor binding (Roslyn stops routing the user's call
/// through the emitted interceptor once the DIM overload set widens). New
/// <c>Set</c> overloads — including the runtime-conditional <c>Set(T.Patch)</c>
/// and <c>Set(PatchAction&lt;T.Patch&gt;)</c> forms — are defined as extension
/// methods on <see cref="Quarry.UpdateBuilderPatchExtensions"/> instead. See the
/// 2026-05-22 decision in <c>_sessions/add-patch-partial-update/workflow.md</c>.
/// </remarks>
public interface IUpdateBuilder<T> where T : class
{
    IUpdateBuilder<T> Set(T entity)
        => throw new InvalidOperationException("Carrier method IUpdateBuilder.Set is not intercepted in this optimized chain. This indicates a code generation bug.");
    IUpdateBuilder<T> Set(Action<T> assignment)
        => throw new InvalidOperationException("Carrier method IUpdateBuilder.Set is not intercepted in this optimized chain. This indicates a code generation bug.");
    IExecutableUpdateBuilder<T> Where(Func<T, bool> predicate)
        => throw new InvalidOperationException("Carrier method IUpdateBuilder.Where is not intercepted in this optimized chain. This indicates a code generation bug.");
    IExecutableUpdateBuilder<T> All()
        => throw new InvalidOperationException("Carrier method IUpdateBuilder.All is not intercepted in this optimized chain. This indicates a code generation bug.");
    IUpdateBuilder<T> WithTimeout(TimeSpan timeout)
        => throw new InvalidOperationException("Carrier method IUpdateBuilder.WithTimeout is not intercepted in this optimized chain. This indicates a code generation bug.");

    QueryDiagnostics ToDiagnostics()
        => throw new InvalidOperationException("Carrier method IUpdateBuilder.ToDiagnostics is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Prepares this query chain for multiple terminal operations.
    /// </summary>
    PreparedQuery<int> Prepare()
        => throw new InvalidOperationException("Carrier method IUpdateBuilder.Prepare is not intercepted in this optimized chain. This indicates a code generation bug.");
}

/// <summary>
/// Interface for constructing UPDATE operations that can be executed (after WHERE/All).
/// </summary>
public interface IExecutableUpdateBuilder<T> where T : class
{
    IExecutableUpdateBuilder<T> Set(T entity)
        => throw new InvalidOperationException("Carrier method IExecutableUpdateBuilder.Set is not intercepted in this optimized chain. This indicates a code generation bug.");
    IExecutableUpdateBuilder<T> Set(Action<T> assignment)
        => throw new InvalidOperationException("Carrier method IExecutableUpdateBuilder.Set is not intercepted in this optimized chain. This indicates a code generation bug.");
    IExecutableUpdateBuilder<T> Where(Func<T, bool> predicate)
        => throw new InvalidOperationException("Carrier method IExecutableUpdateBuilder.Where is not intercepted in this optimized chain. This indicates a code generation bug.");
    IExecutableUpdateBuilder<T> WithTimeout(TimeSpan timeout)
        => throw new InvalidOperationException("Carrier method IExecutableUpdateBuilder.WithTimeout is not intercepted in this optimized chain. This indicates a code generation bug.");
    Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IExecutableUpdateBuilder.ExecuteNonQueryAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    QueryDiagnostics ToDiagnostics()
        => throw new InvalidOperationException("Carrier method IExecutableUpdateBuilder.ToDiagnostics is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Prepares this query chain for multiple terminal operations.
    /// </summary>
    PreparedQuery<int> Prepare()
        => throw new InvalidOperationException("Carrier method IExecutableUpdateBuilder.Prepare is not intercepted in this optimized chain. This indicates a code generation bug.");
}

/// <summary>
/// Interface for constructing single-entity INSERT operations.
/// </summary>
public interface IInsertBuilder<T> where T : class
{
    IInsertBuilder<T> WithTimeout(TimeSpan timeout)
        => throw new InvalidOperationException("Carrier method IInsertBuilder.WithTimeout is not intercepted in this optimized chain. This indicates a code generation bug.");
    Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IInsertBuilder.ExecuteNonQueryAsync is not intercepted in this optimized chain. This indicates a code generation bug.");
    Task<TKey> ExecuteScalarAsync<TKey>(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IInsertBuilder.ExecuteScalarAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    QueryDiagnostics ToDiagnostics()
        => throw new InvalidOperationException("Carrier method IInsertBuilder.ToDiagnostics is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Prepares this query chain for multiple terminal operations.
    /// </summary>
    PreparedQuery<int> Prepare()
        => throw new InvalidOperationException("Carrier method IInsertBuilder.Prepare is not intercepted in this optimized chain. This indicates a code generation bug.");
}

/// <summary>
/// Interface for constructing batch INSERT operations after column selection.
/// Returned by the column-selector <c>InsertBatch(lambda)</c> overload.
/// </summary>
public interface IBatchInsertBuilder<T> where T : class
{
    IExecutableBatchInsert<T> Values(IEnumerable<T> entities)
        => throw new InvalidOperationException("Carrier method IBatchInsertBuilder.Values is not intercepted in this optimized chain. This indicates a code generation bug.");
    IBatchInsertBuilder<T> WithTimeout(TimeSpan timeout)
        => throw new InvalidOperationException("Carrier method IBatchInsertBuilder.WithTimeout is not intercepted in this optimized chain. This indicates a code generation bug.");
}

/// <summary>
/// Terminal interface for batch INSERT operations after <c>Values()</c>.
/// Supports execution and diagnostics.
/// </summary>
public interface IExecutableBatchInsert<T> where T : class
{
    Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IExecutableBatchInsert.ExecuteNonQueryAsync is not intercepted in this optimized chain. This indicates a code generation bug.");
    Task<TKey> ExecuteScalarAsync<TKey>(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Carrier method IExecutableBatchInsert.ExecuteScalarAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    QueryDiagnostics ToDiagnostics()
        => throw new InvalidOperationException("Carrier method IExecutableBatchInsert.ToDiagnostics is not intercepted in this optimized chain. This indicates a code generation bug.");

    /// <summary>
    /// Prepares this query chain for multiple terminal operations.
    /// </summary>
    PreparedQuery<int> Prepare()
        => throw new InvalidOperationException("Carrier method IExecutableBatchInsert.Prepare is not intercepted in this optimized chain. This indicates a code generation bug.");
}
