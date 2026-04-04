using System;
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

    #region SimplifyTypeName Tests

    [Test]
    [TestCase("System.String", "string")]
    [TestCase("System.Int32", "int")]
    [TestCase("System.Boolean", "bool")]
    [TestCase("System.DateTime", "DateTime")]
    [TestCase("System.Int64", "long")]
    [TestCase("System.Double", "double")]
    [TestCase("System.Decimal", "decimal")]
    [TestCase("System.Byte", "byte")]
    [TestCase("Nullable<System.Int32>", "int?")]
    [TestCase("System.Nullable<System.Boolean>", "bool?")]
    [TestCase("System.Guid", "Guid")]
    [TestCase("System.DateTimeOffset", "DateTimeOffset")]
    [TestCase("MyApp.CustomType", "MyApp.CustomType")]
    [TestCase("?", "object")]
    [TestCase("", "object")]
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
        Assert.That(markdown, Does.Contain("| `@p1` | `string` | Yes |"));
        Assert.That(markdown, Does.Contain("| Parameter | Type | Sensitive |"));
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
        // Count HRs in the query body only (exclude the HR + Summary section at the end)
        var summaryHrIdx = markdown.IndexOf("---\r\n\r\n## Summary");
        if (summaryHrIdx < 0) summaryHrIdx = markdown.IndexOf("---\n\n## Summary");
        var bodyMarkdown = summaryHrIdx >= 0 ? markdown.Substring(0, summaryHrIdx) : markdown;
        var hrCount = System.Text.RegularExpressions.Regex.Matches(bodyMarkdown, @"^---\r?$",
            System.Text.RegularExpressions.RegexOptions.Multiline).Count;
        Assert.That(hrCount, Is.EqualTo(1), "Should have exactly one horizontal rule between two chains");
    }

    #endregion

    #region Excluded Count Tests

    [Test]
    public void RenderManifest_SummaryTable_ShowsAllCounts()
    {
        var exec1 = TestCallSiteBuilder.CreateExecutionSite(
            InterceptorKind.ExecuteFetchAll, "User", "User", uniqueId: "exec_1");
        var root1 = new TestCallSiteBuilder()
            .WithMethodName("Users")
            .WithKind(InterceptorKind.ChainRoot)
            .WithEntityType("User")
            .WithUniqueId("root_1")
            .Build();

        // Two plans with identical shape+SQL → one will be consolidated
        var exec2 = TestCallSiteBuilder.CreateExecutionSite(
            InterceptorKind.ExecuteFetchAll, "User", "User", uniqueId: "exec_2");
        var root2 = new TestCallSiteBuilder()
            .WithMethodName("Users")
            .WithKind(InterceptorKind.ChainRoot)
            .WithEntityType("User")
            .WithUniqueId("root_2")
            .Build();

        var plan1 = CreatePlanWithSql(exec1, new[] { root1 }, "SELECT 1",
            dialect: GenSqlDialect.SQLite);
        var plan2 = CreatePlanWithSql(exec2, new[] { root2 }, "SELECT 1",
            dialect: GenSqlDialect.SQLite);

        var plans = new List<(AssembledPlan, string, string)>
        {
            (plan1, "TestDb", "TestApp"),
            (plan2, "TestDb", "TestApp")
        };

        // totalCount=5 (2 valid + 3 skipped), excludedCount=3
        var markdown = ManifestEmitter.RenderManifest(GenSqlDialect.SQLite, plans,
            totalCount: 5, excludedCount: 3);

        Assert.That(markdown, Does.Contain("## Summary"));
        Assert.That(markdown, Does.Contain("| Total discovered | 5 |"));
        Assert.That(markdown, Does.Contain("| Skipped (errors) | 3 |"));
        Assert.That(markdown, Does.Contain("| Consolidated (deduped) | 1 |"));
        Assert.That(markdown, Does.Contain("| Rendered | 1 |"));
    }

    [Test]
    public void RenderManifest_SummaryTable_ZeroSkipped()
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

        var markdown = ManifestEmitter.RenderManifest(GenSqlDialect.SQLite, plans,
            totalCount: 1, excludedCount: 0);

        Assert.That(markdown, Does.Contain("| Total discovered | 1 |"));
        Assert.That(markdown, Does.Contain("| Skipped (errors) | 0 |"));
        Assert.That(markdown, Does.Contain("| Rendered | 1 |"));
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

    #region BuildBitIndexToConditionText Tests

    [Test]
    public void BuildBitIndexToConditionText_NoConds_ReturnsEmpty()
    {
        var execution = TestCallSiteBuilder.CreateExecutionSite(
            InterceptorKind.ExecuteFetchAll, "User", "User");
        var chainRoot = new TestCallSiteBuilder()
            .WithMethodName("Users")
            .WithKind(InterceptorKind.ChainRoot)
            .WithEntityType("User")
            .WithUniqueId("root_0")
            .Build();

        var plan = CreatePlanWithConditionals(execution, new[] { chainRoot },
            "SELECT 1", Array.Empty<ConditionalTerm>(), new int[] { 0 });

        var result = ManifestEmitter.BuildBitIndexToConditionText(plan);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void BuildBitIndexToConditionText_SingleConditional_MapsCorrectly()
    {
        // Execution site at baseline depth 1
        var execution = new TestCallSiteBuilder()
            .WithMethodName("ExecuteFetchAllAsync")
            .WithKind(InterceptorKind.ExecuteFetchAll)
            .WithEntityType("User")
            .WithResultType("User")
            .WithUniqueId("exec_0")
            .WithNestingContext("baseline", 1)
            .Build();

        var chainRoot = new TestCallSiteBuilder()
            .WithMethodName("Users")
            .WithKind(InterceptorKind.ChainRoot)
            .WithEntityType("User")
            .WithUniqueId("root_0")
            .Build();

        // Where clause at depth 2 (relative depth 1 → conditional)
        var where = new TestCallSiteBuilder()
            .WithMethodName("Where")
            .WithKind(InterceptorKind.Where)
            .WithEntityType("User")
            .WithUniqueId("where_0")
            .WithNestingContext("id > 0", 2)
            .Build();

        var plan = CreatePlanWithConditionals(execution, new[] { chainRoot, where },
            "SELECT 1", new[] { new ConditionalTerm(0, ClauseRole.Where) },
            new int[] { 0, 1 });

        var result = ManifestEmitter.BuildBitIndexToConditionText(plan);

        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0], Is.EqualTo("id > 0"));
    }

    [Test]
    public void BuildBitIndexToConditionText_MultipleConditionals_MapsInOrder()
    {
        var execution = new TestCallSiteBuilder()
            .WithMethodName("ExecuteFetchAllAsync")
            .WithKind(InterceptorKind.ExecuteFetchAll)
            .WithEntityType("User")
            .WithResultType("User")
            .WithUniqueId("exec_0")
            .WithNestingContext("baseline", 1)
            .Build();

        var chainRoot = new TestCallSiteBuilder()
            .WithMethodName("Users")
            .WithKind(InterceptorKind.ChainRoot)
            .WithEntityType("User")
            .WithUniqueId("root_0")
            .Build();

        var where = new TestCallSiteBuilder()
            .WithMethodName("Where")
            .WithKind(InterceptorKind.Where)
            .WithEntityType("User")
            .WithUniqueId("where_0")
            .WithNestingContext("id > 0", 2)
            .Build();

        var orderBy = new TestCallSiteBuilder()
            .WithMethodName("OrderBy")
            .WithKind(InterceptorKind.OrderBy)
            .WithEntityType("User")
            .WithUniqueId("order_0")
            .WithNestingContext("name != null", 2)
            .Build();

        var plan = CreatePlanWithConditionals(execution, new[] { chainRoot, where, orderBy },
            "SELECT 1",
            new[]
            {
                new ConditionalTerm(0, ClauseRole.Where),
                new ConditionalTerm(1, ClauseRole.OrderBy)
            },
            new int[] { 0, 1, 2, 3 });

        var result = ManifestEmitter.BuildBitIndexToConditionText(plan);

        Assert.That(result.Count, Is.EqualTo(2));
        Assert.That(result[0], Is.EqualTo("id > 0"));
        Assert.That(result[1], Is.EqualTo("name != null"));
    }

    [Test]
    public void BuildBitIndexToConditionText_MixedConditionalAndUnconditional_OnlyMapsConditional()
    {
        var execution = new TestCallSiteBuilder()
            .WithMethodName("ExecuteFetchAllAsync")
            .WithKind(InterceptorKind.ExecuteFetchAll)
            .WithEntityType("User")
            .WithResultType("User")
            .WithUniqueId("exec_0")
            .WithNestingContext("baseline", 1)
            .Build();

        var chainRoot = new TestCallSiteBuilder()
            .WithMethodName("Users")
            .WithKind(InterceptorKind.ChainRoot)
            .WithEntityType("User")
            .WithUniqueId("root_0")
            .Build();

        // Unconditional Where (no NestingContext)
        var whereUnconditional = new TestCallSiteBuilder()
            .WithMethodName("Where")
            .WithKind(InterceptorKind.Where)
            .WithEntityType("User")
            .WithUniqueId("where_0")
            .Build();

        // Conditional OrderBy (depth 2 > baseline 1)
        var orderBy = new TestCallSiteBuilder()
            .WithMethodName("OrderBy")
            .WithKind(InterceptorKind.OrderBy)
            .WithEntityType("User")
            .WithUniqueId("order_0")
            .WithNestingContext("sortAsc", 2)
            .Build();

        var plan = CreatePlanWithConditionals(execution,
            new[] { chainRoot, whereUnconditional, orderBy },
            "SELECT 1",
            new[] { new ConditionalTerm(0, ClauseRole.OrderBy) },
            new int[] { 0, 1 });

        var result = ManifestEmitter.BuildBitIndexToConditionText(plan);

        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0], Is.EqualTo("sortAsc"));
    }

    [Test]
    public void BuildBitIndexToConditionText_LongConditionText_Truncated()
    {
        var execution = new TestCallSiteBuilder()
            .WithMethodName("ExecuteFetchAllAsync")
            .WithKind(InterceptorKind.ExecuteFetchAll)
            .WithEntityType("User")
            .WithResultType("User")
            .WithUniqueId("exec_0")
            .WithNestingContext("baseline", 1)
            .Build();

        var chainRoot = new TestCallSiteBuilder()
            .WithMethodName("Users")
            .WithKind(InterceptorKind.ChainRoot)
            .WithEntityType("User")
            .WithUniqueId("root_0")
            .Build();

        // Condition text longer than 60 characters
        var longCondition = "someReallyLongVariableName.Property.SubProperty != null && anotherCondition";
        var where = new TestCallSiteBuilder()
            .WithMethodName("Where")
            .WithKind(InterceptorKind.Where)
            .WithEntityType("User")
            .WithUniqueId("where_0")
            .WithNestingContext(longCondition, 2)
            .Build();

        var plan = CreatePlanWithConditionals(execution, new[] { chainRoot, where },
            "SELECT 1",
            new[] { new ConditionalTerm(0, ClauseRole.Where) },
            new int[] { 0, 1 });

        var result = ManifestEmitter.BuildBitIndexToConditionText(plan);

        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0], Does.EndWith("..."));
        Assert.That(result[0].Length, Is.EqualTo(60));
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

    private static AssembledPlan CreatePlanWithConditionals(
        TranslatedCallSite executionSite,
        TranslatedCallSite[] clauseSites,
        string sql,
        ConditionalTerm[] conditionalTerms,
        int[] possibleMasks,
        QueryParameter[]? parameters = null,
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
            conditionalTerms: conditionalTerms,
            possibleMasks: possibleMasks,
            parameters: parameters ?? Array.Empty<QueryParameter>(),
            tier: OptimizationTier.PrebuiltDispatch);

        var sqlVariants = new Dictionary<int, AssembledSqlVariant>();
        foreach (var mask in possibleMasks)
            sqlVariants[mask] = new AssembledSqlVariant(sql, parameters?.Length ?? 0);

        return new AssembledPlan(
            plan: queryPlan,
            sqlVariants: sqlVariants,
            readerDelegateCode: null,
            maxParameterCount: parameters?.Length ?? 0,
            executionSite: executionSite,
            clauseSites: clauseSites,
            entityTypeName: executionSite.EntityTypeName,
            resultTypeName: executionSite.ResultTypeName,
            dialect: dialect);
    }

    #endregion
}
