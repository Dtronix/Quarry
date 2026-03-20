using Quarry.Internal;
using Quarry.Shared.Sql;

namespace Quarry.Tests.SqlOutput;

[TestFixture]
public class StringOperationTests
{
    [Test]
    public void Concat_PipeOperator_SQLite()
    {
        var d = SqlDialectFactory.SQLite;
        var result = SqlFormatting.FormatStringConcat(d, "\"Name\"", "' '", "\"Email\"");
        Assert.That(result, Is.EqualTo("\"Name\" || ' ' || \"Email\""));
    }

    [Test]
    public void Concat_PipeOperator_PostgreSQL()
    {
        var d = SqlDialectFactory.PostgreSQL;
        var result = SqlFormatting.FormatStringConcat(d, "\"Name\"", "' '", "\"Email\"");
        Assert.That(result, Is.EqualTo("\"Name\" || ' ' || \"Email\""));
    }

    [Test]
    public void Concat_ConcatFunction_MySQL()
    {
        var d = SqlDialectFactory.MySQL;
        var result = SqlFormatting.FormatStringConcat(d, "`Name`", "' '", "`Email`");
        Assert.That(result, Is.EqualTo("CONCAT(`Name`, ' ', `Email`)"));
    }

    [Test]
    public void Concat_PlusOperator_SqlServer()
    {
        var d = SqlDialectFactory.SqlServer;
        var result = SqlFormatting.FormatStringConcat(d, "[Name]", "' '", "[Email]");
        Assert.That(result, Is.EqualTo("[Name] + ' ' + [Email]"));
    }

    [Test]
    public void ConcatSingleOperand()
    {
        Assert.That(SqlFormatting.FormatStringConcat(SqlDialectFactory.SQLite, "\"Name\""), Is.EqualTo("\"Name\""));
        Assert.That(SqlFormatting.FormatStringConcat(SqlDialectFactory.MySQL, "`Name`"), Is.EqualTo("`Name`"));
        Assert.That(SqlFormatting.FormatStringConcat(SqlDialectFactory.SqlServer, "[Name]"), Is.EqualTo("[Name]"));
    }

    [Test]
    public void LikeContains()
    {
        var state = new QueryState(SqlDialectFactory.SQLite, "users", null)
            .WithWhere("\"UserName\" LIKE '%test%'");
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT * FROM \"users\" WHERE \"UserName\" LIKE '%test%'"));
    }

    [Test]
    public void LikeStartsWith()
    {
        var state = new QueryState(SqlDialectFactory.SQLite, "users", null)
            .WithWhere("\"UserName\" LIKE 'test%'");
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT * FROM \"users\" WHERE \"UserName\" LIKE 'test%'"));
    }

    [Test]
    public void LikeEndsWith()
    {
        var state = new QueryState(SqlDialectFactory.SQLite, "users", null)
            .WithWhere("\"UserName\" LIKE '%test'");
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT * FROM \"users\" WHERE \"UserName\" LIKE '%test'"));
    }

    [Test]
    public void Lower()
    {
        var state = new QueryState(SqlDialectFactory.SQLite, "users", null)
            .WithWhere("LOWER(\"UserName\") = @p0");
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT * FROM \"users\" WHERE LOWER(\"UserName\") = @p0"));
    }

    [Test]
    public void Upper()
    {
        var state = new QueryState(SqlDialectFactory.SQLite, "users", null)
            .WithWhere("UPPER(\"UserName\") = @p0");
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT * FROM \"users\" WHERE UPPER(\"UserName\") = @p0"));
    }

    [Test]
    public void Trim()
    {
        var state = new QueryState(SqlDialectFactory.SQLite, "users", null)
            .WithWhere("TRIM(\"UserName\") = @p0");
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT * FROM \"users\" WHERE TRIM(\"UserName\") = @p0"));
    }
}
