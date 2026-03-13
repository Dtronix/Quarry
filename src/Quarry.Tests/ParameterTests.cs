using Quarry;
using Quarry.Internal;
using Quarry.Shared.Sql;
using System.Collections.Immutable;

namespace Quarry.Tests;

/// <summary>
/// Tests for parameter handling and binding.
/// Tests parameter generation across dialects and parameter ordering.
/// </summary>
[TestFixture]
public class ParameterTests
{
    private SqlDialect _dialect;

    [SetUp]
    public void Setup()
    {
        _dialect = SqlDialect.SQLite;
    }

    #region Basic Parameter Tests

    [Test]
    public void QueryState_SingleParameter_AddsToState()
    {
        var state = new QueryState(_dialect, "users", null, null)
            .WithSelect(ImmutableArray.Create("*"))
            .WithWhere("\"user_id\" = @p0")
            .WithParameter(42);

        Assert.That(state.Parameters.Length, Is.EqualTo(1));
        Assert.That(state.Parameters[0].Value, Is.EqualTo(42));
        Assert.That(state.Parameters[0].Index, Is.EqualTo(0));
    }

    [Test]
    public void QueryState_MultipleParameters_MaintainsOrder()
    {
        var state = new QueryState(_dialect, "users", null, null)
            .WithSelect(ImmutableArray.Create("*"))
            .WithWhere("\"age\" BETWEEN @p0 AND @p1")
            .WithParameter(18)
            .WithParameter(65);

        Assert.That(state.Parameters.Length, Is.EqualTo(2));
        Assert.That(state.Parameters[0].Value, Is.EqualTo(18));
        Assert.That(state.Parameters[0].Index, Is.EqualTo(0));
        Assert.That(state.Parameters[1].Value, Is.EqualTo(65));
        Assert.That(state.Parameters[1].Index, Is.EqualTo(1));
    }

    [Test]
    public void QueryState_NextParameterIndex_ReturnsCorrectValue()
    {
        var state = new QueryState(_dialect, "users", null, null);

        Assert.That(state.NextParameterIndex, Is.EqualTo(0));

        state = state.WithParameter("value1");
        Assert.That(state.NextParameterIndex, Is.EqualTo(1));

        state = state.WithParameter("value2");
        Assert.That(state.NextParameterIndex, Is.EqualTo(2));
    }

    #endregion

    #region Parameter Type Tests

    [Test]
    public void QueryState_StringParameter_HandledCorrectly()
    {
        var state = new QueryState(_dialect, "users", null, null)
            .WithParameter("john");

        Assert.That(state.Parameters[0].Value, Is.EqualTo("john"));
    }

    [Test]
    public void QueryState_IntParameter_HandledCorrectly()
    {
        var state = new QueryState(_dialect, "users", null, null)
            .WithParameter(42);

        Assert.That(state.Parameters[0].Value, Is.EqualTo(42));
    }

    [Test]
    public void QueryState_DecimalParameter_HandledCorrectly()
    {
        var state = new QueryState(_dialect, "products", null, null)
            .WithParameter(99.99m);

        Assert.That(state.Parameters[0].Value, Is.EqualTo(99.99m));
    }

    [Test]
    public void QueryState_DateTimeParameter_HandledCorrectly()
    {
        var now = DateTime.UtcNow;
        var state = new QueryState(_dialect, "events", null, null)
            .WithParameter(now);

        Assert.That(state.Parameters[0].Value, Is.EqualTo(now));
    }

    [Test]
    public void QueryState_BoolParameter_HandledCorrectly()
    {
        var state = new QueryState(_dialect, "users", null, null)
            .WithParameter(true);

        Assert.That(state.Parameters[0].Value, Is.EqualTo(true));
    }

    [Test]
    public void QueryState_GuidParameter_HandledCorrectly()
    {
        var guid = Guid.NewGuid();
        var state = new QueryState(_dialect, "items", null, null)
            .WithParameter(guid);

        Assert.That(state.Parameters[0].Value, Is.EqualTo(guid));
    }

    [Test]
    public void QueryState_NullParameter_HandledCorrectly()
    {
        var state = new QueryState(_dialect, "users", null, null)
            .WithParameter(null);

        Assert.That(state.Parameters[0].Value, Is.Null);
    }

    #endregion

    #region Dialect Parameter Format Tests

