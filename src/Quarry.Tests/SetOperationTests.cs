using NUnit.Framework;
using Quarry;
using Quarry.Internal;
using System.Collections.Immutable;

namespace Quarry.Tests;

/// <summary>
/// Tests for set operations (UNION, UNION ALL, EXCEPT, INTERSECT).
/// </summary>
[TestFixture]
public class SetOperationTests
{
    #region SetOperationBuilder Tests

    [Test]
    public void SetOperationBuilder_ToSql_GeneratesUnionSql()
    {
        var dialect = SqlDialect.SQLite;
        var queries = ImmutableArray.Create(
            "SELECT \"user_id\", \"name\" FROM \"users\" WHERE \"active\" = 1",
            "SELECT \"user_id\", \"name\" FROM \"users\" WHERE \"created_at\" > '2024-01-01'"
        );

        var builder = new SetOperationBuilder<object>(
            queries,
            SetOperationKind.Union,
            dialect,
            null,
            null,
            ImmutableArray<QueryParameter>.Empty);

        var sql = builder.ToSql();

        Assert.That(sql, Does.Contain("UNION"));
        Assert.That(sql, Does.Contain("SELECT \"user_id\", \"name\" FROM \"users\" WHERE \"active\" = 1"));
        Assert.That(sql, Does.Contain("SELECT \"user_id\", \"name\" FROM \"users\" WHERE \"created_at\" > '2024-01-01'"));
    }

    [Test]
    public void SetOperationBuilder_ToSql_GeneratesUnionAllSql()
    {
        var dialect = SqlDialect.SQLite;
        var queries = ImmutableArray.Create(
            "SELECT \"id\" FROM \"table1\"",
            "SELECT \"id\" FROM \"table2\""
        );

        var builder = new SetOperationBuilder<object>(
            queries,
            SetOperationKind.UnionAll,
            dialect,
            null,
            null,
            ImmutableArray<QueryParameter>.Empty);

        var sql = builder.ToSql();

        Assert.That(sql, Does.Contain("UNION ALL"));
    }

    [Test]
    public void SetOperationBuilder_ToSql_GeneratesExceptSql()
    {
        var dialect = SqlDialect.SQLite;
        var queries = ImmutableArray.Create(
            "SELECT \"id\" FROM \"table1\"",
            "SELECT \"id\" FROM \"table2\""
        );

        var builder = new SetOperationBuilder<object>(
            queries,
            SetOperationKind.Except,
            dialect,
            null,
            null,
            ImmutableArray<QueryParameter>.Empty);

        var sql = builder.ToSql();

        Assert.That(sql, Does.Contain("EXCEPT"));
    }

    [Test]
    public void SetOperationBuilder_ToSql_GeneratesIntersectSql()
    {
        var dialect = SqlDialect.SQLite;
        var queries = ImmutableArray.Create(
            "SELECT \"id\" FROM \"table1\"",
            "SELECT \"id\" FROM \"table2\""
        );

        var builder = new SetOperationBuilder<object>(
            queries,
            SetOperationKind.Intersect,
            dialect,
            null,
            null,
            ImmutableArray<QueryParameter>.Empty);

        var sql = builder.ToSql();

        Assert.That(sql, Does.Contain("INTERSECT"));
    }

    [Test]
    public void SetOperationBuilder_WithLimit_AppendsLimitToSql()
    {
        var dialect = SqlDialect.SQLite;
        var queries = ImmutableArray.Create(
            "SELECT \"id\" FROM \"table1\"",
            "SELECT \"id\" FROM \"table2\""
        );

        var builder = new SetOperationBuilder<object>(
            queries,
            SetOperationKind.Union,
            dialect,
            null,
            null,
            ImmutableArray<QueryParameter>.Empty)
            .Limit(10);

        var sql = builder.ToSql();

        Assert.That(sql, Does.Contain("LIMIT 10"));
    }

    [Test]
    public void SetOperationBuilder_WithOffset_AppendsOffsetToSql()
    {
        var dialect = SqlDialect.SQLite;
        var queries = ImmutableArray.Create(
            "SELECT \"id\" FROM \"table1\"",
            "SELECT \"id\" FROM \"table2\""
        );

        var builder = new SetOperationBuilder<object>(
            queries,
            SetOperationKind.Union,
            dialect,
            null,
            null,
            ImmutableArray<QueryParameter>.Empty)
            .Offset(5)
            .Limit(10);

        var sql = builder.ToSql();

        Assert.That(sql, Does.Contain("OFFSET 5"));
    }

