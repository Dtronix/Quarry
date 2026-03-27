using System;
using System.Collections.Generic;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;
using IRQueryPlan = Quarry.Generators.IR.QueryPlan;

namespace Quarry.Tests.Testing;

/// <summary>
/// Shared helper for constructing minimal IRQueryPlan and AssembledPlan instances in tests.
/// </summary>
internal static class TestPlanHelper
{
    public static IRQueryPlan CreateQueryPlanWithProjection(SelectProjection? projection)
    {
        return new IRQueryPlan(
            kind: QueryKind.Select,
            primaryTable: new TableRef("users", null, "t0"),
            joins: Array.Empty<JoinPlan>(),
            whereTerms: Array.Empty<WhereTerm>(),
            orderTerms: Array.Empty<OrderTerm>(),
            groupByExprs: Array.Empty<SqlExpr>(),
            havingExprs: Array.Empty<SqlExpr>(),
            projection: projection ?? new SelectProjection(
                ProjectionKind.Entity, "User",
                Array.Empty<ProjectedColumn>(), isIdentity: true),
            pagination: null,
            isDistinct: false,
            setTerms: Array.Empty<SetTerm>(),
            insertColumns: Array.Empty<InsertColumn>(),
            conditionalTerms: Array.Empty<ConditionalTerm>(),
            possibleMasks: new int[] { 0 },
            parameters: Array.Empty<QueryParameter>(),
            tier: OptimizationTier.PrebuiltDispatch);
    }

    public static AssembledPlan CreateMinimalAssembledPlan(
        TranslatedCallSite executionSite,
        TranslatedCallSite[] clauseSites)
    {
        return CreateAssembledPlanWithProjection(executionSite, clauseSites, null);
    }

    public static AssembledPlan CreateAssembledPlanWithProjection(
        TranslatedCallSite executionSite,
        TranslatedCallSite[] clauseSites,
        SelectProjection? projection)
    {
        var queryPlan = CreateQueryPlanWithProjection(projection);

        var sqlVariants = new Dictionary<int, AssembledSqlVariant>
        {
            [0] = new AssembledSqlVariant("SELECT 1", 0)
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
            entitySchemaNamespace: null,
            isTraced: false);
    }
}
