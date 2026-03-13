using Quarry.Internal;

namespace Quarry.Tests.SqlOutput;

[TestFixture]
#pragma warning disable QRY001
public class SchemaQualificationTests
{
    [Test]
    public void Select_WithSchema()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", "public");
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT * FROM \"public\".\"users\""));
    }

    [Test]
    public void Insert_WithSchema()
    {
        var state = new InsertState(SqlDialect.SQLite, "users", "public", null);
        state.Columns.AddRange(["\"Name\""]);
        state.Rows.Add([0]);
        Assert.That(SqlModificationBuilder.BuildInsertSql(state, 1),
            Is.EqualTo("INSERT INTO \"public\".\"users\" (\"Name\") VALUES (@p0)"));
    }

    [Test]
    public void Update_WithSchema()
    {
        var state = new UpdateState(SqlDialect.SQLite, "users", "public", null);
        state.SetClauses.Add(new SetClause("\"Name\"", 0));
        Assert.That(SqlModificationBuilder.BuildUpdateSql(state),
            Is.EqualTo("UPDATE \"public\".\"users\" SET \"Name\" = @p0"));
    }

    [Test]
    public void Delete_WithSchema()
    {
        var state = new DeleteState(SqlDialect.SQLite, "users", "public", null);
        Assert.That(SqlModificationBuilder.BuildDeleteSql(state),
            Is.EqualTo("DELETE FROM \"public\".\"users\""));
    }

    [Test]
    public void Join_WithSchema()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", "public")
            .WithJoin(new JoinClause(JoinKind.Inner, "orders", "public", "t1", "t0.\"UserId\" = t1.\"UserId\""));
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Does.Contain("FROM \"public\".\"users\" AS \"t0\""));
        Assert.That(sql, Does.Contain("INNER JOIN \"public\".\"orders\" AS \"t1\""));
    }

    [TestCase(SqlDialect.SQLite, "\"public\".\"users\"")]
    [TestCase(SqlDialect.PostgreSQL, "\"public\".\"users\"")]
    [TestCase(SqlDialect.MySQL, "`public`.`users`")]
    [TestCase(SqlDialect.SqlServer, "[public].[users]")]
    public void AllDialects_SchemaQuoting(SqlDialect dialect, string expected)
    {
        var state = new QueryState(dialect, "users", "public");
        Assert.That(SqlBuilder.BuildSelectSql(state), Is.EqualTo($"SELECT * FROM {expected}"));
    }
}
