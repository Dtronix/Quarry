using Quarry;

namespace Quarry.Tests.Samples.MyDefault;

// MySQL context with MySqlBackslashEscapes = true (the attribute default).
// Matches stock MySQL sql_mode where backslash IS a string-literal escape
// character. Used by SQL-shape tests that verify doubled-backslash LIKE
// emission, and by execution tests that boot a default-sql_mode container.
// Lives in its own namespace so the generator emits a separate entity class
// per the per-context resolution rule (App.My.User vs App.MyDefault.User).
[QuarryContext(Dialect = SqlDialect.MySQL, MySqlBackslashEscapes = true)]
public partial class MyDefaultDb : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

