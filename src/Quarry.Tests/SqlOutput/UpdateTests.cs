using Quarry.Internal;
using Quarry.Shared.Sql;

namespace Quarry.Tests.SqlOutput;

[TestFixture]
public class UpdateTests
{
    [Test]
    public void SingleSet()
    {
        var state = new UpdateState(SqlDialect.SQLite, "users", null, null);
        state.SetClauses.Add(new SetClause("\"Name\"", 0));
        var sql = SqlModificationBuilder.BuildUpdateSql(state);
        Assert.That(sql, Is.EqualTo("UPDATE \"users\" SET \"Name\" = @p0"));
    }

    [Test]
    public void MultipleSet()
    {
        var state = new UpdateState(SqlDialect.SQLite, "users", null, null);
        state.SetClauses.Add(new SetClause("\"Name\"", 0));
        state.SetClauses.Add(new SetClause("\"Email\"", 1));
        var sql = SqlModificationBuilder.BuildUpdateSql(state);
        Assert.That(sql, Is.EqualTo("UPDATE \"users\" SET \"Name\" = @p0, \"Email\" = @p1"));
    }

    [Test]
    public void WithWhere()
    {
        var state = new UpdateState(SqlDialect.SQLite, "users", null, null);
        state.SetClauses.Add(new SetClause("\"Name\"", 0));
        state.WhereConditions.Add("\"UserId\" = @p1");
        var sql = SqlModificationBuilder.BuildUpdateSql(state);
        Assert.That(sql, Is.EqualTo("UPDATE \"users\" SET \"Name\" = @p0 WHERE \"UserId\" = @p1"));
    }

    [Test]
    public void MultipleWhere_NoParens()
    {
        var state = new UpdateState(SqlDialect.SQLite, "users", null, null);
        state.SetClauses.Add(new SetClause("\"Name\"", 0));
        state.WhereConditions.Add("\"UserId\" = @p1");
        state.WhereConditions.Add("\"IsActive\" = 1");
        var sql = SqlModificationBuilder.BuildUpdateSql(state);
        Assert.That(sql, Is.EqualTo("UPDATE \"users\" SET \"Name\" = @p0 WHERE \"UserId\" = @p1 AND \"IsActive\" = 1"));
    }

    [Test]
    public void All_NoWhere()
    {
        var state = new UpdateState(SqlDialect.SQLite, "users", null, null);
        state.SetClauses.Add(new SetClause("\"Name\"", 0));
        state.AllowAll = true;
        var sql = SqlModificationBuilder.BuildUpdateSql(state);
        Assert.That(sql, Is.EqualTo("UPDATE \"users\" SET \"Name\" = @p0"));
    }

    [Test]
    public void SchemaQualified()
    {
        var state = new UpdateState(SqlDialect.SQLite, "users", "public", null);
        state.SetClauses.Add(new SetClause("\"Name\"", 0));
        var sql = SqlModificationBuilder.BuildUpdateSql(state);
        Assert.That(sql, Is.EqualTo("UPDATE \"public\".\"users\" SET \"Name\" = @p0"));
    }

    [TestCase(SqlDialect.SQLite, "@p0", "@p1")]
    [TestCase(SqlDialect.PostgreSQL, "$1", "$2")]
    [TestCase(SqlDialect.MySQL, "?", "?")]
    [TestCase(SqlDialect.SqlServer, "@p0", "@p1")]
    public void AllDialects_ParameterFormat(SqlDialect dialect, string setParam, string whereParam)
    {
        var state = new UpdateState(dialect, "users", null, null);
        state.SetClauses.Add(new SetClause(SqlFormatting.QuoteIdentifier(dialect, "Name"), 0));
        state.WhereConditions.Add($"{SqlFormatting.QuoteIdentifier(dialect, "UserId")} = {SqlFormatting.FormatParameter(dialect, 1)}");
        var sql = SqlModificationBuilder.BuildUpdateSql(state);
        Assert.That(sql, Does.Contain($"SET {SqlFormatting.QuoteIdentifier(dialect, "Name")} = {setParam}"));
        Assert.That(sql, Does.Contain($"WHERE {SqlFormatting.QuoteIdentifier(dialect, "UserId")} = {whereParam}"));
    }
}
