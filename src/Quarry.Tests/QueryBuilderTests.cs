namespace Quarry.Tests;

/// <summary>
/// Unit tests for QueryBuilder.
/// These tests construct QueryBuilder directly (not via a QuarryContext),
/// so the generator correctly cannot analyze them.
/// </summary>
[TestFixture]
public class QueryBuilderTests
{
    #region Basic Query Building

    [Test]
    public void QueryBuilder_ToSql_GeneratesBasicSelectStar()
    {
        var builder = new QueryBuilder<TestEntity>(SqlDialectFactory.PostgreSQL, "users", null);

        var sql = builder.ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo("SELECT * FROM \"users\""));
    }

    [Test]
    public void QueryBuilder_ToSql_GeneratesSelectStarWithSchema()
    {
        var builder = new QueryBuilder<TestEntity>(SqlDialectFactory.PostgreSQL, "users", "public");

        var sql = builder.ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo("SELECT * FROM \"public\".\"users\""));
    }

    [Test]
    public void QueryBuilder_ToSql_UsesDialectSpecificQuoting_MySQL()
    {
        var builder = new QueryBuilder<TestEntity>(SqlDialectFactory.MySQL, "users", null);

        var sql = builder.ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo("SELECT * FROM `users`"));
    }

    [Test]
    public void QueryBuilder_ToSql_UsesDialectSpecificQuoting_SqlServer()
    {
        var builder = new QueryBuilder<TestEntity>(SqlDialectFactory.SqlServer, "users", null);

        var sql = builder.ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo("SELECT * FROM [users]"));
    }

    [Test]
    public void QueryBuilder_ToSql_UsesDialectSpecificQuoting_SQLite()
    {
        var builder = new QueryBuilder<TestEntity>(SqlDialectFactory.SQLite, "users", null);

        var sql = builder.ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo("SELECT * FROM \"users\""));
    }

    #endregion

    #region Immutability Tests

    [Test]
    public void QueryBuilder_Offset_ReturnsNewInstance()
    {
        var original = new QueryBuilder<TestEntity>(SqlDialectFactory.PostgreSQL, "users", null);

        var modified = original.Offset(10);

        Assert.That(modified, Is.Not.SameAs(original));
    }

    [Test]
    public void QueryBuilder_Limit_ReturnsNewInstance()
    {
        var original = new QueryBuilder<TestEntity>(SqlDialectFactory.PostgreSQL, "users", null);

        var modified = original.Limit(20);

        Assert.That(modified, Is.Not.SameAs(original));
    }

    [Test]
    public void QueryBuilder_Distinct_ReturnsNewInstance()
    {
        var original = new QueryBuilder<TestEntity>(SqlDialectFactory.PostgreSQL, "users", null);

        var modified = original.Distinct();

        Assert.That(modified, Is.Not.SameAs(original));
    }

    [Test]
    public void QueryBuilder_OriginalNotModifiedAfterOffset()
    {
        var original = new QueryBuilder<TestEntity>(SqlDialectFactory.PostgreSQL, "users", null);
        var originalSql = original.ToDiagnostics().Sql;

        var _ = original.Offset(10);

        Assert.That(original.ToDiagnostics().Sql, Is.EqualTo(originalSql));
    }

    [Test]
    public void QueryBuilder_OriginalNotModifiedAfterLimit()
    {
        var original = new QueryBuilder<TestEntity>(SqlDialectFactory.PostgreSQL, "users", null);
        var originalSql = original.ToDiagnostics().Sql;

        var _ = original.Limit(20);

        Assert.That(original.ToDiagnostics().Sql, Is.EqualTo(originalSql));
    }

    #endregion

    #region Pagination Tests

    [Test]
    public void QueryBuilder_Limit_GeneratesLimitClause()
    {
        var builder = new QueryBuilder<TestEntity>(SqlDialectFactory.PostgreSQL, "users", null);

        var sql = builder.Limit(10).ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo("SELECT * FROM \"users\" LIMIT 10"));
    }

    [Test]
    public void QueryBuilder_Offset_GeneratesOffsetClause()
    {
        var builder = new QueryBuilder<TestEntity>(SqlDialectFactory.PostgreSQL, "users", null);

        var sql = builder.Offset(20).ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo("SELECT * FROM \"users\" OFFSET 20"));
    }

    [Test]
    public void QueryBuilder_OffsetAndLimit_GeneratesBothClauses()
    {
        var builder = new QueryBuilder<TestEntity>(SqlDialectFactory.PostgreSQL, "users", null);

        var sql = builder.Offset(20).Limit(10).ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo("SELECT * FROM \"users\" LIMIT 10 OFFSET 20"));
    }

    [Test]
    public void QueryBuilder_Pagination_SqlServer_UsesFetchSyntax()
    {
        var builder = new QueryBuilder<TestEntity>(SqlDialectFactory.SqlServer, "users", null);

        var sql = builder.Offset(20).Limit(10).ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo("SELECT * FROM [users] ORDER BY (SELECT NULL) OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY"));
    }

    [Test]
    public void QueryBuilder_Offset_ThrowsOnNegative()
    {
        var builder = new QueryBuilder<TestEntity>(SqlDialectFactory.PostgreSQL, "users", null);

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Offset(-1));
    }

    [Test]
    public void QueryBuilder_Limit_ThrowsOnNegative()
    {
        var builder = new QueryBuilder<TestEntity>(SqlDialectFactory.PostgreSQL, "users", null);

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Limit(-1));
    }

    [Test]
    public void QueryBuilder_Offset_ZeroIsValid()
    {
        var builder = new QueryBuilder<TestEntity>(SqlDialectFactory.PostgreSQL, "users", null);

        var sql = builder.Offset(0).ToDiagnostics().Sql;

        // OFFSET 0 is effectively no offset, but we still allow it
        Assert.That(sql, Is.EqualTo("SELECT * FROM \"users\""));
    }

    #endregion

    #region Distinct Tests

    [Test]
    public void QueryBuilder_Distinct_GeneratesDistinctClause()
    {
        var builder = new QueryBuilder<TestEntity>(SqlDialectFactory.PostgreSQL, "users", null);

        var sql = builder.Distinct().ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo("SELECT DISTINCT * FROM \"users\""));
    }

    [Test]
    public void QueryBuilder_Distinct_WithPagination()
    {
        var builder = new QueryBuilder<TestEntity>(SqlDialectFactory.PostgreSQL, "users", null);

        var sql = builder.Distinct().Limit(10).ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo("SELECT DISTINCT * FROM \"users\" LIMIT 10"));
    }

    #endregion

    #region Chaining Tests

    [Test]
    public void QueryBuilder_SupportsMethodChaining()
    {
        var builder = new QueryBuilder<TestEntity>(SqlDialectFactory.PostgreSQL, "users", null);

        var sql = builder
            .Distinct()
            .Offset(10)
            .Limit(20)
            .ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo("SELECT DISTINCT * FROM \"users\" LIMIT 20 OFFSET 10"));
    }

    [Test]
    public void QueryBuilder_Select_ReturnsTypedBuilder()
    {
        var builder = new QueryBuilder<TestEntity>(SqlDialectFactory.PostgreSQL, "users", null);

        var typedBuilder = builder.Select(e => e);

        Assert.That(typedBuilder, Is.Not.Null);
    }

    [Test]
    public void QueryBuilder_Select_WithAnonymousType()
    {
        var builder = new QueryBuilder<TestEntity>(SqlDialectFactory.PostgreSQL, "users", null);

        var typedBuilder = builder.Select(e => new { e.Id, e.Name });

        Assert.That(typedBuilder, Is.Not.Null);
    }

    #endregion

    #region QueryBuilder<TEntity, TResult> Tests

    [Test]
    public void QueryBuilderWithResult_Where_ReturnsNewInstance()
    {
        var builder = new QueryBuilder<TestEntity>(SqlDialectFactory.PostgreSQL, "users", null);
        var projected = builder.Select(e => e);

        var filtered = projected.Where(e => e.Id > 0);

        Assert.That(filtered, Is.Not.SameAs(projected));
    }

    [Test]
    public void QueryBuilderWithResult_Offset_Works()
    {
        var builder = new QueryBuilder<TestEntity>(SqlDialectFactory.PostgreSQL, "users", null);

        var sql = builder.Select(e => e).Offset(10).ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("OFFSET 10"));
    }

    [Test]
    public void QueryBuilderWithResult_Limit_Works()
    {
        var builder = new QueryBuilder<TestEntity>(SqlDialectFactory.PostgreSQL, "users", null);

        var sql = builder.Select(e => e).Limit(20).ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("LIMIT 20"));
    }

    [Test]
    public void QueryBuilderWithResult_Distinct_Works()
    {
        var builder = new QueryBuilder<TestEntity>(SqlDialectFactory.PostgreSQL, "users", null);

        var sql = builder.Select(e => e).Distinct().ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("DISTINCT"));
    }

    [Test]
    public void QueryBuilderWithResult_Chaining()
    {
        var builder = new QueryBuilder<TestEntity>(SqlDialectFactory.PostgreSQL, "users", null);

        var sql = builder
            .Select(e => e)
            .Distinct()
            .Offset(5)
            .Limit(10)
            .ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo("SELECT DISTINCT * FROM \"users\" LIMIT 10 OFFSET 5"));
    }

    #endregion

    #region Direction Enum Tests

    [Test]
    public void Direction_HasAscendingValue()
    {
        Assert.That((int)Direction.Ascending, Is.EqualTo(0));
    }

    [Test]
    public void Direction_HasDescendingValue()
    {
        Assert.That((int)Direction.Descending, Is.EqualTo(1));
    }

    #endregion

    #region Dialect-Specific Pagination Tests

    [Test]
    [TestCase("SQLite", "SELECT * FROM \"users\" LIMIT 10 OFFSET 20")]
    [TestCase("PostgreSQL", "SELECT * FROM \"users\" LIMIT 10 OFFSET 20")]
    [TestCase("MySQL", "SELECT * FROM `users` LIMIT 10 OFFSET 20")]
    public void QueryBuilder_Pagination_DialectSpecific(string dialectName, string expected)
    {
        var dialect = dialectName switch
        {
            "SQLite" => SqlDialectFactory.SQLite,
            "PostgreSQL" => SqlDialectFactory.PostgreSQL,
            "MySQL" => SqlDialectFactory.MySQL,
            _ => throw new ArgumentException("Unknown dialect", nameof(dialectName))
        };

        var builder = new QueryBuilder<TestEntity>(dialect, "users", null);

        var sql = builder.Offset(20).Limit(10).ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo(expected));
    }

    #endregion

    #region Timeout Tests

    [Test]
    public void QueryBuilder_WithTimeout_ReturnsNewInstance()
    {
        var original = new QueryBuilder<TestEntity>(SqlDialectFactory.PostgreSQL, "users", null);

        var modified = original.WithTimeout(TimeSpan.FromSeconds(30));

        Assert.That(modified, Is.Not.SameAs(original));
    }

    [Test]
    public void QueryBuilder_WithTimeout_ThrowsOnZero()
    {
        var builder = new QueryBuilder<TestEntity>(SqlDialectFactory.PostgreSQL, "users", null);

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithTimeout(TimeSpan.Zero));
    }

    [Test]
    public void QueryBuilder_WithTimeout_ThrowsOnNegative()
    {
        var builder = new QueryBuilder<TestEntity>(SqlDialectFactory.PostgreSQL, "users", null);

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithTimeout(TimeSpan.FromSeconds(-1)));
    }

    [Test]
    public void QueryBuilderWithResult_WithTimeout_ReturnsNewInstance()
    {
        var builder = new QueryBuilder<TestEntity>(SqlDialectFactory.PostgreSQL, "users", null);
        var original = builder.Select(e => e);

        var modified = original.WithTimeout(TimeSpan.FromSeconds(30));

        Assert.That(modified, Is.Not.SameAs(original));
    }

    [Test]
    public void QueryBuilderWithResult_WithTimeout_ThrowsOnZero()
    {
        var builder = new QueryBuilder<TestEntity>(SqlDialectFactory.PostgreSQL, "users", null);
        var projected = builder.Select(e => e);

        Assert.Throws<ArgumentOutOfRangeException>(() => projected.WithTimeout(TimeSpan.Zero));
    }

    [Test]
    public void QueryBuilderWithResult_WithTimeout_ThrowsOnNegative()
    {
        var builder = new QueryBuilder<TestEntity>(SqlDialectFactory.PostgreSQL, "users", null);
        var projected = builder.Select(e => e);

        Assert.Throws<ArgumentOutOfRangeException>(() => projected.WithTimeout(TimeSpan.FromSeconds(-1)));
    }

    #endregion

    // Test entity class for testing
    public class TestEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
    }
}
