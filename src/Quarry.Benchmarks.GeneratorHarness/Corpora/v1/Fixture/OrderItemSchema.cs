// CORPUS — embedded resource for QuarryGenerator benchmarks; not compiled into Quarry.Benchmarks.
using Quarry;

namespace BenchHarness;

public class OrderItemSchema : Schema
{
    public static string Table => "order_items";

    public Key<int> OrderItemId => Identity();
    public Ref<OrderSchema, int> OrderId => ForeignKey<OrderSchema, int>();
    public Col<string> ProductName => Length(200);
    public Col<int> Quantity { get; }
    public Col<decimal> UnitPrice => Precision(18, 2);
    public Col<decimal> LineTotal { get; }

    public One<OrderSchema> Order { get; }
}
