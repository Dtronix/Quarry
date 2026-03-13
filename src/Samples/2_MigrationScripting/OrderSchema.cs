using Quarry;

namespace MigrationScripting;

public class OrderSchema : Schema
{
    public static string Table => "orders";

    public Key<int> OrderId => Identity();
    public Col<DateTime> OrderDate => Default(() => DateTime.UtcNow);
    public Col<string?> Notes { get; }

    public Many<OrderItemSchema> Items => HasMany<OrderItemSchema>(i => i.OrderId);
}
