using Quarry.Internal;
using Quarry.Shared.Sql;

namespace Quarry.Tests.SqlOutput;

[TestFixture]
#pragma warning disable QRY001
public class OrderByTests
{
    private static string Q(SqlDialect d, string id) => SqlFormatting.QuoteIdentifier(d, id);

    [Test]
    public void SingleColumnAsc()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithOrderBy("\"UserName\"", Direction.Ascending);
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Is.EqualTo("SELECT * FROM \"users\" ORDER BY \"UserName\" ASC"));
    }

    [Test]
    public void SingleColumnDesc()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithOrderBy("\"UserName\"", Direction.Descending);
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Is.EqualTo("SELECT * FROM \"users\" ORDER BY \"UserName\" DESC"));
    }

    [Test]
    public void MultipleColumns()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithOrderBy("\"UserName\"", Direction.Ascending)
            .WithOrderBy("\"CreatedAt\"", Direction.Descending);
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Is.EqualTo("SELECT * FROM \"users\" ORDER BY \"UserName\" ASC, \"CreatedAt\" DESC"));
    }

    [Test]
    public void WithWhereAndOrderBy()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithWhere("\"IsActive\" = 1")
            .WithOrderBy("\"UserName\"", Direction.Ascending);
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Is.EqualTo("SELECT * FROM \"users\" WHERE \"IsActive\" = 1 ORDER BY \"UserName\" ASC"));
    }

    [Test]
    public void AliasedColumn()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithOrderBy("t0.\"UserName\"", Direction.Ascending);
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Is.EqualTo("SELECT * FROM \"users\" ORDER BY t0.\"UserName\" ASC"));
    }

    [TestCase(SqlDialect.SQLite, "SELECT * FROM \"users\" ORDER BY \"UserName\" ASC")]
    [TestCase(SqlDialect.PostgreSQL, "SELECT * FROM \"users\" ORDER BY \"UserName\" ASC")]
    [TestCase(SqlDialect.MySQL, "SELECT * FROM `users` ORDER BY `UserName` ASC")]
    [TestCase(SqlDialect.SqlServer, "SELECT * FROM [users] ORDER BY [UserName] ASC")]
    public void OrderByAllDialects(SqlDialect dialect, string expected)
    {
        var state = new QueryState(dialect, "users", null)
            .WithOrderBy(Q(dialect, "UserName"), Direction.Ascending);
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Is.EqualTo(expected));
    }
}
