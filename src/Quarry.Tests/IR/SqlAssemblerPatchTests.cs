using System;
using System.Reflection;
using NUnit.Framework;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;
using GenSqlDialectConfig = Quarry.Generators.Sql.SqlDialectConfig;
using IRQueryPlan = Quarry.Generators.IR.QueryPlan;

namespace Quarry.Tests.IR;

/// <summary>
/// Phase 5 tests for <see cref="SqlAssembler"/>: a UPDATE plan whose single SET term
/// carries a <see cref="PatchSetPlaceholderExpr"/> must render with the literal
/// <c>{__PATCH_SET__}</c> token instead of <c>SET col = @pN, ...</c>, and WHERE
/// parameter indices must start at 0/$1/@p0 since the placeholder contributes zero
/// compile-time parameters (the runtime emitter applies <c>__setShift</c> at execute
/// time).
/// </summary>
[TestFixture]
public class SqlAssemblerPatchTests
{
    [Test]
    public void RenderUpdateSql_PatchPlaceholder_SQLite_EmitsTokenAndNoSetKeyword()
    {
        var sql = RenderUpdate(GenSqlDialect.SQLite);
        Assert.That(sql, Is.EqualTo("UPDATE \"users\"{__PATCH_SET__} WHERE \"UserId\" = @p0"));
    }

    [Test]
    public void RenderUpdateSql_PatchPlaceholder_PostgreSQL_EmitsTokenAndNoSetKeyword()
    {
        var sql = RenderUpdate(GenSqlDialect.PostgreSQL);
        Assert.That(sql, Is.EqualTo("UPDATE \"users\"{__PATCH_SET__} WHERE \"UserId\" = $1"));
    }

    [Test]
    public void RenderUpdateSql_PatchPlaceholder_MySQL_EmitsTokenAndNoSetKeyword()
    {
        var sql = RenderUpdate(GenSqlDialect.MySQL);
        Assert.That(sql, Is.EqualTo("UPDATE `users`{__PATCH_SET__} WHERE `UserId` = ?"));
    }

    [Test]
    public void RenderUpdateSql_PatchPlaceholder_SqlServer_EmitsTokenAndNoSetKeyword()
    {
        var sql = RenderUpdate(GenSqlDialect.SqlServer);
        Assert.That(sql, Is.EqualTo("UPDATE [users]{__PATCH_SET__} WHERE [UserId] = @p0"));
    }

    [Test]
    public void RenderUpdateSql_PatchPlaceholder_DoesNotContainSetKeyword()
    {
        // The runtime emitter (Phase 6) writes " SET " together with the active columns.
        // Compile-time SQL must NOT contain a SET keyword — otherwise the runtime would
        // emit two of them and produce invalid SQL.
        foreach (var d in new[] { GenSqlDialect.SQLite, GenSqlDialect.PostgreSQL,
                                  GenSqlDialect.MySQL, GenSqlDialect.SqlServer })
        {
            var sql = RenderUpdate(d);
            Assert.That(sql, Does.Not.Contain(" SET "), $"dialect={d}");
            Assert.That(sql, Does.Contain(PatchSetPlaceholderExpr.Token), $"dialect={d}");
        }
    }

    [Test]
    public void RenderUpdateSql_PatchPlaceholder_PlanReportsZeroSetParameters()
    {
        // Patch contributes no compile-time params; WHERE param starts at index 0.
        // The returned ParameterCount must reflect WHERE alone (1), not WHERE + Patch.
        var (_, paramCount) = RenderUpdateWithCount(GenSqlDialect.SQLite);
        Assert.That(paramCount, Is.EqualTo(1));
    }

    [Test]
    public void RenderUpdateSql_NonPatchSetTerm_StillEmitsSetKeyword()
    {
        // Regression guard: the Patch detection must trigger only when the single
        // SET term's Value is a PatchSetPlaceholderExpr; a regular ParamSlotExpr-backed
        // term still produces " SET col = @p0".
        var setTerm = new SetTerm(
            new ResolvedColumnExpr("\"UserName\""),
            new ParamSlotExpr(0, "string", "@p0"));
        var sql = RenderUpdateWithTerms(GenSqlDialect.SQLite, new[] { setTerm });
        Assert.That(sql, Does.Contain(" SET \"UserName\" = @p0"));
        Assert.That(sql, Does.Not.Contain(PatchSetPlaceholderExpr.Token));
    }

