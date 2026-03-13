using NUnit.Framework;
using Quarry;
using Quarry.Internal;

namespace Quarry.Tests;

/// <summary>
/// Tests for aggregate functions, GROUP BY, HAVING, and related features.
/// </summary>
[TestFixture]
public class AggregateAndGroupingTests
{
    #region SQL Static Class Tests

    [Test]
    public void SqlCount_WithNoArguments_ThrowsAtRuntime()
    {
        // Sql.Count() should throw when invoked at runtime
        // because it's meant to be translated at compile-time
        Assert.Throws<InvalidOperationException>(() => Sql.Count());
    }

    [Test]
    public void SqlCount_WithArgument_ThrowsAtRuntime()
    {
        Assert.Throws<InvalidOperationException>(() => Sql.Count(42));
    }

    [Test]
    public void SqlSum_WithIntArgument_ThrowsAtRuntime()
    {
        Assert.Throws<InvalidOperationException>(() => Sql.Sum(42));
    }

    [Test]
    public void SqlSum_WithLongArgument_ThrowsAtRuntime()
    {
        Assert.Throws<InvalidOperationException>(() => Sql.Sum(42L));
    }

    [Test]
    public void SqlSum_WithDecimalArgument_ThrowsAtRuntime()
    {
        Assert.Throws<InvalidOperationException>(() => Sql.Sum(42.5m));
    }

    [Test]
    public void SqlSum_WithDoubleArgument_ThrowsAtRuntime()
    {
        Assert.Throws<InvalidOperationException>(() => Sql.Sum(42.5));
    }

    [Test]
    public void SqlAvg_WithIntArgument_ThrowsAtRuntime()
    {
        Assert.Throws<InvalidOperationException>(() => Sql.Avg(42));
    }

    [Test]
    public void SqlAvg_WithDecimalArgument_ThrowsAtRuntime()
    {
        Assert.Throws<InvalidOperationException>(() => Sql.Avg(42.5m));
    }

    [Test]
    public void SqlMin_ThrowsAtRuntime()
    {
        Assert.Throws<InvalidOperationException>(() => Sql.Min(42));
    }

    [Test]
    public void SqlMax_ThrowsAtRuntime()
    {
        Assert.Throws<InvalidOperationException>(() => Sql.Max(42));
    }

    [Test]
    public void SqlRaw_ThrowsAtRuntime()
    {
        Assert.Throws<InvalidOperationException>(() => Sql.Raw<int>("SELECT 1"));
    }

    [Test]
    public void SqlExists_ThrowsAtRuntime()
    {
        Assert.Throws<InvalidOperationException>(() => Sql.Exists(Array.Empty<int>()));
    }

    #endregion

    #region QueryState GROUP BY Tests

    [Test]
    public void QueryState_WithGroupBy_AddsGroupByColumn()
    {
        var dialect = SqlDialect.SQLite;
        var state = new QueryState(dialect, "orders", null, null);

        var newState = state.WithGroupBy("\"user_id\"");

        Assert.That(newState.GroupByColumns.Length, Is.EqualTo(1));
        Assert.That(newState.GroupByColumns[0], Is.EqualTo("\"user_id\""));
    }

    [Test]
    public void QueryState_WithMultipleGroupBy_AddsAllColumns()
    {
        var dialect = SqlDialect.SQLite;
        var state = new QueryState(dialect, "orders", null, null);

        var newState = state
            .WithGroupBy("\"user_id\"")
            .WithGroupBy("\"status\"");

        Assert.That(newState.GroupByColumns.Length, Is.EqualTo(2));
        Assert.That(newState.GroupByColumns[0], Is.EqualTo("\"user_id\""));
        Assert.That(newState.GroupByColumns[1], Is.EqualTo("\"status\""));
    }

    [Test]
    public void QueryState_WithGroupByFragment_AddsFragment()
    {
        var dialect = SqlDialect.SQLite;
        var state = new QueryState(dialect, "orders", null, null);

        var newState = state.WithGroupByFragment("\"user_id\", \"status\"");

        Assert.That(newState.GroupByColumns.Length, Is.EqualTo(1));
        Assert.That(newState.GroupByColumns[0], Is.EqualTo("\"user_id\", \"status\""));
    }

    #endregion

    #region QueryState HAVING Tests

    [Test]
    public void QueryState_WithHaving_AddsHavingCondition()
    {
        var dialect = SqlDialect.SQLite;
        var state = new QueryState(dialect, "orders", null, null);

        var newState = state.WithHaving("COUNT(*) > 5");

        Assert.That(newState.HavingConditions.Length, Is.EqualTo(1));
        Assert.That(newState.HavingConditions[0], Is.EqualTo("COUNT(*) > 5"));
    }

    [Test]
    public void QueryState_WithMultipleHaving_CombinesWithAnd()
    {
        var dialect = SqlDialect.SQLite;
        var state = new QueryState(dialect, "orders", null, null);

        var newState = state
            .WithHaving("COUNT(*) > 5")
            .WithHaving("SUM(\"total\") > 1000");

        Assert.That(newState.HavingConditions.Length, Is.EqualTo(2));
        Assert.That(newState.HavingConditions[0], Is.EqualTo("COUNT(*) > 5"));
        Assert.That(newState.HavingConditions[1], Is.EqualTo("SUM(\"total\") > 1000"));
    }

    #endregion

    #region SqlBuilder GROUP BY and HAVING Tests

