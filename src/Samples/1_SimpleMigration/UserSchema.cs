using Quarry;
using Index = Quarry.Index;

namespace SimpleMigration;

public class UserSchema : Schema
{
    public static string Table => "users";

    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
    public Col<string?> Email { get; }
    public Col<bool> IsActive => Default(true);
    public Col<DateTime> CreatedAt => Default(() => DateTime.UtcNow);

    public Many<PostSchema> Posts => HasMany<PostSchema>(p => p.UserId);

    public Index IX_Email => Index(Email).Unique();
}
