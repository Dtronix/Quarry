using Quarry.Internal;
using Quarry.Shared.Sql;

namespace Quarry.Tests.SqlOutput;

[TestFixture]
#pragma warning disable QRY001
public class ParameterFormattingTests
{
    [TestCase(SqlDialect.SQLite, "@p0")]
    [TestCase(SqlDialect.PostgreSQL, "$1")]
    [TestCase(SqlDialect.MySQL, "?")]
    [TestCase(SqlDialect.SqlServer, "@p0")]
    public void SelectWhere_SingleParam(SqlDialect dialect, string param)
    {
        Assert.That(SqlFormatting.FormatParameter(dialect, 0), Is.EqualTo(param));
    }

    [TestCase(SqlDialect.SQLite, "@p0", "@p1")]
    [TestCase(SqlDialect.PostgreSQL, "$1", "$2")]
    [TestCase(SqlDialect.MySQL, "?", "?")]
    [TestCase(SqlDialect.SqlServer, "@p0", "@p1")]
    public void SelectWhere_MultipleParams(SqlDialect dialect, string p0, string p1)
    {
        Assert.That(SqlFormatting.FormatParameter(dialect, 0), Is.EqualTo(p0));
        Assert.That(SqlFormatting.FormatParameter(dialect, 1), Is.EqualTo(p1));
    }

    [TestCase(SqlDialect.SQLite, "@p0")]
    [TestCase(SqlDialect.PostgreSQL, "$1")]
    [TestCase(SqlDialect.MySQL, "?")]
    [TestCase(SqlDialect.SqlServer, "@p0")]
    public void Insert_SingleRow(SqlDialect dialect, string param)
    {
        var state = new InsertState(dialect, "users", null, null);
        state.Columns.AddRange([SqlFormatting.QuoteIdentifier(dialect, "Name")]);
        state.Rows.Add([0]);
        var sql = SqlModificationBuilder.BuildInsertSql(state, 1);
        Assert.That(sql, Does.Contain($"VALUES ({param})"));
    }

    [TestCase(SqlDialect.SQLite, "(@p0), (@p1)")]
    [TestCase(SqlDialect.PostgreSQL, "($1), ($2)")]
    [TestCase(SqlDialect.MySQL, "(?), (?)")]
    [TestCase(SqlDialect.SqlServer, "(@p0), (@p1)")]
    public void Insert_BatchRow(SqlDialect dialect, string expectedValues)
    {
        var state = new InsertState(dialect, "users", null, null);
        state.Columns.AddRange([SqlFormatting.QuoteIdentifier(dialect, "Name")]);
        state.Rows.Add([0]);
        state.Rows.Add([1]);
        var sql = SqlModificationBuilder.BuildInsertSql(state, 2);
        Assert.That(sql, Does.Contain($"VALUES {expectedValues}"));
    }

    [Test]
    public void PostgreSQL_OneBasedIndex()
    {
        Assert.That(SqlFormatting.FormatParameter(SqlDialect.PostgreSQL, 0), Is.EqualTo("$1"));
        Assert.That(SqlFormatting.FormatParameter(SqlDialect.PostgreSQL, 1), Is.EqualTo("$2"));
        Assert.That(SqlFormatting.FormatParameter(SqlDialect.PostgreSQL, 2), Is.EqualTo("$3"));
    }

    [Test]
    public void MySQL_AllQuestionMarks()
    {
        Assert.That(SqlFormatting.FormatParameter(SqlDialect.MySQL, 0), Is.EqualTo("?"));
        Assert.That(SqlFormatting.FormatParameter(SqlDialect.MySQL, 1), Is.EqualTo("?"));
        Assert.That(SqlFormatting.FormatParameter(SqlDialect.MySQL, 2), Is.EqualTo("?"));
    }

    [Test]
    public void SqlServer_AtPrefixed()
    {
        Assert.That(SqlFormatting.FormatParameter(SqlDialect.SqlServer, 0), Is.EqualTo("@p0"));
        Assert.That(SqlFormatting.FormatParameter(SqlDialect.SqlServer, 1), Is.EqualTo("@p1"));
        Assert.That(SqlFormatting.FormatParameter(SqlDialect.SqlServer, 2), Is.EqualTo("@p2"));
    }

    [Test]
    public void SQLite_AtPrefixed()
    {
        Assert.That(SqlFormatting.FormatParameter(SqlDialect.SQLite, 0), Is.EqualTo("@p0"));
        Assert.That(SqlFormatting.FormatParameter(SqlDialect.SQLite, 1), Is.EqualTo("@p1"));
    }
}
