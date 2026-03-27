using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using NUnit.Framework;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Tests.Testing;
using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;

namespace Quarry.Tests.IR;

[TestFixture]
public class PipelineOrchestratorTests
{
    #region PropagateChainUpdatedSites

    [Test]
    public void PropagateChainUpdatedSites_ReplacesMatchingSitesWithChainUpdated()
    {
        var originalSite = new TestCallSiteBuilder()
            .WithUniqueId("where_1")
            .WithKind(InterceptorKind.Where)
            .WithEntityType("User")
            .Build();

        var updatedSite = originalSite.WithJoinedEntityTypeNames(
            new[] { "User", "Order" }, null);

        var allSites = ImmutableArray.Create(originalSite);
        var plan = TestPlanHelper.CreateMinimalAssembledPlan(updatedSite, new[] { updatedSite });

        var result = InvokePropagateChainUpdatedSites(allSites, new List<AssembledPlan> { plan });

        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0].JoinedEntityTypeNames, Is.Not.Null);
        Assert.That(result[0].JoinedEntityTypeNames!.Count, Is.EqualTo(2));
        Assert.That(result[0].JoinedEntityTypeNames![0], Is.EqualTo("User"));
        Assert.That(result[0].JoinedEntityTypeNames![1], Is.EqualTo("Order"));
    }

    [Test]
    public void PropagateChainUpdatedSites_PreservesNonChainSites()
    {
        var chainSite = new TestCallSiteBuilder()
            .WithUniqueId("where_1")
            .WithEntityType("User")
            .Build();

        var standaloneSite = new TestCallSiteBuilder()
            .WithUniqueId("standalone_1")
            .WithEntityType("Product")
            .Build();

        var updatedChainSite = chainSite.WithJoinedEntityTypeNames(
            new[] { "User", "Order" }, null);

        var allSites = ImmutableArray.Create(chainSite, standaloneSite);
        var plan = TestPlanHelper.CreateMinimalAssembledPlan(updatedChainSite, new[] { updatedChainSite });

        var result = InvokePropagateChainUpdatedSites(allSites, new List<AssembledPlan> { plan });

        Assert.That(result, Has.Length.EqualTo(2));
        Assert.That(result[0].JoinedEntityTypeNames, Is.Not.Null);
        Assert.That(result[1].UniqueId, Is.EqualTo("standalone_1"));
        Assert.That(result[1].JoinedEntityTypeNames, Is.Null);
    }

    [Test]
    public void PropagateChainUpdatedSites_NoChains_ReturnsSameArray()
    {
        var site = new TestCallSiteBuilder()
            .WithUniqueId("where_1")
            .WithEntityType("User")
            .Build();

        var allSites = ImmutableArray.Create(site);
        var result = InvokePropagateChainUpdatedSites(allSites, new List<AssembledPlan>());

        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0].UniqueId, Is.EqualTo("where_1"));
    }

    [Test]
    public void PropagateChainUpdatedSites_ExecutionSiteAlsoUpdated()
    {
        var execSite = TestCallSiteBuilder.CreateExecutionSite(
            InterceptorKind.ExecuteFetchAll, "User", uniqueId: "exec_1");

        var updatedExecSite = execSite.WithJoinedEntityTypeNames(
            new[] { "User", "Order" }, null);

        var allSites = ImmutableArray.Create(execSite);
        var plan = TestPlanHelper.CreateMinimalAssembledPlan(updatedExecSite, Array.Empty<TranslatedCallSite>());

        var result = InvokePropagateChainUpdatedSites(allSites, new List<AssembledPlan> { plan });

        Assert.That(result[0].JoinedEntityTypeNames, Is.Not.Null);
        Assert.That(result[0].JoinedEntityTypeNames!.Count, Is.EqualTo(2));
    }

    #endregion

    #region IsUnresolvedResultType

    [TestCase(null, ExpectedResult = false, Description = "null is valid (no result type)")]
    [TestCase("", ExpectedResult = true, Description = "Empty string is unresolved")]
    [TestCase("?", ExpectedResult = true)]
    [TestCase("object", ExpectedResult = true)]
    [TestCase("int", ExpectedResult = false)]
    [TestCase("string", ExpectedResult = false)]
    [TestCase("UserDto", ExpectedResult = false)]
    [TestCase("(object, object, object)", ExpectedResult = true, Description = "Tuple with object elements")]
    [TestCase("(int, decimal, OrderPriority)", ExpectedResult = false, Description = "Tuple with resolved types")]
    [TestCase("(int OrderId, string UserName)", ExpectedResult = false, Description = "Named tuple with types")]
    [TestCase("(object OrderId, object UserName)", ExpectedResult = true, Description = "Named tuple with object types")]
    [TestCase("( OrderId,  Total,  Priority)", ExpectedResult = true, Description = "Tuple with empty type parts (leading space)")]
    [TestCase("( OrderId)", ExpectedResult = true, Description = "Single-element tuple with empty type")]
    public bool IsUnresolvedResultType_DetectsPatterns(string? resultTypeName)
    {
        return InvokeIsUnresolvedResultType(resultTypeName);
    }

    #endregion

    #region BuildResultTypePatches

    [Test]
    public void BuildResultTypePatches_PatchesUnresolvedClauseSites()
    {
        // Clause site with unresolved tuple type
        var whereSite = new TestCallSiteBuilder()
            .WithUniqueId("where_1")
            .WithKind(InterceptorKind.Where)
            .WithEntityType("Order")
            .WithResultType("(object, object, object)")
            .Build();

        var execSite = TestCallSiteBuilder.CreateExecutionSite(
            InterceptorKind.ExecuteFetchAll, "Order",
            resultType: "(int, decimal, OrderPriority)",
            uniqueId: "exec_1");

        // Projection has the resolved type
        var projection = new SelectProjection(
            ProjectionKind.Tuple,
            "(int OrderId, decimal Total, OrderPriority Priority)",
            Array.Empty<ProjectedColumn>());
        var plan = TestPlanHelper.CreateAssembledPlanWithProjection(
            execSite, new[] { whereSite }, projection);

        var patches = InvokeBuildResultTypePatches(new List<AssembledPlan> { plan });

        Assert.That(patches.ContainsKey("where_1"), Is.True);
        Assert.That(patches["where_1"], Is.EqualTo("(int OrderId, decimal Total, OrderPriority Priority)"));
    }

    [Test]
    public void BuildResultTypePatches_SkipsResolvedClauseSites()
    {
        var whereSite = new TestCallSiteBuilder()
            .WithUniqueId("where_1")
            .WithKind(InterceptorKind.Where)
            .WithEntityType("User")
            .WithResultType("(int, string)")
            .Build();

        var execSite = TestCallSiteBuilder.CreateExecutionSite(
            InterceptorKind.ExecuteFetchAll, "User",
            resultType: "(int, string)",
            uniqueId: "exec_1");

        var projection = new SelectProjection(
            ProjectionKind.Tuple, "(int, string)",
            Array.Empty<ProjectedColumn>());
        var plan = TestPlanHelper.CreateAssembledPlanWithProjection(
            execSite, new[] { whereSite }, projection);

        var patches = InvokeBuildResultTypePatches(new List<AssembledPlan> { plan });

        Assert.That(patches.ContainsKey("where_1"), Is.False,
            "Clause site with resolved type should not be patched");
    }

    [Test]
    public void BuildResultTypePatches_SkipsNullResultType()
    {
        // Clause site with null ResultTypeName (no projection — entity-only query)
        var whereSite = new TestCallSiteBuilder()
            .WithUniqueId("where_1")
            .WithKind(InterceptorKind.Where)
            .WithEntityType("User")
            .WithResultType(null)
            .Build();

        var execSite = TestCallSiteBuilder.CreateExecutionSite(
            InterceptorKind.ExecuteFetchAll, "User",
            resultType: "(int, string)",
            uniqueId: "exec_1");

        var projection = new SelectProjection(
            ProjectionKind.Tuple, "(int, string)",
            Array.Empty<ProjectedColumn>());
        var plan = TestPlanHelper.CreateAssembledPlanWithProjection(
            execSite, new[] { whereSite }, projection);

        var patches = InvokeBuildResultTypePatches(new List<AssembledPlan> { plan });

        Assert.That(patches.ContainsKey("where_1"), Is.False,
            "Clause site with null ResultTypeName should not be patched");
    }

    [Test]
    public void BuildResultTypePatches_PatchesEmptyTypeTuplePattern()
    {
        // The "( OrderId,  Total)" pattern from Roslyn error type rendering
        var whereSite = new TestCallSiteBuilder()
            .WithUniqueId("where_1")
            .WithKind(InterceptorKind.Where)
            .WithEntityType("Order")
            .WithResultType("( OrderId,  Total,  Priority)")
            .Build();

        var execSite = TestCallSiteBuilder.CreateExecutionSite(
            InterceptorKind.ExecuteFetchAll, "Order",
            resultType: "( OrderId,  Total,  Priority)",
            uniqueId: "exec_1");

        var projection = new SelectProjection(
            ProjectionKind.Tuple,
            "(int OrderId, decimal Total, OrderPriority Priority)",
            Array.Empty<ProjectedColumn>());
        var plan = TestPlanHelper.CreateAssembledPlanWithProjection(
            execSite, new[] { whereSite }, projection);

        var patches = InvokeBuildResultTypePatches(new List<AssembledPlan> { plan });

        Assert.That(patches.ContainsKey("where_1"), Is.True);
        Assert.That(patches["where_1"], Is.EqualTo("(int OrderId, decimal Total, OrderPriority Priority)"));
    }

    #endregion

    #region PropagateChainUpdatedSites with ResultType Patches

    [Test]
    public void PropagateChainUpdatedSites_AppliesResultTypePatches()
    {
        var whereSite = new TestCallSiteBuilder()
            .WithUniqueId("where_1")
            .WithKind(InterceptorKind.Where)
            .WithEntityType("Order")
            .WithResultType("(object, object, object)")
            .Build();

        var allSites = ImmutableArray.Create(whereSite);
        var plan = TestPlanHelper.CreateMinimalAssembledPlan(
            TestCallSiteBuilder.CreateExecutionSite(
                InterceptorKind.ExecuteFetchAll, "Order", uniqueId: "exec_1"),
            new[] { whereSite });

        var patches = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["where_1"] = "(int, decimal, OrderPriority)"
        };

        var result = InvokePropagateChainUpdatedSites(allSites,
            new List<AssembledPlan> { plan }, patches);

        Assert.That(result[0].ResultTypeName, Is.EqualTo("(int, decimal, OrderPriority)"));
    }

    [Test]
    public void PropagateChainUpdatedSites_PatchesDoNotAffectUnrelatedSites()
    {
        var whereSite = new TestCallSiteBuilder()
            .WithUniqueId("where_1")
            .WithKind(InterceptorKind.Where)
            .WithEntityType("Order")
            .WithResultType("(object, object)")
            .Build();

        var otherSite = new TestCallSiteBuilder()
            .WithUniqueId("other_1")
            .WithEntityType("User")
            .Build();

        var allSites = ImmutableArray.Create(whereSite, otherSite);
        var plan = TestPlanHelper.CreateMinimalAssembledPlan(
            TestCallSiteBuilder.CreateExecutionSite(
                InterceptorKind.ExecuteFetchAll, "Order", uniqueId: "exec_1"),
            new[] { whereSite });

        var patches = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["where_1"] = "(int, string)"
        };

        var result = InvokePropagateChainUpdatedSites(allSites,
            new List<AssembledPlan> { plan }, patches);

        Assert.That(result[0].ResultTypeName, Is.EqualTo("(int, string)"));
        Assert.That(result[1].ResultTypeName, Is.Null, "Unrelated site should be unchanged");
    }

    #endregion

    #region WithResolvedResultType (IR copy methods)

    [Test]
    public void WithResultTypeName_CreatesNewRawCallSiteWithUpdatedType()
    {
        var site = new TestCallSiteBuilder()
            .WithUniqueId("where_1")
            .WithEntityType("Order")
            .WithResultType("(object, object)")
            .Build();

        var patched = site.WithResolvedResultType("(int, string)");

        Assert.That(patched.ResultTypeName, Is.EqualTo("(int, string)"));
        Assert.That(patched.UniqueId, Is.EqualTo("where_1"), "UniqueId must be preserved");
        Assert.That(patched.EntityTypeName, Is.EqualTo("Order"), "EntityTypeName must be preserved");
    }

    [Test]
    public void WithResolvedResultType_PreservesAllOtherProperties()
    {
        var clause = TestCallSiteBuilder.CreateSimpleClause(ClauseKind.Where);
        var site = new TestCallSiteBuilder()
            .WithUniqueId("where_1")
            .WithKind(InterceptorKind.Where)
            .WithEntityType("Order")
            .WithResultType("(object, object)")
            .WithKeyType("string")
            .WithClause(clause)
            .WithTable("orders", "public")
            .Build();

        var patched = site.WithResolvedResultType("(int, decimal)");

        Assert.That(patched.Kind, Is.EqualTo(InterceptorKind.Where));
        Assert.That(patched.Clause, Is.EqualTo(clause));
        Assert.That(patched.KeyTypeName, Is.EqualTo("string"));
        Assert.That(patched.TableName, Is.EqualTo("orders"));
        Assert.That(patched.SchemaName, Is.EqualTo("public"));
    }

    #endregion

    #region Helpers

    private static ImmutableArray<TranslatedCallSite> InvokePropagateChainUpdatedSites(
        ImmutableArray<TranslatedCallSite> allSites,
        List<AssembledPlan> assembledPlans)
    {
        return InvokePropagateChainUpdatedSites(allSites, assembledPlans,
            new Dictionary<string, string>(StringComparer.Ordinal));
    }

    private static ImmutableArray<TranslatedCallSite> InvokePropagateChainUpdatedSites(
        ImmutableArray<TranslatedCallSite> allSites,
        List<AssembledPlan> assembledPlans,
        Dictionary<string, string> resultTypePatches)
    {
        var method = typeof(PipelineOrchestrator).GetMethod(
            "PropagateChainUpdatedSites",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (ImmutableArray<TranslatedCallSite>)method.Invoke(null,
            new object[] { allSites, assembledPlans, resultTypePatches })!;
    }

    private static bool InvokeIsUnresolvedResultType(string? resultTypeName)
    {
        var method = typeof(PipelineOrchestrator).GetMethod(
            "IsUnresolvedResultType",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (bool)method.Invoke(null, new object?[] { resultTypeName })!;
    }

    private static Dictionary<string, string> InvokeBuildResultTypePatches(
        List<AssembledPlan> assembledPlans)
    {
        var method = typeof(PipelineOrchestrator).GetMethod(
            "BuildResultTypePatches",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (Dictionary<string, string>)method.Invoke(null,
            new object[] { assembledPlans })!;
    }

    #endregion
}
