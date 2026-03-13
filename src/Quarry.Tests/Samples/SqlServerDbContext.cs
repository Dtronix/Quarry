using Quarry;

namespace Quarry.Tests.Samples.Ss;

[QuarryContext(Dialect = SqlDialect.SqlServer)]
public partial class SsDb : QuarryContext
{
    public partial IQueryBuilder<User> Users { get; }
    public partial IQueryBuilder<Order> Orders { get; }
    public partial IQueryBuilder<OrderItem> OrderItems { get; }
    public partial IQueryBuilder<Account> Accounts { get; }
    public partial IQueryBuilder<Product> Products { get; }
    public partial IQueryBuilder<Widget> Widgets { get; }
}
