using Quarry;

namespace Quarry.Tests.Samples.Pg;

[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class PgDb : QuarryContext
{
    public partial IQueryBuilder<User> Users();
    public partial IQueryBuilder<Order> Orders();
    public partial IQueryBuilder<OrderItem> OrderItems();
    public partial IQueryBuilder<Account> Accounts();
    public partial IQueryBuilder<Product> Products();
    public partial IQueryBuilder<Widget> Widgets();
}
