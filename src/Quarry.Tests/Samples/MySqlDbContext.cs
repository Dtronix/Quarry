using Quarry;

namespace Quarry.Tests.Samples.My;

[QuarryContext(Dialect = SqlDialect.MySQL)]
public partial class MyDb : QuarryContext
{
    public partial IQueryBuilder<User> Users { get; }
    public partial IQueryBuilder<Order> Orders { get; }
    public partial IQueryBuilder<OrderItem> OrderItems { get; }
    public partial IQueryBuilder<Account> Accounts { get; }
    public partial IQueryBuilder<Product> Products { get; }
    public partial IQueryBuilder<Widget> Widgets { get; }
}
