using Quarry;

namespace Quarry.Benchmarks.Context;

public class OrderSchema : Schema
{
    public static string Table => "orders";

    public Key<int> OrderId => Identity();
    public Ref<UserSchema, int> UserId => ForeignKey<UserSchema, int>();
    public Col<decimal> Total => Precision(18, 2);
    public Col<string> Status { get; }
    public Col<DateTime> OrderDate => Default(() => DateTime.UtcNow);
    public Col<string?> Notes { get; }

    public Many<OrderItemSchema> Items => HasMany<OrderItemSchema>(i => i.OrderId);
}
