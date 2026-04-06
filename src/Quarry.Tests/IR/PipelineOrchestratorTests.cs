using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using NUnit.Framework;
using Quarry.Generators.IR;
using Quarry.Generators.Utilities;
using Quarry.Generators.Models;
using Quarry.Tests.Testing;
using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;
using IRQueryPlan = Quarry.Generators.IR.QueryPlan;

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
    [TestCase("(int, (string, object))", ExpectedResult = true, Description = "Nested tuple with unresolved inner element")]
    [TestCase("(int, (string, decimal))", ExpectedResult = false, Description = "Nested tuple, all resolved")]
    [TestCase("((object, int), string)", ExpectedResult = true, Description = "Unresolved element in first nested tuple")]
    [TestCase("(int, (string, decimal) Named)", ExpectedResult = false, Description = "Nested named tuple, all resolved")]
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

    #region CollectPostAnalysisDiagnostics — QRY072

    [Test]
    public void CollectPostAnalysisDiagnostics_SetOperationColumnCountMismatch_EmitsQRY072()
    {
        // QRY072 (SetOperationProjectionMismatch) is reachable from valid C# source via
        // asymmetric DTO object initializers across the two sides of a Union/Intersect/Except:
        //   Select(u => new MyDto { A = u.X, B = u.Y })  // 2 columns
        //     .Union(Select(u => new MyDto { A = u.X })) // 1 column
        // Both sides share TResult=MyDto so the C# type system permits the call, but
        // ProjectionAnalyzer counts one column per assignment expression — not per type
        // member — so the IR projections end up with different Columns.Count values.
        // This test verifies the diagnostic still fires for that shape.

        var mainProjection = new SelectProjection(
            ProjectionKind.Dto,
            "MyDto",
            new[]
            {
                CreateProjectedColumn("A", "Col_A", 0),
                CreateProjectedColumn("B", "Col_B", 1),
            });

        var operandProjection = new SelectProjection(
            ProjectionKind.Dto,
            "MyDto",
            new[]
            {
                CreateProjectedColumn("A", "Col_A", 0),
            });

        var operandPlan = CreateQueryPlanWithProjectionAndSetOps(operandProjection, setOperations: null);
        var setOp = new SetOperationPlan(SetOperatorKind.Union, operandPlan, parameterOffset: 0);
        var mainPlan = CreateQueryPlanWithProjectionAndSetOps(mainProjection, new[] { setOp });

        var execSite = new TestCallSiteBuilder()
            .WithUniqueId("exec_1")
            .WithKind(InterceptorKind.ExecuteFetchAll)
            .WithEntityType("User")
            .WithResultType("MyDto")
            .Build();

        var assembled = WrapInAssembledPlan(mainPlan, execSite);

        var diagnostics = InvokeCollectPostAnalysisDiagnostics(new List<AssembledPlan> { assembled });

        Assert.That(diagnostics, Has.Count.EqualTo(1));
        Assert.That(diagnostics[0].DiagnosticId, Is.EqualTo("QRY072"));
        Assert.That(diagnostics[0].MessageArgs, Has.Length.EqualTo(2));
        Assert.That(diagnostics[0].MessageArgs[0], Is.EqualTo("1"), "operand column count");
        Assert.That(diagnostics[0].MessageArgs[1], Is.EqualTo("2"), "main column count");
    }

    [Test]
    public void CollectPostAnalysisDiagnostics_SetOperationColumnCountsMatch_NoDiagnostic()
    {
        var mainProjection = new SelectProjection(
            ProjectionKind.Tuple,
            "(int, string)",
            new[]
            {
                CreateProjectedColumn("Item1", "UserId", 0),
                CreateProjectedColumn("Item2", "UserName", 1),
            });

        var operandProjection = new SelectProjection(
            ProjectionKind.Tuple,
            "(int, string)",
            new[]
            {
                CreateProjectedColumn("Item1", "ProductId", 0),
                CreateProjectedColumn("Item2", "ProductName", 1),
            });

        var operandPlan = CreateQueryPlanWithProjectionAndSetOps(operandProjection, setOperations: null);
        var setOp = new SetOperationPlan(SetOperatorKind.Union, operandPlan, parameterOffset: 0, operandEntityTypeName: "global::TestApp.Product");
        var mainPlan = CreateQueryPlanWithProjectionAndSetOps(mainProjection, new[] { setOp });

        var execSite = new TestCallSiteBuilder()
            .WithUniqueId("exec_2")
            .WithKind(InterceptorKind.ExecuteFetchAll)
            .WithEntityType("User")
            .WithResultType("(int, string)")
            .Build();

        var assembled = WrapInAssembledPlan(mainPlan, execSite);

        var diagnostics = InvokeCollectPostAnalysisDiagnostics(new List<AssembledPlan> { assembled });

        Assert.That(diagnostics, Is.Empty);
    }

    #endregion

    #region Helpers

    private static ProjectedColumn CreateProjectedColumn(string propertyName, string columnName, int ordinal)
    {
        return new ProjectedColumn(
            propertyName: propertyName,
            columnName: columnName,
            clrType: "int",
            fullClrType: "int",
            isNullable: false,
            ordinal: ordinal);
    }

    private static IRQueryPlan CreateQueryPlanWithProjectionAndSetOps(
        SelectProjection projection,
        IReadOnlyList<SetOperationPlan>? setOperations)
    {
        return new IRQueryPlan(
            kind: QueryKind.Select,
            primaryTable: new TableRef("users", null, "t0"),
            joins: Array.Empty<JoinPlan>(),
            whereTerms: Array.Empty<WhereTerm>(),
            orderTerms: Array.Empty<OrderTerm>(),
            groupByExprs: Array.Empty<SqlExpr>(),
            havingExprs: Array.Empty<SqlExpr>(),
            projection: projection,
            pagination: null,
            isDistinct: false,
            setTerms: Array.Empty<SetTerm>(),
            insertColumns: Array.Empty<InsertColumn>(),
            conditionalTerms: Array.Empty<ConditionalTerm>(),
            possibleMasks: new int[] { 0 },
            parameters: Array.Empty<QueryParameter>(),
            tier: OptimizationTier.PrebuiltDispatch,
            setOperations: setOperations);
    }

    private static AssembledPlan WrapInAssembledPlan(IRQueryPlan plan, TranslatedCallSite executionSite)
    {
        var sqlVariants = new Dictionary<int, AssembledSqlVariant>
        {
            [0] = new AssembledSqlVariant("SELECT 1", 0)
        };

        return new AssembledPlan(
            plan: plan,
            sqlVariants: sqlVariants,
            readerDelegateCode: null,
            maxParameterCount: 0,
            executionSite: executionSite,
            clauseSites: Array.Empty<TranslatedCallSite>(),
            entityTypeName: executionSite.EntityTypeName,
            resultTypeName: executionSite.ResultTypeName,
            dialect: GenSqlDialect.SQLite,
            entitySchemaNamespace: null,
            isTraced: false);
    }

    private static List<DiagnosticInfo> InvokeCollectPostAnalysisDiagnostics(List<AssembledPlan> assembledPlans)
    {
        var diagnostics = new List<DiagnosticInfo>();
        var method = typeof(PipelineOrchestrator).GetMethod(
            "CollectPostAnalysisDiagnostics",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        method.Invoke(null, new object[] { assembledPlans, diagnostics });
        return diagnostics;
    }

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
        return TypeClassification.IsUnresolvedResultType(resultTypeName);
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
