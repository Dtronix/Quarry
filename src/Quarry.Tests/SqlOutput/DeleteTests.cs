using Quarry.Internal;
using Quarry.Shared.Sql;

namespace Quarry.Tests.SqlOutput;

[TestFixture]
public class DeleteTests
{
    [Test]
    public void BasicDelete_NoWhere()
    {
        var state = new DeleteState(SqlDialect.SQLite, "users", null, null);
        var sql = SqlModificationBuilder.BuildDeleteSql(state);
        Assert.That(sql, Is.EqualTo("DELETE FROM \"users\""));
    }

    [Test]
    public void WithWhere()
    {
        var state = new DeleteState(SqlDialect.SQLite, "users", null, null);
        state.WhereConditions.Add("\"UserId\" = @p0");
        var sql = SqlModificationBuilder.BuildDeleteSql(state);
        Assert.That(sql, Is.EqualTo("DELETE FROM \"users\" WHERE \"UserId\" = @p0"));
    }

    [Test]
    public void MultipleWhere()
    {
        var state = new DeleteState(SqlDialect.SQLite, "users", null, null);
        state.WhereConditions.Add("\"UserId\" = @p0");
        state.WhereConditions.Add("\"IsActive\" = 0");
        var sql = SqlModificationBuilder.BuildDeleteSql(state);
        Assert.That(sql, Is.EqualTo("DELETE FROM \"users\" WHERE \"UserId\" = @p0 AND \"IsActive\" = 0"));
    }

    [Test]
    public void All()
    {
        var state = new DeleteState(SqlDialect.SQLite, "users", null, null);
        state.AllowAll = true;
        var sql = SqlModificationBuilder.BuildDeleteSql(state);
        Assert.That(sql, Is.EqualTo("DELETE FROM \"users\""));
    }

    [Test]
    public void SchemaQualified()
    {
        var state = new DeleteState(SqlDialect.SQLite, "users", "public", null);
        var sql = SqlModificationBuilder.BuildDeleteSql(state);
        Assert.That(sql, Is.EqualTo("DELETE FROM \"public\".\"users\""));
    }

    [TestCase(SqlDialect.SQLite, "DELETE FROM \"users\"")]
    [TestCase(SqlDialect.PostgreSQL, "DELETE FROM \"users\"")]
    [TestCase(SqlDialect.MySQL, "DELETE FROM `users`")]
    [TestCase(SqlDialect.SqlServer, "DELETE FROM [users]")]
    public void AllDialects_TableQuoting(SqlDialect dialect, string expected)
    {
        var state = new DeleteState(dialect, "users", null, null);
        var sql = SqlModificationBuilder.BuildDeleteSql(state);
        Assert.That(sql, Is.EqualTo(expected));
    }

    [TestCase(SqlDialect.SQLite, "@p0")]
    [TestCase(SqlDialect.PostgreSQL, "$1")]
    [TestCase(SqlDialect.MySQL, "?")]
    [TestCase(SqlDialect.SqlServer, "@p0")]
    public void AllDialects_WhereParameterFormat(SqlDialect dialect, string param)
    {
        var state = new DeleteState(dialect, "users", null, null);
        state.WhereConditions.Add($"{SqlFormatting.QuoteIdentifier(dialect, "UserId")} = {SqlFormatting.FormatParameter(dialect, 0)}");
        var sql = SqlModificationBuilder.BuildDeleteSql(state);
        Assert.That(sql, Does.Contain($"WHERE {SqlFormatting.QuoteIdentifier(dialect, "UserId")} = {param}"));
    }
}
