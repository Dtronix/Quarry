using Quarry.Internal;
using Quarry.Shared.Sql;

namespace Quarry.Tests.SqlOutput;

[TestFixture]
#pragma warning disable QRY001
public class JoinTests
{
    private static string Q(SqlDialect d, string id) => SqlFormatting.QuoteIdentifier(d, id);
    private static string T(SqlDialect d, string tbl, string? schema = null) => SqlFormatting.FormatTableName(d, tbl, schema);

    [Test]
    public void InnerJoin_TwoTables()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithJoin(new JoinClause(JoinKind.Inner, "orders", null, "t1", "t0.\"UserId\" = t1.\"UserId\""));
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Is.EqualTo(
            "SELECT * FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON t0.\"UserId\" = t1.\"UserId\""));
    }

    [Test]
    public void LeftJoin_TwoTables()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithJoin(new JoinClause(JoinKind.Left, "orders", null, "t1", "t0.\"UserId\" = t1.\"UserId\""));
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Is.EqualTo(
            "SELECT * FROM \"users\" AS \"t0\" LEFT JOIN \"orders\" AS \"t1\" ON t0.\"UserId\" = t1.\"UserId\""));
    }

    [Test]
    public void RightJoin_TwoTables()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithJoin(new JoinClause(JoinKind.Right, "orders", null, "t1", "t0.\"UserId\" = t1.\"UserId\""));
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Is.EqualTo(
            "SELECT * FROM \"users\" AS \"t0\" RIGHT JOIN \"orders\" AS \"t1\" ON t0.\"UserId\" = t1.\"UserId\""));
    }

    [Test]
    public void JoinAddsFromAlias()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithJoin(new JoinClause(JoinKind.Inner, "orders", null, "t1", "t0.\"UserId\" = t1.\"UserId\""));
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Does.Contain("FROM \"users\" AS \"t0\""));
    }

    [Test]
    public void ThreeTableChain()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithJoin(new JoinClause(JoinKind.Inner, "orders", null, "t1", "t0.\"UserId\" = t1.\"UserId\""))
            .WithJoin(new JoinClause(JoinKind.Inner, "order_items", null, "t2", "t1.\"OrderId\" = t2.\"OrderId\""));
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Is.EqualTo(
            "SELECT * FROM \"users\" AS \"t0\" " +
            "INNER JOIN \"orders\" AS \"t1\" ON t0.\"UserId\" = t1.\"UserId\" " +
            "INNER JOIN \"order_items\" AS \"t2\" ON t1.\"OrderId\" = t2.\"OrderId\""));
    }

    [Test]
    public void FourTableChain()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithJoin(new JoinClause(JoinKind.Inner, "orders", null, "t1", "t0.\"UserId\" = t1.\"UserId\""))
            .WithJoin(new JoinClause(JoinKind.Inner, "order_items", null, "t2", "t1.\"OrderId\" = t2.\"OrderId\""))
            .WithJoin(new JoinClause(JoinKind.Inner, "products", null, "t3", "t2.\"ProductId\" = t3.\"ProductId\""));
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Does.Contain("AS \"t0\""));
        Assert.That(sql, Does.Contain("AS \"t1\""));
        Assert.That(sql, Does.Contain("AS \"t2\""));
        Assert.That(sql, Does.Contain("AS \"t3\""));
    }

    [Test]
    public void JoinWithWhere()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithJoin(new JoinClause(JoinKind.Inner, "orders", null, "t1", "t0.\"UserId\" = t1.\"UserId\""))
            .WithWhere("t0.\"IsActive\" = 1");
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Does.Contain("WHERE t0.\"IsActive\" = 1"));
    }

    [Test]
    public void JoinWithOrderBy()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithJoin(new JoinClause(JoinKind.Inner, "orders", null, "t1", "t0.\"UserId\" = t1.\"UserId\""))
            .WithOrderBy("t0.\"UserName\"", Direction.Ascending);
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Does.Contain("ORDER BY t0.\"UserName\" ASC"));
    }

    [Test]
    public void JoinWithSelect()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithJoin(new JoinClause(JoinKind.Inner, "orders", null, "t1", "t0.\"UserId\" = t1.\"UserId\""))
            .WithSelect(["t0.\"UserName\"", "t1.\"Total\""]);
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Is.EqualTo(
            "SELECT t0.\"UserName\", t1.\"Total\" FROM \"users\" AS \"t0\" " +
            "INNER JOIN \"orders\" AS \"t1\" ON t0.\"UserId\" = t1.\"UserId\""));
    }

    [Test]
    public void JoinWithSchemaQualified()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", "public")
            .WithJoin(new JoinClause(JoinKind.Inner, "orders", "public", "t1", "t0.\"UserId\" = t1.\"UserId\""));
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Does.Contain("FROM \"public\".\"users\" AS \"t0\""));
        Assert.That(sql, Does.Contain("INNER JOIN \"public\".\"orders\" AS \"t1\""));
    }

    [Test]
    public void MixedJoinTypes()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithJoin(new JoinClause(JoinKind.Inner, "orders", null, "t1", "t0.\"UserId\" = t1.\"UserId\""))
            .WithJoin(new JoinClause(JoinKind.Left, "order_items", null, "t2", "t1.\"OrderId\" = t2.\"OrderId\""));
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Does.Contain("INNER JOIN \"orders\""));
        Assert.That(sql, Does.Contain("LEFT JOIN \"order_items\""));
    }

    [TestCase(SqlDialect.SQLite, "\"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\"")]
    [TestCase(SqlDialect.PostgreSQL, "\"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\"")]
    [TestCase(SqlDialect.MySQL, "`users` AS `t0` INNER JOIN `orders` AS `t1`")]
    [TestCase(SqlDialect.SqlServer, "[users] AS [t0] INNER JOIN [orders] AS [t1]")]
    public void AllDialects_JoinQuoting(SqlDialect dialect, string expectedFragment)
    {
        var q0 = Q(dialect, "UserId");
        var state = new QueryState(dialect, "users", null)
            .WithJoin(new JoinClause(JoinKind.Inner, "orders", null, "t1",
                $"{Q(dialect, "t0")}.{q0} = {Q(dialect, "t1")}.{q0}"));
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Does.Contain(expectedFragment));
    }
}
