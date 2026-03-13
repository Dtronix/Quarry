using Quarry;

namespace Quarry.Tests.Samples;

/// <summary>
/// Sample database context for testing with the Quarry source generator.
/// The source generator will generate the constructor and query builder properties.
/// </summary>
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IQueryBuilder<User> Users { get; }
    public partial IQueryBuilder<Order> Orders { get; }
    public partial IQueryBuilder<OrderItem> OrderItems { get; }
    public partial IQueryBuilder<Account> Accounts { get; }
    public partial IQueryBuilder<Product> Products { get; }
    public partial IQueryBuilder<Widget> Widgets { get; }
}
