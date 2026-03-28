using Quarry;

namespace Quarry.Sample.Aot.Data.Schemas;

public class CategorySchema : Schema
{
    public static string Table => "categories";

    public Key<int> CategoryId => Identity();
    public Col<string> Name => Length(100);
}
