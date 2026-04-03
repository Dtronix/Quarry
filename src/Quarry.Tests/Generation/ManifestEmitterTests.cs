using System.Collections.Generic;
using NUnit.Framework;
using Quarry.Generators.CodeGen;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Tests.Testing;
using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;
using IRQueryPlan = Quarry.Generators.IR.QueryPlan;

namespace Quarry.Tests.Generation;

[TestFixture]
public class ManifestEmitterTests
{
    #region BuildChainShape Tests

    [Test]
    public void BuildChainShape_WhereSelectFetchAll_ProducesExpectedShape()
    {
        var chainRoot = new TestCallSiteBuilder()
            .WithMethodName("Users")
            .WithKind(InterceptorKind.ChainRoot)
            .WithEntityType("User")
            .WithUniqueId("root_0")
            .Build();

        var where = TestCallSiteBuilder.CreateWhereSite("User", uniqueId: "where_0");

        var select = TestCallSiteBuilder.CreateSelectSite("User", "(int, string)");

        var execution = TestCallSiteBuilder.CreateExecutionSite(
            InterceptorKind.ExecuteFetchAll, "User", "(int, string)");

        var plan = CreatePlanWithSites(execution, new[] { chainRoot, where, select },
            "SELECT 1");

        var shape = ManifestEmitter.BuildChainShape(plan);
        Assert.That(shape, Is.EqualTo("Users().Where(...).Select(...).ExecuteFetchAllAsync()"));
    }

    [Test]
    public void BuildChainShape_DeleteWithWhere_ProducesExpectedShape()
    {
        var chainRoot = new TestCallSiteBuilder()
            .WithMethodName("Users")
            .WithKind(InterceptorKind.ChainRoot)
            .WithEntityType("User")
            .WithUniqueId("root_0")
            .Build();

        var deleteTransition = new TestCallSiteBuilder()
            .WithMethodName("Delete")
            .WithKind(InterceptorKind.DeleteTransition)
            .WithEntityType("User")
            .WithUniqueId("del_0")
            .Build();

        var where = new TestCallSiteBuilder()
            .WithMethodName("Where")
            .WithKind(InterceptorKind.DeleteWhere)
            .WithEntityType("User")
            .WithUniqueId("where_0")
            .Build();

        var execution = TestCallSiteBuilder.CreateExecutionSite(
            InterceptorKind.ExecuteNonQuery, "User", "int");

        var plan = CreatePlanWithSites(execution, new[] { chainRoot, deleteTransition, where },
            "DELETE FROM users WHERE 1=1");

        var shape = ManifestEmitter.BuildChainShape(plan);
        Assert.That(shape, Is.EqualTo("Users().Delete().Where(...).ExecuteNonQueryAsync()"));
    }

    [Test]
    public void BuildChainShape_WithPrepare_IncludesPrepareMarker()
    {
        var chainRoot = new TestCallSiteBuilder()
            .WithMethodName("Users")
            .WithKind(InterceptorKind.ChainRoot)
            .WithEntityType("User")
            .WithUniqueId("root_0")
            .Build();

        var select = TestCallSiteBuilder.CreateSelectSite("User", "(int, string)");

        var execution = TestCallSiteBuilder.CreateExecutionSite(
            InterceptorKind.ExecuteFetchAll, "User", "(int, string)");

        var prepareSite = new TestCallSiteBuilder()
            .WithMethodName("Prepare")
            .WithKind(InterceptorKind.Prepare)
            .WithEntityType("User")
            .WithUniqueId("prepare_0")
            .Build();

        var plan = CreatePlanWithSites(execution, new[] { chainRoot, select },
            "SELECT 1", prepareSite: prepareSite);

        var shape = ManifestEmitter.BuildChainShape(plan);
        Assert.That(shape, Is.EqualTo("Users().Select(...).Prepare().ExecuteFetchAllAsync()"));
    }

