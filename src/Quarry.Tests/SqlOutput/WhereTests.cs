using Quarry.Internal;
using Quarry.Shared.Sql;

namespace Quarry.Tests.SqlOutput;

[TestFixture]
public class WhereTests
{
    private static string Q(SqlDialect d, string id) => SqlFormatting.QuoteIdentifier(d, id);
    private static string P(SqlDialect d, int idx) => SqlFormatting.FormatParameter(d, idx);

    [Test]
    public void SingleEquality()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithWhere("\"UserId\" = @p0");
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Is.EqualTo("SELECT * FROM \"users\" WHERE \"UserId\" = @p0"));
    }

    [Test]
    public void MultipleConditions_JoinedWithAnd()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithWhere("\"UserId\" = @p0")
            .WithWhere("\"IsActive\" = 1");
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Is.EqualTo("SELECT * FROM \"users\" WHERE (\"UserId\" = @p0) AND (\"IsActive\" = 1)"));
    }

    [Test]
    public void ThreeConditions()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithWhere("\"UserId\" > @p0")
            .WithWhere("\"IsActive\" = 1")
            .WithWhere("\"Email\" IS NOT NULL");
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Is.EqualTo("SELECT * FROM \"users\" WHERE (\"UserId\" > @p0) AND (\"IsActive\" = 1) AND (\"Email\" IS NOT NULL)"));
    }

    [Test]
    public void NullCheck()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithWhere("\"Email\" IS NULL");
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Is.EqualTo("SELECT * FROM \"users\" WHERE \"Email\" IS NULL"));
    }

    [Test]
    public void NotNullCheck()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithWhere("\"Email\" IS NOT NULL");
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Is.EqualTo("SELECT * FROM \"users\" WHERE \"Email\" IS NOT NULL"));
    }

    [Test]
    public void InList()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithWhere("\"UserId\" IN (1, 2, 3)");
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Is.EqualTo("SELECT * FROM \"users\" WHERE \"UserId\" IN (1, 2, 3)"));
    }

    [Test]
    public void NotInList()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithWhere("\"UserId\" NOT IN (1, 2, 3)");
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Is.EqualTo("SELECT * FROM \"users\" WHERE \"UserId\" NOT IN (1, 2, 3)"));
    }

    [Test]
    public void LikeContains()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithWhere("\"UserName\" LIKE '%test%'");
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Is.EqualTo("SELECT * FROM \"users\" WHERE \"UserName\" LIKE '%test%'"));
    }

    [Test]
    public void LikeStartsWith()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithWhere("\"UserName\" LIKE 'test%'");
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Is.EqualTo("SELECT * FROM \"users\" WHERE \"UserName\" LIKE 'test%'"));
    }

    [Test]
    public void LikeEndsWith()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithWhere("\"UserName\" LIKE '%test'");
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Is.EqualTo("SELECT * FROM \"users\" WHERE \"UserName\" LIKE '%test'"));
    }

    [TestCase("<")]
    [TestCase(">")]
    [TestCase("<=")]
    [TestCase(">=")]
    [TestCase("!=")]
    public void ComparisonOperators(string op)
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithWhere($"\"UserId\" {op} @p0");
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Is.EqualTo($"SELECT * FROM \"users\" WHERE \"UserId\" {op} @p0"));
    }

    [TestCase(SqlDialect.SQLite, "@p0")]
    [TestCase(SqlDialect.PostgreSQL, "$1")]
    [TestCase(SqlDialect.MySQL, "?")]
    [TestCase(SqlDialect.SqlServer, "@p0")]
    public void ParameterPlaceholders_DialectSpecific(SqlDialect dialect, string param)
    {
        var state = new QueryState(dialect, "users", null)
            .WithWhere($"{Q(dialect, "UserId")} = {P(dialect, 0)}");
        var sql = SqlBuilder.BuildSelectSql(state);
        var expected = $"SELECT * FROM {SqlFormatting.FormatTableName(dialect, "users", null)} WHERE {Q(dialect, "UserId")} = {param}";
        Assert.That(sql, Is.EqualTo(expected));
    }

    [Test]
    public void WhereWithSelect()
    {
        var state = new QueryState(SqlDialect.SQLite, "users", null)
            .WithSelect(["\"UserName\"", "\"Email\""])
            .WithWhere("\"IsActive\" = 1");
        var sql = SqlBuilder.BuildSelectSql(state);
        Assert.That(sql, Is.EqualTo("SELECT \"UserName\", \"Email\" FROM \"users\" WHERE \"IsActive\" = 1"));
    }
}
