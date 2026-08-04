using Quarry.Generators.CodeGen;
using Quarry.Generators.Models;
using GenDialect = Quarry.Generators.Sql.SqlDialect;

namespace Quarry.Tests;

/// <summary>
/// Tests for compile-time SQL column resolution logic.
/// </summary>
[TestFixture]
public class RawSqlColumnResolverTests
{
    private static readonly RawSqlPropertyInfo[] SampleProperties = new[]
    {
        new RawSqlPropertyInfo("UserId", "int", "GetInt32", false),
        new RawSqlPropertyInfo("UserName", "string", "GetString", false),
        new RawSqlPropertyInfo("Email", "string", "GetString", true)
    };

    #region Success Cases

    [Test]
    public void Resolve_SimpleSelect_ReturnsCorrectOrdinals()
    {
        var result = RawSqlColumnResolver.Resolve(
            "SELECT UserId, UserName, Email FROM users",
            GenDialect.SQLite,
            SampleProperties);

        Assert.That(result.IsResolved, Is.True);
        Assert.That(result.Columns, Has.Count.EqualTo(3));
        Assert.That(result.Columns![0].PropertyName, Is.EqualTo("UserId"));
        Assert.That(result.Columns[0].Ordinal, Is.EqualTo(0));
        Assert.That(result.Columns![1].PropertyName, Is.EqualTo("UserName"));
        Assert.That(result.Columns[1].Ordinal, Is.EqualTo(1));
        Assert.That(result.Columns![2].PropertyName, Is.EqualTo("Email"));
        Assert.That(result.Columns[2].Ordinal, Is.EqualTo(2));
    }

    [Test]
    public void Resolve_WithAliases_MatchesAlias()
    {
        var result = RawSqlColumnResolver.Resolve(
            "SELECT id AS UserId, name AS UserName FROM users",
            GenDialect.SQLite,
            SampleProperties);

        Assert.That(result.IsResolved, Is.True);
        Assert.That(result.Columns, Has.Count.EqualTo(2));
        Assert.That(result.Columns![0].PropertyName, Is.EqualTo("UserId"));
        Assert.That(result.Columns[0].Ordinal, Is.EqualTo(0));
        Assert.That(result.Columns![1].PropertyName, Is.EqualTo("UserName"));
        Assert.That(result.Columns[1].Ordinal, Is.EqualTo(1));
    }

    [Test]
    public void Resolve_CaseInsensitive_MatchesColumns()
    {
        var result = RawSqlColumnResolver.Resolve(
            "SELECT userid, username, email FROM users",
            GenDialect.SQLite,
            SampleProperties);

        Assert.That(result.IsResolved, Is.True);
        Assert.That(result.Columns, Has.Count.EqualTo(3));
    }

    [Test]
    public void Resolve_PartialColumns_OnlyMatchedPropertiesReturned()
    {
        var result = RawSqlColumnResolver.Resolve(
            "SELECT UserId FROM users",
            GenDialect.SQLite,
            SampleProperties);

        Assert.That(result.IsResolved, Is.True);
        Assert.That(result.Columns, Has.Count.EqualTo(1));
        Assert.That(result.Columns![0].PropertyName, Is.EqualTo("UserId"));
        Assert.That(result.Columns[0].Ordinal, Is.EqualTo(0));
    }

    [Test]
    public void Resolve_QualifiedColumns_MatchesColumnName()
    {
        var result = RawSqlColumnResolver.Resolve(
            "SELECT u.UserId, u.UserName FROM users u",
            GenDialect.SQLite,
            SampleProperties);

        Assert.That(result.IsResolved, Is.True);
        Assert.That(result.Columns, Has.Count.EqualTo(2));
    }

    [TestCase(0)] // SQLite
    [TestCase(1)] // PostgreSQL
    [TestCase(2)] // MySQL
    [TestCase(3)] // SqlServer
    public void Resolve_AllDialects_WorkCorrectly(int dialectValue)
    {
        var dialect = (GenDialect)dialectValue;
        var result = RawSqlColumnResolver.Resolve(
            "SELECT UserId, UserName FROM users",
            dialect,
            SampleProperties);

        Assert.That(result.IsResolved, Is.True);
        Assert.That(result.Columns, Has.Count.EqualTo(2));
    }

    #endregion

    #region CTE Cases (#331)

