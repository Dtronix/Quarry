using Quarry;

namespace Quarry.Benchmarks.Context;

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
}
