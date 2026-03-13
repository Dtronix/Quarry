using Quarry.Internal;

namespace Quarry.Tests.SqlOutput;

[TestFixture]
#pragma warning disable QRY001
public class AggregateTests
{
    [Test]
    public void CountStar()
    {
        var state = new QueryState(SqlDialectFactory.SQLite, "users", null)
            .WithSelect(["COUNT(*)"]);
        Assert.That(SqlBuilder.BuildSelectSql(state), Is.EqualTo("SELECT COUNT(*) FROM \"users\""));
    }

    [Test]
    public void CountColumn()
    {
        var state = new QueryState(SqlDialectFactory.SQLite, "users", null)
            .WithSelect(["COUNT(\"UserId\")"]);
        Assert.That(SqlBuilder.BuildSelectSql(state), Is.EqualTo("SELECT COUNT(\"UserId\") FROM \"users\""));
    }

    [Test]
    public void Sum()
    {
        var state = new QueryState(SqlDialectFactory.SQLite, "orders", null)
            .WithSelect(["SUM(\"Total\")"]);
        Assert.That(SqlBuilder.BuildSelectSql(state), Is.EqualTo("SELECT SUM(\"Total\") FROM \"orders\""));
    }

    [Test]
    public void Avg()
    {
        var state = new QueryState(SqlDialectFactory.SQLite, "orders", null)
            .WithSelect(["AVG(\"Total\")"]);
        Assert.That(SqlBuilder.BuildSelectSql(state), Is.EqualTo("SELECT AVG(\"Total\") FROM \"orders\""));
    }

    [Test]
    public void Min()
    {
        var state = new QueryState(SqlDialectFactory.SQLite, "orders", null)
            .WithSelect(["MIN(\"Total\")"]);
        Assert.That(SqlBuilder.BuildSelectSql(state), Is.EqualTo("SELECT MIN(\"Total\") FROM \"orders\""));
    }

    [Test]
    public void Max()
    {
        var state = new QueryState(SqlDialectFactory.SQLite, "orders", null)
            .WithSelect(["MAX(\"Total\")"]);
        Assert.That(SqlBuilder.BuildSelectSql(state), Is.EqualTo("SELECT MAX(\"Total\") FROM \"orders\""));
    }

    [Test]
    public void GroupBySingle()
    {
        var state = new QueryState(SqlDialectFactory.SQLite, "users", null)
            .WithSelect(["\"Status\"", "COUNT(*)"])
            .WithGroupBy("\"Status\"");
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT \"Status\", COUNT(*) FROM \"users\" GROUP BY \"Status\""));
    }

    [Test]
    public void GroupByMultiple()
    {
        var state = new QueryState(SqlDialectFactory.SQLite, "users", null)
            .WithSelect(["\"Status\"", "\"IsActive\"", "COUNT(*)"])
            .WithGroupBy("\"Status\"")
            .WithGroupBy("\"IsActive\"");
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT \"Status\", \"IsActive\", COUNT(*) FROM \"users\" GROUP BY \"Status\", \"IsActive\""));
    }

    [Test]
    public void Having()
    {
        var state = new QueryState(SqlDialectFactory.SQLite, "users", null)
            .WithSelect(["\"Status\"", "COUNT(*)"])
            .WithGroupBy("\"Status\"")
            .WithHaving("COUNT(*) > 5");
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT \"Status\", COUNT(*) FROM \"users\" GROUP BY \"Status\" HAVING COUNT(*) > 5"));
    }

    [Test]
    public void MultipleHaving()
    {
        var state = new QueryState(SqlDialectFactory.SQLite, "users", null)
            .WithSelect(["\"Status\"", "COUNT(*)"])
            .WithGroupBy("\"Status\"")
            .WithHaving("COUNT(*) > 5")
            .WithHaving("COUNT(*) < 100");
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT \"Status\", COUNT(*) FROM \"users\" GROUP BY \"Status\" HAVING (COUNT(*) > 5) AND (COUNT(*) < 100)"));
    }

    [Test]
    public void AggregateWithGroupByAndHaving()
    {
        var state = new QueryState(SqlDialectFactory.SQLite, "orders", null)
            .WithSelect(["\"Status\"", "COUNT(*)", "SUM(\"Total\")"])
            .WithGroupBy("\"Status\"")
            .WithHaving("SUM(\"Total\") > 1000");
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT \"Status\", COUNT(*), SUM(\"Total\") FROM \"orders\" GROUP BY \"Status\" HAVING SUM(\"Total\") > 1000"));
    }

    [Test]
    public void MixedAggregateAndColumn()
    {
        var state = new QueryState(SqlDialectFactory.SQLite, "users", null)
            .WithSelect(["\"Status\"", "COUNT(*)"]);
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT \"Status\", COUNT(*) FROM \"users\""));
    }

    [Test]
    public void GroupByWithWhere()
    {
        var state = new QueryState(SqlDialectFactory.SQLite, "users", null)
            .WithSelect(["\"Status\"", "COUNT(*)"])
            .WithWhere("\"IsActive\" = 1")
            .WithGroupBy("\"Status\"");
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT \"Status\", COUNT(*) FROM \"users\" WHERE \"IsActive\" = 1 GROUP BY \"Status\""));
    }
}