    [Test]
    public void SetOperationBuilder_Offset_ThrowsOnNegativeValue()
    {
        var dialect = SqlDialect.SQLite;
        var queries = ImmutableArray.Create("SELECT 1");

        var builder = new SetOperationBuilder<object>(
            queries,
            SetOperationKind.Union,
            dialect,
            null,
            null,
            ImmutableArray<QueryParameter>.Empty);

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Offset(-1));
    }

    [Test]
    public void SetOperationBuilder_Limit_ThrowsOnNegativeValue()
    {
        var dialect = SqlDialect.SQLite;
        var queries = ImmutableArray.Create("SELECT 1");

        var builder = new SetOperationBuilder<object>(
            queries,
            SetOperationKind.Union,
            dialect,
            null,
            null,
            ImmutableArray<QueryParameter>.Empty);

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Limit(-1));
    }

    [Test]
    public void SetOperationBuilder_WithMultipleQueries_JoinsWithOperator()
    {
        var dialect = SqlDialect.SQLite;
        var queries = ImmutableArray.Create(
            "SELECT 1",
            "SELECT 2",
            "SELECT 3"
        );

        var builder = new SetOperationBuilder<object>(
            queries,
            SetOperationKind.Union,
            dialect,
            null,
            null,
            ImmutableArray<QueryParameter>.Empty);

        var sql = builder.ToSql();

        // Should have UNION between each query
        var unionCount = sql.Split("UNION").Length - 1;
        Assert.That(unionCount, Is.EqualTo(2)); // 3 queries = 2 UNIONs
    }

    [Test]
    public void SetOperationBuilder_WrapsQueriesInParentheses_WhenDialectSupportsIt()
    {
        var dialect = SqlDialect.PostgreSQL;
        var queries = ImmutableArray.Create(
            "SELECT 1",
            "SELECT 2"
        );

        var builder = new SetOperationBuilder<object>(
            queries,
            SetOperationKind.Union,
            dialect,
            null,
            null,
            ImmutableArray<QueryParameter>.Empty);

        var sql = builder.ToSql();

        Assert.That(sql, Does.Contain("(SELECT 1)"));
        Assert.That(sql, Does.Contain("(SELECT 2)"));
    }

    [Test]
    public void SetOperationBuilder_OmitsParentheses_ForSQLite()
    {
        var dialect = SqlDialect.SQLite;
        var queries = ImmutableArray.Create(
            "SELECT 1",
            "SELECT 2"
        );

        var builder = new SetOperationBuilder<object>(
            queries,
            SetOperationKind.Union,
            dialect,
            null,
            null,
            ImmutableArray<QueryParameter>.Empty);

        var sql = builder.ToSql();

        Assert.That(sql, Is.EqualTo("SELECT 1 UNION SELECT 2"));
    }

    #endregion

    #region SetOperationKind Tests

    [Test]
    public void SetOperationKind_HasExpectedValues()
    {
        Assert.That(Enum.GetValues<SetOperationKind>(), Has.Length.EqualTo(4));
        Assert.That(Enum.IsDefined(SetOperationKind.Union), Is.True);
        Assert.That(Enum.IsDefined(SetOperationKind.UnionAll), Is.True);
        Assert.That(Enum.IsDefined(SetOperationKind.Except), Is.True);
        Assert.That(Enum.IsDefined(SetOperationKind.Intersect), Is.True);
    }

    #endregion

    #region SQL Server Specific Tests

    [Test]
    public void SetOperationBuilder_SqlServer_WithOffsetNoOrder_AddsOrderByNull()
    {
        var dialect = SqlDialect.SqlServer;
        var queries = ImmutableArray.Create(
            "SELECT \"id\" FROM \"table1\"",
            "SELECT \"id\" FROM \"table2\""
        );

        var builder = new SetOperationBuilder<object>(
            queries,
            SetOperationKind.Union,
            dialect,
            null,
            null,
            ImmutableArray<QueryParameter>.Empty)
            .Offset(5)
            .Limit(10);

        var sql = builder.ToSql();

        Assert.That(sql, Does.Contain("ORDER BY (SELECT NULL)"));
    }

    #endregion
}
