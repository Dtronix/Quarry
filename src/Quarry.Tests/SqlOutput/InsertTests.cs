using Quarry.Internal;
using Quarry.Shared.Sql;

namespace Quarry.Tests.SqlOutput;

[TestFixture]
#pragma warning disable QRY001
public class InsertTests
{
    [Test]
    public void BasicInsert_ColumnsOnly()
    {
        var state = new InsertState(SqlDialect.SQLite, "users", null, null);
        state.Columns.AddRange(["\"Name\"", "\"Email\""]);
        var sql = SqlModificationBuilder.BuildInsertSql(state, 0);
        Assert.That(sql, Is.EqualTo("INSERT INTO \"users\" (\"Name\", \"Email\")"));
    }

    [Test]
    public void SingleRow()
    {
        var state = new InsertState(SqlDialect.SQLite, "users", null, null);
        state.Columns.AddRange(["\"Name\""]);
        state.Rows.Add([0]);
        var sql = SqlModificationBuilder.BuildInsertSql(state, 1);
        Assert.That(sql, Is.EqualTo("INSERT INTO \"users\" (\"Name\") VALUES (@p0)"));
    }

    [Test]
    public void BatchInsert_TwoRows()
    {
        var state = new InsertState(SqlDialect.SQLite, "users", null, null);
        state.Columns.AddRange(["\"Name\""]);
        state.Rows.Add([0]);
        state.Rows.Add([1]);
        var sql = SqlModificationBuilder.BuildInsertSql(state, 2);
        Assert.That(sql, Is.EqualTo("INSERT INTO \"users\" (\"Name\") VALUES (@p0), (@p1)"));
    }

    [Test]
    public void BatchInsert_MultiColumn()
    {
        var state = new InsertState(SqlDialect.SQLite, "users", null, null);
        state.Columns.AddRange(["\"Name\"", "\"Email\""]);
        state.Rows.Add([0, 1]);
        state.Rows.Add([2, 3]);
        var sql = SqlModificationBuilder.BuildInsertSql(state, 2);
        Assert.That(sql, Is.EqualTo("INSERT INTO \"users\" (\"Name\", \"Email\") VALUES (@p0, @p1), (@p2, @p3)"));
    }

    [TestCase(SqlDialect.SQLite, "INSERT INTO \"users\" (\"Name\") VALUES (@p0) RETURNING \"UserId\"")]
    [TestCase(SqlDialect.PostgreSQL, "INSERT INTO \"users\" (\"Name\") VALUES ($1) RETURNING \"UserId\"")]
    public void WithIdentity_Returning(SqlDialect dialect, string expected)
    {
        var state = new InsertState(dialect, "users", null, null);
        state.Columns.AddRange([SqlFormatting.QuoteIdentifier(dialect, "Name")]);
        state.Rows.Add([0]);
        state.IdentityColumn = "UserId";
        var sql = SqlModificationBuilder.BuildInsertSql(state, 1);
        Assert.That(sql, Is.EqualTo(expected));
    }

    [Test]
    public void WithIdentity_OutputInserted_SqlServer()
    {
        var state = new InsertState(SqlDialect.SqlServer, "users", null, null);
        state.Columns.AddRange(["[Name]"]);
        state.Rows.Add([0]);
        state.IdentityColumn = "UserId";
        var sql = SqlModificationBuilder.BuildInsertSql(state, 1);
        Assert.That(sql, Is.EqualTo("INSERT INTO [users] ([Name]) VALUES (@p0) OUTPUT INSERTED.[UserId]"));
    }

    [Test]
    public void WithIdentity_MySQL_NoReturning()
    {
        var state = new InsertState(SqlDialect.MySQL, "users", null, null);
        state.Columns.AddRange(["`Name`"]);
        state.Rows.Add([0]);
        state.IdentityColumn = "UserId";
        var sql = SqlModificationBuilder.BuildInsertSql(state, 1);
        // MySQL returns null for FormatReturningClause, so no RETURNING appended
        Assert.That(sql, Is.EqualTo("INSERT INTO `users` (`Name`) VALUES (?)"));
    }

    [Test]
    public void SchemaQualified()
    {
        var state = new InsertState(SqlDialect.SQLite, "users", "public", null);
        state.Columns.AddRange(["\"Name\""]);
        state.Rows.Add([0]);
        var sql = SqlModificationBuilder.BuildInsertSql(state, 1);
        Assert.That(sql, Is.EqualTo("INSERT INTO \"public\".\"users\" (\"Name\") VALUES (@p0)"));
    }

    [TestCase(SqlDialect.SQLite, "VALUES (@p0, @p1)")]
    [TestCase(SqlDialect.PostgreSQL, "VALUES ($1, $2)")]
    [TestCase(SqlDialect.MySQL, "VALUES (?, ?)")]
    [TestCase(SqlDialect.SqlServer, "VALUES (@p0, @p1)")]
    public void AllDialects_ParameterFormat(SqlDialect dialect, string expectedValues)
    {
        var state = new InsertState(dialect, "users", null, null);
        state.Columns.AddRange([SqlFormatting.QuoteIdentifier(dialect, "Name"), SqlFormatting.QuoteIdentifier(dialect, "Email")]);
        state.Rows.Add([0, 1]);
        var sql = SqlModificationBuilder.BuildInsertSql(state, 1);
        Assert.That(sql, Does.Contain(expectedValues));
    }

    [Test]
    public void PreviewMode_NoRows_GeneratesPlaceholders()
    {
        var state = new InsertState(SqlDialect.SQLite, "users", null, null);
        state.Columns.AddRange(["\"Name\"", "\"Email\""]);
        // No rows added, but rowCount=1 triggers preview placeholder generation
        var sql = SqlModificationBuilder.BuildInsertSql(state, 1);
        Assert.That(sql, Is.EqualTo("INSERT INTO \"users\" (\"Name\", \"Email\") VALUES (@p0, @p1)"));
    }
}
