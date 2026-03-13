using Quarry;

namespace SimpleMigration;

public class PostSchema : Schema
{
    public static string Table => "posts";

    public Key<int> PostId => Identity();
    public Ref<UserSchema, int> UserId => ForeignKey<UserSchema, int>();
    public Col<string> Title => Length(200);
    public Col<string?> Body { get; }
    public Col<DateTime> CreatedAt => Default(() => DateTime.UtcNow);
}
