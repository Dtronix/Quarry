using Quarry;

namespace BenchHarness;

public class ProductSchema : Schema
{
    public static string Table => "products";

    public Key<int> ProductId => Identity();
    public Col<string> ProductName => Length(200);
    public Col<decimal> Price => Precision(18, 2);
    public Col<string?> Description { get; }
}
