using Quarry;

namespace Quarry.Tests.Samples.Pg;

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class PgDb : QuarryContext
{
    public partial IQueryBuilder<User> Users { get; }
    public partial IQueryBuilder<Order> Orders { get; }
    public partial IQueryBuilder<OrderItem> OrderItems { get; }
    public partial IQueryBuilder<Account> Accounts { get; }
    public partial IQueryBuilder<Product> Products { get; }
    public partial IQueryBuilder<Widget> Widgets { get; }
}
