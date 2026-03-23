namespace Quarry.Generators.Models;

/// <summary>
/// The kind of query for execution interceptor routing.
/// </summary>
internal enum QueryKind
{
    Select,
    Delete,
    Update,
    Insert,
    BatchInsert
}
