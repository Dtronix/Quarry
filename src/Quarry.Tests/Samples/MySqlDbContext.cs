using Quarry;

namespace Quarry.Tests.Samples.My;

[QuarryContext(Dialect = SqlDialect.MySQL)]
public partial class MyDb : QuarryContext
{
    public partial IQueryBuilder<User> Users();
    public partial IQueryBuilder<Order> Orders();
    public partial IQueryBuilder<OrderItem> OrderItems();
    public partial IQueryBuilder<Account> Accounts();
    public partial IQueryBuilder<Product> Products();
    public partial IQueryBuilder<Widget> Widgets();
}
