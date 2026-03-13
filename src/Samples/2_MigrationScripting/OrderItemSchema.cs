using Quarry;

namespace MigrationScripting;

public class OrderItemSchema : Schema
{
    public static string Table => "order_items";

    public Key<int> OrderItemId => Identity();
    public Ref<OrderSchema, int> OrderId => ForeignKey<OrderSchema, int>();
    public Ref<ProductSchema, int> ProductId => ForeignKey<ProductSchema, int>();
    public Col<int> Quantity => Default(1);
}
