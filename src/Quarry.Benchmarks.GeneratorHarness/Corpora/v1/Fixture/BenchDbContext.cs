// CORPUS — embedded resource for QuarryGenerator benchmarks; not compiled into Quarry.Benchmarks.
using Quarry;

namespace BenchHarness;

[QuarryContext(Dialect = SqlDialect.PostgreSQL, Schema = "public")]
public partial class BenchDb : QuarryContext
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Order> Orders();
    public partial IEntityAccessor<OrderItem> OrderItems();
    public partial IEntityAccessor<Product> Products();
    public partial IEntityAccessor<Address> Addresses();
}
