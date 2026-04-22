using Quarry;

namespace DocsValidation;

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class AppDb : QuarryContext
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Order> Orders();
    public partial IEntityAccessor<Tag> Tags();
    public partial IEntityAccessor<OrderTag> OrderTags();
    public partial IEntityAccessor<Sale> Sales();
    public partial IEntityAccessor<Student> Students();
}
