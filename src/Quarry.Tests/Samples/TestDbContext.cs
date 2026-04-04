using Quarry;

namespace Quarry.Tests.Samples;

/// <summary>
/// Sample database context for testing with the Quarry source generator.
/// The source generator will generate the constructor and query builder properties.
/// </summary>
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Order> Orders();
    public partial IEntityAccessor<OrderItem> OrderItems();
    public partial IEntityAccessor<Account> Accounts();
    public partial IEntityAccessor<Product> Products();
    public partial IEntityAccessor<Widget> Widgets();
    public partial IEntityAccessor<Event> Events();
    public partial IEntityAccessor<Address> Addresses();
    public partial IEntityAccessor<UserAddress> UserAddresses();
    public partial IEntityAccessor<Warehouse> Warehouses();
    public partial IEntityAccessor<Shipment> Shipments();
}
