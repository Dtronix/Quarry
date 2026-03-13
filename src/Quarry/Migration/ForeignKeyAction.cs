namespace Quarry.Migration;

/// <summary>
/// Specifies the action to take when a foreign key constraint is violated.
/// </summary>
public enum ForeignKeyAction
{
    NoAction,
    Cascade,
    SetNull,
    SetDefault,
    Restrict
}
