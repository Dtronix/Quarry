using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;
using GenSqlDialectConfig = Quarry.Generators.Sql.SqlDialectConfig;
using IRQueryPlan = Quarry.Generators.IR.QueryPlan;

namespace Quarry.Tests.IR;

/// <summary>
/// Verifies that batch (incremental) SQL rendering produces identical output
/// to per-mask rendering for SELECT and DELETE queries.
/// </summary>
[TestFixture]
public class SqlAssemblerBatchTests
{
    #region SELECT — Conditional WHERE

    [Test]
    public void BatchSelect_TwoConditionalWhereTerms_MatchesPerMask()
    {
        // WHERE "age" > @p0 (conditional, bit 0)
        // WHERE "name" = @p1 (conditional, bit 1)
        var plan = CreateSelectPlan(
            whereTerms: new[]
            {
                new WhereTerm(MakeComparison("age", SqlBinaryOperator.GreaterThan, paramLocalIndex: 0), bitIndex: 0),
                new WhereTerm(MakeComparison("name", SqlBinaryOperator.Equal, paramLocalIndex: 0), bitIndex: 1),
            },
            conditionalTerms: new[] { new ConditionalTerm(0, ClauseRole.Where), new ConditionalTerm(1, ClauseRole.Where) },
            possibleMasks: new[] { 0, 1, 2, 3 },
            parameters: new[]
            {
                new QueryParameter(0, "int", "age"),
                new QueryParameter(1, "string", "name"),
            });

        AssertBatchMatchesPerMask(plan, GenSqlDialect.SQLite, plan.PossibleMasks);
    }

    [Test]
    public void BatchSelect_UnconditionalPlusConditionalWhere_MatchesPerMask()
    {
        // WHERE "active" = 1 (unconditional)
        // WHERE "age" > @p0 (conditional, bit 0)
        var plan = CreateSelectPlan(
            whereTerms: new[]
            {
                new WhereTerm(MakeComparison("active", SqlBinaryOperator.Equal, literalValue: "1")),
                new WhereTerm(MakeComparison("age", SqlBinaryOperator.GreaterThan, paramLocalIndex: 0), bitIndex: 0),
            },
            conditionalTerms: new[] { new ConditionalTerm(0, ClauseRole.Where) },
            possibleMasks: new[] { 0, 1 },
            parameters: new[] { new QueryParameter(0, "int", "age") });

        AssertBatchMatchesPerMask(plan, GenSqlDialect.SQLite, plan.PossibleMasks);
    }

    #endregion

    #region SELECT — Conditional ORDER BY

    [Test]
    public void BatchSelect_ConditionalOrderBy_MatchesPerMask()
    {
        // ORDER BY "name" ASC (conditional, bit 0)
        var plan = CreateSelectPlan(
            orderTerms: new[]
            {
                new OrderTerm(new ResolvedColumnExpr("\"name\""), isDescending: false, bitIndex: 0),
            },
            conditionalTerms: new[] { new ConditionalTerm(0, ClauseRole.OrderBy) },
            possibleMasks: new[] { 0, 1 });

        AssertBatchMatchesPerMask(plan, GenSqlDialect.SQLite, plan.PossibleMasks);
    }

    [Test]
    public void BatchSelect_TwoConditionalOrderByTerms_MatchesPerMask()
    {
        // ORDER BY "name" ASC (conditional, bit 0)
        // ORDER BY "age" DESC (conditional, bit 1)
        var plan = CreateSelectPlan(
            orderTerms: new[]
            {
                new OrderTerm(new ResolvedColumnExpr("\"name\""), isDescending: false, bitIndex: 0),
                new OrderTerm(new ResolvedColumnExpr("\"age\""), isDescending: true, bitIndex: 1),
            },
            conditionalTerms: new[]
            {
                new ConditionalTerm(0, ClauseRole.OrderBy),
                new ConditionalTerm(1, ClauseRole.OrderBy),
            },
            possibleMasks: new[] { 0, 1, 2, 3 });

        AssertBatchMatchesPerMask(plan, GenSqlDialect.SQLite, plan.PossibleMasks);
    }

    #endregion

    #region SELECT — Mixed WHERE + ORDER BY

    [Test]
    public void BatchSelect_ConditionalWherePlusConditionalOrderBy_MatchesPerMask()
    {
        // WHERE "age" > @p0 (conditional, bit 0)
        // ORDER BY "name" ASC (conditional, bit 1)
        var plan = CreateSelectPlan(
            whereTerms: new[]
            {
                new WhereTerm(MakeComparison("age", SqlBinaryOperator.GreaterThan, paramLocalIndex: 0), bitIndex: 0),
            },
            orderTerms: new[]
            {
                new OrderTerm(new ResolvedColumnExpr("\"name\""), isDescending: false, bitIndex: 1),
            },
            conditionalTerms: new[]
            {
                new ConditionalTerm(0, ClauseRole.Where),
                new ConditionalTerm(1, ClauseRole.OrderBy),
            },
            possibleMasks: new[] { 0, 1, 2, 3 },
            parameters: new[] { new QueryParameter(0, "int", "age") });

        AssertBatchMatchesPerMask(plan, GenSqlDialect.SQLite, plan.PossibleMasks);
    }

