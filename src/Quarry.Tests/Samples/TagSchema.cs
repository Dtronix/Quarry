using Quarry;

namespace Quarry.Tests.Samples;

/// <summary>
/// Schema definition for the tags table. Hangs off OrderItem to enable 3-level
/// nested navigation chains (User.Orders.Items.Tags) used by
/// CrossDialectNestedSubqueryTests.
/// </summary>
public class TagSchema : Schema
{
    public static string Table => "tags";

    public Key<int> TagId => Identity();
    public Ref<OrderItemSchema, int> OrderItemId => ForeignKey<OrderItemSchema, int>();
    public Col<string> TagName => Length(50);
    public Col<string> TagValue => Length(100);
}