    [Test]
    public void BuildChainShape_WithModifiers_IncludesModifiers()
    {
        var chainRoot = new TestCallSiteBuilder()
            .WithMethodName("Users")
            .WithKind(InterceptorKind.ChainRoot)
            .WithEntityType("User")
            .WithUniqueId("root_0")
            .Build();

        var select = TestCallSiteBuilder.CreateSelectSite("User", "(int, string)");

        var limit = new TestCallSiteBuilder()
            .WithMethodName("Limit")
            .WithKind(InterceptorKind.Limit)
            .WithEntityType("User")
            .WithUniqueId("limit_0")
            .Build();

        var offset = new TestCallSiteBuilder()
            .WithMethodName("Offset")
            .WithKind(InterceptorKind.Offset)
            .WithEntityType("User")
            .WithUniqueId("offset_0")
            .Build();

        var execution = TestCallSiteBuilder.CreateExecutionSite(
            InterceptorKind.ExecuteFetchAll, "User", "(int, string)");

        var plan = CreatePlanWithSites(execution,
            new[] { chainRoot, select, limit, offset },
            "SELECT 1");

        var shape = ManifestEmitter.BuildChainShape(plan);
        Assert.That(shape, Is.EqualTo("Users().Select(...).Limit(...).Offset(...).ExecuteFetchAllAsync()"));
    }

    #endregion

    #region FormatClrType Tests

    [Test]
    [TestCase("System.String", false, null, "`string`")]
    [TestCase("System.Int32", false, null, "`int`")]
    [TestCase("System.Boolean", false, null, "`bool`")]
    [TestCase("System.DateTime", false, null, "`DateTime`")]
    [TestCase("System.Nullable<System.Int32>", false, null, "`int?`")]
    [TestCase("System.Int32", true, "System.Int32", "`int[]`")]
    [TestCase("System.String", true, "System.String", "`string[]`")]
    [TestCase("MyApp.CustomType", false, null, "`MyApp.CustomType`")]
    [TestCase("System.Decimal", false, null, "`decimal`")]
    public void FormatClrType_ProducesExpectedDisplay(
        string clrType, bool isCollection, string? elementTypeName, string expected)
    {
        var result = ManifestEmitter.FormatClrType(clrType, isCollection, elementTypeName);
        Assert.That(result, Is.EqualTo(expected));
    }

    #endregion

    #region SimplifyTypeName Tests

    [Test]
    [TestCase("System.String", "string")]
    [TestCase("System.Int64", "long")]
    [TestCase("System.Double", "double")]
    [TestCase("System.Byte", "byte")]
    [TestCase("Nullable<System.Int32>", "int?")]
    [TestCase("System.Nullable<System.Boolean>", "bool?")]
    [TestCase("System.Guid", "Guid")]
    [TestCase("System.DateTimeOffset", "DateTimeOffset")]
    public void SimplifyTypeName_ProducesExpectedResult(string input, string expected)
    {
        Assert.That(ManifestEmitter.SimplifyTypeName(input), Is.EqualTo(expected));
    }

    #endregion

    #region RenderManifest Tests

    [Test]
    public void RenderManifest_SingleUnconditionalChain_ProducesExpectedMarkdown()
    {
        var execution = TestCallSiteBuilder.CreateExecutionSite(
            InterceptorKind.ExecuteFetchAll, "User", "(int, string)");

        var chainRoot = new TestCallSiteBuilder()
            .WithMethodName("Users")
            .WithKind(InterceptorKind.ChainRoot)
            .WithEntityType("User")
            .WithUniqueId("root_0")
            .Build();

        var plan = CreatePlanWithSql(execution, new[] { chainRoot },
            "SELECT \"u\".\"user_id\", \"u\".\"user_name\" FROM \"users\" AS \"u\"",
            dialect: GenSqlDialect.SQLite);

        var plans = new List<(AssembledPlan, string, string)>
        {
            (plan, "TestDb", "TestApp")
        };

        var markdown = ManifestEmitter.RenderManifest(GenSqlDialect.SQLite, plans);

        Assert.That(markdown, Does.Contain("# Quarry SQL Manifest \u2014 SQLite"));
        Assert.That(markdown, Does.Contain("## TestDb"));
        Assert.That(markdown, Does.Contain("### Users().ExecuteFetchAllAsync()"));
        Assert.That(markdown, Does.Contain("SELECT \"u\".\"user_id\", \"u\".\"user_name\" FROM \"users\" AS \"u\""));
        Assert.That(markdown, Does.Contain("```sql"));
    }

