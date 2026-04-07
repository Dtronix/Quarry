using Quarry;
using Quarry.Tests.Samples;

namespace Quarry.Tests.Samples.Cte;

/// <summary>
/// Test context that inherits from <see cref="QuarryContext{TSelf}"/> to verify
/// CTE chains with entity accessors (e.g. <c>db.With&lt;A&gt;(q).Users().Join&lt;B&gt;(...)</c>)
/// resolve correctly through the source generator.
/// </summary>
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class CteDb : QuarryContext<CteDb>
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Order> Orders();
    public partial IEntityAccessor<OrderItem> OrderItems();
}
