#if QUARRY_RUNTIME
namespace Quarry.Migration;
#else
namespace Quarry.Shared.Migration;
#endif

/// <summary>
/// Specifies the action to take when a foreign key constraint is violated.
/// </summary>
#if QUARRY_GENERATOR
internal
#else
public
#endif
enum ForeignKeyAction
{
    NoAction,
    Cascade,
    SetNull,
    SetDefault,
    Restrict
}
