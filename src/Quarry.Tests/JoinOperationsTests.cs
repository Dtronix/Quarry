using NUnit.Framework;
using Quarry;
using Quarry.Generators.Models;
using Quarry.Generators.Generation;
using Quarry.Generators.IR;
using Quarry.Generators.Translation;
using Quarry.Tests.Testing;
using System.Text;

// Use explicit namespace aliases to avoid ambiguity
using CoreJoinClause = Quarry.Internal.JoinClause;
using CoreJoinKind = Quarry.Internal.JoinKind;
using GenJoinClauseKind = Quarry.Generators.Models.JoinClauseKind;

namespace Quarry.Tests;


/// <summary>
/// Tests for Phase 7: Join Operations
/// </summary>
[TestFixture]
public class JoinOperationsTests
{
    #region QueryState JoinClause Tests

    [Test]
    public void QueryState_WithJoin_AddsJoinClause()
    {
        var dialect = SqlDialect.PostgreSQL;
        var state = new Quarry.Internal.QueryState(dialect, "users", "public");

        var joinClause = new CoreJoinClause(CoreJoinKind.Inner, "orders", null, null, "users.id = orders.user_id");
        var newState = state.WithJoin(joinClause);

        Assert.That(newState.JoinClauses.Length, Is.EqualTo(1));
        Assert.That(newState.JoinClauses[0].Kind, Is.EqualTo(CoreJoinKind.Inner));
        Assert.That(newState.JoinClauses[0].JoinedTableName, Is.EqualTo("orders"));
        Assert.That(newState.JoinClauses[0].OnConditionSql, Is.EqualTo("users.id = orders.user_id"));
    }

    [Test]
    public void QueryState_WithJoin_MultipleJoins_AccumulatesJoins()
    {
        var dialect = SqlDialect.PostgreSQL;
        var state = new Quarry.Internal.QueryState(dialect, "users", "public");

        var join1 = new CoreJoinClause(CoreJoinKind.Inner, "orders", null, null, "users.id = orders.user_id");
        var join2 = new CoreJoinClause(CoreJoinKind.Left, "items", null, null, "orders.id = items.order_id");

        var newState = state.WithJoin(join1).WithJoin(join2);

        Assert.That(newState.JoinClauses.Length, Is.EqualTo(2));
        Assert.That(newState.JoinClauses[0].Kind, Is.EqualTo(CoreJoinKind.Inner));
        Assert.That(newState.JoinClauses[1].Kind, Is.EqualTo(CoreJoinKind.Left));
    }

    [Test]
    public void QueryState_WithJoin_IsImmutable()
    {
        var dialect = SqlDialect.PostgreSQL;
        var state = new Quarry.Internal.QueryState(dialect, "users", "public");

        var joinClause = new CoreJoinClause(CoreJoinKind.Inner, "orders", null, null, "users.id = orders.user_id");
        var newState = state.WithJoin(joinClause);

        Assert.That(state.JoinClauses.Length, Is.EqualTo(0));
        Assert.That(newState.JoinClauses.Length, Is.EqualTo(1));
        Assert.That(state, Is.Not.SameAs(newState));
    }

    #endregion

    #region SqlBuilder Join SQL Generation Tests

