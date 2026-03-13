using Quarry.Generators.Sql;
using Quarry.Tests.Samples;
using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;

#pragma warning disable QRY001

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
    /// SELECT overload: asserts runtime SQL matches expected, then verifies
    /// compile-time SQL from the same state matches runtime output.
    /// Pagination tests use prefix comparison (compile-time uses parameterized pagination).
    /// </summary>
    protected static void AssertDialects(
        SqlTestCase sqliteCase, SqlTestCase pgCase,
        SqlTestCase mysqlCase, SqlTestCase ssCase,
        string sqlite, string pg, string mysql, string ss)
    {
        Assert.Multiple(() =>
        {
            // Runtime assertions
            Assert.That(sqliteCase.RuntimeSql, Is.EqualTo(sqlite), "SQLite runtime");
            Assert.That(pgCase.RuntimeSql, Is.EqualTo(pg), "PostgreSQL runtime");
            Assert.That(mysqlCase.RuntimeSql, Is.EqualTo(mysql), "MySQL runtime");
            Assert.That(ssCase.RuntimeSql, Is.EqualTo(ss), "SqlServer runtime");

            // Compile-time equivalence assertions
            AssertSelectCompileTime(sqliteCase, "SQLite");
            AssertSelectCompileTime(pgCase, "PostgreSQL");
            AssertSelectCompileTime(mysqlCase, "MySQL");
            AssertSelectCompileTime(ssCase, "SqlServer");
        });
    }

    /// <summary>
    /// UPDATE overload: asserts runtime SQL matches expected, then verifies
    /// compile-time SQL from the same state matches runtime output.
    /// </summary>
    protected static void AssertDialects(
        UpdateTestCase sqliteCase, UpdateTestCase pgCase,
        UpdateTestCase mysqlCase, UpdateTestCase ssCase,
        string sqlite, string pg, string mysql, string ss)
    {
        Assert.Multiple(() =>
        {
            Assert.That(sqliteCase.RuntimeSql, Is.EqualTo(sqlite), "SQLite runtime");
            Assert.That(pgCase.RuntimeSql, Is.EqualTo(pg), "PostgreSQL runtime");
            Assert.That(mysqlCase.RuntimeSql, Is.EqualTo(mysql), "MySQL runtime");
            Assert.That(ssCase.RuntimeSql, Is.EqualTo(ss), "SqlServer runtime");

            AssertUpdateCompileTime(sqliteCase, "SQLite");
            AssertUpdateCompileTime(pgCase, "PostgreSQL");
            AssertUpdateCompileTime(mysqlCase, "MySQL");
            AssertUpdateCompileTime(ssCase, "SqlServer");
        });
    }

    /// <summary>
    /// DELETE overload: asserts runtime SQL matches expected, then verifies
    /// compile-time SQL from the same state matches runtime output.
    /// </summary>
    protected static void AssertDialects(
        DeleteTestCase sqliteCase, DeleteTestCase pgCase,
        DeleteTestCase mysqlCase, DeleteTestCase ssCase,
        string sqlite, string pg, string mysql, string ss)
    {
        Assert.Multiple(() =>
        {
            Assert.That(sqliteCase.RuntimeSql, Is.EqualTo(sqlite), "SQLite runtime");
            Assert.That(pgCase.RuntimeSql, Is.EqualTo(pg), "PostgreSQL runtime");
            Assert.That(mysqlCase.RuntimeSql, Is.EqualTo(mysql), "MySQL runtime");
            Assert.That(ssCase.RuntimeSql, Is.EqualTo(ss), "SqlServer runtime");

            AssertDeleteCompileTime(sqliteCase, "SQLite");
            AssertDeleteCompileTime(pgCase, "PostgreSQL");
            AssertDeleteCompileTime(mysqlCase, "MySQL");
            AssertDeleteCompileTime(ssCase, "SqlServer");
        });
    }

    /// <summary>
    /// INSERT overload: asserts runtime SQL matches expected, then verifies
    /// compile-time SQL from the same state matches runtime output.
    /// </summary>
    protected static void AssertDialects(
        InsertTestCase sqliteCase, InsertTestCase pgCase,
        InsertTestCase mysqlCase, InsertTestCase ssCase,
        string sqlite, string pg, string mysql, string ss)
    {
        Assert.Multiple(() =>
        {
            Assert.That(sqliteCase.RuntimeSql, Is.EqualTo(sqlite), "SQLite runtime");
            Assert.That(pgCase.RuntimeSql, Is.EqualTo(pg), "PostgreSQL runtime");
            Assert.That(mysqlCase.RuntimeSql, Is.EqualTo(mysql), "MySQL runtime");
            Assert.That(ssCase.RuntimeSql, Is.EqualTo(ss), "SqlServer runtime");

            AssertInsertCompileTime(sqliteCase, "SQLite");
            AssertInsertCompileTime(pgCase, "PostgreSQL");
            AssertInsertCompileTime(mysqlCase, "MySQL");
            AssertInsertCompileTime(ssCase, "SqlServer");
        });
    }

    // ───────────────────────────────────────────────────────────────
    // Compile-time assertion helpers
    // ───────────────────────────────────────────────────────────────

    private static void AssertSelectCompileTime(SqlTestCase testCase, string dialectName)
    {
        var (clauses, dialect, table, schema, alias) =
            CompileTimeConverter.ConvertSelectState(testCase.State);
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates, dialect, table, schema, alias);

        bool hasPagination = testCase.State.Limit.HasValue || testCase.State.Offset.HasValue;
        if (hasPagination)
        {
            // Compare non-pagination prefix only
            var runtimePrefix = GetPaginationPrefix(testCase.RuntimeSql, dialect);
            var compilePrefix = GetPaginationPrefix(result.Sql, dialect);
            Assert.That(compilePrefix, Is.EqualTo(runtimePrefix),
                $"{dialectName} compile-time prefix mismatch");
        }
        else
        {
            Assert.That(result.Sql, Is.EqualTo(testCase.RuntimeSql),
                $"{dialectName} compile-time mismatch");
        }
    }

    private static void AssertUpdateCompileTime(UpdateTestCase testCase, string dialectName)
    {
        var (clauses, dialect, table, schema) =
            CompileTimeConverter.ConvertUpdateState(testCase.State);
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);
        var result = CompileTimeSqlBuilder.BuildUpdateSql(
            0UL, clauses, templates, dialect, table, schema);

        Assert.That(result.Sql, Is.EqualTo(testCase.RuntimeSql),
            $"{dialectName} compile-time mismatch");
    }

    private static void AssertDeleteCompileTime(DeleteTestCase testCase, string dialectName)
    {
        var (clauses, dialect, table, schema) =
            CompileTimeConverter.ConvertDeleteState(testCase.State);
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);
        var result = CompileTimeSqlBuilder.BuildDeleteSql(
            0UL, clauses, templates, dialect, table, schema);

        Assert.That(result.Sql, Is.EqualTo(testCase.RuntimeSql),
            $"{dialectName} compile-time mismatch");
    }

    private static void AssertInsertCompileTime(InsertTestCase testCase, string dialectName)
    {
        var state = testCase.State;
        var genDialect = (GenSqlDialect)(int)state.Dialect;
        var result = CompileTimeSqlBuilder.BuildInsertSql(
            genDialect,
            state.TableName,
            state.SchemaName,
            state.Columns,
            state.Columns.Count * testCase.RowCount,
            state.IdentityColumn);

        Assert.That(result.Sql, Is.EqualTo(testCase.RuntimeSql),
            $"{dialectName} compile-time mismatch");
    }

    /// <summary>
    /// Extracts the SQL prefix before pagination keywords (LIMIT/OFFSET/FETCH).
    /// For SQL Server, pagination starts at "OFFSET"; for others at "LIMIT".
    /// </summary>
    private static string GetPaginationPrefix(string sql, GenSqlDialect dialect)
    {
        // For SQL Server with no ORDER BY, the "ORDER BY (SELECT NULL)" is pagination-related
        // Find the start of pagination
        int paginationStart;
        if (dialect == GenSqlDialect.SqlServer)
        {
            // SQL Server: pagination starts at ORDER BY (SELECT NULL) or OFFSET
            paginationStart = sql.IndexOf(" ORDER BY (SELECT NULL)", StringComparison.Ordinal);
            if (paginationStart < 0)
                paginationStart = sql.LastIndexOf(" OFFSET ", StringComparison.Ordinal);
        }
        else
        {
            paginationStart = sql.LastIndexOf(" LIMIT ", StringComparison.Ordinal);
        }

        return paginationStart >= 0 ? sql[..paginationStart] : sql;
    }
}
