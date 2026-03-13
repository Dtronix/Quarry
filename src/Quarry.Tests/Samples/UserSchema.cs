using Quarry;

namespace Quarry.Tests.Samples;

/// <summary>
/// Schema definition for the users table.
/// </summary>
public class UserSchema : Schema
{
    public static string Table => "users";

    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
    public Col<string?> Email { get; }
    public Col<bool> IsActive => Default(true);
    public Col<DateTime> CreatedAt => Default(() => DateTime.UtcNow);
    public Col<DateTime?> LastLogin { get; }

    public Many<OrderSchema> Orders => HasMany<OrderSchema>(o => o.UserId);

    // Index definitions
    public Index IX_Email => Index(Email);
    public Index IX_Name_Email => Index(UserName, Email);
    public Index IX_Created => Index(CreatedAt.Desc());
    public Index IX_Active => Index(Email).Where(IsActive);
    public Index IX_Covering => Index(Email).Include(UserName, CreatedAt);
}