    #endregion

    #region SELECT — Dialect variations

    [Test]
    public void BatchSelect_ConditionalWhere_AllDialects_MatchesPerMask()
    {
        var dialects = new[] { GenSqlDialect.SQLite, GenSqlDialect.PostgreSQL, GenSqlDialect.SqlServer, GenSqlDialect.MySQL };
        foreach (var dialect in dialects)
        {
            var plan = CreateSelectPlan(
                whereTerms: new[]
                {
                    new WhereTerm(MakeComparison("age", SqlBinaryOperator.GreaterThan, paramLocalIndex: 0), bitIndex: 0),
                },
                conditionalTerms: new[] { new ConditionalTerm(0, ClauseRole.Where) },
                possibleMasks: new[] { 0, 1 },
                parameters: new[] { new QueryParameter(0, "int", "age") });

            AssertBatchMatchesPerMask(plan, dialect, plan.PossibleMasks);
        }
    }

    #endregion

    #region SELECT — Parameterized ORDER BY correctness

    [Test]
    public void BatchSelect_ParameterizedConditionalOrderBy_CorrectParamIndices()
    {
        // WHERE "active" = @p0 (unconditional)
        // ORDER BY @p1 ASC (conditional, bit 0) — parameterized ORDER BY
        // ORDER BY @p2 DESC (conditional, bit 1) — parameterized ORDER BY
        var plan = CreateSelectPlan(
            whereTerms: new[]
            {
                new WhereTerm(MakeComparison("active", SqlBinaryOperator.Equal, paramLocalIndex: 0)),
            },
            orderTerms: new[]
            {
                new OrderTerm(new ParamSlotExpr(0, "int", "@p0"), isDescending: false, bitIndex: 0),
                new OrderTerm(new ParamSlotExpr(0, "int", "@p0"), isDescending: true, bitIndex: 1),
            },
            conditionalTerms: new[]
            {
                new ConditionalTerm(0, ClauseRole.OrderBy),
                new ConditionalTerm(1, ClauseRole.OrderBy),
            },
            possibleMasks: new[] { 0, 1, 2, 3 },
            parameters: new[]
            {
                new QueryParameter(0, "int", "active"),
                new QueryParameter(1, "int", "sortVal1"),
                new QueryParameter(2, "int", "sortVal2"),
            });

        AssertBatchMatchesPerMask(plan, GenSqlDialect.SQLite, plan.PossibleMasks);

        // Verify specific parameter indices: when ORDER BY bit 0 is inactive (mask=2),
        // bit 1's parameter should still use @p2 (not @p1) due to all-terms pre-computation.
        var batchResults = InvokeRenderSelectSqlBatch(plan, GenSqlDialect.SQLite, plan.PossibleMasks);
        var mask2Sql = batchResults[2].Sql;
        Assert.That(mask2Sql, Does.Contain("@p2"), "ORDER BY term 1 should use @p2 even when term 0 is inactive");
        Assert.That(mask2Sql, Does.Not.Contain("@p1"), "ORDER BY term 1 should not shift to @p1 when term 0 is inactive");
    }

    #endregion

    #region SELECT — SQL Server ORDER BY (SELECT NULL) fallback

    [Test]
    public void BatchSelect_SqlServer_ConditionalOrderByWithPagination_FallbackOrderBy()
    {
        // ORDER BY "name" ASC (conditional, bit 0)
        // LIMIT 10
        var plan = CreateSelectPlan(
            orderTerms: new[]
            {
                new OrderTerm(new ResolvedColumnExpr("\"name\""), isDescending: false, bitIndex: 0),
            },
            conditionalTerms: new[] { new ConditionalTerm(0, ClauseRole.OrderBy) },
            possibleMasks: new[] { 0, 1 },
            pagination: new PaginationPlan(literalLimit: 10));

        var batchResults = InvokeRenderSelectSqlBatch(plan, GenSqlDialect.SqlServer, plan.PossibleMasks);

        // Mask 0: no active ORDER BY → needs SQL Server fallback ORDER BY (SELECT NULL)
        Assert.That(batchResults[0].Sql, Does.Contain("ORDER BY (SELECT NULL)"));
        // Mask 1: active ORDER BY → no fallback needed
        Assert.That(batchResults[1].Sql, Does.Not.Contain("SELECT NULL"));
        Assert.That(batchResults[1].Sql, Does.Contain("ORDER BY"));

        // Both should match per-mask rendering
        AssertBatchMatchesPerMask(plan, GenSqlDialect.SqlServer, plan.PossibleMasks);
    }