    [Test]
    public void SQLite_ParameterFormat_GeneratesAtPrefix()
    {
        var dialect = SqlDialect.SQLite;

        var state = new QueryState(dialect, "users", null, null)
            .WithSelect(ImmutableArray.Create("*"))
            .WithWhere($"\"user_id\" = {SqlFormatting.FormatParameter(dialect, 0)}");

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("= @p0"));
    }

    [Test]
    public void PostgreSQL_ParameterFormat_GeneratesDollarPrefix()
    {
        var dialect = SqlDialect.PostgreSQL;

        var state = new QueryState(dialect, "users", null, null)
            .WithSelect(ImmutableArray.Create("*"))
            .WithWhere($"\"user_id\" = {SqlFormatting.FormatParameter(dialect, 0)}");

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("= $1"));
    }

    [Test]
    public void MySQL_ParameterFormat_GeneratesQuestionMark()
    {
        var dialect = SqlDialect.MySQL;

        var state = new QueryState(dialect, "users", null, null)
            .WithSelect(ImmutableArray.Create("*"))
            .WithWhere($"`user_id` = {SqlFormatting.FormatParameter(dialect, 0)}");

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("= ?"));
    }

    [Test]
    public void SqlServer_ParameterFormat_GeneratesAtPrefix()
    {
        var dialect = SqlDialect.SqlServer;

        var state = new QueryState(dialect, "users", null, null)
            .WithSelect(ImmutableArray.Create("*"))
            .WithWhere($"[user_id] = {SqlFormatting.FormatParameter(dialect, 0)}");

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("= @p0"));
    }

    #endregion

    #region Parameter Name Tests

    [Test]
    public void SQLite_GetParameterName_ReturnsAtPrefix()
    {
        var name = SqlFormatting.GetParameterName(SqlDialect.SQLite, 0);

        Assert.That(name, Is.EqualTo("@p0"));
    }

    [Test]
    public void PostgreSQL_GetParameterName_ReturnsAtPrefix()
    {
        // Note: PostgreSQL uses $1 in SQL but @p0 for DbParameter.ParameterName
        var name = SqlFormatting.GetParameterName(SqlDialect.PostgreSQL, 0);

        Assert.That(name, Is.EqualTo("@p0"));
    }

    [Test]
    public void MySQL_GetParameterName_ReturnsAtPrefix()
    {
        // Note: MySQL uses ? in SQL but @p0 for DbParameter.ParameterName
        var name = SqlFormatting.GetParameterName(SqlDialect.MySQL, 0);

        Assert.That(name, Is.EqualTo("@p0"));
    }

    [Test]
    public void SqlServer_GetParameterName_ReturnsAtPrefix()
    {
        var name = SqlFormatting.GetParameterName(SqlDialect.SqlServer, 0);

        Assert.That(name, Is.EqualTo("@p0"));
    }

    #endregion

    #region Multiple Parameter Placement Tests

    [Test]
    public void QueryState_ParametersInMultipleClauses_MaintainsIndexOrder()
    {
        var state = new QueryState(_dialect, "users", null, null)
            .WithSelect(ImmutableArray.Create("*"))
            .WithWhere("\"name\" = @p0")
            .WithParameter("john")
            .WithWhere("\"age\" > @p1")
            .WithParameter(18)
            .WithWhere("\"status\" = @p2")
            .WithParameter("active");

        Assert.That(state.Parameters.Length, Is.EqualTo(3));
        Assert.That(state.Parameters[0].Value, Is.EqualTo("john"));
        Assert.That(state.Parameters[1].Value, Is.EqualTo(18));
        Assert.That(state.Parameters[2].Value, Is.EqualTo("active"));
    }

    [Test]
    public void QueryState_ParameterInHaving_MaintainsOrder()
    {
        var state = new QueryState(_dialect, "orders", null, null)
            .WithSelect(ImmutableArray.Create("\"user_id\"", "COUNT(*)"))
            .WithWhere("\"status\" = @p0")
            .WithParameter("completed")
            .WithGroupBy("\"user_id\"")
            .WithHaving("COUNT(*) > @p1")
            .WithParameter(5);

        Assert.That(state.Parameters.Length, Is.EqualTo(2));
        Assert.That(state.Parameters[0].Value, Is.EqualTo("completed"));
        Assert.That(state.Parameters[1].Value, Is.EqualTo(5));
    }

    #endregion

    #region Parameter Immutability Tests

    [Test]
    public void QueryState_ParameterAddition_DoesNotMutateOriginal()
    {
        var original = new QueryState(_dialect, "users", null, null);
        var withParam = original.WithParameter("test");

        Assert.That(original.Parameters.Length, Is.EqualTo(0));
        Assert.That(withParam.Parameters.Length, Is.EqualTo(1));
    }

    [Test]
    public void QueryState_MultipleParameterChains_Independent()
    {
        var original = new QueryState(_dialect, "users", null, null);
        var chain1 = original.WithParameter("value1");
        var chain2 = original.WithParameter("value2");

        Assert.That(chain1.Parameters[0].Value, Is.EqualTo("value1"));
        Assert.That(chain2.Parameters[0].Value, Is.EqualTo("value2"));
    }

    #endregion

    #region Parameter in Complex Query Tests

    [Test]
    public void QueryState_ComplexQueryWithParameters_GeneratesCorrectSql()
    {
        var state = new QueryState(_dialect, "orders", null, null)
            .WithSelect(ImmutableArray.Create("*"))
            .WithJoin(new JoinClause(JoinKind.Inner, "users", null, null, "\"orders\".\"user_id\" = \"users\".\"user_id\""))
            .WithWhere("\"orders\".\"total\" > @p0")
            .WithParameter(100.00m)
            .WithWhere("\"users\".\"status\" = @p1")
            .WithParameter("active")
            .WithOrderBy("\"orders\".\"created_at\"", Direction.Descending)
            .WithLimit(10);

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("@p0"));
        Assert.That(sql, Does.Contain("@p1"));
        Assert.That(state.Parameters.Length, Is.EqualTo(2));
    }

    #endregion

    #region Array/Collection Parameter Tests

    [Test]
    public void QueryState_ArrayParameter_HandledCorrectly()
    {
        var ids = new[] { 1, 2, 3, 4, 5 };
        var state = new QueryState(_dialect, "users", null, null)
            .WithParameter(ids);

        Assert.That(state.Parameters[0].Value, Is.EqualTo(ids));
    }

    [Test]
    public void QueryState_ListParameter_HandledCorrectly()
    {
        var names = new List<string> { "john", "jane", "bob" };
        var state = new QueryState(_dialect, "users", null, null)
            .WithParameter(names);

        Assert.That(state.Parameters[0].Value, Is.EqualTo(names));
    }

    #endregion

    #region Edge Case Tests

    [Test]
    public void QueryState_EmptyStringParameter_HandledCorrectly()
    {
        var state = new QueryState(_dialect, "users", null, null)
            .WithParameter(string.Empty);

        Assert.That(state.Parameters[0].Value, Is.EqualTo(string.Empty));
    }

    [Test]
    public void QueryState_WhitespaceParameter_HandledCorrectly()
    {
        var state = new QueryState(_dialect, "users", null, null)
            .WithParameter("   ");

        Assert.That(state.Parameters[0].Value, Is.EqualTo("   "));
    }

    [Test]
    public void QueryState_SpecialCharacterParameter_HandledCorrectly()
    {
        var value = "O'Brien";
        var state = new QueryState(_dialect, "users", null, null)
            .WithParameter(value);

        Assert.That(state.Parameters[0].Value, Is.EqualTo("O'Brien"));
    }

    [Test]
    public void QueryState_UnicodeParameter_HandledCorrectly()
    {
        var value = "日本語テスト";
        var state = new QueryState(_dialect, "users", null, null)
            .WithParameter(value);

        Assert.That(state.Parameters[0].Value, Is.EqualTo("日本語テスト"));
    }

    [Test]
    public void QueryState_LargeNumberOfParameters_HandledCorrectly()
    {
        var state = new QueryState(_dialect, "users", null, null);

        for (int i = 0; i < 100; i++)
        {
            state = state.WithParameter(i);
        }

        Assert.That(state.Parameters.Length, Is.EqualTo(100));
        Assert.That(state.NextParameterIndex, Is.EqualTo(100));

        for (int i = 0; i < 100; i++)
        {
            Assert.That(state.Parameters[i].Index, Is.EqualTo(i));
            Assert.That(state.Parameters[i].Value, Is.EqualTo(i));
        }
    }

    #endregion
}
