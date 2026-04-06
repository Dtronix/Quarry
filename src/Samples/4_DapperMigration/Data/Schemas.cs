using Quarry;
using Index = Quarry.Index;

namespace DapperMigration.Data;

public class UserSchema : Schema
{
    public static string Table => "users";

    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
    public Col<string> Email => Length(255);
    public Col<bool> IsActive => Default(true);
    public Col<DateTime> CreatedAt => Default(() => DateTime.UtcNow);

    public Many<OrderSchema> Orders => HasMany<OrderSchema>(o => o.UserId);

    public Index IX_Email => Index(Email).Unique();
}

public class OrderSchema : Schema
{
    public static string Table => "orders";

    public Key<int> OrderId => Identity();
    public Ref<UserSchema, int> UserId => ForeignKey<UserSchema, int>();
    public Col<decimal> Total => Precision(18, 2);
    public Col<string> Status => Length(50);
    public Col<DateTime> OrderDate => Default(() => DateTime.UtcNow);

    public One<UserSchema> User { get; }
}

public class ProductSchema : Schema
{
    public static string Table => "products";

    public Key<int> ProductId => Identity();
    public Col<string> Name => Length(200);
    public Col<decimal> Price => Precision(18, 2);
    public Col<int> Stock { get; }
    public Col<string?> Category { get; }
}