    #endregion

    #region SELECT — Set operations with conditional post-union WHERE

    [Test]
    public void BatchSelect_SetOperationWithConditionalPostUnionWhere_MatchesPerMask()
    {
        // Main query: SELECT * FROM users
        // UNION ALL: SELECT * FROM admins
        // Post-union WHERE "role" = @p0 (conditional, bit 0)

        var operandPlan = new IRQueryPlan(
            kind: QueryKind.Select,
            primaryTable: new TableRef("admins"),
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
            possibleMasks: new[] { 0 },
            parameters: Array.Empty<QueryParameter>(),
            tier: OptimizationTier.PrebuiltDispatch);

        var plan = new IRQueryPlan(
            kind: QueryKind.Select,
            primaryTable: new TableRef("users"),
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
            conditionalTerms: new[] { new ConditionalTerm(0, ClauseRole.Where) },
            possibleMasks: new[] { 0, 1 },
            parameters: new[] { new QueryParameter(0, "int", "role") },
            tier: OptimizationTier.PrebuiltDispatch,
            setOperations: new[] { new SetOperationPlan(SetOperatorKind.UnionAll, operandPlan, 0) },
            postUnionWhereTerms: new[]
            {
                new WhereTerm(MakeComparison("role", SqlBinaryOperator.Equal, paramLocalIndex: 0), bitIndex: 0),
            });

        AssertBatchMatchesPerMask(plan, GenSqlDialect.SQLite, plan.PossibleMasks);
    }

    #endregion

    #region DELETE — Conditional WHERE

