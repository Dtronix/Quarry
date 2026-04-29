using Quarry.Generators.Models;
using Quarry.Generators.Projection;

namespace Quarry.Tests;

/// <summary>
/// Tests for the SQL Server int-cast wrap on window-function projections (#274).
///
/// Two layers are covered:
///   • Public helper methods <see cref="ReaderCodeGenerator.GenerateColumnList"/> and
///     <see cref="ReaderCodeGenerator.GenerateColumnNamesArray"/> — direct unit tests
///     against constructed <see cref="ProjectedColumn"/> instances.
///   • The production SELECT-clause emit path
///     (<c>SqlAssembler.AppendProjectionColumnSql</c>) — driven end-to-end via
///     <see cref="QueryTestHarness"/> + <c>Prepare().ToDiagnostics()</c>. Cross-dialect
///     tests in <c>CrossDialectWindowFunctionTests</c> exercise this path broadly; the
///     production-path test here pins the wrap behaviour specifically as a regression
///     anchor for the cast logic itself.
/// </summary>
[TestFixture]
public class SqlServerWindowIntCastTests
{
    private static ProjectionInfo MakeProjection(bool requiresCast) => new(
        ProjectionKind.Tuple,
        "(int, int)",
        new[]
        {
            new ProjectedColumn(
                propertyName: "Item1",
                columnName: "OrderId",
                clrType: "int",
                fullClrType: "int",
                isNullable: false,
                ordinal: 0,
                isValueType: true,
                readerMethodName: "GetInt32"),
            new ProjectedColumn(
                propertyName: "RowNum",
                columnName: "",
                clrType: "int",
                fullClrType: "int",
                isNullable: false,
                ordinal: 1,
                alias: "RowNum",
                sqlExpression: "ROW_NUMBER() OVER (ORDER BY {OrderDate})",
                isAggregateFunction: true,
                isValueType: true,
                readerMethodName: "GetInt32",
                requiresSqlServerIntCast: requiresCast),
        });

    [Test]
    public void ColumnList_FlaggedColumn_OnSqlServer_WrapsWithCast()
    {
        var projection = MakeProjection(requiresCast: true);

        var sql = ReaderCodeGenerator.GenerateColumnList(projection, Quarry.Generators.Sql.SqlDialect.SqlServer);

        Assert.That(sql, Does.Contain("CAST(ROW_NUMBER() OVER (ORDER BY [OrderDate]) AS INT) AS [RowNum]"),
            "On Ss, flagged window-function int projection must be wrapped with CAST(... AS INT)");
    }

    [Test]
    public void ColumnList_FlaggedColumn_OnPostgreSQL_DoesNotWrap()
    {
        AssertNoWrap_ColumnList(Quarry.Generators.Sql.SqlDialect.PostgreSQL);
    }

    [Test]
    public void ColumnList_FlaggedColumn_OnMySQL_DoesNotWrap()
    {
        AssertNoWrap_ColumnList(Quarry.Generators.Sql.SqlDialect.MySQL);
    }

    [Test]
    public void ColumnList_FlaggedColumn_OnSQLite_DoesNotWrap()
    {
        AssertNoWrap_ColumnList(Quarry.Generators.Sql.SqlDialect.SQLite);
    }

    private static void AssertNoWrap_ColumnList(Quarry.Generators.Sql.SqlDialect dialect)
    {
        var projection = MakeProjection(requiresCast: true);

        var sql = ReaderCodeGenerator.GenerateColumnList(projection, dialect);

        Assert.That(sql, Does.Not.Contain("CAST("),
            $"Dialect {dialect} must not wrap with CAST when only Ss is targeted");
        Assert.That(sql, Does.Contain("ROW_NUMBER() OVER"),
            "Window-function expression must still be present");
    }

    [Test]
    public void ColumnList_UnflaggedColumn_OnSqlServer_DoesNotWrap()
    {
        var projection = MakeProjection(requiresCast: false);

        var sql = ReaderCodeGenerator.GenerateColumnList(projection, Quarry.Generators.Sql.SqlDialect.SqlServer);

        Assert.That(sql, Does.Not.Contain("CAST("),
            "Without the flag, no CAST wrap should be emitted on Ss");
        Assert.That(sql, Does.Contain("ROW_NUMBER() OVER (ORDER BY [OrderDate]) AS [RowNum]"),
            "Window-function expression must be emitted unchanged");
    }