    [Test]
    public void RenderManifest_MultipleContexts_SortedAlphabetically()
    {
        var exec1 = TestCallSiteBuilder.CreateExecutionSite(
            InterceptorKind.ExecuteFetchAll, "User", "User", uniqueId: "exec_1");
        var root1 = new TestCallSiteBuilder()
            .WithMethodName("Users")
            .WithKind(InterceptorKind.ChainRoot)
            .WithEntityType("User")
            .WithUniqueId("root_1")
            .Build();

        var exec2 = TestCallSiteBuilder.CreateExecutionSite(
            InterceptorKind.ExecuteFetchAll, "Order", "Order", uniqueId: "exec_2");
        var root2 = new TestCallSiteBuilder()
            .WithMethodName("Orders")
            .WithKind(InterceptorKind.ChainRoot)
            .WithEntityType("Order")
            .WithUniqueId("root_2")
            .Build();

        var plan1 = CreatePlanWithSql(exec1, new[] { root1 }, "SELECT 1",
            dialect: GenSqlDialect.SQLite);
        var plan2 = CreatePlanWithSql(exec2, new[] { root2 }, "SELECT 2",
            dialect: GenSqlDialect.SQLite);

        var plans = new List<(AssembledPlan, string, string)>
        {
            (plan2, "ZetaDb", "App"),
            (plan1, "AlphaDb", "App")
        };

        var markdown = ManifestEmitter.RenderManifest(GenSqlDialect.SQLite, plans);

        var alphaIndex = markdown.IndexOf("## AlphaDb");
        var zetaIndex = markdown.IndexOf("## ZetaDb");
        Assert.That(alphaIndex, Is.LessThan(zetaIndex),
            "AlphaDb should appear before ZetaDb");
    }

    [Test]
    public void RenderManifest_WithParameters_RendersParameterTable()
    {
        var execution = TestCallSiteBuilder.CreateExecutionSite(
            InterceptorKind.ExecuteFetchAll, "User", "(int, string)");
        var chainRoot = new TestCallSiteBuilder()
            .WithMethodName("Users")
            .WithKind(InterceptorKind.ChainRoot)
            .WithEntityType("User")
            .WithUniqueId("root_0")
            .Build();

        var parameters = new[]
        {
            new QueryParameter(0, "System.Boolean", "active", isSensitive: false),
            new QueryParameter(1, "System.String", "name", isSensitive: true),
        };

        var plan = CreatePlanWithParams(execution, new[] { chainRoot },
            "SELECT 1 WHERE active = @p0 AND name = @p1",
            parameters, dialect: GenSqlDialect.SQLite);

        var plans = new List<(AssembledPlan, string, string)>
        {
            (plan, "TestDb", "TestApp")
        };

        var markdown = ManifestEmitter.RenderManifest(GenSqlDialect.SQLite, plans);

        Assert.That(markdown, Does.Contain("| `@p0` | `bool` |"));
        Assert.That(markdown, Does.Contain("| `@p1` | `string` `[sensitive]` |"));
        Assert.That(markdown, Does.Contain("| Parameter | Type |"));
    }

    [Test]
    public void RenderManifest_CollectionParameter_ShowsArrayType()
    {
        var execution = TestCallSiteBuilder.CreateExecutionSite(
            InterceptorKind.ExecuteFetchAll, "User", "(int, string)");
        var chainRoot = new TestCallSiteBuilder()
            .WithMethodName("Users")
            .WithKind(InterceptorKind.ChainRoot)
            .WithEntityType("User")
            .WithUniqueId("root_0")
            .Build();

        var parameters = new[]
        {
            new QueryParameter(0, "System.Collections.Generic.IEnumerable<System.Int32>",
                "ids", isCollection: true, elementTypeName: "System.Int32"),
        };

        var plan = CreatePlanWithParams(execution, new[] { chainRoot },
            "SELECT 1 WHERE id IN @p0", parameters, dialect: GenSqlDialect.SQLite);

        var plans = new List<(AssembledPlan, string, string)>
        {
            (plan, "TestDb", "TestApp")
        };

        var markdown = ManifestEmitter.RenderManifest(GenSqlDialect.SQLite, plans);

        Assert.That(markdown, Does.Contain("`int[]`"));
    }

