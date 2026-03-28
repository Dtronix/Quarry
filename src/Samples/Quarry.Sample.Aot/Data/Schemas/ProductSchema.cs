using Quarry;
using Index = Quarry.Index;

namespace Quarry.Sample.Aot.Data.Schemas;

public class ProductSchema : Schema
{
    public static string Table => "products";

    public Key<int> ProductId => Identity();
    public Col<string> Name => Length(200);
    public Col<Money> Price => Mapped<Money, MoneyMapping>();
    public Col<bool> IsActive => Default(true);
    public Col<DateTime> CreatedAt => Default(() => DateTime.UtcNow);
    public Col<Priority> Priority { get; }
    public Col<string?> Description { get; }
    public Ref<CategorySchema, int> CategoryId => ForeignKey<CategorySchema, int>();

    public Many<OrderItemSchema> OrderItems => HasMany<OrderItemSchema>(oi => oi.ProductId);

    public Index IX_Name => Index(Name);
}
