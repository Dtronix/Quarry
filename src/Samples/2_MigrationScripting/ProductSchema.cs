using Quarry;

namespace MigrationScripting;

public class ProductSchema : Schema
{
    public static string Table => "products";

    public Key<int> ProductId => Identity();
    public Col<string> Name => Length(100);
    public Col<decimal> Price { get; }
    public Col<int> Stock => Default(0);
}
