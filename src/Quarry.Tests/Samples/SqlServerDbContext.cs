using Quarry;

namespace Quarry.Tests.Samples.Ss;

[QuarryContext(Dialect = SqlDialect.SqlServer)]
public partial class SsDb : QuarryContext
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Order> Orders();
    public partial IEntityAccessor<OrderItem> OrderItems();
    public partial IEntityAccessor<Account> Accounts();
    public partial IEntityAccessor<Product> Products();
    public partial IEntityAccessor<Widget> Widgets();
}
