using Quarry.Internal;

namespace Quarry.Tests.SqlOutput;

[TestFixture]
public class SubqueryTests
{
    [Test]
    public void InSubquery()
    {
        var state = new QueryState(SqlDialectFactory.SQLite, "users", null)
            .WithWhere("\"UserId\" IN (SELECT \"UserId\" FROM \"orders\")");
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT * FROM \"users\" WHERE \"UserId\" IN (SELECT \"UserId\" FROM \"orders\")"));
    }

    [Test]
    public void NotInSubquery()
    {
        var state = new QueryState(SqlDialectFactory.SQLite, "users", null)
            .WithWhere("\"UserId\" NOT IN (SELECT \"UserId\" FROM \"orders\")");
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT * FROM \"users\" WHERE \"UserId\" NOT IN (SELECT \"UserId\" FROM \"orders\")"));
    }

    [Test]
    public void ExistsSubquery()
    {
        var state = new QueryState(SqlDialectFactory.SQLite, "users", null)
            .WithWhere("EXISTS (SELECT 1 FROM \"orders\" WHERE \"orders\".\"UserId\" = \"users\".\"UserId\")");
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT * FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"orders\" WHERE \"orders\".\"UserId\" = \"users\".\"UserId\")"));
    }

    [Test]
    public void NotExistsSubquery()
    {
        var state = new QueryState(SqlDialectFactory.SQLite, "users", null)
            .WithWhere("NOT EXISTS (SELECT 1 FROM \"orders\" WHERE \"orders\".\"UserId\" = \"users\".\"UserId\")");
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT * FROM \"users\" WHERE NOT EXISTS (SELECT 1 FROM \"orders\" WHERE \"orders\".\"UserId\" = \"users\".\"UserId\")"));
    }

    [Test]
    public void SubqueryWithWhere()
    {
        var state = new QueryState(SqlDialectFactory.SQLite, "users", null)
            .WithWhere("\"UserId\" IN (SELECT \"UserId\" FROM \"orders\" WHERE \"Total\" > @p0)");
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT * FROM \"users\" WHERE \"UserId\" IN (SELECT \"UserId\" FROM \"orders\" WHERE \"Total\" > @p0)"));
    }

    [Test]
    public void SubqueryWithMultipleOuterConditions()
    {
        var state = new QueryState(SqlDialectFactory.SQLite, "users", null)
            .WithWhere("\"UserId\" IN (SELECT \"UserId\" FROM \"orders\")")
            .WithWhere("\"IsActive\" = 1");
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT * FROM \"users\" WHERE (\"UserId\" IN (SELECT \"UserId\" FROM \"orders\")) AND (\"IsActive\" = 1)"));
    }
}
