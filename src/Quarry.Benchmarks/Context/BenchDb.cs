using Quarry;

namespace Quarry.Benchmarks.Context;

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class BenchDb : QuarryContext
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Order> Orders();
    public partial IEntityAccessor<OrderItem> OrderItems();
}