    [Test]
    public void Resolve_Cte_ResolvesOuterSelectOrdinals()
    {
        // The headline win of #331. Before the parser understood WITH, this returned a
        // null AST, HasUnsupported was set, and the generator fell back to the runtime
        // ordinal IRowReader<T> path instead of emitting hardcoded ordinals.
        var result = RawSqlColumnResolver.Resolve(
            "WITH recent AS (SELECT UserId, UserName, Email FROM users WHERE IsActive = 1) " +
            "SELECT UserId, UserName, Email FROM recent",
            GenDialect.SQLite,
            SampleProperties);

        Assert.That(result.IsResolved, Is.True);
        Assert.That(result.Columns, Has.Count.EqualTo(3));
        Assert.That(result.Columns![0].PropertyName, Is.EqualTo("UserId"));
        Assert.That(result.Columns[0].Ordinal, Is.EqualTo(0));
        Assert.That(result.Columns[1].Ordinal, Is.EqualTo(1));
        Assert.That(result.Columns[2].Ordinal, Is.EqualTo(2));
    }

    [Test]
    public void Resolve_Cte_OrdinalsComeFromOuterSelectNotCteBody()
    {
        // The CTE body's column order must not leak into the result ordinals — only the
        // outer SELECT determines what the reader sees.
        var result = RawSqlColumnResolver.Resolve(
            "WITH recent AS (SELECT Email, UserName, UserId FROM users) " +
            "SELECT UserId, UserName FROM recent",
            GenDialect.SQLite,
            SampleProperties);

        Assert.That(result.IsResolved, Is.True);
        Assert.That(result.Columns, Has.Count.EqualTo(2));
        Assert.That(result.Columns![0].PropertyName, Is.EqualTo("UserId"));
        Assert.That(result.Columns[0].Ordinal, Is.EqualTo(0));
        Assert.That(result.Columns[1].PropertyName, Is.EqualTo("UserName"));
        Assert.That(result.Columns[1].Ordinal, Is.EqualTo(1));
    }

    [Test]
    public void Resolve_MultipleCtes_ResolvesOrdinals()
    {
        var result = RawSqlColumnResolver.Resolve(
            "WITH a AS (SELECT UserId FROM users), b AS (SELECT UserName FROM users) " +
            "SELECT UserId, UserName FROM a JOIN b ON 1 = 1",
            GenDialect.SQLite,
            SampleProperties);

        Assert.That(result.IsResolved, Is.True);
        Assert.That(result.Columns, Has.Count.EqualTo(2));
    }

    [Test]
    public void Resolve_RecursiveCte_ResolvesOrdinals()
    {
        // A recursive body is UNION-joined. It parses into a set-operation node as of
        // #331, so the query no longer degrades to the runtime-ordinal path.
        var result = RawSqlColumnResolver.Resolve(
            "WITH RECURSIVE tree AS (" +
            "SELECT UserId, UserName FROM users WHERE UserId = 1 " +
            "UNION ALL SELECT u.UserId, u.UserName FROM users u JOIN tree t ON u.UserId = t.UserId) " +
            "SELECT UserId, UserName FROM tree",
            GenDialect.SQLite,
            SampleProperties);

        Assert.That(result.IsResolved, Is.True);
        Assert.That(result.Columns, Has.Count.EqualTo(2));
        Assert.That(result.Columns![0].PropertyName, Is.EqualTo("UserId"));
        Assert.That(result.Columns[1].PropertyName, Is.EqualTo("UserName"));
    }

    [Test]
    public void Resolve_CteWithAliasedOuterColumns_UsesAliases()
    {
        var result = RawSqlColumnResolver.Resolve(
            "WITH recent AS (SELECT id, name FROM users) " +
            "SELECT id AS UserId, name AS UserName FROM recent",
            GenDialect.SQLite,
            SampleProperties);

        Assert.That(result.IsResolved, Is.True);
        Assert.That(result.Columns, Has.Count.EqualTo(2));
        Assert.That(result.Columns![0].PropertyName, Is.EqualTo("UserId"));
        Assert.That(result.Columns[1].PropertyName, Is.EqualTo("UserName"));
    }

    [Test]
    public void Resolve_CteWithSelectStarOuter_FallsBack()
    {
        // Parsing the CTE does not exempt the outer query from the SELECT * rule.
        var result = RawSqlColumnResolver.Resolve(
            "WITH recent AS (SELECT UserId FROM users) SELECT * FROM recent",
            GenDialect.SQLite,
            SampleProperties);

        Assert.That(result.IsResolved, Is.False);
        Assert.That(result.FallbackReason, Does.Contain("SELECT *"));
    }