    [Test]
    public void SqlBuilder_WithInnerJoin_GeneratesCorrectSql()
    {
        var dialect = SqlDialect.PostgreSQL;
        var state = new Quarry.Internal.QueryState(dialect, "users", null);

        var joinClause = new CoreJoinClause(CoreJoinKind.Inner, "orders", null, null, "\"t0\".\"id\" = \"t1\".\"user_id\"");
        state = state.WithJoin(joinClause);

        var sql = Quarry.Internal.SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\""));
        Assert.That(sql, Does.Contain("ON \"t0\".\"id\" = \"t1\".\"user_id\""));
    }

    [Test]
    public void SqlBuilder_WithLeftJoin_GeneratesCorrectSql()
    {
        var dialect = SqlDialect.PostgreSQL;
        var state = new Quarry.Internal.QueryState(dialect, "users", null);

        var joinClause = new CoreJoinClause(CoreJoinKind.Left, "orders", null, null, "\"users\".\"id\" = \"orders\".\"user_id\"");
        state = state.WithJoin(joinClause);

        var sql = Quarry.Internal.SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("LEFT JOIN \"orders\""));
    }

    [Test]
    public void SqlBuilder_WithRightJoin_GeneratesCorrectSql()
    {
        var dialect = SqlDialect.PostgreSQL;
        var state = new Quarry.Internal.QueryState(dialect, "users", null);

        var joinClause = new CoreJoinClause(CoreJoinKind.Right, "orders", null, null, "\"users\".\"id\" = \"orders\".\"user_id\"");
        state = state.WithJoin(joinClause);

        var sql = Quarry.Internal.SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("RIGHT JOIN \"orders\""));
    }

    [Test]
    public void SqlBuilder_WithJoinAndSchema_GeneratesQualifiedTableName()
    {
        var dialect = SqlDialect.PostgreSQL;
        var state = new Quarry.Internal.QueryState(dialect, "users", "public");

        var joinClause = new CoreJoinClause(CoreJoinKind.Inner, "orders", "public", null, "\"users\".\"id\" = \"orders\".\"user_id\"");
        state = state.WithJoin(joinClause);

        var sql = Quarry.Internal.SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("INNER JOIN \"public\".\"orders\""));
    }

    [Test]
    public void SqlBuilder_WithJoinAlias_GeneratesAliasedTableName()
    {
        var dialect = SqlDialect.PostgreSQL;
        var state = new Quarry.Internal.QueryState(dialect, "users", null);

        var joinClause = new CoreJoinClause(CoreJoinKind.Inner, "orders", null, "o", "\"users\".\"id\" = \"o\".\"user_id\"");
        state = state.WithJoin(joinClause);

        var sql = Quarry.Internal.SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("\"orders\" AS \"o\""));
    }

    [Test]
    public void SqlBuilder_WithMultipleJoins_GeneratesJoinsInOrder()
    {
        var dialect = SqlDialect.PostgreSQL;
        var state = new Quarry.Internal.QueryState(dialect, "users", null);

        var join1 = new CoreJoinClause(CoreJoinKind.Inner, "orders", null, null, "\"users\".\"id\" = \"orders\".\"user_id\"");
        var join2 = new CoreJoinClause(CoreJoinKind.Left, "items", null, null, "\"orders\".\"id\" = \"items\".\"order_id\"");
        state = state.WithJoin(join1).WithJoin(join2);

        var sql = Quarry.Internal.SqlBuilder.BuildSelectSql(state);

        var innerJoinPos = sql.IndexOf("INNER JOIN");
        var leftJoinPos = sql.IndexOf("LEFT JOIN");
        Assert.That(innerJoinPos, Is.LessThan(leftJoinPos));
    }

    [Test]
    public void SqlBuilder_WithJoinAndWhere_GeneratesCorrectOrder()
    {
        var dialect = SqlDialect.PostgreSQL;
        var state = new Quarry.Internal.QueryState(dialect, "users", null);

        var joinClause = new CoreJoinClause(CoreJoinKind.Inner, "orders", null, null, "\"users\".\"id\" = \"orders\".\"user_id\"");
        state = state.WithJoin(joinClause).WithWhere("\"orders\".\"total\" > 100");

        var sql = Quarry.Internal.SqlBuilder.BuildSelectSql(state);

        var joinPos = sql.IndexOf("JOIN");
        var wherePos = sql.IndexOf("WHERE");
        Assert.That(joinPos, Is.LessThan(wherePos));
    }

    #endregion

    #region JoinedQueryBuilder Tests

    [Test]
    public void JoinedQueryBuilder_Offset_AppliesOffset()
    {
        var dialect = SqlDialect.PostgreSQL;
        var state = new Quarry.Internal.QueryState(dialect, "users", null);
        var builder = new JoinedQueryBuilder<TestUser, TestOrder>(state);

        var newBuilder = builder.Offset(10);
        var sql = newBuilder.ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("OFFSET 10"));
    }

    [Test]
    public void JoinedQueryBuilder_Limit_AppliesLimit()
    {
        var dialect = SqlDialect.PostgreSQL;
        var state = new Quarry.Internal.QueryState(dialect, "users", null);
        var builder = new JoinedQueryBuilder<TestUser, TestOrder>(state);

        var newBuilder = builder.Limit(5);
        var sql = newBuilder.ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("LIMIT 5"));
    }

    [Test]
    public void JoinedQueryBuilder_Distinct_AppliesDistinct()
    {
        var dialect = SqlDialect.PostgreSQL;
        var state = new Quarry.Internal.QueryState(dialect, "users", null);
        var builder = new JoinedQueryBuilder<TestUser, TestOrder>(state);

        var newBuilder = builder.Distinct();
        var sql = newBuilder.ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("SELECT DISTINCT"));
    }

    [Test]
    public void JoinedQueryBuilder_MethodChaining_WorksCorrectly()
    {
        var dialect = SqlDialect.PostgreSQL;
        var state = new Quarry.Internal.QueryState(dialect, "users", null);
        var builder = new JoinedQueryBuilder<TestUser, TestOrder>(state);

        var newBuilder = builder.Offset(10).Limit(5).Distinct();
        var sql = newBuilder.ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("SELECT DISTINCT"));
        Assert.That(sql, Does.Contain("LIMIT 5"));
        Assert.That(sql, Does.Contain("OFFSET 10"));
    }

    [Test]
    public void JoinedQueryBuilder_Offset_ThrowsOnNegative()
    {
        var dialect = SqlDialect.PostgreSQL;
        var state = new Quarry.Internal.QueryState(dialect, "users", null);
        var builder = new JoinedQueryBuilder<TestUser, TestOrder>(state);

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Offset(-1));
    }

    [Test]
    public void JoinedQueryBuilder_Limit_ThrowsOnNegative()
    {
        var dialect = SqlDialect.PostgreSQL;
        var state = new Quarry.Internal.QueryState(dialect, "users", null);
        var builder = new JoinedQueryBuilder<TestUser, TestOrder>(state);

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Limit(-1));
    }

    #endregion

    #region Dialect-Specific Join SQL Tests

    [Test]
    public void SqlBuilder_WithJoin_PostgreSQL_UsesDoubleQuotes()
    {
        var dialect = SqlDialect.PostgreSQL;
        var state = new Quarry.Internal.QueryState(dialect, "users", null);

        var joinClause = new CoreJoinClause(CoreJoinKind.Inner, "orders", null, null, "condition");
        state = state.WithJoin(joinClause);

        var sql = Quarry.Internal.SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("\"users\""));
        Assert.That(sql, Does.Contain("\"orders\""));
    }

    [Test]
    public void SqlBuilder_WithJoin_MySQL_UsesBackticks()
    {
        var dialect = SqlDialect.MySQL;
        var state = new Quarry.Internal.QueryState(dialect, "users", null);

        var joinClause = new CoreJoinClause(CoreJoinKind.Inner, "orders", null, null, "condition");
        state = state.WithJoin(joinClause);

        var sql = Quarry.Internal.SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("`users`"));
        Assert.That(sql, Does.Contain("`orders`"));
    }

    [Test]
    public void SqlBuilder_WithJoin_SqlServer_UsesBrackets()
    {
        var dialect = SqlDialect.SqlServer;
        var state = new Quarry.Internal.QueryState(dialect, "users", null);

        var joinClause = new CoreJoinClause(CoreJoinKind.Inner, "orders", null, null, "condition");
        state = state.WithJoin(joinClause);

        var sql = Quarry.Internal.SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("[users]"));
        Assert.That(sql, Does.Contain("[orders]"));
    }

    [Test]
    public void SqlBuilder_WithJoin_SQLite_UsesDoubleQuotes()
    {
        var dialect = SqlDialect.SQLite;
        var state = new Quarry.Internal.QueryState(dialect, "users", null);

        var joinClause = new CoreJoinClause(CoreJoinKind.Inner, "orders", null, null, "condition");
        state = state.WithJoin(joinClause);

        var sql = Quarry.Internal.SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("\"users\""));
        Assert.That(sql, Does.Contain("\"orders\""));
    }

    [Test]
    public void SqlBuilder_ThreeTableJoin_PostgreSQL_UsesDoubleQuotesAndAliases()
    {
        var dialect = SqlDialect.PostgreSQL;
        var state = new Quarry.Internal.QueryState(dialect, "users", null);

        state = state.WithJoin(new CoreJoinClause(CoreJoinKind.Inner, "orders", null, null, "t0.\"id\" = t1.\"user_id\""));
        state = state.WithJoin(new CoreJoinClause(CoreJoinKind.Inner, "payments", null, null, "t1.\"id\" = t2.\"order_id\""));

        var sql = Quarry.Internal.SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("\"users\" AS \"t0\""));
        Assert.That(sql, Does.Contain("\"orders\" AS \"t1\""));
        Assert.That(sql, Does.Contain("\"payments\" AS \"t2\""));
    }

    [Test]
    public void SqlBuilder_ThreeTableJoin_MySQL_UsesBackticksAndAliases()
    {
        var dialect = SqlDialect.MySQL;
        var state = new Quarry.Internal.QueryState(dialect, "users", null);

        state = state.WithJoin(new CoreJoinClause(CoreJoinKind.Inner, "orders", null, null, "t0.`id` = t1.`user_id`"));
        state = state.WithJoin(new CoreJoinClause(CoreJoinKind.Inner, "payments", null, null, "t1.`id` = t2.`order_id`"));

        var sql = Quarry.Internal.SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("`users` AS `t0`"));
        Assert.That(sql, Does.Contain("`orders` AS `t1`"));
        Assert.That(sql, Does.Contain("`payments` AS `t2`"));
    }

    [Test]
    public void SqlBuilder_ThreeTableJoin_SqlServer_UsesBracketsAndAliases()
    {
        var dialect = SqlDialect.SqlServer;
        var state = new Quarry.Internal.QueryState(dialect, "users", null);

        state = state.WithJoin(new CoreJoinClause(CoreJoinKind.Inner, "orders", null, null, "[t0].[id] = [t1].[user_id]"));
        state = state.WithJoin(new CoreJoinClause(CoreJoinKind.Inner, "payments", null, null, "[t1].[id] = [t2].[order_id]"));

        var sql = Quarry.Internal.SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("[users] AS [t0]"));
        Assert.That(sql, Does.Contain("[orders] AS [t1]"));
        Assert.That(sql, Does.Contain("[payments] AS [t2]"));
    }

    [Test]
    public void SqlBuilder_ThreeTableJoin_SQLite_UsesDoubleQuotesAndAliases()
    {
        var dialect = SqlDialect.SQLite;
        var state = new Quarry.Internal.QueryState(dialect, "users", null);

        state = state.WithJoin(new CoreJoinClause(CoreJoinKind.Inner, "orders", null, null, "t0.\"id\" = t1.\"user_id\""));
        state = state.WithJoin(new CoreJoinClause(CoreJoinKind.Inner, "payments", null, null, "t1.\"id\" = t2.\"order_id\""));

        var sql = Quarry.Internal.SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("\"users\" AS \"t0\""));
        Assert.That(sql, Does.Contain("\"orders\" AS \"t1\""));
        Assert.That(sql, Does.Contain("\"payments\" AS \"t2\""));
    }

    #endregion

    #region InterceptorCodeGenerator Join Tests

    [Test]
    public void InterceptorCodeGenerator_Join_GeneratesFallbackWithConcreteType()
    {
        var sites = new List<TranslatedCallSite>
        {
            CreateJoinCallSite(InterceptorKind.Join, withClause: false)
        };

        var source = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "TestContext",
            "TestNamespace",
            "test0000",
            sites);

        Assert.That(source, Does.Contain("public static IJoinedQueryBuilder<TestNamespace.TestUser, TestOrder>"));
        Assert.That(source, Does.Contain("return __b.Join(condition)"));
    }

    [Test]
    public void InterceptorCodeGenerator_LeftJoin_GeneratesFallbackWithConcreteType()
    {
        var sites = new List<TranslatedCallSite>
        {
            CreateJoinCallSite(InterceptorKind.LeftJoin, withClause: false)
        };

        var source = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "TestContext",
            "TestNamespace",
            "test0000",
            sites);

        Assert.That(source, Does.Contain("public static IJoinedQueryBuilder<TestNamespace.TestUser, TestOrder>"));
        Assert.That(source, Does.Contain("return __b.LeftJoin(condition)"));
    }

    [Test]
    public void InterceptorCodeGenerator_RightJoin_GeneratesFallbackWithConcreteType()
    {
        var sites = new List<TranslatedCallSite>
        {
            CreateJoinCallSite(InterceptorKind.RightJoin, withClause: false)
        };

        var source = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "TestContext",
            "TestNamespace",
            "test0000",
            sites);

        Assert.That(source, Does.Contain("public static IJoinedQueryBuilder<TestNamespace.TestUser, TestOrder>"));
        Assert.That(source, Does.Contain("return __b.RightJoin(condition)"));
    }

    [Test]
    public void InterceptorCodeGenerator_Join_WithClauseInfo_GeneratesOptimizedJoin()
    {
        var sites = new List<TranslatedCallSite>
        {
            CreateJoinCallSite(InterceptorKind.Join)
        };

        var source = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "TestContext",
            "TestNamespace",
            "test0000",
            sites);

        Assert.That(source, Does.Contain("JoinedQueryBuilder<TestNamespace.TestUser, TestOrder>"));
        Assert.That(source, Does.Contain("JoinKind.Inner"));
        Assert.That(source, Does.Contain("AddJoinClause"));
    }

    private TranslatedCallSite CreateJoinCallSite(InterceptorKind kind, bool withClause = true)
    {
        var joinKind = kind switch
        {
            InterceptorKind.LeftJoin => JoinClauseKind.Left,
            InterceptorKind.RightJoin => JoinClauseKind.Right,
            _ => JoinClauseKind.Inner
        };
        var builder = new TestCallSiteBuilder()
            .WithMethodName(kind.ToString())
            .WithKind(kind)
            .WithEntityType("global::TestNamespace.TestUser")
            .WithJoinedEntityType("TestOrder")
            .WithBuilderTypeName("IQueryBuilder")
            .WithContext("TestContext", "TestNamespace")
            .WithUniqueId("abc12345");
        if (withClause)
            builder.WithClause(TestCallSiteBuilder.CreateJoinClause("orders", joinKind: joinKind));
        return builder.Build();
    }

    [Test]
    public void InterceptorCodeGenerator_ChainedJoin_2To3_GeneratesCorrectTypes()
    {
        var site = new TestCallSiteBuilder()
            .WithMethodName("Join")
            .WithKind(InterceptorKind.Join)
            .WithEntityType("global::TestNamespace.TestUser")
            .WithBuilderTypeName("IJoinedQueryBuilder")
            .WithUniqueId("chain23")
            .WithContext("TestContext", "TestNamespace")
            .WithJoinedEntityType("global::TestNamespace.TestItem")
            .WithJoinedEntityTypeNames(new[] { "global::TestNamespace.TestUser", "global::TestNamespace.TestOrder" })
            .WithClause(TestCallSiteBuilder.CreateJoinClause("items"))
            .Build();

        var source = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "TestContext", "TestNamespace", "test0000", new List<TranslatedCallSite> { site });

        Assert.That(source, Does.Contain("JoinedQueryBuilder3<TestNamespace.TestUser, TestNamespace.TestOrder, TestNamespace.TestItem>"));
        Assert.That(source, Does.Contain("this IJoinedQueryBuilder<TestNamespace.TestUser, TestNamespace.TestOrder> builder"));
        Assert.That(source, Does.Contain("AddJoinClause<TestNamespace.TestItem>"));
    }

    [Test]
    public void InterceptorCodeGenerator_ChainedJoin_3To4_GeneratesCorrectTypes()
    {
        var site = new TestCallSiteBuilder()
            .WithMethodName("LeftJoin")
            .WithKind(InterceptorKind.LeftJoin)
            .WithEntityType("global::TestNamespace.TestUser")
            .WithBuilderTypeName("IJoinedQueryBuilder3")
            .WithUniqueId("chain34")
            .WithContext("TestContext", "TestNamespace")
            .WithJoinedEntityType("global::TestNamespace.TestCategory")
            .WithJoinedEntityTypeNames(new[] { "global::TestNamespace.TestUser", "global::TestNamespace.TestOrder", "global::TestNamespace.TestItem" })
            .WithClause(TestCallSiteBuilder.CreateJoinClause("categories", joinKind: JoinClauseKind.Left))
            .Build();

        var source = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "TestContext", "TestNamespace", "test0000", new List<TranslatedCallSite> { site });

        Assert.That(source, Does.Contain("JoinedQueryBuilder4<TestNamespace.TestUser, TestNamespace.TestOrder, TestNamespace.TestItem, TestNamespace.TestCategory>"));
        Assert.That(source, Does.Contain("this IJoinedQueryBuilder3<TestNamespace.TestUser, TestNamespace.TestOrder, TestNamespace.TestItem> builder"));
        Assert.That(source, Does.Contain("JoinKind.Left"));
    }

    [Test]
    public void InterceptorCodeGenerator_JoinedWhere_2Table_GeneratesCorrectInterceptor()
    {
        var site = new TestCallSiteBuilder()
            .WithMethodName("Where")
            .WithKind(InterceptorKind.Where)
            .WithEntityType("global::TestNamespace.TestUser")
            .WithBuilderTypeName("IJoinedQueryBuilder")
            .WithUniqueId("jwhere2")
            .WithContext("TestContext", "TestNamespace")
            .WithJoinedEntityTypeNames(new[] { "global::TestNamespace.TestUser", "global::TestNamespace.TestOrder" })
            .WithClause(TestCallSiteBuilder.CreateSimpleClause(ClauseKind.Where, "\"IsActive\" = 1"))
            .Build();

        var source = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "TestContext", "TestNamespace", "test0000", new List<TranslatedCallSite> { site });

        Assert.That(source, Does.Contain("this IJoinedQueryBuilder<TestNamespace.TestUser, TestNamespace.TestOrder> builder"));
        Assert.That(source, Does.Contain("Expression<Func<TestNamespace.TestUser, TestNamespace.TestOrder, bool>>"));
        Assert.That(source, Does.Contain("AddWhereClause"));
    }

    [Test]
    public void InterceptorCodeGenerator_JoinedOrderBy_3Table_GeneratesCorrectInterceptor()
    {
        var site = new TestCallSiteBuilder()
            .WithMethodName("OrderBy")
            .WithKind(InterceptorKind.OrderBy)
            .WithEntityType("global::TestNamespace.TestUser")
            .WithBuilderTypeName("IJoinedQueryBuilder3")
            .WithUniqueId("jorder3")
            .WithContext("TestContext", "TestNamespace")
            .WithJoinedEntityTypeNames(new[] { "global::TestNamespace.TestUser", "global::TestNamespace.TestOrder", "global::TestNamespace.TestItem" })
            .WithClause(TestCallSiteBuilder.CreateSimpleClause(ClauseKind.OrderBy, "\"UserName\""))
            .Build();

        var source = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "TestContext", "TestNamespace", "test0000", new List<TranslatedCallSite> { site });

        Assert.That(source, Does.Contain("this IJoinedQueryBuilder3<T1, T2, T3> builder"));
        Assert.That(source, Does.Contain("Expression<Func<T1, T2, T3, TKey>>"));
        Assert.That(source, Does.Contain("AddOrderByClause"));
    }

    [Test]
    public void InterceptorCodeGenerator_JoinedOrderBy_3Table_ConcreteKeyType_GeneratesNonGenericInterceptor()
    {
        var site = new TestCallSiteBuilder()
            .WithMethodName("OrderBy")
            .WithKind(InterceptorKind.OrderBy)
            .WithEntityType("global::TestNamespace.TestUser")
            .WithBuilderTypeName("IJoinedQueryBuilder3")
            .WithUniqueId("jorder3c")
            .WithContext("TestContext", "TestNamespace")
            .WithJoinedEntityTypeNames(new[] { "global::TestNamespace.TestUser", "global::TestNamespace.TestOrder", "global::TestNamespace.TestItem" })
            .WithKeyType("global::System.String")
            .WithClause(TestCallSiteBuilder.CreateSimpleClause(ClauseKind.OrderBy, "\"UserName\""))
            .Build();

        var source = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "TestContext", "TestNamespace", "test0000", new List<TranslatedCallSite> { site });

        Assert.That(source, Does.Contain("this IJoinedQueryBuilder3<TestNamespace.TestUser, TestNamespace.TestOrder, TestNamespace.TestItem> builder"));
        Assert.That(source, Does.Contain("Expression<Func<TestNamespace.TestUser, TestNamespace.TestOrder, TestNamespace.TestItem, System.String>>"));
        Assert.That(source, Does.Not.Contain("<TKey>"));
        Assert.That(source, Does.Contain("AddOrderByClause"));
    }

    #endregion

    #region 3-Table and 4-Table Builder Tests

    [Test]
    public void JoinedQueryBuilder3_Where_ReturnsNewInstance()
    {
        var dialect = SqlDialect.PostgreSQL;
        var state = new Quarry.Internal.QueryState(dialect, "users", null);
        var builder = new JoinedQueryBuilder3<TestUser, TestOrder, TestItem>(state);

        var result = builder.Where((u, o, i) => true);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Not.SameAs(builder));
    }

    [Test]
    public void JoinedQueryBuilder3_Offset_AppliesOffset()
    {
        var dialect = SqlDialect.PostgreSQL;
        var state = new Quarry.Internal.QueryState(dialect, "users", null);
        var builder = new JoinedQueryBuilder3<TestUser, TestOrder, TestItem>(state);

        var sql = builder.Offset(10).ToDiagnostics().Sql;
        Assert.That(sql, Does.Contain("OFFSET 10"));
    }

    [Test]
    public void JoinedQueryBuilder3_AddWhereClause_UpdatesState()
    {
        var dialect = SqlDialect.PostgreSQL;
        var state = new Quarry.Internal.QueryState(dialect, "users", null);
        var builder = new JoinedQueryBuilder3<TestUser, TestOrder, TestItem>(state);

        var result = builder.AddWhereClause("\"t0\".\"id\" = 1");
        var sql = result.ToDiagnostics().Sql;
        Assert.That(sql, Does.Contain("WHERE \"t0\".\"id\" = 1"));
    }

    [Test]
    public void JoinedQueryBuilder4_Where_ReturnsNewInstance()
    {
        var dialect = SqlDialect.PostgreSQL;
        var state = new Quarry.Internal.QueryState(dialect, "users", null);
        var builder = new JoinedQueryBuilder4<TestUser, TestOrder, TestItem, TestCategory>(state);

        var result = builder.Where((u, o, i, c) => true);
        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public void JoinedQueryBuilder4_NoJoinMethod()
    {
        var methods = typeof(JoinedQueryBuilder4<TestUser, TestOrder, TestItem, TestCategory>).GetMethods();
        var joinMethods = methods.Where(m => m.Name == "Join" || m.Name == "LeftJoin" || m.Name == "RightJoin").ToArray();
        Assert.That(joinMethods, Is.Empty);
    }

    [Test]
    public void JoinedQueryBuilder_Join_ReturnsBuilder3()
    {
        var dialect = SqlDialect.PostgreSQL;
        var state = new Quarry.Internal.QueryState(dialect, "users", null);
        var builder = new JoinedQueryBuilder<TestUser, TestOrder>(state);

        var result = builder.Join<TestItem>((u, o, i) => true);
        Assert.That(result, Is.InstanceOf<JoinedQueryBuilder3<TestUser, TestOrder, TestItem>>());
    }

    [Test]
    public void JoinedQueryBuilder3_Join_ReturnsBuilder4()
    {
        var dialect = SqlDialect.PostgreSQL;
        var state = new Quarry.Internal.QueryState(dialect, "users", null);
        var builder = new JoinedQueryBuilder3<TestUser, TestOrder, TestItem>(state);

        var result = builder.Join<TestCategory>((u, o, i, c) => true);
        Assert.That(result, Is.InstanceOf<JoinedQueryBuilder4<TestUser, TestOrder, TestItem, TestCategory>>());
    }

    [Test]
    public void JoinedQueryBuilder_AddJoinClause_ReturnsBuilder3WithState()
    {
        var dialect = SqlDialect.PostgreSQL;
        var state = new Quarry.Internal.QueryState(dialect, "users", null);
        state = state.WithJoin(new CoreJoinClause(CoreJoinKind.Inner, "orders", null, null, "cond1"));
        var builder = new JoinedQueryBuilder<TestUser, TestOrder>(state);

        var result = builder.AddJoinClause<TestItem>(CoreJoinKind.Inner, "items", "\"t1\".\"id\" = \"t2\".\"order_id\"");
        var sql = result.ToDiagnostics().Sql;
        Assert.That(sql, Does.Contain("INNER JOIN"));
    }

    #endregion

    #region AsJoined (Prebuilt Path) Tests

    [Test]
    public void QueryBuilder_AsJoined_ReturnsJoinedBuilderWithSameState()
    {
        var dialect = SqlDialect.PostgreSQL;
        var builder = new QueryBuilder<TestUser>(dialect, "users", null);

        var result = builder.AsJoined<TestOrder>();

        Assert.That(result, Is.InstanceOf<JoinedQueryBuilder<TestUser, TestOrder>>());
    }

    [Test]
    public void QueryBuilder_AsJoined_DoesNotMutateJoinClauses()
    {
        var dialect = SqlDialect.PostgreSQL;
        var builder = new QueryBuilder<TestUser>(dialect, "users", null);

        var result = builder.AsJoined<TestOrder>();

        // AsJoined should NOT add JoinClauses or set FromTableAlias
        var resultState = result.State;
        Assert.Multiple(() =>
        {
            Assert.That(resultState.JoinClauses.Length, Is.EqualTo(0));
            Assert.That(resultState.FromTableAlias, Is.Null);
        });
    }

    [Test]
    public void QueryBuilder_AsJoined_PropagatesPrebuiltParams()
    {
        var dialect = SqlDialect.PostgreSQL;
        var builder = new QueryBuilder<TestUser>(dialect, "users", null);
        builder.AllocatePrebuiltParams(3);
        builder.BindParam(42);

        var result = builder.AsJoined<TestOrder>();

        Assert.Multiple(() =>
        {
            Assert.That(result.PrebuiltParams, Is.Not.Null);
            Assert.That(result.PrebuiltParams!.Length, Is.EqualTo(3));
            Assert.That(result.PrebuiltParams![0], Is.EqualTo(42));
            Assert.That(result.PrebuiltParamIndex, Is.EqualTo(1));
        });
    }

    [Test]
    public void JoinedQueryBuilder_AsJoined_ReturnsBuilder3WithSameState()
    {
        var dialect = SqlDialect.PostgreSQL;
        var state = new Quarry.Internal.QueryState(dialect, "users", null);
        var builder = new JoinedQueryBuilder<TestUser, TestOrder>(state);

        var result = builder.AsJoined<TestItem>();

        Assert.That(result, Is.InstanceOf<JoinedQueryBuilder3<TestUser, TestOrder, TestItem>>());
    }

    [Test]
    public void JoinedQueryBuilder_AsJoined_DoesNotMutateJoinClauses()
    {
        var dialect = SqlDialect.PostgreSQL;
        var state = new Quarry.Internal.QueryState(dialect, "users", null);
        var builder = new JoinedQueryBuilder<TestUser, TestOrder>(state);

        var result = builder.AsJoined<TestItem>();

        var resultState = result.State;
        Assert.Multiple(() =>
        {
            Assert.That(resultState.JoinClauses.Length, Is.EqualTo(0));
            Assert.That(resultState.FromTableAlias, Is.Null);
        });
    }

    [Test]
    public void JoinedQueryBuilder_AsJoined_PropagatesPrebuiltParams()
    {
        var dialect = SqlDialect.PostgreSQL;
        var state = new Quarry.Internal.QueryState(dialect, "users", null);
        var builder = new JoinedQueryBuilder<TestUser, TestOrder>(state);
        builder.AllocatePrebuiltParams(2);
        builder.BindParam("hello");

        var result = builder.AsJoined<TestItem>();

        Assert.Multiple(() =>
        {
            Assert.That(result.PrebuiltParams, Is.Not.Null);
            Assert.That(result.PrebuiltParams!.Length, Is.EqualTo(2));
            Assert.That(result.PrebuiltParams![0], Is.EqualTo("hello"));
            Assert.That(result.PrebuiltParamIndex, Is.EqualTo(1));
        });
    }

    [Test]
    public void JoinedQueryBuilder3_AsJoined_ReturnsBuilder4WithSameState()
    {
        var dialect = SqlDialect.PostgreSQL;
        var state = new Quarry.Internal.QueryState(dialect, "users", null);
        var builder = new JoinedQueryBuilder3<TestUser, TestOrder, TestItem>(state);

        var result = builder.AsJoined<TestCategory>();

        Assert.That(result, Is.InstanceOf<JoinedQueryBuilder4<TestUser, TestOrder, TestItem, TestCategory>>());
    }

    [Test]
    public void JoinedQueryBuilder3_AsJoined_DoesNotMutateJoinClauses()
    {
        var dialect = SqlDialect.PostgreSQL;
        var state = new Quarry.Internal.QueryState(dialect, "users", null);
        var builder = new JoinedQueryBuilder3<TestUser, TestOrder, TestItem>(state);

        var result = builder.AsJoined<TestCategory>();

        var resultState = result.State;
        Assert.Multiple(() =>
        {
            Assert.That(resultState.JoinClauses.Length, Is.EqualTo(0));
            Assert.That(resultState.FromTableAlias, Is.Null);
        });
    }

    [Test]
    public void JoinedQueryBuilder3_AsJoined_PropagatesPrebuiltParams()
    {
        var dialect = SqlDialect.PostgreSQL;
        var state = new Quarry.Internal.QueryState(dialect, "users", null);
        var builder = new JoinedQueryBuilder3<TestUser, TestOrder, TestItem>(state);
        builder.AllocatePrebuiltParams(5);
        builder.BindParam(100);
        builder.BindParam(200);

        var result = builder.AsJoined<TestCategory>();

        Assert.Multiple(() =>
        {
            Assert.That(result.PrebuiltParams, Is.Not.Null);
            Assert.That(result.PrebuiltParams!.Length, Is.EqualTo(5));
            Assert.That(result.PrebuiltParams![0], Is.EqualTo(100));
            Assert.That(result.PrebuiltParams![1], Is.EqualTo(200));
            Assert.That(result.PrebuiltParamIndex, Is.EqualTo(2));
        });
    }

    [Test]
    public void AsJoined_VsAddJoinClause_StateIsDifferent()
    {
        // AsJoined should NOT add JoinClauses, while AddJoinClause should
        var dialect = SqlDialect.PostgreSQL;
        var builder = new QueryBuilder<TestUser>(dialect, "users", null);

        var asJoinedResult = builder.AsJoined<TestOrder>();
        var addJoinResult = builder.AddJoinClause<TestOrder>(CoreJoinKind.Inner, "orders", "cond");

        Assert.Multiple(() =>
        {
            // AsJoined: no join clauses, no alias
            Assert.That(asJoinedResult.State.JoinClauses.Length, Is.EqualTo(0));
            Assert.That(asJoinedResult.State.FromTableAlias, Is.Null);

            // AddJoinClause: has join clause and alias
            Assert.That(addJoinResult.State.JoinClauses.Length, Is.EqualTo(1));
            Assert.That(addJoinResult.State.FromTableAlias, Is.EqualTo("t0"));
        });
    }

    #endregion

    #region Test Entity Classes

    private class TestUser
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
    }

    private class TestOrder
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public decimal Total { get; set; }
    }

    private class TestItem
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public string ItemName { get; set; } = null!;
    }

    private class TestCategory
    {
        public int Id { get; set; }
        public string CategoryName { get; set; } = null!;
    }

    #endregion
}
