using Quarry;

namespace MigrationScripting;

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class AppDbContext : QuarryContext
{
    public partial QueryBuilder<Product> Products { get; }
    public partial QueryBuilder<Order> Orders { get; }
    public partial QueryBuilder<OrderItem> OrderItems { get; }
}