    [Test]
    public void ColumnNamesArray_FlaggedColumn_OnSqlServer_WrapsWithCast()
    {
        var projection = MakeProjection(requiresCast: true);

        var arr = ReaderCodeGenerator.GenerateColumnNamesArray(projection, Quarry.Generators.Sql.SqlDialect.SqlServer);

        Assert.That(arr, Does.Contain("CAST(ROW_NUMBER() OVER (ORDER BY [OrderDate]) AS INT)"),
            "Dynamic-SQL column-names array must apply the same CAST wrap on Ss");
    }

    [Test]
    public void ColumnNamesArray_FlaggedColumn_OnPostgreSQL_DoesNotWrap()
    {
        AssertNoWrap_ColumnNamesArray(Quarry.Generators.Sql.SqlDialect.PostgreSQL);
    }

    [Test]
    public void ColumnNamesArray_FlaggedColumn_OnMySQL_DoesNotWrap()
    {
        AssertNoWrap_ColumnNamesArray(Quarry.Generators.Sql.SqlDialect.MySQL);
    }

    [Test]
    public void ColumnNamesArray_FlaggedColumn_OnSQLite_DoesNotWrap()
    {
        AssertNoWrap_ColumnNamesArray(Quarry.Generators.Sql.SqlDialect.SQLite);
    }

    private static void AssertNoWrap_ColumnNamesArray(Quarry.Generators.Sql.SqlDialect dialect)
    {
        var projection = MakeProjection(requiresCast: true);

        var arr = ReaderCodeGenerator.GenerateColumnNamesArray(projection, dialect);

        Assert.That(arr, Does.Not.Contain("CAST("),
            $"Dialect {dialect} must not wrap with CAST in the column-names array");
    }

    /// <summary>
    /// Production-path regression: a generated chain that selects ROW_NUMBER OVER produces
    /// CAST(ROW_NUMBER() OVER (...) AS INT) on Ss and bare ROW_NUMBER on every other dialect.
    /// Drives <c>SqlAssembler.AppendProjectionColumnSql</c> end-to-end via the test harness.
    /// </summary>
    [Test]
    public async Task ProductionPath_RowNumber_OnSqlServer_WrapsWithCast()
    {
        await using var t = await Quarry.Tests.QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var ss = Ss.Orders().Where(o => true)
            .Select(o => (o.OrderId, RowNum: Sql.RowNumber(over => over.OrderBy(o.OrderDate))))
            .Prepare();

        var ssDiag = ss.ToDiagnostics();
        Assert.That(ssDiag.Sql, Does.Contain("CAST(ROW_NUMBER() OVER (ORDER BY [OrderDate]) AS INT) AS [RowNum]"),
            "Production SELECT-clause emit on Ss must wrap the int-typed window projection with CAST(... AS INT)");
    }

    /// <summary>
    /// Production-path regression: the same chain on PostgreSQL/MySQL/SQLite must NOT include CAST.
    /// </summary>
    [Test]
    public async Task ProductionPath_RowNumber_OnNonSqlServer_DoesNotWrap()
    {
        await using var t = await Quarry.Tests.QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var pg = Pg.Orders().Where(o => true)
            .Select(o => (o.OrderId, RowNum: Sql.RowNumber(over => over.OrderBy(o.OrderDate))))
            .Prepare();
        var my = My.Orders().Where(o => true)
            .Select(o => (o.OrderId, RowNum: Sql.RowNumber(over => over.OrderBy(o.OrderDate))))
            .Prepare();
        var lt = Lite.Orders().Where(o => true)
            .Select(o => (o.OrderId, RowNum: Sql.RowNumber(over => over.OrderBy(o.OrderDate))))
            .Prepare();

        Assert.That(pg.ToDiagnostics().Sql, Does.Not.Contain("CAST("),
            "PostgreSQL emit must not include CAST");
        Assert.That(my.ToDiagnostics().Sql, Does.Not.Contain("CAST("),
            "MySQL emit must not include CAST");
        Assert.That(lt.ToDiagnostics().Sql, Does.Not.Contain("CAST("),
            "SQLite emit must not include CAST");
    }
}
