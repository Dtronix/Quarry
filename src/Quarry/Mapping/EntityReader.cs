using System.Data.Common;

namespace Quarry;

/// <summary>
/// Base class for custom entity materialization from DbDataReader.
/// Implement this to take full control of how entities are read from query results.
/// </summary>
/// <typeparam name="T">The entity type to materialize.</typeparam>
public abstract class EntityReader<T> where T : class
{
    /// <summary>
    /// Reads a single entity instance from the current row of the reader.
    /// </summary>
    /// <param name="reader">The data reader positioned at the current row.</param>
    /// <returns>The materialized entity instance.</returns>
    public abstract T Read(DbDataReader reader);
}
