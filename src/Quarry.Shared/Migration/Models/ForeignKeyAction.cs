namespace Quarry.Shared.Migration;

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
