using Quarry.Internal;

namespace Quarry.Tests.SqlOutput;

[TestFixture]
public class PaginationTests
{
    [TestCase(SqlDialect.SQLite, "SELECT * FROM \"users\" LIMIT 10")]
    [TestCase(SqlDialect.PostgreSQL, "SELECT * FROM \"users\" LIMIT 10")]
    [TestCase(SqlDialect.MySQL, "SELECT * FROM `users` LIMIT 10")]
    public void LimitOnly_LimitOffset(SqlDialect dialect, string expected)
    {
        var state = new QueryState(dialect, "users", null).WithLimit(10);
        Assert.That(SqlBuilder.BuildSelectSql(state), Is.EqualTo(expected));
    }

    [Test]
    public void LimitOnly_SqlServer()
    {
        var state = new QueryState(SqlDialect.SqlServer, "users", null).WithLimit(10);
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT * FROM [users] ORDER BY (SELECT NULL) OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY"));
    }

    [TestCase(SqlDialect.SQLite, "SELECT * FROM \"users\" OFFSET 20")]
    [TestCase(SqlDialect.PostgreSQL, "SELECT * FROM \"users\" OFFSET 20")]
    [TestCase(SqlDialect.MySQL, "SELECT * FROM `users` OFFSET 20")]
    public void OffsetOnly_LimitOffset(SqlDialect dialect, string expected)
    {
        var state = new QueryState(dialect, "users", null).WithOffset(20);
        Assert.That(SqlBuilder.BuildSelectSql(state), Is.EqualTo(expected));
    }

    [Test]
    public void OffsetOnly_SqlServer()
    {
        var state = new QueryState(SqlDialect.SqlServer, "users", null).WithOffset(20);
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT * FROM [users] ORDER BY (SELECT NULL) OFFSET 20 ROWS"));
    }

    [TestCase(SqlDialect.SQLite, "SELECT * FROM \"users\" LIMIT 10 OFFSET 20")]
    [TestCase(SqlDialect.PostgreSQL, "SELECT * FROM \"users\" LIMIT 10 OFFSET 20")]
    [TestCase(SqlDialect.MySQL, "SELECT * FROM `users` LIMIT 10 OFFSET 20")]
    public void LimitAndOffset_LimitOffset(SqlDialect dialect, string expected)
    {
        var state = new QueryState(dialect, "users", null).WithLimit(10).WithOffset(20);
        Assert.That(SqlBuilder.BuildSelectSql(state), Is.EqualTo(expected));
    }

    [Test]
    public void LimitAndOffset_SqlServer()
    {
        var state = new QueryState(SqlDialect.SqlServer, "users", null).WithLimit(10).WithOffset(20);
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT * FROM [users] ORDER BY (SELECT NULL) OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY"));
    }

    [Test]
    public void LimitWithOrderBy_SqlServer_NoSelectNullInjected()
    {
        var state = new QueryState(SqlDialect.SqlServer, "users", null)
            .WithOrderBy("[UserName]", Direction.Ascending)
            .WithLimit(10);
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT * FROM [users] ORDER BY [UserName] ASC OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY"));
    }

    [TestCase(SqlDialect.SQLite, "SELECT * FROM \"users\" LIMIT 10")]
    [TestCase(SqlDialect.PostgreSQL, "SELECT * FROM \"users\" LIMIT 10")]
    [TestCase(SqlDialect.MySQL, "SELECT * FROM `users` LIMIT 10")]
    public void ZeroOffset_Omitted_LimitOffset(SqlDialect dialect, string expected)
    {
        var state = new QueryState(dialect, "users", null).WithLimit(10).WithOffset(0);
        Assert.That(SqlBuilder.BuildSelectSql(state), Is.EqualTo(expected));
    }

    [Test]
    public void ZeroOffset_SqlServer_StillEmitted()
    {
        var state = new QueryState(SqlDialect.SqlServer, "users", null).WithLimit(10).WithOffset(0);
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT * FROM [users] ORDER BY (SELECT NULL) OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY"));
    }

    [Test]
    public void OffsetAndOrderBy_SqlServer()
    {
        var state = new QueryState(SqlDialect.SqlServer, "users", null)
            .WithOrderBy("[UserName]", Direction.Ascending)
            .WithOffset(20);
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT * FROM [users] ORDER BY [UserName] ASC OFFSET 20 ROWS"));
    }
}
