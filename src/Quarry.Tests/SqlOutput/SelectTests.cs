using Quarry.Internal;
using Quarry.Shared.Sql;

namespace Quarry.Tests.SqlOutput;

[TestFixture]
#pragma warning disable QRY001
public class SelectTests
{
    private static string Q(SqlDialect d, string id) => SqlFormatting.QuoteIdentifier(d, id);
    private static string T(SqlDialect d, string tbl, string? schema = null) => SqlFormatting.FormatTableName(d, tbl, schema);

    private class TestEntity;

    [Test]
    public void SelectStar()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null);
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Is.EqualTo("SELECT * FROM \"users\""));
    }

    [Test]
    public void SelectSingleColumn()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithSelect(["\"UserName\""]);
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Is.EqualTo("SELECT \"UserName\" FROM \"users\""));
    }

    [Test]
    public void SelectMultipleColumns()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithSelect(["\"UserId\"", "\"UserName\"", "\"Email\""]);
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Is.EqualTo("SELECT \"UserId\", \"UserName\", \"Email\" FROM \"users\""));
    }

    [Test]
    public void SelectDistinct()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithDistinct();
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Is.EqualTo("SELECT DISTINCT * FROM \"users\""));
    }

    [Test]
    public void SelectDistinctColumns()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithDistinct()
            .WithSelect(["\"UserName\"", "\"Email\""]);
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Is.EqualTo("SELECT DISTINCT \"UserName\", \"Email\" FROM \"users\""));
    }

    [Test]
    public void SelectWithPreQuotedExpression()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithSelect(["COUNT(*)"]);
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Is.EqualTo("SELECT COUNT(*) FROM \"users\""));
    }

    [Test]
    public void SelectSimpleIdentifierGetsAutoQuoted()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithSelect(["UserName"]);
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Is.EqualTo("SELECT \"UserName\" FROM \"users\""));
    }

    [TestCase(SqlDialect.SQLite, "SELECT * FROM \"users\"")]
    [TestCase(SqlDialect.PostgreSQL, "SELECT * FROM \"users\"")]
    [TestCase(SqlDialect.MySQL, "SELECT * FROM `users`")]
    [TestCase(SqlDialect.SqlServer, "SELECT * FROM [users]")]
    public void SelectStarAllDialects(SqlDialect dialect, string expected)
    {
        var state = new QueryState(dialect, "users", null);
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Is.EqualTo(expected));
    }

    [TestCase(SqlDialect.SQLite, "SELECT \"UserName\" FROM \"users\"")]
    [TestCase(SqlDialect.PostgreSQL, "SELECT \"UserName\" FROM \"users\"")]
    [TestCase(SqlDialect.MySQL, "SELECT `UserName` FROM `users`")]
    [TestCase(SqlDialect.SqlServer, "SELECT [UserName] FROM [users]")]
    public void SelectSingleColumnAllDialects(SqlDialect dialect, string expected)
    {
        var state = new QueryState(dialect, "users", null)
            .WithSelect([Q(dialect, "UserName")]);
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Is.EqualTo(expected));
    }

    [Test]
    public void SelectWithSchema()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", "public");
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Is.EqualTo("SELECT * FROM \"public\".\"users\""));
    }

    [Test]
    public void SelectMultipleExpressionsNotRequoted()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithSelect(["\"Status\"", "COUNT(*)", "SUM(\"Total\")"]);
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Is.EqualTo("SELECT \"Status\", COUNT(*), SUM(\"Total\") FROM \"users\""));
    }
}
