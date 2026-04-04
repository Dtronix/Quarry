using Quarry;

namespace Quarry.Tests.Samples;

public class ShipmentSchema : Schema
{
    public static string Table => "shipments";
    public Key<int> ShipmentId => Identity();
    public Ref<OrderSchema, int> OrderId => ForeignKey<OrderSchema, int>();
    public Ref<WarehouseSchema, int> WarehouseId => ForeignKey<WarehouseSchema, int>();
    public Ref<WarehouseSchema, int?> ReturnWarehouseId => ForeignKey<WarehouseSchema, int?>();
    public Col<DateTime> ShipDate { get; }

    public One<OrderSchema> Order { get; }
    public One<WarehouseSchema> ReturnWarehouse => HasOne<WarehouseSchema>(nameof(ReturnWarehouseId));
}
