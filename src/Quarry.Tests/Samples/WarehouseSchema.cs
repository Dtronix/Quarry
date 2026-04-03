using Quarry;

namespace Quarry.Tests.Samples;

public class WarehouseSchema : Schema
{
    public static string Table => "warehouses";
    public Key<int> WarehouseId => Identity();
    public Col<string> WarehouseName => Length(100);
    public Col<string> Region => Length(50);
}