    [Test]
    public void BatchDelete_ConditionalWhere_MatchesPerMask()
    {
        var plan = CreateDeletePlan(
            whereTerms: new[]
            {
                new WhereTerm(MakeComparison("id", SqlBinaryOperator.Equal, paramLocalIndex: 0), bitIndex: 0),
                new WhereTerm(MakeComparison("active", SqlBinaryOperator.Equal, literalValue: "1"), bitIndex: 1),
            },
            conditionalTerms: new[]
            {
                new ConditionalTerm(0, ClauseRole.Where),
                new ConditionalTerm(1, ClauseRole.Where),
            },
            possibleMasks: new[] { 0, 1, 2, 3 },
            parameters: new[] { new QueryParameter(0, "int", "id") });

        AssertDeleteBatchMatchesPerMask(plan, GenSqlDialect.SQLite, plan.PossibleMasks);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Asserts that RenderSelectSqlBatch produces identical SQL and parameter counts
    /// to calling RenderSelectSql per mask.
    /// </summary>
    private static void AssertBatchMatchesPerMask(
        IRQueryPlan plan, GenSqlDialect dialect, IReadOnlyList<int> masks)
    {
        var batchResults = InvokeRenderSelectSqlBatch(plan, dialect, masks);

        foreach (var mask in masks)
        {
            var perMask = InvokeRenderSelectSql(plan, mask, dialect);
            Assert.That(batchResults.ContainsKey(mask), Is.True, $"Batch missing mask {mask}");
            var batch = batchResults[mask];
            Assert.That(batch.Sql, Is.EqualTo(perMask.Sql),
                $"SQL mismatch for mask {mask}.\nPer-mask: {perMask.Sql}\nBatch:    {batch.Sql}");
            Assert.That(batch.ParameterCount, Is.EqualTo(perMask.ParameterCount),
                $"ParameterCount mismatch for mask {mask}");
        }
    }

    /// <summary>
    /// Asserts that RenderDeleteSqlBatch produces identical SQL and parameter counts
    /// to calling RenderDeleteSql per mask.
    /// </summary>
    private static void AssertDeleteBatchMatchesPerMask(
        IRQueryPlan plan, GenSqlDialect dialect, IReadOnlyList<int> masks)
    {
        var batchResults = InvokeRenderDeleteSqlBatch(plan, dialect, masks);

        foreach (var mask in masks)
        {
            var perMask = InvokeRenderDeleteSql(plan, mask, dialect);
            Assert.That(batchResults.ContainsKey(mask), Is.True, $"Batch missing mask {mask}");
            var batch = batchResults[mask];
            Assert.That(batch.Sql, Is.EqualTo(perMask.Sql),
                $"SQL mismatch for mask {mask}.\nPer-mask: {perMask.Sql}\nBatch:    {batch.Sql}");
            Assert.That(batch.ParameterCount, Is.EqualTo(perMask.ParameterCount),
                $"ParameterCount mismatch for mask {mask}");
        }
    }

    private static BinaryOpExpr MakeComparison(string column, SqlBinaryOperator op, int? paramLocalIndex = null, string? literalValue = null)
    {
        SqlExpr right = paramLocalIndex.HasValue
            ? new ParamSlotExpr(paramLocalIndex.Value, "object", $"@p{paramLocalIndex.Value}")
            : new LiteralExpr(literalValue!, "object");
        return new BinaryOpExpr(
            new ResolvedColumnExpr($"\"{column}\""),
            op,
            right);
    }

    private static IRQueryPlan CreateSelectPlan(
        WhereTerm[]? whereTerms = null,
        OrderTerm[]? orderTerms = null,
        ConditionalTerm[]? conditionalTerms = null,
        int[]? possibleMasks = null,
        QueryParameter[]? parameters = null,
        PaginationPlan? pagination = null)
    {
        return new IRQueryPlan(
            kind: QueryKind.Select,
            primaryTable: new TableRef("users"),
            joins: Array.Empty<JoinPlan>(),
            whereTerms: whereTerms ?? Array.Empty<WhereTerm>(),
            orderTerms: orderTerms ?? Array.Empty<OrderTerm>(),
            groupByExprs: Array.Empty<SqlExpr>(),
            havingExprs: Array.Empty<SqlExpr>(),
            projection: new SelectProjection(
                ProjectionKind.Entity, "User",
                Array.Empty<ProjectedColumn>(), isIdentity: true),
            pagination: pagination,
            isDistinct: false,
            setTerms: Array.Empty<SetTerm>(),
            insertColumns: Array.Empty<InsertColumn>(),
            conditionalTerms: conditionalTerms ?? Array.Empty<ConditionalTerm>(),
            possibleMasks: possibleMasks ?? new[] { 0 },
            parameters: parameters ?? Array.Empty<QueryParameter>(),
            tier: OptimizationTier.PrebuiltDispatch);
    }

    private static IRQueryPlan CreateDeletePlan(
        WhereTerm[]? whereTerms = null,
        ConditionalTerm[]? conditionalTerms = null,
        int[]? possibleMasks = null,
        QueryParameter[]? parameters = null)
    {
        return new IRQueryPlan(
            kind: QueryKind.Delete,
            primaryTable: new TableRef("users"),
            joins: Array.Empty<JoinPlan>(),
            whereTerms: whereTerms ?? Array.Empty<WhereTerm>(),
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
            conditionalTerms: conditionalTerms ?? Array.Empty<ConditionalTerm>(),
            possibleMasks: possibleMasks ?? new[] { 0 },
            parameters: parameters ?? Array.Empty<QueryParameter>(),
            tier: OptimizationTier.PrebuiltDispatch);
    }

    #endregion

    #region Reflection Invokers

    private static AssembledSqlVariant InvokeRenderSelectSql(IRQueryPlan plan, int mask, GenSqlDialect dialect)
    {
        var method = typeof(SqlAssembler).GetMethod(
            "RenderSelectSql",
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            new[] { typeof(IRQueryPlan), typeof(int), typeof(GenSqlDialectConfig), typeof(int) },
            null)!;
        return (AssembledSqlVariant)method.Invoke(null, new object[] { plan, mask, new GenSqlDialectConfig(dialect), 0 })!;
    }

    private static Dictionary<int, AssembledSqlVariant> InvokeRenderSelectSqlBatch(
        IRQueryPlan plan, GenSqlDialect dialect, IReadOnlyList<int> masks)
    {
        var method = typeof(SqlAssembler).GetMethod(
            "RenderSelectSqlBatch",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var results = new Dictionary<int, AssembledSqlVariant>();
        method.Invoke(null, new object[] { plan, new GenSqlDialectConfig(dialect), masks, results });
        return results;
    }

    private static AssembledSqlVariant InvokeRenderDeleteSql(IRQueryPlan plan, int mask, GenSqlDialect dialect)
    {
        var method = typeof(SqlAssembler).GetMethod(
            "RenderDeleteSql",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (AssembledSqlVariant)method.Invoke(null, new object[] { plan, mask, new GenSqlDialectConfig(dialect) })!;
    }

    private static Dictionary<int, AssembledSqlVariant> InvokeRenderDeleteSqlBatch(
        IRQueryPlan plan, GenSqlDialect dialect, IReadOnlyList<int> masks)
    {
        var method = typeof(SqlAssembler).GetMethod(
            "RenderDeleteSqlBatch",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var results = new Dictionary<int, AssembledSqlVariant>();
        method.Invoke(null, new object[] { plan, new GenSqlDialectConfig(dialect), masks, results });
        return results;
    }

    #endregion
}
