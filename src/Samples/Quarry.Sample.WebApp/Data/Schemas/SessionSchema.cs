using Quarry;
using Index = Quarry.Index;

namespace Quarry.Sample.WebApp.Data.Schemas;

public class SessionSchema : Schema
{
    public static string Table => "sessions";

    public Key<int> SessionId => Identity();
    public Ref<UserSchema, int> UserId => ForeignKey<UserSchema, int>();
    public Col<string> Token => Length(64);
    public Col<DateTime> ExpiresAt { get; }
    public Col<DateTime> CreatedAt => Default(() => DateTime.UtcNow);

    public Index IX_Token => Index(Token).Unique();
    public Index IX_ExpiresAt => Index(ExpiresAt);
}
