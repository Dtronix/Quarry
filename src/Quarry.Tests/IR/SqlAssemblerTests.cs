using System;
using System.Reflection;
using NUnit.Framework;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Tests.Testing;
using IRQueryPlan = Quarry.Generators.IR.QueryPlan;

namespace Quarry.Tests.IR;

[TestFixture]
public class SqlAssemblerTests
{
    #region ResolveResultTypeName

    [Test]
    public void ResolveResultTypeName_UsesExecutionSiteResultType_WhenAvailable()
    {
        var execSite = new TestCallSiteBuilder()
            .WithKind(InterceptorKind.ExecuteFetchAll)
            .WithEntityType("User")
            .WithResultType("UserDto")
            .Build();

        var plan = TestPlanHelper.CreateQueryPlanWithProjection(null);

        var result = InvokeResolveResultTypeName(execSite, plan);
        Assert.That(result, Is.EqualTo("UserDto"));
    }

    [Test]
    public void ResolveResultTypeName_FallsBackToProjection_WhenSiteHasNoResultType()
    {
        var execSite = new TestCallSiteBuilder()
            .WithKind(InterceptorKind.ExecuteFetchAll)
            .WithEntityType("User")
            .WithResultType(null)
            .Build();

        var projection = new SelectProjection(
            ProjectionKind.SingleColumn, "string",
            Array.Empty<ProjectedColumn>(), isIdentity: false);

        var plan = TestPlanHelper.CreateQueryPlanWithProjection(projection);

        var result = InvokeResolveResultTypeName(execSite, plan);
        Assert.That(result, Is.EqualTo("string"));
    }

    [Test]
    public void ResolveResultTypeName_IgnoresIdentityProjection()
    {
        var execSite = new TestCallSiteBuilder()
            .WithKind(InterceptorKind.ExecuteFetchAll)
            .WithEntityType("User")
            .WithResultType(null)
            .Build();

        var projection = new SelectProjection(
            ProjectionKind.Entity, "User",
            Array.Empty<ProjectedColumn>(), isIdentity: true);

        var plan = TestPlanHelper.CreateQueryPlanWithProjection(projection);

        var result = InvokeResolveResultTypeName(execSite, plan);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ResolveResultTypeName_ExecutionSiteWins_OverProjection()
    {
        // When both execution site and projection have result types,
        // execution site takes precedence
        var execSite = new TestCallSiteBuilder()
            .WithKind(InterceptorKind.ExecuteFetchAll)
            .WithEntityType("User")
            .WithResultType("UserDto")
            .Build();

        var projection = new SelectProjection(
            ProjectionKind.SingleColumn, "string",
            Array.Empty<ProjectedColumn>(), isIdentity: false);

        var plan = TestPlanHelper.CreateQueryPlanWithProjection(projection);

        var result = InvokeResolveResultTypeName(execSite, plan);
        Assert.That(result, Is.EqualTo("UserDto"));
    }

    #endregion

    #region Helpers

    private static string? InvokeResolveResultTypeName(TranslatedCallSite execSite, IRQueryPlan plan)
    {
        var method = typeof(SqlAssembler).GetMethod(
            "ResolveResultTypeName",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string?)method.Invoke(null, new object[] { execSite, plan });
    }

    #endregion
}
