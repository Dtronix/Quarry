using Quarry.Internal;
using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;


/// <summary>
/// Shared base for cross-dialect tests. Each concrete context type is exposed
/// so that call sites use concrete types and the generator resolves the dialect.
/// </summary>
internal abstract class CrossDialectTestBase
{
    protected MockDbConnection Connection = null!;
    protected TestDbContext Lite = null!;
    protected Pg.PgDb Pg = null!;
    protected My.MyDb My = null!;
    protected Ss.SsDb Ss = null!;

    [SetUp]
    public void Setup()
    {
        Connection = new MockDbConnection();
        Lite = new TestDbContext(Connection);
        Pg = new Pg.PgDb(Connection);
        My = new My.MyDb(Connection);
        Ss = new Ss.SsDb(Connection);
    }

    [TearDown]
    public void TearDown()
    {
        Ss.Dispose();
        My.Dispose();
        Pg.Dispose();
        Lite.Dispose();
        Connection.Dispose();
    }

    /// <summary>
    /// Asserts exact SQL equality for all 4 dialects. Reports all failures at once.
    /// </summary>
    protected static void AssertDialects(
        string sqliteActual, string pgActual, string mysqlActual, string ssActual,
        string sqlite, string pg, string mysql, string ss)
    {
        Assert.Multiple(() =>
        {
            Assert.That(sqliteActual, Is.EqualTo(sqlite), "SQLite");
            Assert.That(pgActual, Is.EqualTo(pg), "PostgreSQL");
            Assert.That(mysqlActual, Is.EqualTo(mysql), "MySQL");
            Assert.That(ssActual, Is.EqualTo(ss), "SqlServer");
        });
    }

    /// <summary>
    /// Unified QueryDiagnostics overload: verifies runtime SQL matches expected values.
    /// </summary>
    protected static void AssertDialects(
        QueryDiagnostics sqliteDiag, QueryDiagnostics pgDiag,
        QueryDiagnostics mysqlDiag, QueryDiagnostics ssDiag,
        string sqlite, string pg, string mysql, string ss)
    {
        Assert.Multiple(() =>
        {
            Assert.That(sqliteDiag.Sql, Is.EqualTo(sqlite), "SQLite");
            Assert.That(pgDiag.Sql, Is.EqualTo(pg), "PostgreSQL");
            Assert.That(mysqlDiag.Sql, Is.EqualTo(mysql), "MySQL");
            Assert.That(ssDiag.Sql, Is.EqualTo(ss), "SqlServer");
        });
    }
}