    [Test]
    public void RenderManifest_MultipleVariants_RendersAllVariantsInSingleBlock()
    {
        var execution = TestCallSiteBuilder.CreateExecutionSite(
            InterceptorKind.ExecuteFetchAll, "User", "(int, string)");
        var chainRoot = new TestCallSiteBuilder()
            .WithMethodName("Users")
            .WithKind(InterceptorKind.ChainRoot)
            .WithEntityType("User")
            .WithUniqueId("root_0")
            .Build();

        var sqlVariants = new Dictionary<int, AssembledSqlVariant>
        {
            [0] = new AssembledSqlVariant("SELECT * FROM users WHERE active = @p0", 1),
            [1] = new AssembledSqlVariant("SELECT * FROM users WHERE active = @p0 AND email IS NOT NULL", 1),
        };

        var plan = CreatePlanWithVariants(execution, new[] { chainRoot },
            sqlVariants, possibleMasks: new[] { 0, 1 },
            dialect: GenSqlDialect.SQLite);

        var plans = new List<(AssembledPlan, string, string)>
        {
            (plan, "TestDb", "TestApp")
        };

        var markdown = ManifestEmitter.RenderManifest(GenSqlDialect.SQLite, plans);

        Assert.That(markdown, Does.Contain("\u2014 2 variants"));
        Assert.That(markdown, Does.Contain("-- base"));
        Assert.That(markdown, Does.Contain("SELECT * FROM users WHERE active = @p0"));
        Assert.That(markdown, Does.Contain("SELECT * FROM users WHERE active = @p0 AND email IS NOT NULL"));
    }

    [Test]
    public void RenderManifest_NoParameters_OmitsParameterTable()
    {
        var execution = TestCallSiteBuilder.CreateExecutionSite(
            InterceptorKind.ExecuteFetchAll, "User", "User");
        var chainRoot = new TestCallSiteBuilder()
            .WithMethodName("Users")
            .WithKind(InterceptorKind.ChainRoot)
            .WithEntityType("User")
            .WithUniqueId("root_0")
            .Build();

        var plan = CreatePlanWithSql(execution, new[] { chainRoot },
            "SELECT * FROM users", dialect: GenSqlDialect.SQLite);

        var plans = new List<(AssembledPlan, string, string)>
        {
            (plan, "TestDb", "TestApp")
        };

        var markdown = ManifestEmitter.RenderManifest(GenSqlDialect.SQLite, plans);

        Assert.That(markdown, Does.Not.Contain("| Parameter |"));
    }

    [Test]
    public void RenderManifest_AutoGeneratedHeader_Present()
    {
        var execution = TestCallSiteBuilder.CreateExecutionSite(
            InterceptorKind.ExecuteFetchAll, "User", "User");
        var chainRoot = new TestCallSiteBuilder()
            .WithMethodName("Users")
            .WithKind(InterceptorKind.ChainRoot)
            .WithEntityType("User")
            .WithUniqueId("root_0")
            .Build();

        var plan = CreatePlanWithSql(execution, new[] { chainRoot },
            "SELECT 1", dialect: GenSqlDialect.SQLite);

        var plans = new List<(AssembledPlan, string, string)>
        {
            (plan, "TestDb", "TestApp")
        };

        var markdown = ManifestEmitter.RenderManifest(GenSqlDialect.SQLite, plans);

        Assert.That(markdown, Does.Contain("> Auto-generated by Quarry. Do not edit manually."));
    }

    [Test]
    public void RenderManifest_ChainsWithinContextSortedByShape()
    {
        var exec1 = TestCallSiteBuilder.CreateExecutionSite(
            InterceptorKind.ExecuteFetchAll, "User", "User", uniqueId: "exec_z");
        var root1 = new TestCallSiteBuilder()
            .WithMethodName("Users")
            .WithKind(InterceptorKind.ChainRoot)
            .WithEntityType("User")
            .WithUniqueId("root_z")
            .Build();
        var where1 = TestCallSiteBuilder.CreateWhereSite("User", uniqueId: "where_z");

        var exec2 = TestCallSiteBuilder.CreateExecutionSite(
            InterceptorKind.ExecuteFetchAll, "User", "User", uniqueId: "exec_a");
        var root2 = new TestCallSiteBuilder()
            .WithMethodName("Accounts")
            .WithKind(InterceptorKind.ChainRoot)
            .WithEntityType("User")
            .WithUniqueId("root_a")
            .Build();

        var plan1 = CreatePlanWithSql(exec1, new[] { root1, where1 }, "SELECT 1",
            dialect: GenSqlDialect.SQLite);
        var plan2 = CreatePlanWithSql(exec2, new[] { root2 }, "SELECT 2",
            dialect: GenSqlDialect.SQLite);

        var plans = new List<(AssembledPlan, string, string)>
        {
            (plan1, "TestDb", "TestApp"),
            (plan2, "TestDb", "TestApp")
        };

        var markdown = ManifestEmitter.RenderManifest(GenSqlDialect.SQLite, plans);

        // "Accounts" comes before "Users" alphabetically
        var accountsIndex = markdown.IndexOf("### Accounts()");
        var usersIndex = markdown.IndexOf("### Users()");
        Assert.That(accountsIndex, Is.LessThan(usersIndex),
            "Accounts chain should appear before Users chain (alphabetical sort)");
    }

