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

    #region Helpers

    private static ImmutableArray<TranslatedCallSite> InvokePropagateChainUpdatedSites(
        ImmutableArray<TranslatedCallSite> allSites,
        List<AssembledPlan> assembledPlans)
    {
        var method = typeof(PipelineOrchestrator).GetMethod(
            "PropagateChainUpdatedSites",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var emptyPatches = new Dictionary<string, string>(StringComparer.Ordinal);
        return (ImmutableArray<TranslatedCallSite>)method.Invoke(null,
            new object[] { allSites, assembledPlans, emptyPatches })!;
    }

    #endregion
}
