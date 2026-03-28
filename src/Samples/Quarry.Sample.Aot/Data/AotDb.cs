using Quarry;

namespace Quarry.Sample.Aot.Data;

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class AotDb : QuarryContext
{
    public partial IEntityAccessor<Category> Categories();
    public partial IEntityAccessor<Product> Products();
    public partial IEntityAccessor<OrderItem> OrderItems();
}