    [Test]
    public void RenderManifest_HorizontalRuleBetweenChains_ButNotAfterLast()
    {
        var exec1 = TestCallSiteBuilder.CreateExecutionSite(
            InterceptorKind.ExecuteFetchAll, "User", "User", uniqueId: "exec_1");
        var root1 = new TestCallSiteBuilder()
            .WithMethodName("Users")
            .WithKind(InterceptorKind.ChainRoot)
            .WithEntityType("User")
            .WithUniqueId("root_1")
            .Build();

        var exec2 = TestCallSiteBuilder.CreateExecutionSite(
            InterceptorKind.ExecuteFetchAll, "User", "User", uniqueId: "exec_2");
        var root2 = new TestCallSiteBuilder()
            .WithMethodName("Accounts")
            .WithKind(InterceptorKind.ChainRoot)
            .WithEntityType("User")
            .WithUniqueId("root_2")
            .Build();

        var plan1 = CreatePlanWithSql(exec1, new[] { root1 }, "SELECT 1",
            dialect: GenSqlDialect.SQLite);
        var plan2 = CreatePlanWithSql(exec2, new[] { root2 }, "SELECT 2",
            dialect: GenSqlDialect.SQLite);

        var plans = new List<(AssembledPlan, string, string)>
        {
            (plan1, "TestDb", "TestApp"),
            (plan2, "TestDb", "TestApp")
        };

        var markdown = ManifestEmitter.RenderManifest(GenSqlDialect.SQLite, plans);

        // Should have exactly one "---" separator between the two chains
        var hrCount = System.Text.RegularExpressions.Regex.Matches(markdown, @"^---\r?$",
            System.Text.RegularExpressions.RegexOptions.Multiline).Count;
        Assert.That(hrCount, Is.EqualTo(1), "Should have exactly one horizontal rule between two chains");
    }

    #endregion

    #region Dialect File Name Tests

    [Test]
    [TestCase(0, "SQLite")]      // GenSqlDialect.SQLite
    [TestCase(1, "PostgreSQL")]  // GenSqlDialect.PostgreSQL
    [TestCase(2, "MySQL")]       // GenSqlDialect.MySQL
    [TestCase(3, "SQL Server")]  // GenSqlDialect.SqlServer
    public void RenderManifest_DialectHeader_ShowsCorrectDialectName(
        int dialectInt, string expectedName)
    {
        var dialect = (GenSqlDialect)dialectInt;
        var execution = TestCallSiteBuilder.CreateExecutionSite(
            InterceptorKind.ExecuteFetchAll, "User", "User");
        var chainRoot = new TestCallSiteBuilder()
            .WithMethodName("Users")
            .WithKind(InterceptorKind.ChainRoot)
            .WithEntityType("User")
            .WithUniqueId("root_0")
            .Build();

        var plan = CreatePlanWithSql(execution, new[] { chainRoot },
            "SELECT 1", dialect: dialect);

        var plans = new List<(AssembledPlan, string, string)>
        {
            (plan, "TestDb", "TestApp")
        };

        var markdown = ManifestEmitter.RenderManifest(dialect, plans);
        Assert.That(markdown, Does.Contain($"# Quarry SQL Manifest \u2014 {expectedName}"));
    }

    #endregion

    #region Helper Methods

    private static AssembledPlan CreatePlanWithSites(
        TranslatedCallSite executionSite,
        TranslatedCallSite[] clauseSites,
        string sql,
        TranslatedCallSite? prepareSite = null)
    {
        var queryPlan = TestPlanHelper.CreateQueryPlanWithProjection(null);
        var sqlVariants = new Dictionary<int, AssembledSqlVariant>
        {
            [0] = new AssembledSqlVariant(sql, 0)
        };

        return new AssembledPlan(
            plan: queryPlan,
            sqlVariants: sqlVariants,
            readerDelegateCode: null,
            maxParameterCount: 0,
            executionSite: executionSite,
            clauseSites: clauseSites,
            entityTypeName: executionSite.EntityTypeName,
            resultTypeName: executionSite.ResultTypeName,
            dialect: GenSqlDialect.SQLite,
            prepareSite: prepareSite);
    }

