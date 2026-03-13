using Quarry.Internal;

namespace Quarry.Tests.SqlOutput;

[TestFixture]
#pragma warning disable QRY001
public class NullHandlingTests
{
    [Test]
    public void IsNull()
    {
        var state = new QueryState(SqlDialectFactory.SQLite, "users", null)
            .WithWhere("\"Email\" IS NULL");
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT * FROM \"users\" WHERE \"Email\" IS NULL"));
    }

    [Test]
    public void IsNotNull()
    {
        var state = new QueryState(SqlDialectFactory.SQLite, "users", null)
            .WithWhere("\"Email\" IS NOT NULL");
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT * FROM \"users\" WHERE \"Email\" IS NOT NULL"));
    }

    [Test]
    public void NullableColumnSelect()
    {
        var state = new QueryState(SqlDialectFactory.SQLite, "users", null)
            .WithSelect(["\"Email\""]);
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT \"Email\" FROM \"users\""));
    }

    [Test]
    public void CoalesceExpression()
    {
        var state = new QueryState(SqlDialectFactory.SQLite, "users", null)
            .WithSelect(["COALESCE(\"Email\", 'N/A')"]);
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT COALESCE(\"Email\", 'N/A') FROM \"users\""));
    }

    [Test]
    public void NullCheckWithOtherConditions()
    {
        var state = new QueryState(SqlDialectFactory.SQLite, "users", null)
            .WithWhere("\"Email\" IS NULL")
            .WithWhere("\"IsActive\" = 1");
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT * FROM \"users\" WHERE (\"Email\" IS NULL) AND (\"IsActive\" = 1)"));
    }

    [Test]
    public void MultipleNullChecks()
    {
        var state = new QueryState(SqlDialectFactory.SQLite, "users", null)
            .WithWhere("\"Email\" IS NULL")
            .WithWhere("\"LastLogin\" IS NOT NULL");
        Assert.That(SqlBuilder.BuildSelectSql(state),
            Is.EqualTo("SELECT * FROM \"users\" WHERE (\"Email\" IS NULL) AND (\"LastLogin\" IS NOT NULL)"));
    }
}
