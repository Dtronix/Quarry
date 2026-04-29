using Quarry.Generators.Models;
using Quarry.Generators.Projection;

namespace Quarry.Tests;

/// <summary>
/// Tests that <see cref="ReaderCodeGenerator.GenerateColumnList"/> wraps the rendered SQL
/// expression with <c>CAST(... AS INT)</c> on SQL Server when a <see cref="ProjectedColumn"/>
/// sets <c>RequiresSqlServerIntCast = true</c>, and leaves the expression unwrapped on every
/// other dialect. See issue #274.
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
}
