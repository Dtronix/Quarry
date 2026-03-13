using Quarry;

namespace SimpleMigration;

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class SampleDbContext : QuarryContext
{
    public partial QueryBuilder<User> Users { get; }
    public partial QueryBuilder<Post> Posts { get; }
}
