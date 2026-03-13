using Quarry;

namespace Quarry.Benchmarks.Context;

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class BenchDb : QuarryContext
{
    public partial IQueryBuilder<User> Users { get; }
    public partial IQueryBuilder<Order> Orders { get; }
    public partial IQueryBuilder<OrderItem> OrderItems { get; }
}
