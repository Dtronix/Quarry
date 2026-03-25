using Quarry;

namespace Quarry.Sample.WebApp.Data;

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class AppDb : QuarryContext
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Session> Sessions();
    public partial IEntityAccessor<AuditLog> AuditLogs();
}
