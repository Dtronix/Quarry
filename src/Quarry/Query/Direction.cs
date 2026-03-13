namespace Quarry;

/// <summary>
/// Specifies the sort direction for ORDER BY clauses.
/// </summary>
public enum Direction
{
    /// <summary>
    /// Sort in ascending order (A-Z, 0-9, oldest to newest).
    /// </summary>
    Ascending,

    /// <summary>
    /// Sort in descending order (Z-A, 9-0, newest to oldest).
    /// </summary>
    Descending
}
