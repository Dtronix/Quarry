using System.Data;
using System.Data.Common;

namespace Quarry.Internal;

/// <summary>
/// Interface for query execution context.
/// Provides access to connection and execution infrastructure.
/// </summary>
/// <remarks>
/// This interface is public to support source-generated code but is not intended
/// to be implemented by user code. Use <see cref="QuarryContext"/> instead.
/// </remarks>
public interface IQueryExecutionContext
{
    /// <summary>
    /// Gets the database connection.
    /// </summary>
    DbConnection Connection { get; }

    /// <summary>
    /// Gets the default query timeout.
    /// </summary>
    TimeSpan DefaultTimeout { get; }

    /// <summary>
    /// Gets the slow query threshold, or null if slow query detection is disabled.
    /// </summary>
    TimeSpan? SlowQueryThreshold { get; }

    /// <summary>
    /// Ensures the connection is open.
    /// </summary>
    Task EnsureConnectionOpenAsync(CancellationToken cancellationToken);
}
