namespace Quarry.Shared.Migration;

/// <summary>
/// Represents the naming convention for column name mapping.
/// </summary>
#if QUARRY_GENERATOR
internal
#else
public
#endif
enum NamingStyleKind
{
    Exact = 0,
    SnakeCase = 1,
    CamelCase = 2,
    LowerCase = 3
}