    [Test]
    public void SqlBuilder_WithGroupBy_GeneratesGroupByClause()
    {
        var dialect = SqlDialect.SQLite;
        var state = new QueryState(dialect, "orders", null, null)
            .WithSelect(System.Collections.Immutable.ImmutableArray.Create("\"user_id\"", "COUNT(*)"))
            .WithGroupBy("\"user_id\"");

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("GROUP BY \"user_id\""));
    }

    [Test]
    public void SqlBuilder_WithGroupByAndHaving_GeneratesBothClauses()
    {
        var dialect = SqlDialect.SQLite;
        var state = new QueryState(dialect, "orders", null, null)
            .WithSelect(System.Collections.Immutable.ImmutableArray.Create("\"user_id\"", "COUNT(*)"))
            .WithGroupBy("\"user_id\"")
            .WithHaving("COUNT(*) > 5");

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("GROUP BY \"user_id\""));
        Assert.That(sql, Does.Contain("HAVING COUNT(*) > 5"));
    }

    [Test]
    public void SqlBuilder_WithMultipleGroupByColumns_JoinsWithCommas()
    {
        var dialect = SqlDialect.SQLite;
        var state = new QueryState(dialect, "orders", null, null)
            .WithSelect(System.Collections.Immutable.ImmutableArray.Create("\"user_id\"", "\"status\"", "COUNT(*)"))
            .WithGroupBy("\"user_id\"")
            .WithGroupBy("\"status\"");

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("GROUP BY \"user_id\", \"status\""));
    }

    [Test]
    public void SqlBuilder_WithMultipleHavingConditions_CombinesWithAnd()
    {
        var dialect = SqlDialect.SQLite;
        var state = new QueryState(dialect, "orders", null, null)
            .WithSelect(System.Collections.Immutable.ImmutableArray.Create("\"user_id\"", "COUNT(*)"))
            .WithGroupBy("\"user_id\"")
            .WithHaving("COUNT(*) > 5")
            .WithHaving("SUM(\"total\") > 1000");

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("HAVING (COUNT(*) > 5) AND (SUM(\"total\") > 1000)"));
    }

    [Test]
    public void SqlBuilder_GroupByComesAfterWhere()
    {
        var dialect = SqlDialect.SQLite;
        var state = new QueryState(dialect, "orders", null, null)
            .WithSelect(System.Collections.Immutable.ImmutableArray.Create("\"user_id\"", "COUNT(*)"))
            .WithWhere("\"status\" = 'active'")
            .WithGroupBy("\"user_id\"");

        var sql = SqlBuilder.BuildSelectSql(state);

        var whereIndex = sql.IndexOf("WHERE");
        var groupByIndex = sql.IndexOf("GROUP BY");

        Assert.That(groupByIndex, Is.GreaterThan(whereIndex));
    }

    [Test]
    public void SqlBuilder_HavingComesAfterGroupBy()
    {
        var dialect = SqlDialect.SQLite;
        var state = new QueryState(dialect, "orders", null, null)
            .WithSelect(System.Collections.Immutable.ImmutableArray.Create("\"user_id\"", "COUNT(*)"))
            .WithGroupBy("\"user_id\"")
            .WithHaving("COUNT(*) > 5");

        var sql = SqlBuilder.BuildSelectSql(state);

        var groupByIndex = sql.IndexOf("GROUP BY");
        var havingIndex = sql.IndexOf("HAVING");

        Assert.That(havingIndex, Is.GreaterThan(groupByIndex));
    }

    [Test]
    public void SqlBuilder_OrderByComesAfterHaving()
    {
        var dialect = SqlDialect.SQLite;
        var state = new QueryState(dialect, "orders", null, null)
            .WithSelect(System.Collections.Immutable.ImmutableArray.Create("\"user_id\"", "COUNT(*)"))
            .WithGroupBy("\"user_id\"")
            .WithHaving("COUNT(*) > 5")
            .WithOrderBy("COUNT(*)", Direction.Descending);

        var sql = SqlBuilder.BuildSelectSql(state);

        var havingIndex = sql.IndexOf("HAVING");
        var orderByIndex = sql.IndexOf("ORDER BY");

        Assert.That(orderByIndex, Is.GreaterThan(havingIndex));
    }

    #endregion

    #region Full Query SQL Tests

    [Test]
    public void SqlBuilder_AggregateQuery_GeneratesCorrectSql()
    {
        var dialect = SqlDialect.PostgreSQL;
        var state = new QueryState(dialect, "orders", "public", null)
            .WithSelect(System.Collections.Immutable.ImmutableArray.Create(
                "\"user_id\"",
                "COUNT(*)",
                "SUM(\"total\")",
                "AVG(\"total\")"))
            .WithWhere("\"status\" = 'completed'")
            .WithGroupBy("\"user_id\"")
            .WithHaving("COUNT(*) > 5")
            .WithOrderBy("SUM(\"total\")", Direction.Descending)
            .WithLimit(10);

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.StartWith("SELECT "));
        Assert.That(sql, Does.Contain("FROM \"public\".\"orders\""));
        Assert.That(sql, Does.Contain("WHERE \"status\" = 'completed'"));
        Assert.That(sql, Does.Contain("GROUP BY \"user_id\""));
        Assert.That(sql, Does.Contain("HAVING COUNT(*) > 5"));
        Assert.That(sql, Does.Contain("ORDER BY SUM(\"total\") DESC"));
        Assert.That(sql, Does.Contain("LIMIT 10"));
    }

    #endregion
}
