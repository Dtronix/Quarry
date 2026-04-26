using Quarry;

namespace Quarry.Tests.Samples.My;

[QuarryContext(Dialect = SqlDialect.MySQL)]
public partial class MyDb : QuarryContext
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Order> Orders();
    public partial IEntityAccessor<OrderItem> OrderItems();
    public partial IEntityAccessor<Account> Accounts();
    public partial IEntityAccessor<Product> Products();
    public partial IEntityAccessor<Widget> Widgets();
    public partial IEntityAccessor<Address> Addresses();
    public partial IEntityAccessor<UserAddress> UserAddresses();
    public partial IEntityAccessor<Warehouse> Warehouses();
    public partial IEntityAccessor<Shipment> Shipments();
    public partial IEntityAccessor<Event> Events();
}
