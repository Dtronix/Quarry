using Quarry;

namespace Quarry.Tests.Samples.MyAnsi;

// MySQL context with MySqlBackslashEscapes = false. Paired in tests with a
// per-session `SET sql_mode = ...,NO_BACKSLASH_ESCAPES` against the
// default-mode container — proves the opt-out emit path (single-backslash
// ANSI form) works on a server where the session-level sql_mode matches the
// attribute flag. Lives in its own namespace so the generator emits a
// distinct entity class (per-context entity resolution rule).
[QuarryContext(Dialect = SqlDialect.MySQL, MySqlBackslashEscapes = false)]
public partial class MyAnsiSessionDb : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}