    private static AssembledPlan CreatePlanWithSql(
        TranslatedCallSite executionSite,
        TranslatedCallSite[] clauseSites,
        string sql,
        GenSqlDialect dialect = GenSqlDialect.SQLite)
    {
        var queryPlan = TestPlanHelper.CreateQueryPlanWithProjection(null);
        var sqlVariants = new Dictionary<int, AssembledSqlVariant>
        {
            [0] = new AssembledSqlVariant(sql, 0)
        };

        return new AssembledPlan(
            plan: queryPlan,
            sqlVariants: sqlVariants,
            readerDelegateCode: null,
            maxParameterCount: 0,
            executionSite: executionSite,
            clauseSites: clauseSites,
            entityTypeName: executionSite.EntityTypeName,
            resultTypeName: executionSite.ResultTypeName,
            dialect: dialect);
    }

    private static AssembledPlan CreatePlanWithParams(
        TranslatedCallSite executionSite,
        TranslatedCallSite[] clauseSites,
        string sql,
        QueryParameter[] parameters,
        GenSqlDialect dialect = GenSqlDialect.SQLite)
    {
        var queryPlan = new IRQueryPlan(
            kind: QueryKind.Select,
            primaryTable: new TableRef("users", null, "t0"),
            joins: Array.Empty<JoinPlan>(),
            whereTerms: Array.Empty<WhereTerm>(),
            orderTerms: Array.Empty<OrderTerm>(),
            groupByExprs: Array.Empty<SqlExpr>(),
            havingExprs: Array.Empty<SqlExpr>(),
            projection: new SelectProjection(
                ProjectionKind.Entity, "User",
                Array.Empty<ProjectedColumn>(), isIdentity: true),
            pagination: null,
            isDistinct: false,
            setTerms: Array.Empty<SetTerm>(),
            insertColumns: Array.Empty<InsertColumn>(),
            conditionalTerms: Array.Empty<ConditionalTerm>(),
            possibleMasks: new int[] { 0 },
            parameters: parameters,
            tier: OptimizationTier.PrebuiltDispatch);

        var sqlVariants = new Dictionary<int, AssembledSqlVariant>
        {
            [0] = new AssembledSqlVariant(sql, parameters.Length)
        };

        return new AssembledPlan(
            plan: queryPlan,
            sqlVariants: sqlVariants,
            readerDelegateCode: null,
            maxParameterCount: parameters.Length,
            executionSite: executionSite,
            clauseSites: clauseSites,
            entityTypeName: executionSite.EntityTypeName,
            resultTypeName: executionSite.ResultTypeName,
            dialect: dialect);
    }

    private static AssembledPlan CreatePlanWithVariants(
        TranslatedCallSite executionSite,
        TranslatedCallSite[] clauseSites,
        Dictionary<int, AssembledSqlVariant> sqlVariants,
        int[] possibleMasks,
        GenSqlDialect dialect = GenSqlDialect.SQLite)
    {
        var queryPlan = new IRQueryPlan(
            kind: QueryKind.Select,
            primaryTable: new TableRef("users", null, "t0"),
            joins: Array.Empty<JoinPlan>(),
            whereTerms: Array.Empty<WhereTerm>(),
            orderTerms: Array.Empty<OrderTerm>(),
            groupByExprs: Array.Empty<SqlExpr>(),
            havingExprs: Array.Empty<SqlExpr>(),
            projection: new SelectProjection(
                ProjectionKind.Entity, "User",
                Array.Empty<ProjectedColumn>(), isIdentity: true),
            pagination: null,
            isDistinct: false,
            setTerms: Array.Empty<SetTerm>(),
            insertColumns: Array.Empty<InsertColumn>(),
            conditionalTerms: Array.Empty<ConditionalTerm>(),
            possibleMasks: possibleMasks,
            parameters: Array.Empty<QueryParameter>(),
            tier: OptimizationTier.PrebuiltDispatch);

        return new AssembledPlan(
            plan: queryPlan,
            sqlVariants: sqlVariants,
            readerDelegateCode: null,
            maxParameterCount: 0,
            executionSite: executionSite,
            clauseSites: clauseSites,
            entityTypeName: executionSite.EntityTypeName,
            resultTypeName: executionSite.ResultTypeName,
            dialect: dialect);
    }

    #endregion
}