    // ── Harness ─────────────────────────────────────────────────────────

    private static string RenderUpdate(GenSqlDialect dialect)
    {
        return RenderUpdateWithCount(dialect).Sql;
    }

    private static (string Sql, int ParamCount) RenderUpdateWithCount(GenSqlDialect dialect)
    {
        var quotedUserId = Quotes(dialect, "UserId");
        var paramPlaceholder = dialect switch
        {
            GenSqlDialect.PostgreSQL => "$1",
            GenSqlDialect.MySQL => "?",
            _ => "@p0",
        };

        var whereTerm = new WhereTerm(new BinaryOpExpr(
            new ResolvedColumnExpr(quotedUserId),
            SqlBinaryOperator.Equal,
            new ParamSlotExpr(0, "int", paramPlaceholder)));

        var patchSet = new SetTerm(
            new ResolvedColumnExpr(string.Empty),
            new PatchSetPlaceholderExpr());

        var plan = MakePlan(new[] { patchSet }, new[] { whereTerm });
        return InvokeRenderUpdateSql(plan, mask: 0, new GenSqlDialectConfig(dialect));
    }

    private static string RenderUpdateWithTerms(GenSqlDialect dialect, SetTerm[] terms)
    {
        var quotedUserId = Quotes(dialect, "UserId");
        var paramPlaceholder = dialect switch
        {
            GenSqlDialect.PostgreSQL => "$2",
            GenSqlDialect.MySQL => "?",
            _ => "@p1",
        };

        var whereTerm = new WhereTerm(new BinaryOpExpr(
            new ResolvedColumnExpr(quotedUserId),
            SqlBinaryOperator.Equal,
            new ParamSlotExpr(0, "int", paramPlaceholder)));

        var plan = MakePlan(terms, new[] { whereTerm });
        return InvokeRenderUpdateSql(plan, mask: 0, new GenSqlDialectConfig(dialect)).Sql;
    }

    private static IRQueryPlan MakePlan(SetTerm[] setTerms, WhereTerm[] whereTerms)
    {
        return new IRQueryPlan(
            kind: QueryKind.Update,
            primaryTable: new TableRef("users", null, null),
            joins: Array.Empty<JoinPlan>(),
            whereTerms: whereTerms,
            orderTerms: Array.Empty<OrderTerm>(),
            groupByExprs: Array.Empty<SqlExpr>(),
            havingExprs: Array.Empty<SqlExpr>(),
            projection: new SelectProjection(
                ProjectionKind.Entity, "User",
                Array.Empty<ProjectedColumn>(), isIdentity: true),
            pagination: null,
            isDistinct: false,
            setTerms: setTerms,
            insertColumns: Array.Empty<InsertColumn>(),
            conditionalTerms: Array.Empty<ConditionalTerm>(),
            possibleMasks: new int[] { 0 },
            parameters: Array.Empty<QueryParameter>(),
            tier: OptimizationTier.PrebuiltDispatch);
    }

    private static (string Sql, int ParamCount) InvokeRenderUpdateSql(
        IRQueryPlan plan, int mask, GenSqlDialectConfig config)
    {
        var method = typeof(SqlAssembler).GetMethod(
            "RenderUpdateSql", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("RenderUpdateSql not found");
        var variant = method.Invoke(null, new object[] { plan, mask, config })!;
        var t = variant.GetType();
        return ((string)t.GetProperty("Sql")!.GetValue(variant)!,
                (int)t.GetProperty("ParameterCount")!.GetValue(variant)!);
    }

    private static string Quotes(GenSqlDialect d, string name) => d switch
    {
        GenSqlDialect.SqlServer => $"[{name}]",
        GenSqlDialect.MySQL => $"`{name}`",
        _ => $"\"{name}\"",
    };
}
