using Quarry;

namespace Quarry.Tests.Samples;

/// <summary>
/// Schema definition for the order_items table.
/// </summary>
public class OrderItemSchema : Schema
{
    public static string Table => "order_items";

    public Key<int> OrderItemId => Identity();
    public Ref<OrderSchema, int> OrderId => ForeignKey<OrderSchema, int>();
    public Col<string> ProductName => Length(200);
    public Col<int> Quantity { get; }
    public Col<decimal> UnitPrice => Precision(18, 2);
    public Col<decimal> LineTotal { get; }

    /// <summary>
    /// Singular navigation to the related Order entity (N:1).
    /// FK inferred automatically from single Ref&lt;OrderSchema, int&gt;.
    /// </summary>
    public One<OrderSchema> Order { get; }
}
