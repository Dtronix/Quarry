using Quarry;

namespace Quarry.Tests.Samples.SchemaPg
{
    [QuarryContext(Dialect = SqlDialect.PostgreSQL, Schema = "public")]
    public partial class SchemaPgDb : QuarryContext
    {
        public partial IQueryBuilder<User> Users { get; }
    }
}

namespace Quarry.Tests.Samples.SchemaMy
{
    [QuarryContext(Dialect = SqlDialect.MySQL, Schema = "myapp")]
    public partial class SchemaMyDb : QuarryContext
    {
        public partial IQueryBuilder<User> Users { get; }
    }
}

namespace Quarry.Tests.Samples.SchemaSs
{
    [QuarryContext(Dialect = SqlDialect.SqlServer, Schema = "dbo")]
    public partial class SchemaSsDb : QuarryContext
    {
        public partial IQueryBuilder<User> Users { get; }
    }
}
