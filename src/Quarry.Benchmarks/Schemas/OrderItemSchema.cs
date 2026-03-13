using Quarry;

namespace Quarry.Benchmarks.Context;

public class OrderItemSchema : Schema
{
    public static string Table => "order_items";

    public Key<int> OrderItemId => Identity();
    public Ref<OrderSchema, int> OrderId => ForeignKey<OrderSchema, int>();
    public Col<string> ProductName => Length(200);
    public Col<int> Quantity { get; }
    public Col<decimal> UnitPrice => Precision(18, 2);
    public Col<decimal> LineTotal { get; }
}
