using Quarry;

namespace Quarry.Benchmarks.Context;

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class BenchDb : QuarryContext
{
    public partial IQueryBuilder<User> Users();
    public partial IQueryBuilder<Order> Orders();
    public partial IQueryBuilder<OrderItem> OrderItems();
}
