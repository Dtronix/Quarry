using Quarry.Internal;
using Quarry.Shared.Sql;

namespace Quarry.Tests.SqlOutput;

[TestFixture]
#pragma warning disable QRY001
public class CombinedClauseTests
{
    private static string Q(SqlDialect d, string id) => SqlFormatting.QuoteIdentifier(d, id);

    [Test]
    public void SelectWhereOrderByLimit()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithSelect(["\"UserName\"", "\"Email\""])
            .WithWhere("\"IsActive\" = 1")
            .WithOrderBy("\"UserName\"", Direction.Ascending)
            .WithLimit(10);
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT \"UserName\", \"Email\" FROM \"users\" WHERE \"IsActive\" = 1 ORDER BY \"UserName\" ASC LIMIT 10"));
    }

    [Test]
    public void SelectDistinctWhereOrderBy()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithDistinct()
            .WithSelect(["\"UserName\""])
            .WithWhere("\"IsActive\" = 1")
            .WithOrderBy("\"UserName\"", Direction.Ascending);
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT DISTINCT \"UserName\" FROM \"users\" WHERE \"IsActive\" = 1 ORDER BY \"UserName\" ASC"));
    }

    [Test]
    public void JoinWhereOrderByLimit()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithJoin(new JoinClause(JoinKind.Inner, "orders", null, "t1", "t0.\"UserId\" = t1.\"UserId\""))
            .WithWhere("t0.\"IsActive\" = 1")
            .WithOrderBy("t1.\"Total\"", Direction.Descending)
            .WithLimit(10);
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT * FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON t0.\"UserId\" = t1.\"UserId\" WHERE t0.\"IsActive\" = 1 ORDER BY t1.\"Total\" DESC LIMIT 10"));
    }

    [Test]
    public void JoinSelectWhereGroupByHaving()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithJoin(new JoinClause(JoinKind.Inner, "orders", null, "t1", "t0.\"UserId\" = t1.\"UserId\""))
            .WithSelect(["t0.\"UserName\"", "COUNT(*)"])
            .WithWhere("t0.\"IsActive\" = 1")
            .WithGroupBy("t0.\"UserName\"")
            .WithHaving("COUNT(*) > 5");
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT t0.\"UserName\", COUNT(*) FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON t0.\"UserId\" = t1.\"UserId\" WHERE t0.\"IsActive\" = 1 GROUP BY t0.\"UserName\" HAVING COUNT(*) > 5"));
    }

    [Test]
    public void SelectGroupByHavingOrderByLimit()
    {
        var state = new QueryState(SqlDialect.SQLite, "orders", null)
            .WithSelect(["\"Status\"", "COUNT(*)", "SUM(\"Total\")"])
            .WithGroupBy("\"Status\"")
            .WithHaving("COUNT(*) > 5")
            .WithOrderBy("\"Status\"", Direction.Ascending)
            .WithLimit(10);
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT \"Status\", COUNT(*), SUM(\"Total\") FROM \"orders\" GROUP BY \"Status\" HAVING COUNT(*) > 5 ORDER BY \"Status\" ASC LIMIT 10"));
    }

    [Test]
    public void ThreeTableJoinWhereOrderBy()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithJoin(new JoinClause(JoinKind.Inner, "orders", null, "t1", "t0.\"UserId\" = t1.\"UserId\""))
            .WithJoin(new JoinClause(JoinKind.Inner, "order_items", null, "t2", "t1.\"OrderId\" = t2.\"OrderId\""))
            .WithWhere("t0.\"IsActive\" = 1")
            .WithOrderBy("t2.\"UnitPrice\"", Direction.Descending);
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo(
                "SELECT * FROM \"users\" AS \"t0\" " +
                "INNER JOIN \"orders\" AS \"t1\" ON t0.\"UserId\" = t1.\"UserId\" " +
                "INNER JOIN \"order_items\" AS \"t2\" ON t1.\"OrderId\" = t2.\"OrderId\" " +
                "WHERE t0.\"IsActive\" = 1 " +
                "ORDER BY t2.\"UnitPrice\" DESC"));
    }

    [Test]
    public void FourTableJoinSelectWhere()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithJoin(new JoinClause(JoinKind.Inner, "orders", null, "t1", "t0.\"UserId\" = t1.\"UserId\""))
            .WithJoin(new JoinClause(JoinKind.Inner, "order_items", null, "t2", "t1.\"OrderId\" = t2.\"OrderId\""))
            .WithJoin(new JoinClause(JoinKind.Inner, "products", null, "t3", "t2.\"ProductId\" = t3.\"ProductId\""))
            .WithSelect(["t0.\"UserName\"", "t3.\"ProductName\""])
            .WithWhere("t1.\"Status\" = @p0");
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Does.StartWith("SELECT t0.\"UserName\", t3.\"ProductName\" FROM \"users\" AS \"t0\""));
        Assert.That(sql, Does.Contain("INNER JOIN \"products\" AS \"t3\""));
        Assert.That(sql, Does.EndWith("WHERE t1.\"Status\" = @p0"));
    }

    [Test]
    public void AllClausesCombined_SqlServer()
    {
        var state = new QueryState(SqlDialect.SqlServer, "users", null)
            .WithSelect(["[UserName]", "[Email]"])
            .WithWhere("[IsActive] = 1")
            .WithOrderBy("[UserName]", Direction.Ascending)
            .WithLimit(10)
            .WithOffset(20);
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT [UserName], [Email] FROM [users] WHERE [IsActive] = 1 ORDER BY [UserName] ASC OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY"));
    }

    [Test]
    public void AllClausesCombined_LimitOffset()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithSelect(["\"UserName\"", "\"Email\""])
            .WithWhere("\"IsActive\" = 1")
            .WithOrderBy("\"UserName\"", Direction.Ascending)
            .WithLimit(10)
            .WithOffset(20);
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT \"UserName\", \"Email\" FROM \"users\" WHERE \"IsActive\" = 1 ORDER BY \"UserName\" ASC LIMIT 10 OFFSET 20"));
    }

    [Test]
    public void SubqueryInJoinedQuery()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithJoin(new JoinClause(JoinKind.Inner, "orders", null, "t1", "t0.\"UserId\" = t1.\"UserId\""))
            .WithWhere("t1.\"Total\" > (SELECT AVG(\"Total\") FROM \"orders\")");
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Does.Contain("WHERE t1.\"Total\" > (SELECT AVG(\"Total\") FROM \"orders\")"));
    }

    [Test]
    public void MultipleWheresWithJoinAndPagination()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithJoin(new JoinClause(JoinKind.Inner, "orders", null, "t1", "t0.\"UserId\" = t1.\"UserId\""))
            .WithWhere("t0.\"IsActive\" = 1")
            .WithWhere("t1.\"Total\" > @p0")
            .WithLimit(10)
            .WithOffset(5);
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Does.Contain("WHERE (t0.\"IsActive\" = 1) AND (t1.\"Total\" > @p0)"));
        Assert.That(sql, Does.EndWith("LIMIT 10 OFFSET 5"));
    }
}
