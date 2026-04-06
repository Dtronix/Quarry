// Re-export SqlDialect for the shared SQL parser which uses 'using Quarry;'
// This is in a separate namespace to avoid conflicts when Quarry.dll is also referenced.
// The parser files use 'using Quarry;' in the non-QUARRY_GENERATOR path, so we keep
// the same namespace but mark it internal to limit visibility.
//
// However, InternalsVisibleTo can still cause conflicts. To fix this, we use a
// type-forwarding approach: exclude this file from the build and instead use a
// namespace alias in the shared parser.
//
// Actually, the simplest fix is to just keep the same namespace and internal visibility.
// The conflict happens because IVT exposes it. Let's remove IVT for Quarry.Tool and
// use only the public DapperConverter API.
namespace Quarry;

internal enum SqlDialect
{
    SQLite = 0,
    PostgreSQL = 1,
    MySQL = 2,
    SqlServer = 3,
}
