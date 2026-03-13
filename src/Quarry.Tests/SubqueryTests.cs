using Quarry;
using Quarry.Internal;
using System.Collections.Immutable;

namespace Quarry.Tests;

/// <summary>
/// Tests for subquery SQL generation.
/// Tests IN subqueries, EXISTS subqueries, and scalar subqueries.
/// </summary>
[TestFixture]
public class SubqueryTests
{
    private SqlDialect _dialect;

    [SetUp]
    public void Setup()
    {
        _dialect = SqlDialect.SQLite;
    }

    #region IN Subquery Tests

    [Test]
    public void QueryState_InSubquery_GeneratesInWithSubselect()
    {
        // Simulating: WHERE user_id IN (SELECT user_id FROM active_users)
        var subquery = "SELECT \"user_id\" FROM \"active_users\"";
        var whereClause = $"\"user_id\" IN ({subquery})";

        var state = new QueryState(_dialect, "orders", null, null)
            .WithSelect(ImmutableArray.Create("*"))
            .WithWhere(whereClause);

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("WHERE \"user_id\" IN (SELECT \"user_id\" FROM \"active_users\")"));
    }

    [Test]
    public void QueryState_NotInSubquery_GeneratesNotInWithSubselect()
    {
        // Simulating: WHERE user_id NOT IN (SELECT user_id FROM blacklisted_users)
        var subquery = "SELECT \"user_id\" FROM \"blacklisted_users\"";
        var whereClause = $"\"user_id\" NOT IN ({subquery})";

        var state = new QueryState(_dialect, "users", null, null)
            .WithSelect(ImmutableArray.Create("*"))
            .WithWhere(whereClause);

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("NOT IN"));
        Assert.That(sql, Does.Contain("SELECT \"user_id\" FROM \"blacklisted_users\""));
    }

    [Test]
    public void QueryState_InSubqueryWithConditions_GeneratesComplexSubselect()
    {
        // Simulating: WHERE user_id IN (SELECT user_id FROM orders WHERE total > 100 GROUP BY user_id HAVING COUNT(*) > 5)
        var subquery = "SELECT \"user_id\" FROM \"orders\" WHERE \"total\" > 100 GROUP BY \"user_id\" HAVING COUNT(*) > 5";
        var whereClause = $"\"user_id\" IN ({subquery})";

        var state = new QueryState(_dialect, "users", null, null)
            .WithSelect(ImmutableArray.Create("*"))
            .WithWhere(whereClause);

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("IN (SELECT"));
        Assert.That(sql, Does.Contain("GROUP BY"));
        Assert.That(sql, Does.Contain("HAVING"));
    }

    #endregion

    #region EXISTS Subquery Tests

    [Test]
    public void QueryState_ExistsSubquery_GeneratesExistsClause()
    {
        // Simulating: WHERE EXISTS (SELECT 1 FROM orders WHERE orders.user_id = users.user_id)
        var subquery = "SELECT 1 FROM \"orders\" WHERE \"orders\".\"user_id\" = \"users\".\"user_id\"";
        var whereClause = $"EXISTS ({subquery})";

        var state = new QueryState(_dialect, "users", null, null)
            .WithSelect(ImmutableArray.Create("*"))
            .WithWhere(whereClause);

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("WHERE EXISTS (SELECT 1"));
        Assert.That(sql, Does.Contain("\"orders\".\"user_id\" = \"users\".\"user_id\""));
    }

    [Test]
    public void QueryState_NotExistsSubquery_GeneratesNotExistsClause()
    {
        // Simulating: WHERE NOT EXISTS (SELECT 1 FROM blacklist WHERE blacklist.user_id = users.user_id)
        var subquery = "SELECT 1 FROM \"blacklist\" WHERE \"blacklist\".\"user_id\" = \"users\".\"user_id\"";
        var whereClause = $"NOT EXISTS ({subquery})";

        var state = new QueryState(_dialect, "users", null, null)
            .WithSelect(ImmutableArray.Create("*"))
            .WithWhere(whereClause);

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("WHERE NOT EXISTS"));
    }

    #endregion

    #region Scalar Subquery Tests

    [Test]
    public void QueryState_ScalarSubqueryInSelect_GeneratesSubselectColumn()
    {
        // Simulating: SELECT user_name, (SELECT COUNT(*) FROM orders WHERE orders.user_id = users.user_id) AS order_count
        var scalarSubquery = "(SELECT COUNT(*) FROM \"orders\" WHERE \"orders\".\"user_id\" = \"users\".\"user_id\") AS order_count";

        var state = new QueryState(_dialect, "users", null, null)
            .WithSelect(ImmutableArray.Create("\"user_name\"", scalarSubquery));

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("SELECT \"user_name\", (SELECT COUNT(*) FROM \"orders\""));
        Assert.That(sql, Does.Contain("AS order_count"));
    }

    [Test]
    public void QueryState_ScalarSubqueryInWhere_GeneratesComparison()
    {
        // Simulating: WHERE balance > (SELECT AVG(balance) FROM users)
        var subquery = "(SELECT AVG(\"balance\") FROM \"users\")";
        var whereClause = $"\"balance\" > {subquery}";

        var state = new QueryState(_dialect, "users", null, null)
            .WithSelect(ImmutableArray.Create("*"))
            .WithWhere(whereClause);

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("WHERE \"balance\" > (SELECT AVG(\"balance\") FROM \"users\")"));
    }

    [Test]
    public void QueryState_MultipleScalarSubqueries_GeneratesAllSubselects()
    {
        // Simulating: SELECT (SELECT MIN(total) FROM orders) AS min_order, (SELECT MAX(total) FROM orders) AS max_order
        var minSubquery = "(SELECT MIN(\"total\") FROM \"orders\") AS min_order";
        var maxSubquery = "(SELECT MAX(\"total\") FROM \"orders\") AS max_order";

        var state = new QueryState(_dialect, "dual", null, null)
            .WithSelect(ImmutableArray.Create(minSubquery, maxSubquery));

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("SELECT MIN(\"total\")"));
        Assert.That(sql, Does.Contain("SELECT MAX(\"total\")"));
        Assert.That(sql, Does.Contain("AS min_order"));
        Assert.That(sql, Does.Contain("AS max_order"));
    }

    #endregion

    #region Correlated Subquery Tests

    [Test]
    public void QueryState_CorrelatedSubquery_ReferencesOuterTable()
    {
        // Simulating: WHERE total > (SELECT AVG(total) FROM orders o2 WHERE o2.user_id = orders.user_id)
        var correlatedSubquery = "(SELECT AVG(\"total\") FROM \"orders\" AS \"o2\" WHERE \"o2\".\"user_id\" = \"orders\".\"user_id\")";
        var whereClause = $"\"total\" > {correlatedSubquery}";

        var state = new QueryState(_dialect, "orders", null, null)
            .WithSelect(ImmutableArray.Create("*"))
            .WithWhere(whereClause);

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("FROM \"orders\""));
        Assert.That(sql, Does.Contain("\"o2\".\"user_id\" = \"orders\".\"user_id\""));
    }

    #endregion

    #region Nested Subquery Tests

    [Test]
    public void QueryState_NestedSubqueries_GeneratesMultipleLevels()
    {
        // Simulating: WHERE user_id IN (SELECT user_id FROM orders WHERE total > (SELECT AVG(total) FROM orders))
        var innerSubquery = "SELECT AVG(\"total\") FROM \"orders\"";
        var outerSubquery = $"SELECT \"user_id\" FROM \"orders\" WHERE \"total\" > ({innerSubquery})";
        var whereClause = $"\"user_id\" IN ({outerSubquery})";

        var state = new QueryState(_dialect, "users", null, null)
            .WithSelect(ImmutableArray.Create("*"))
            .WithWhere(whereClause);

        var sql = SqlBuilder.BuildSelectSql(state);

        // Verify both levels of subquery are present
        Assert.That(sql, Does.Contain("IN (SELECT \"user_id\" FROM \"orders\""));
        Assert.That(sql, Does.Contain("(SELECT AVG(\"total\") FROM \"orders\")"));
    }

    #endregion

    #region Subquery with Join Tests

    [Test]
    public void QueryState_SubqueryWithJoin_GeneratesJoinedSubselect()
    {
        // Simulating: WHERE user_id IN (SELECT u.user_id FROM users u JOIN orders o ON u.user_id = o.user_id WHERE o.total > 100)
        var subquery = "SELECT \"u\".\"user_id\" FROM \"users\" AS \"u\" INNER JOIN \"orders\" AS \"o\" ON \"u\".\"user_id\" = \"o\".\"user_id\" WHERE \"o\".\"total\" > 100";
        var whereClause = $"\"user_id\" IN ({subquery})";

        var state = new QueryState(_dialect, "notifications", null, null)
            .WithSelect(ImmutableArray.Create("*"))
            .WithWhere(whereClause);

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("INNER JOIN"));
        Assert.That(sql, Does.Contain("ON \"u\".\"user_id\" = \"o\".\"user_id\""));
    }

    #endregion

    #region Subquery in HAVING Tests

    [Test]
    public void QueryState_SubqueryInHaving_GeneratesHavingWithSubselect()
    {
        // Simulating: HAVING SUM(total) > (SELECT AVG(total_spent) FROM user_stats)
        var subquery = "(SELECT AVG(\"total_spent\") FROM \"user_stats\")";
        var havingClause = $"SUM(\"total\") > {subquery}";

        var state = new QueryState(_dialect, "orders", null, null)
            .WithSelect(ImmutableArray.Create("\"user_id\"", "SUM(\"total\")"))
            .WithGroupBy("\"user_id\"")
            .WithHaving(havingClause);

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("HAVING SUM(\"total\") > (SELECT AVG(\"total_spent\")"));
    }

    #endregion

    #region All Dialects Tests

    [Test]
    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void QueryState_AllDialects_SubqueryInWhereWorks(SqlDialect dialect)
    {
        var subquery = "SELECT id FROM other_table";
        var whereClause = $"id IN ({subquery})";

        var state = new QueryState(dialect, "my_table", null, null)
            .WithSelect(ImmutableArray.Create("*"))
            .WithWhere(whereClause);

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("IN (SELECT id FROM other_table)"));
    }

    [Test]
    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void QueryState_AllDialects_ExistsSubqueryWorks(SqlDialect dialect)
    {
        var subquery = "SELECT 1 FROM related WHERE related.id = main.id";
        var whereClause = $"EXISTS ({subquery})";

        var state = new QueryState(dialect, "main", null, null)
            .WithSelect(ImmutableArray.Create("*"))
            .WithWhere(whereClause);

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("EXISTS (SELECT 1"));
    }

    #endregion
}
