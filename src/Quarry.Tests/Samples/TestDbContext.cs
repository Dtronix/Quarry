using Quarry;

namespace Quarry.Tests.Samples;

/// <summary>
/// Sample database context for testing with the Quarry source generator.
/// The source generator will generate the constructor and query builder properties.
/// </summary>
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IQueryBuilder<User> Users();
    public partial IQueryBuilder<Order> Orders();
    public partial IQueryBuilder<OrderItem> OrderItems();
    public partial IQueryBuilder<Account> Accounts();
    public partial IQueryBuilder<Product> Products();
    public partial IQueryBuilder<Widget> Widgets();
}
