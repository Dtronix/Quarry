using Quarry.Generators.Sql;
using Quarry.Internal;
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
    /// Unified QueryDiagnostics overload: dispatches compile-time verification
    /// based on <see cref="DiagnosticQueryKind"/>.
    /// </summary>
    protected static void AssertDialects(
        QueryDiagnostics sqliteDiag, QueryDiagnostics pgDiag,
        QueryDiagnostics mysqlDiag, QueryDiagnostics ssDiag,
        string sqlite, string pg, string mysql, string ss)
    {
        Assert.Multiple(() =>
        {
            // Runtime assertions
            Assert.That(sqliteDiag.Sql, Is.EqualTo(sqlite), "SQLite runtime");
            Assert.That(pgDiag.Sql, Is.EqualTo(pg), "PostgreSQL runtime");
            Assert.That(mysqlDiag.Sql, Is.EqualTo(mysql), "MySQL runtime");
            Assert.That(ssDiag.Sql, Is.EqualTo(ss), "SqlServer runtime");

            // Compile-time equivalence assertions
            AssertDiagnosticsCompileTime(sqliteDiag, "SQLite");
            AssertDiagnosticsCompileTime(pgDiag, "PostgreSQL");
            AssertDiagnosticsCompileTime(mysqlDiag, "MySQL");
            AssertDiagnosticsCompileTime(ssDiag, "SqlServer");
        });
    }

    // ───────────────────────────────────────────────────────────────
    // Compile-time assertion helpers
    // ───────────────────────────────────────────────────────────────

    private static void AssertDiagnosticsCompileTime(QueryDiagnostics diag, string dialectName)
    {
        // When the generated interceptor provides prebuilt SQL, RawState is null.
        // Compile-time verification is unnecessary — the prebuilt SQL IS the compile-time output.
        if (diag.RawState == null)
            return;

        switch (diag.Kind)
        {
            case DiagnosticQueryKind.Select:
                AssertSelectCompileTime(diag.Sql, (QueryState)diag.RawState!, dialectName);
                break;
            case DiagnosticQueryKind.Update:
                AssertUpdateCompileTime(diag.Sql, (UpdateState)diag.RawState!, dialectName);
                break;
            case DiagnosticQueryKind.Delete:
                AssertDeleteCompileTime(diag.Sql, (DeleteState)diag.RawState!, dialectName);
                break;
            case DiagnosticQueryKind.Insert:
                AssertInsertCompileTime(diag.Sql, (InsertState)diag.RawState!, diag.InsertRowCount, dialectName);
                break;
        }
    }

    private static void AssertSelectCompileTime(string runtimeSql, QueryState state, string dialectName)
    {
        var (clauses, dialect, table, schema, alias) =
            CompileTimeConverter.ConvertSelectState(state);
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);
        var result = CompileTimeSqlBuilder.BuildSelectSql(
            0UL, clauses, templates, dialect, table, schema, alias);

        bool hasPagination = state.Limit.HasValue || state.Offset.HasValue;
        if (hasPagination)
        {
            // Compare non-pagination prefix only
            var runtimePrefix = GetPaginationPrefix(runtimeSql, dialect);
            var compilePrefix = GetPaginationPrefix(result.Sql, dialect);
            Assert.That(compilePrefix, Is.EqualTo(runtimePrefix),
                $"{dialectName} compile-time prefix mismatch");
        }
        else
        {
            Assert.That(result.Sql, Is.EqualTo(runtimeSql),
                $"{dialectName} compile-time mismatch");
        }
    }

    private static void AssertUpdateCompileTime(string runtimeSql, UpdateState state, string dialectName)
    {
        var (clauses, dialect, table, schema) =
            CompileTimeConverter.ConvertUpdateState(state);
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);
        var result = CompileTimeSqlBuilder.BuildUpdateSql(
            0UL, clauses, templates, dialect, table, schema);

        Assert.That(result.Sql, Is.EqualTo(runtimeSql),
            $"{dialectName} compile-time mismatch");
    }

    private static void AssertDeleteCompileTime(string runtimeSql, DeleteState state, string dialectName)
    {
        var (clauses, dialect, table, schema) =
            CompileTimeConverter.ConvertDeleteState(state);
        var templates = CompileTimeSqlBuilder.BuildTemplates(clauses);
        var result = CompileTimeSqlBuilder.BuildDeleteSql(
            0UL, clauses, templates, dialect, table, schema);

        Assert.That(result.Sql, Is.EqualTo(runtimeSql),
            $"{dialectName} compile-time mismatch");
    }

    private static void AssertInsertCompileTime(string runtimeSql, InsertState state, int rowCount, string dialectName)
    {
        var genDialect = (GenSqlDialect)(int)state.Dialect;
        var result = CompileTimeSqlBuilder.BuildInsertSql(
            genDialect,
            state.TableName,
            state.SchemaName,
            state.Columns,
            state.Columns.Count * rowCount,
            state.IdentityColumn);

        Assert.That(result.Sql, Is.EqualTo(runtimeSql),
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
