using Quarry;
using Quarry.Internal;
using System.Collections.Immutable;

namespace Quarry.Tests;

/// <summary>
/// Tests for null handling in SQL generation.
/// Tests IS NULL, IS NOT NULL patterns and nullable column handling.
/// </summary>
[TestFixture]
public class NullHandlingTests
{
    private SqlDialect _dialect;

    [SetUp]
    public void Setup()
    {
        _dialect = SqlDialect.SQLite;
    }

    #region IS NULL Tests

    [Test]
    public void QueryState_IsNull_GeneratesIsNullClause()
    {
        var state = new QueryState(_dialect, "users", null, null)
            .WithSelect(ImmutableArray.Create("*"))
            .WithWhere("\"email\" IS NULL");

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("WHERE \"email\" IS NULL"));
    }

    [Test]
    public void QueryState_IsNotNull_GeneratesIsNotNullClause()
    {
        var state = new QueryState(_dialect, "users", null, null)
            .WithSelect(ImmutableArray.Create("*"))
            .WithWhere("\"last_login\" IS NOT NULL");

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("WHERE \"last_login\" IS NOT NULL"));
    }

    [Test]
    public void QueryState_MultipleNullChecks_CombinesWithAnd()
    {
        var state = new QueryState(_dialect, "users", null, null)
            .WithSelect(ImmutableArray.Create("*"))
            .WithWhere("\"email\" IS NOT NULL")
            .WithWhere("\"phone\" IS NOT NULL");

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("IS NOT NULL"));
        Assert.That(sql, Does.Contain("AND"));
    }

    #endregion

    #region Nullable Column Select Tests

    [Test]
    public void QueryState_NullableColumn_SelectsCorrectly()
    {
        var state = new QueryState(_dialect, "users", null, null)
            .WithSelect(ImmutableArray.Create("\"user_id\"", "\"email\"", "\"phone\""));

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("\"email\""));
        Assert.That(sql, Does.Contain("\"phone\""));
    }

    #endregion

    #region COALESCE Tests

    [Test]
    public void QueryState_Coalesce_GeneratesCoalesceSql()
    {
        var state = new QueryState(_dialect, "users", null, null)
            .WithSelect(ImmutableArray.Create("COALESCE(\"nickname\", \"user_name\") AS display_name"));

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("COALESCE(\"nickname\", \"user_name\")"));
    }

    [Test]
    public void QueryState_CoalesceMultipleValues_GeneratesCorrectSql()
    {
        var state = new QueryState(_dialect, "contacts", null, null)
            .WithSelect(ImmutableArray.Create("COALESCE(\"mobile\", \"home\", \"work\", 'N/A') AS phone"));

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("COALESCE(\"mobile\", \"home\", \"work\", 'N/A')"));
    }

    [Test]
    public void QueryState_CoalesceInWhere_GeneratesCorrectSql()
    {
        var state = new QueryState(_dialect, "users", null, null)
            .WithSelect(ImmutableArray.Create("*"))
            .WithWhere("COALESCE(\"nickname\", \"user_name\") = @p0");

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("WHERE COALESCE(\"nickname\", \"user_name\") = @p0"));
    }

    #endregion

    #region NULLIF Tests

    [Test]
    public void QueryState_NullIf_GeneratesNullIfSql()
    {
        var state = new QueryState(_dialect, "products", null, null)
            .WithSelect(ImmutableArray.Create("\"price\" / NULLIF(\"quantity\", 0) AS unit_price"));

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("NULLIF(\"quantity\", 0)"));
    }

    #endregion

    #region IFNULL / NVL Tests

    [Test]
    public void QueryState_IfNull_GeneratesIfNullSql()
    {
        // SQLite uses IFNULL, Oracle uses NVL, SQL Server uses ISNULL
        var state = new QueryState(_dialect, "users", null, null)
            .WithSelect(ImmutableArray.Create("IFNULL(\"middle_name\", '') AS middle_name"));

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("IFNULL(\"middle_name\", '')"));
    }

    #endregion

    #region Null in Comparison Tests

    [Test]
    public void QueryState_NullComparison_GeneratesCorrectSql()
    {
        // Direct comparison with NULL should use IS NULL, not = NULL
        var state = new QueryState(_dialect, "users", null, null)
            .WithSelect(ImmutableArray.Create("*"))
            .WithWhere("\"deleted_at\" IS NULL");

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("IS NULL"));
        Assert.That(sql, Does.Not.Contain("= NULL"));
    }

    [Test]
    public void QueryState_NullNotEqualComparison_GeneratesCorrectSql()
    {
        var state = new QueryState(_dialect, "users", null, null)
            .WithSelect(ImmutableArray.Create("*"))
            .WithWhere("\"verified_at\" IS NOT NULL");

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("IS NOT NULL"));
        Assert.That(sql, Does.Not.Contain("<> NULL"));
        Assert.That(sql, Does.Not.Contain("!= NULL"));
    }

    #endregion

    #region Null in OrderBy Tests

    [Test]
    public void QueryState_NullsFirst_GeneratesNullsFirstSql()
    {
        var state = new QueryState(_dialect, "users", null, null)
            .WithSelect(ImmutableArray.Create("*"))
            .WithOrderBy("\"last_login\" NULLS FIRST", Direction.Ascending);

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("NULLS FIRST"));
    }

    [Test]
    public void QueryState_NullsLast_GeneratesNullsLastSql()
    {
        var state = new QueryState(_dialect, "users", null, null)
            .WithSelect(ImmutableArray.Create("*"))
            .WithOrderBy("\"last_login\" NULLS LAST", Direction.Descending);

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("NULLS LAST"));
    }

    #endregion

    #region Null in GroupBy/Having Tests

    [Test]
    public void QueryState_GroupByNullable_GeneratesCorrectSql()
    {
        var state = new QueryState(_dialect, "orders", null, null)
            .WithSelect(ImmutableArray.Create("\"category\"", "COUNT(*)"))
            .WithGroupBy("\"category\"");

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("GROUP BY \"category\""));
    }

    [Test]
    public void QueryState_HavingWithNull_GeneratesCorrectSql()
    {
        var state = new QueryState(_dialect, "users", null, null)
            .WithSelect(ImmutableArray.Create("\"country\"", "COUNT(*)"))
            .WithGroupBy("\"country\"")
            .WithHaving("\"country\" IS NOT NULL");

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("HAVING \"country\" IS NOT NULL"));
    }

    #endregion

    #region Null in Join Tests

    [Test]
    public void QueryState_LeftJoinNullable_GeneratesCorrectSql()
    {
        var state = new QueryState(_dialect, "users", null, null)
            .WithSelect(ImmutableArray.Create("\"users\".\"user_name\"", "\"orders\".\"order_id\""))
            .WithJoin(new JoinClause(JoinKind.Left, "orders", null, null, "\"users\".\"user_id\" = \"orders\".\"user_id\""));

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("LEFT JOIN"));
    }

    [Test]
    public void QueryState_JoinWithNullCheck_GeneratesCorrectSql()
    {
        var state = new QueryState(_dialect, "users", null, null)
            .WithSelect(ImmutableArray.Create("*"))
            .WithJoin(new JoinClause(JoinKind.Left, "profiles", null, null, "\"users\".\"user_id\" = \"profiles\".\"user_id\""))
            .WithWhere("\"profiles\".\"user_id\" IS NULL");

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("LEFT JOIN"));
        Assert.That(sql, Does.Contain("IS NULL"));
    }

    #endregion

    #region All Dialects Tests

    [Test]
    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void AllDialects_IsNull_GeneratesCorrectSql(SqlDialect dialect)
    {
        var state = new QueryState(dialect, "users", null, null)
            .WithSelect(ImmutableArray.Create("*"))
            .WithWhere("email IS NULL");

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("IS NULL"));
    }

    [Test]
    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void AllDialects_IsNotNull_GeneratesCorrectSql(SqlDialect dialect)
    {
        var state = new QueryState(dialect, "users", null, null)
            .WithSelect(ImmutableArray.Create("*"))
            .WithWhere("email IS NOT NULL");

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("IS NOT NULL"));
    }

    [Test]
    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void AllDialects_Coalesce_GeneratesCorrectSql(SqlDialect dialect)
    {
        var state = new QueryState(dialect, "users", null, null)
            .WithSelect(ImmutableArray.Create("COALESCE(nickname, name) AS display"));

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("COALESCE(nickname, name)"));
    }

    #endregion
}
