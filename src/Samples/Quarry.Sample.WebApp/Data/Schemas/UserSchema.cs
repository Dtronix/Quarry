using Quarry;
using Index = Quarry.Index;

namespace Quarry.Sample.WebApp.Data.Schemas;

public class UserSchema : Schema
{
    public static string Table => "users";

    public Key<int> UserId => Identity();
    public Col<string> Email => Length(255);
    public Col<string> UserName => Length(100);
    public Col<byte[]> PasswordHash { get; }
    public Col<byte[]> Salt { get; }
    public Col<UserRole> Role { get; }
    public Col<bool> IsActive => Default(true);
    public Col<DateTime> CreatedAt => Default(() => DateTime.UtcNow);
    public Col<DateTime?> LastLoginAt { get; }

    public Many<SessionSchema> Sessions => HasMany<SessionSchema>(s => s.UserId);
    public Many<AuditLogSchema> AuditLogs => HasMany<AuditLogSchema>(a => a.UserId);

    public Index IX_Email => Index(Email).Unique();
    public Index IX_Role => Index(Role);
}
