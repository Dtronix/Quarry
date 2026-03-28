using Quarry;

namespace Quarry.Sample.Aot.Data.Schemas;

public class OrderItemSchema : Schema
{
    public static string Table => "order_items";

    public Key<int> OrderItemId => Identity();
    public Col<int> Quantity { get; }
    public Col<decimal> UnitPrice => Precision(18, 2);
    public Ref<ProductSchema, int> ProductId => ForeignKey<ProductSchema, int>();
}