    [Test]
    public void Resolve_CteWithUnaliasedExpressionOuter_FallsBackWithPosition()
    {
        // QRY041 still fires for a genuinely unresolvable outer column.
        var result = RawSqlColumnResolver.Resolve(
            "WITH recent AS (SELECT UserId, price, quantity FROM orders) " +
            "SELECT UserId, price * quantity FROM recent",
            GenDialect.SQLite,
            SampleProperties);

        Assert.That(result.IsResolved, Is.False);
        Assert.That(result.UnresolvableColumnPosition, Is.EqualTo(1));
    }

    [Test]
    public void Resolve_CteOnDmlStatement_FallsBack()
    {
        // Data-modifying CTEs remain unsupported (decision D6).
        var result = RawSqlColumnResolver.Resolve(
            "WITH recent AS (SELECT UserId FROM users) UPDATE users SET UserName = 'x'",
            GenDialect.SQLite,
            SampleProperties);

        Assert.That(result.IsResolved, Is.False);
    }

    #endregion

    #region Fallback Cases

    [Test]
    public void Resolve_SelectStar_FallsBack()
    {
        var result = RawSqlColumnResolver.Resolve(
            "SELECT * FROM users",
            GenDialect.SQLite,
            SampleProperties);

        Assert.That(result.IsResolved, Is.False);
        Assert.That(result.FallbackReason, Does.Contain("SELECT *"));
    }

    [Test]
    public void Resolve_TableStar_FallsBack()
    {
        var result = RawSqlColumnResolver.Resolve(
            "SELECT u.* FROM users u",
            GenDialect.SQLite,
            SampleProperties);

        Assert.That(result.IsResolved, Is.False);
        Assert.That(result.FallbackReason, Does.Contain("SELECT *"));
    }

    [Test]
    public void Resolve_UnresolvableExpression_FallsBackWithPosition()
    {
        var result = RawSqlColumnResolver.Resolve(
            "SELECT UserId, price * quantity FROM orders",
            GenDialect.SQLite,
            SampleProperties);

        Assert.That(result.IsResolved, Is.False);
        Assert.That(result.UnresolvableColumnPosition, Is.EqualTo(1));
        Assert.That(result.FallbackReason, Does.Contain("position 1"));
    }

    [Test]
    public void Resolve_FunctionWithoutAlias_FallsBack()
    {
        var result = RawSqlColumnResolver.Resolve(
            "SELECT COUNT(*) FROM users",
            GenDialect.SQLite,
            SampleProperties);

        Assert.That(result.IsResolved, Is.False);
        Assert.That(result.UnresolvableColumnPosition, Is.EqualTo(0));
    }

    [Test]
    public void Resolve_FunctionWithAlias_Succeeds()
    {
        var props = new[] { new RawSqlPropertyInfo("Total", "int", "GetInt32", false) };
        var result = RawSqlColumnResolver.Resolve(
            "SELECT COUNT(*) AS Total FROM users",
            GenDialect.SQLite,
            props);

        Assert.That(result.IsResolved, Is.True);
        Assert.That(result.Columns, Has.Count.EqualTo(1));
        Assert.That(result.Columns![0].PropertyName, Is.EqualTo("Total"));
        Assert.That(result.Columns[0].Ordinal, Is.EqualTo(0));
    }

    [Test]
    public void Resolve_InvalidSql_FallsBack()
    {
        var result = RawSqlColumnResolver.Resolve(
            "NOT VALID SQL AT ALL",
            GenDialect.SQLite,
            SampleProperties);

        Assert.That(result.IsResolved, Is.False);
        Assert.That(result.FallbackReason, Does.Contain("parse failed"));
    }

    [Test]
    public void Resolve_NoMatchingColumns_FallsBack()
    {
        var result = RawSqlColumnResolver.Resolve(
            "SELECT foo, bar FROM users",
            GenDialect.SQLite,
            SampleProperties);

        // Zero matched columns → fallback to struct reader
        Assert.That(result.IsResolved, Is.False);
        Assert.That(result.FallbackReason, Does.Contain("No SQL columns match"));
    }

    #endregion
}
