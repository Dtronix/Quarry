using System.Data.Common;

namespace Quarry;

/// <summary>
/// Interface for struct-based row readers used by generated RawSqlAsync interceptors.
/// Implementations resolve column ordinals once via <see cref="Resolve"/> and then
/// read rows efficiently via cached ordinals in <see cref="Read"/>.
/// </summary>
/// <typeparam name="T">The result type to materialize from each row.</typeparam>
public interface IRowReader<T>
{
    /// <summary>
    /// Resolves column ordinals from the reader. Called once after ExecuteReaderAsync,
    /// before the first ReadAsync.
    /// </summary>
    void Resolve(DbDataReader reader);

    /// <summary>
    /// Reads a single row using cached ordinals.
    /// </summary>
    T Read(DbDataReader reader);
}
